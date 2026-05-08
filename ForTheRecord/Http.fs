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
open ForTheRecord.Liquid

module private Realms =
    [<Literal>]
    let configured = "Config"

module private Roles =
    [<Literal>]
    let gmailInsert = "GmailInsert"

    [<Literal>]
    let gmailSend = "GmailSend"

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

let private gmailImportHandlerSimple =
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

            use stream = new MemoryStream()
            do! message.WriteToAsync stream
            stream.Seek(0, SeekOrigin.Begin) |> ignore

            do! (getGmailInbox config).Import stream

            return Some ctx
        })

[<CLIMutable>]
type ImportForm =
    { LabelId: string list option
      Body: string option
      BodyType: string option
      InternalDateSource: string option
      NeverMarkSpam: bool option
      ProcessForCalendar: bool option
      Deleted: bool option }

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

let private gmailImportHandler =
    handleContext (fun ctx ->
        task {
            let config = ctx.GetService<ServeConfig>()
            let! form = ctx.BindFormAsync<ImportForm>()
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
                multipart.Add(entity)

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

let private requiresGmail =
    fun (next: HttpFunc) (ctx: HttpContext) ->
        let config = ctx.GetService<ServeConfig>()

        (if config.Inbox.IsGmail then
             id
         else
             RequestErrors.GONE "Gmail not configured")
            next
            ctx

let private requiresImap =
    fun (next: HttpFunc) (ctx: HttpContext) ->
        let config = ctx.GetService<ServeConfig>()

        (if config.Inbox.IsImap then
             id
         else
             RequestErrors.GONE "IMAP not configured")
            next
            ctx

let private requiresRole role =
    fun (next: HttpFunc) (ctx: HttpContext) ->
        let config = ctx.GetService<ServeConfig>()

        (if config.Htpasswd.IsSome then
             requiresAuthentication (RequestErrors.UNAUTHORIZED "Basic" Realms.configured "Authentication failed")
             >=> requiresRole role (RequestErrors.FORBIDDEN "Required scope not granted")
         else
             id)
            next
            ctx

let private requiresImportRole =
    fun (next: HttpFunc) (ctx: HttpContext) ->
        let config = ctx.GetService<ServeConfig>()

        requiresRole
            (match config.Inbox with
             | Gmail _ -> Roles.gmailInsert
             | Imap _ -> Roles.imapAppend)
            next
            ctx

let private requiresJson =
    hasContentType
        "application/json"
        { InvalidHeaderValue = None
          HeaderNotFound = None }

let private importWholeMessage (model: obj, modelKey: string option) (template: string) =
    handleContext (fun ctx ->
        task {
            let config = ctx.GetService<ServeConfig>()
            let parser = ctx.GetService<FluidParser>()

            let ok, template, error = parser.TryParse template

            if not ok then
                failwithf "Failed to parse template: %s" error

            let ftr =
                seq<string * obj> {
                    "user", authenticatedUser ctx |> Option.defaultValue null :> obj
                    "is_gmail", config.Inbox.IsGmail
                    "is_imap", config.Inbox.IsImap
                    "guid", Guid.NewGuid().ToString()
                }
                |> dict

            let options = ctx.GetService<TemplateOptions>()

            let context =
                TemplateContext(
                    seq<string * obj> {
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
                | Gmail(_, _, inbox) -> importWholeMessageToGmail inbox stream
                | Imap(_, inbox) -> failwith "Not Implemented"

            return Some ctx
        })

type private TemplateRequest =
    | AsConfigured of name: string
    | BuiltIn of fileName: string

let private resolveTemplateForImport (request: TemplateRequest) (importerWithModel: Lazy<string -> HttpHandler>) =
    fun (next: HttpFunc) (ctx: HttpContext) ->
        task {
            let config = ctx.GetService<ServeConfig>()

            match request with
            | AsConfigured name ->
                return!
                    (match config.Templates.TryGetValue name with
                     | true, template -> importerWithModel.Force () template
                     | false, _ -> text "Unknown template specified" |> unprocessableEntity)
                        next
                        ctx
            | BuiltIn fileName ->
                let! template = readEmbeddedText fileName
                return! importerWithModel.Force () template next ctx
        }

let private resolveTemplateForImportTask
    (request: TemplateRequest)
    (importerWithModel: Lazy<Task<string -> HttpHandler>>)
    =
    fun (next: HttpFunc) (ctx: HttpContext) ->
        task {
            let config = ctx.GetService<ServeConfig>()

            match request with
            | AsConfigured name ->
                match config.Templates.TryGetValue name with
                | true, template ->
                    let! importer = importerWithModel.Force()
                    return! importer template next ctx
                | false, _ -> return! unprocessableEntity (text "Unknown template specified") next ctx
            | BuiltIn fileName ->
                let! template = readEmbeddedText fileName
                let! importer = importerWithModel.Force()
                return! importer template next ctx
        }

let private genericJsonHandler (builtInTemplate: string) : HttpHandler =
    fun (next: HttpFunc) (ctx: HttpContext) ->
        task {
            let! json = ctx.BindJsonAsync<JsonElement>()

            let request =
                json
                |> tryJsonProperty "ftr_template"
                |> Option.bind tryJsonString
                |> function
                    | Some name -> AsConfigured name
                    | None -> BuiltIn builtInTemplate

            let importer =
                lazy
                    (let model = jsonLiquidModel json
                     importWholeMessage (model, Some "json"))

            return! resolveTemplateForImport request importer next ctx
        }

let private ntfyHandler (templateName: string option) =
    fun (next: HttpFunc) (ctx: HttpContext) ->
        task {
            let! json = ctx.BindJsonAsync<JsonElement>()

            match json |> tryJsonProperty "topic" |> Option.bind tryJsonString with
            | Some topic ->
                let request =
                    templateName
                    |> function
                        | Some name -> AsConfigured name
                        | None -> BuiltIn "Liquid.Ntfy.liquid"

                let importer =
                    lazy
                        (let model = jsonLiquidModel json
                         importWholeMessage (model, None))

                let response =
                    {| topic = topic
                       event = "message"
                       message = "triggered"
                       id = Guid.NewGuid().ToString()
                       time = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
                       expires = Int32.MaxValue |}
                    |> JsonSerializer.Serialize
                    |> text
                    |> compose (setContentType "application/json")

                let handler = resolveTemplateForImport request importer >=> response
                return! handler next ctx
            | None -> return! badRequest (text "Missing Ntfy topic") next ctx
        }

let private shoutrrrHandler =
    fun (next: HttpFunc) (ctx: HttpContext) ->
        task {
            let request =
                ctx.Request.Query.TryGetValue "ftr_template"
                |> tryGetByref
                |> function
                    | Some sv -> AsConfigured(Seq.last sv)
                    | None -> BuiltIn "Liquid.Shoutrrr.liquid"

            let importer =
                lazy
                    (task {
                        let! body = ctx.ReadBodyFromRequestAsync()
                        let model = seq<string * obj> { "message", body } |> dict
                        return importWholeMessage (model, None)
                    })

            return! resolveTemplateForImportTask request importer next ctx
        }

let gmailApiRoutes =
    requiresRole Roles.gmailInsert
    >=> requiresGmail
    >=> choose
            [ route "/api/gmail/messages/import/ez" >=> gmailImportHandlerSimple
              route "/api/gmail/messages/import" >=> gmailImportHandler ]

let webhookRoutes =
    requiresImportRole
    >=> requiresJson
    >=> choose [ route "/api/webhook"; route "/go/notify" ]
    >=> genericJsonHandler "Liquid.Webhook.liquid"

let appriseRoutes =
    requiresImportRole
    >=> requiresJson
    >=> route "/apprise"
    >=> genericJsonHandler "Liquid.Apprise.liquid"

let ntfyRoutes =
    requiresImportRole
    >=> choose
            [ routex "^/ntfy/?$" >=> ntfyHandler None
              routexp "^/ntfy_template/([^/]+)/?$" (fun capture ->
                  let capture = List.ofSeq capture
                  ntfyHandler (Some capture[1])) ]

let shoutrrrRoutes =
    requiresImportRole
    >=> choose
            [ route "/shoutrrr" >=> shoutrrrHandler
              route "/shoutrrr/json"
              >=> requiresJson
              >=> genericJsonHandler "Liquid.Webhook.liquid" ]

let webApp =
    choose
        [ POST
          >=> choose [ gmailApiRoutes; webhookRoutes; appriseRoutes; ntfyRoutes; shoutrrrRoutes ]
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
                | Gmail(authInsert, authSend, _inbox) ->
                    yield! checkedRole authInsert Roles.gmailInsert
                    yield! checkedRole authSend Roles.gmailSend
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
    match config.HttpUrls with
    | Some urls -> builder.UseUrls(List.toArray urls) |> ignore
    | None -> ()

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
