module ForTheRecord.Http

open System
open System.Collections.Generic
open System.IO
open System.Security.Claims
open System.Text
open System.Text.Json
open System.Threading.Tasks

open Fluid
open Giraffe
open Giraffe.HttpStatusCodeHandlers.RequestErrors
open idunno.Authentication.Basic
open Microsoft.AspNetCore.Hosting
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.Builder
open Microsoft.Extensions.DependencyInjection
open MimeKit

open ForTheRecord.Config
open ForTheRecord.Gmail
open ForTheRecord.Helpers
open ForTheRecord.Imap
open ForTheRecord.Liquid

module private Realms =
    [<Literal>]
    let configured = "Config"

module private Roles =
    [<Literal>]
    let gmailInsert = "GmailInsert"

    [<Literal>]
    let imapAppend = "ImapAppend"

let private parseContentType (s: string) =
    s
    |> ContentType.TryParse
    |> tryGetByref
    |> function
        | Some ct -> Ok ct
        | None -> s |> sprintf "Invalid MIME content type: %s" |> Error

let private authenticatedUser (ctx: HttpContext) =
    match ctx.User.FindFirst ClaimTypes.NameIdentifier with
    | null -> None
    | c -> Some c.Value

let private mapHeaders (ctx: HttpContext) =
    let headers, contentTypeHeaders =
        ctx.Request.Headers
        |> Seq.map (|KeyValue|)
        |> Seq.map (fun (k, sv) -> sv |> Seq.cast<string> |> Seq.map (fun s -> k, s))
        |> Seq.concat
        |> List.ofSeq
        |> List.partition (fun (k, v) -> k.ToLowerInvariant() <> "content-type")

    // All emails must at least have a valid From: field.
    let headersWithFrom =
        [ if not (List.exists (fun (k: string, _) -> k.ToLowerInvariant() = "from") headers) then
              "From", authenticatedUser ctx |> Option.defaultValue "ForTheRecord"
          yield! headers ]

    let headerList = HeaderList()

    for k, v in headersWithFrom do
        headerList.Add(k, v)

    let contentType =
        contentTypeHeaders
        |> Seq.tryExactlyOne
        |> Option.map (fun (_, v) -> v)
        |> Option.bind (parseContentType >> Result.toOption)

    headerList, contentType

let private simpleImportHandler =
    handleContext (fun ctx ->
        task {
            let config = ctx.GetService<ServeConfig>()
            let messageHeaders, contentType = mapHeaders ctx
            use message = new MimeMessage(messageHeaders)

            use body =
                new MimePart(contentType |> Option.defaultWith (fun () -> ContentType("text", "plain")))

            let! bodyText = ctx.ReadBodyFromRequestAsync()
            use bodyStream = new MemoryStream(Encoding.UTF8.GetBytes bodyText)
            body.Content <- new MimeContent(bodyStream)
            message.Body <- body

            match config.Inbox with
            | Gmail(_authInsert, inbox) ->
                use stream = new MemoryStream()
                do! message.WriteToAsync stream
                stream.Seek(0, SeekOrigin.Begin) |> ignore
                do! inbox.Import stream
            | Imap(_authAppend, inbox) -> do! inbox.Append message

            return Some ctx
        })

let private readAttachment (file: IFormFile) =
    let attach =
        new MimePart(
            file.ContentType
            |> parseContentType
            |> Result.defaultWith (fun _ -> ContentType("application", "octet-stream"))
        )

    attach.Content <- new MimeContent(file.OpenReadStream())
    attach.ContentDisposition <- ContentDisposition(ContentDisposition.Attachment)
    attach.ContentTransferEncoding <- ContentEncoding.Base64
    attach.FileName <- file.FileName
    attach

[<CLIMutable>]
type GmailImportForm =
    { LabelId: string list option
      Body: string option
      BodyType: string option
      InternalDateSource: string option
      NeverMarkSpam: bool option
      ProcessForCalendar: bool option
      Deleted: bool option }

let private gmailImportHandler =
    handleContext (fun ctx ->
        task {
            let config = ctx.GetService<ServeConfig>()
            let! form = ctx.BindFormAsync<GmailImportForm>()
            use multipart = new Multipart "mixed"

            use body =
                new MimePart(
                    form.BodyType
                    |> Option.bind (parseContentType >> Result.toOption)
                    |> Option.defaultWith (fun () -> ContentType("text", "plain"))
                )

            use bodyStream =
                new MemoryStream(form.Body |> Option.defaultValue "" |> Encoding.UTF8.GetBytes)

            body.Content <- new MimeContent(bodyStream)

            for entity in
                seq {
                    body
                    yield! ctx.Request.Form.Files |> Seq.map readAttachment
                } do
                multipart.Add entity

            let messageHeaders, _ = mapHeaders ctx
            use message = new MimeMessage(messageHeaders)
            message.Body <- multipart

            use stream = new MemoryStream()
            do! message.WriteToAsync stream
            stream.Seek(0, SeekOrigin.Begin) |> ignore

            do!
                (getGmailInbox config)
                    .Import(
                        stream,
                        ?labelIds = form.LabelId,
                        ?internalDateSource =
                            (form.InternalDateSource
                             |> Option.bind (parseInternalDateSource >> Result.toOption)),
                        ?neverMarkSpam = form.NeverMarkSpam,
                        ?processForCalendar = form.ProcessForCalendar,
                        ?deleted = form.Deleted
                    )

            return Some ctx
        })

[<CLIMutable>]
type ImapAppendForm =
    { Folder: string list option
      Flag: string list option
      Keyword: string list option
      Body: string option
      BodyType: string option }

let private imapAppendHandler =
    handleContext (fun ctx ->
        task {
            let config = ctx.GetService<ServeConfig>()
            let! form = ctx.BindFormAsync<ImapAppendForm>()
            use multipart = new Multipart "mixed"

            use body =
                new MimePart(
                    form.BodyType
                    |> Option.bind (parseContentType >> Result.toOption)
                    |> Option.defaultWith (fun () -> ContentType("text", "plain"))
                )

            use bodyStream =
                new MemoryStream(form.Body |> Option.defaultValue "" |> Encoding.UTF8.GetBytes)

            body.Content <- new MimeContent(bodyStream)

            for entity in
                seq {
                    body
                    yield! ctx.Request.Form.Files |> Seq.map readAttachment
                } do
                multipart.Add entity

            let messageHeaders, _ = mapHeaders ctx
            use message = new MimeMessage(messageHeaders)
            message.Body <- multipart

            let inbox = getImapInbox config
            let flags = Option.map parseImapFlags form.Flag

            match form.Folder with
            | Some folders ->
                do!
                    folders
                    |> Seq.map (fun f -> inbox.Append(message, f, ?flags = flags, ?keywords = form.Keyword))
                    |> Seq.cast<Task>
                    |> Task.WhenAll
            | None -> do! inbox.Append(message, ?flags = flags, ?keywords = form.Keyword)

            return Some ctx
        })

let private requiresGmail =
    fun (next: HttpFunc) (ctx: HttpContext) ->
        let config = ctx.GetService<ServeConfig>()

        match config.Inbox with
        | Gmail _ -> next ctx
        | Imap _ -> GONE "Gmail not configured" earlyReturn ctx

let private requiresImap =
    fun (next: HttpFunc) (ctx: HttpContext) ->
        let config = ctx.GetService<ServeConfig>()

        match config.Inbox with
        | Imap _ -> next ctx
        | Gmail _ -> GONE "IMAP not configured" earlyReturn ctx

let private requiresRole role =
    fun (next: HttpFunc) (ctx: HttpContext) ->
        let config = ctx.GetService<ServeConfig>()

        match config.Htpasswd with
        | Some _ ->
            let handler =
                requiresAuthentication (RequestErrors.UNAUTHORIZED "Basic" Realms.configured "Authentication failed")
                >=> requiresRole role (RequestErrors.FORBIDDEN "Required scope not granted")

            handler next ctx
        | None -> next ctx

let private requiresImportRole =
    fun (next: HttpFunc) (ctx: HttpContext) ->
        let config = ctx.GetService<ServeConfig>()

        let role =
            match config.Inbox with
            | Gmail _ -> Roles.gmailInsert
            | Imap _ -> Roles.imapAppend

        requiresRole role next ctx

let private requiresJson =
    hasContentType
        "application/json"
        { InvalidHeaderValue = None
          HeaderNotFound = None }

let private importWholeMessage (template: string) (model: obj, modelKey: string option) =
    handleContext (fun ctx ->
        task {
            let config = ctx.GetService<ServeConfig>()
            let parser = ctx.GetService<FluidParser>()

            let ok, template, error = parser.TryParse template

            if not ok then
                failwithf "Failed to parse template: %s" error

            let ftr =
                seq {
                    "user", authenticatedUser ctx |> Option.defaultValue null :> obj
                    "is_gmail", config.Inbox.IsGmail
                    "is_imap", config.Inbox.IsImap
                    "guid", string (Guid.NewGuid())
                }
                |> dict

            let options = ctx.GetService<TemplateOptions>()

            let context =
                TemplateContext(
                    seq {
                        match tryDowncast<IDictionary<string, obj>> model with
                        | Some d -> yield! d |> Seq.map (|KeyValue|)
                        | None -> ()

                        match modelKey with
                        | Some key -> key, model
                        | None -> ()

                        "ftr", ftr
                    }
                    |> dict,
                    options
                )

            let! render = template.RenderAsync context
            use stream = new MemoryStream(Encoding.UTF8.GetBytes render)

            do!
                match config.Inbox with
                | Gmail(_, inbox) -> importWholeMessageToGmail inbox stream
                | Imap(_, inbox) -> importWholeMessageToImap inbox stream

            return Some ctx
        })

let private resolveTemplate (ctx: HttpContext) (builtIn: AssemblyTemplate) (request: string option) =
    task {
        let config = ctx.GetService<ServeConfig>()

        match request with
        | Some name ->
            match config.Templates.TryGetValue name with
            | true, template -> return Ok template
            | false, _ -> return Error "Named template does not exist"
        | None ->
            let! template = readAssemblyTemplate builtIn
            return Ok template
    }

let private genericJsonHandler (builtInTemplate: AssemblyTemplate) : HttpHandler =
    fun (next: HttpFunc) (ctx: HttpContext) ->
        task {
            let! json = ctx.BindJsonAsync<JsonElement>()

            let! template =
                json
                |> tryJsonProperty "ftr_template"
                |> Option.bind tryJsonString
                |> resolveTemplate ctx builtInTemplate

            match template with
            | Ok template ->
                let model = jsonLiquidModel json
                return! importWholeMessage template (model, Some "json") next ctx
            | Error err -> return! unprocessableEntity (text err) earlyReturn ctx
        }

let private ntfyMockResponse (topic: string) =
    {| topic = topic
       event = "message"
       message = "triggered"
       id = string (Guid.NewGuid())
       time = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
       expires = Int32.MaxValue |}
    |> JsonSerializer.Serialize
    |> text
    |> compose (setContentType "application/json")

let private ntfyJsonHandler (templateName: string option) =
    fun (next: HttpFunc) (ctx: HttpContext) ->
        task {
            let! json = ctx.BindJsonAsync<JsonElement>()
            and! template = resolveTemplate ctx Ntfy templateName
            let topic = json |> tryJsonProperty "topic" |> Option.bind tryJsonString

            match topic, template with
            | Some topic, Ok template ->
                let model = jsonLiquidModel json
                let handler = importWholeMessage template (model, None) >=> ntfyMockResponse topic
                return! handler next ctx
            | None, _ -> return! badRequest (text "Missing Ntfy topic") earlyReturn ctx
            | _, Error err -> return! unprocessableEntity (text err) earlyReturn ctx
        }

let private ntfySimpleHandler (templateName: string option) (topic: string) =
    fun (next: HttpFunc) (ctx: HttpContext) ->
        task {
            let! template = resolveTemplate ctx Ntfy templateName

            match template with
            | Ok template ->
                let! body = ctx.ReadBodyFromRequestAsync()

                let model =
                    seq {
                        KeyValuePair("topic", topic :> obj)
                        KeyValuePair("message", body)
                        yield! ntfyModelFromRequest ctx.Request
                    }
                    |> Dictionary

                let handler = importWholeMessage template (model, None) >=> ntfyMockResponse topic
                return! handler next ctx
            | Error err -> return! unprocessableEntity (text err) earlyReturn ctx
        }

let private shoutrrrHandler =
    fun (next: HttpFunc) (ctx: HttpContext) ->
        task {
            let! template =
                ctx.Request.Query.TryGetValue "ftr_template"
                |> tryGetByref
                |> Option.map Seq.last
                |> resolveTemplate ctx Shoutrrr

            match template with
            | Ok template ->
                let! body = ctx.ReadBodyFromRequestAsync()
                let model = Dictionary<string, obj>()
                model.Add("message", body)
                return! importWholeMessage template (model, None) next ctx
            | Error err -> return! unprocessableEntity (text err) earlyReturn ctx
        }

let gmailApiRoutes =
    choose
        [ route "/api/gmail/messages/import/ez"
          >=> requiresRole Roles.gmailInsert
          >=> requiresGmail
          >=> simpleImportHandler
          route "/api/gmail/messages/import"
          >=> requiresRole Roles.gmailInsert
          >=> requiresGmail
          >=> gmailImportHandler ]

let imapApiRoutes =
    choose
        [ route "/api/imap/append/ez"
          >=> requiresRole Roles.imapAppend
          >=> requiresImap
          >=> simpleImportHandler
          route "/api/imap/append"
          >=> requiresRole Roles.imapAppend
          >=> requiresImap
          >=> imapAppendHandler ]

let webhookRoutes =
    choose [ route "/api/webhook"; route "/go/notify" ]
    >=> requiresImportRole
    >=> requiresJson
    >=> genericJsonHandler Webhook

let appriseRoutes =
    route "/apprise"
    >=> requiresImportRole
    >=> requiresJson
    >=> genericJsonHandler Apprise

let ntfyRoutes =
    let jsonHandler = requiresImportRole >=> requiresJson >=> ntfyJsonHandler None

    let jsonHandlerWithTemplate =
        fun template -> requiresImportRole >=> requiresJson >=> ntfyJsonHandler (Some template)

    let simpleHandler = fun topic -> requiresImportRole >=> ntfySimpleHandler None topic

    let simpleHandlerWithTemplate =
        fun (template, topic) -> requiresImportRole >=> ntfySimpleHandler (Some template) topic

    choose
        [ route "/ntfy" >=> jsonHandler
          route "/ntfy/" >=> jsonHandler
          routef "/ntfy_template/%s" jsonHandlerWithTemplate
          routef "/ntfy_template/%s/" jsonHandlerWithTemplate
          routef "/ntfy/%s" simpleHandler
          routef "/ntfy_template/%s/%s" simpleHandlerWithTemplate ]

let shoutrrrRoutes =
    choose
        [ route "/shoutrrr" >=> requiresImportRole >=> shoutrrrHandler
          route "/shoutrrr/json"
          >=> requiresImportRole
          >=> requiresJson
          >=> genericJsonHandler Webhook ]

let webApp =
    choose
        [ POST
          >=> choose
                  [ gmailApiRoutes
                    imapApiRoutes
                    webhookRoutes
                    appriseRoutes
                    ntfyRoutes
                    shoutrrrRoutes ]
          RequestErrors.NOT_FOUND "404" ]

let private validateCredentials (context: ValidateCredentialsContext) =
    let config = context.HttpContext.RequestServices.GetService<ServeConfig>()

    let verified =
        match config.Htpasswd with
        | Some htpasswd -> htpasswd.VerifyCredentials(context.Username, context.Password)
        | None -> false

    if verified then
        let checkedRole (auth: Set<string>) (role: string) =
            seq {
                if auth.Contains context.Username then
                    ClaimTypes.Role, role
            }

        let claims =
            seq {
                ClaimTypes.NameIdentifier, context.Username
                ClaimTypes.Name, context.Username

                match config.Inbox with
                | Gmail(authInsert, _inbox) -> yield! checkedRole authInsert Roles.gmailInsert
                | Imap(authAppend, _inbox) -> yield! checkedRole authAppend Roles.imapAppend
            }
            |> Seq.map (fun (``type``, value) ->
                Claim(``type``, value, ClaimValueTypes.String, context.Options.ClaimsIssuer))

        context.Principal <- ClaimsPrincipal(ClaimsIdentity(claims, context.Scheme.Name))
        context.Success()

    Task.CompletedTask

let configureServices (config: ServeConfig) (services: IServiceCollection) =
    let authEvents = BasicAuthenticationEvents()
    authEvents.OnValidateCredentials <- validateCredentials

    services
        .AddAuthentication(BasicAuthenticationDefaults.AuthenticationScheme)
        .AddBasic(fun options ->
            options.Realm <- Realms.configured
            options.Events <- authEvents
            ())
    |> ignore

    services
        .AddSingleton<ServeConfig>(config)
        .AddSingleton<FluidParser>(FluidParser())
        .AddSingleton<TemplateOptions>(createTemplateOptions ())
        .AddGiraffe()
    |> ignore

let configureWebHost (config: ServeConfig) (builder: IWebHostBuilder) =
    builder.UseUrls(List.toArray config.HttpUrls.Value) |> ignore

let serveHttpAsync (config: ServeConfig) =
    task {
        let builder = WebApplication.CreateBuilder()
        configureServices config builder.Services
        configureWebHost config builder.WebHost

        let app = builder.Build()
        app.UseAuthentication() |> ignore
        app.UseGiraffe webApp

        return! app.RunAsync()
    }
