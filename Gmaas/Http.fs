module Gmaas.Http

open System.Security.Claims
open System.Threading.Tasks

open Giraffe
open idunno.Authentication.Basic
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.Builder
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Primitives

open Gmaas.Config
open Gmaas.Gmail
open Gmaas.Helpers

module private Realms =
    [<Literal>]
    let configured = "Config"

module private Roles =
    [<Literal>]
    let gmailInsert = "GmailInsert"

    [<Literal>]
    let gmailSend = "GmailSend"

let private expandHeaders (sv: (string * StringValues) seq) =
    sv
    |> Seq.map (fun (k, sv) -> sv |> Seq.cast<string> |> Seq.map (fun s -> k, s))
    |> Seq.concat

let private ezImportHandler: HttpHandler =
    fun (next: HttpFunc) (ctx: HttpContext) ->
        task {
            let! body = ctx.ReadBodyFromRequestAsync()

            let headers =
                seq { "From", "gmaas" }
                |> overrideHeaders (ctx.Request.Headers |> Seq.map (|KeyValue|) |> expandHeaders |> List.ofSeq)
                |> overrideHeaders [ "Content-Type", "text/plain" ]
                |> List.ofSeq

            let! _ =
                importToGmail
                    (ctx.GetService<ServeConfig>().Gmail)
                    { LabelIds = None
                      Headers = headers
                      Body = SinglePart body
                      InternalDateSource = None
                      NeverMarkSpam = None
                      ProcessForCalendar = None
                      Deleted = None }

            return! next ctx
        }

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

let private webApp =
    choose
        [ route "/api/messages/import/ez"
          >=> requiresRole Roles.gmailInsert
          >=> ezImportHandler ]

let private validateCredentials (context: ValidateCredentialsContext) =
    let config = context.HttpContext.RequestServices.GetService<ServeConfig>()

    let verified =
        match config.Htpasswd with
        | Some htpasswd -> htpasswd.VerifyCredentials(context.Username, context.Password)
        | None -> false

    if verified then
        let claims =
            seq {
                ClaimTypes.NameIdentifier, context.Username
                ClaimTypes.Name, context.Username

                if config.AuthGmailInsert.Contains context.Username then
                    ClaimTypes.Role, Roles.gmailInsert

                if config.AuthGmailSend.Contains context.Username then
                    ClaimTypes.Role, Roles.gmailSend
            }
            |> Seq.map (fun (``type``, value) ->
                Claim(``type``, value, ClaimValueTypes.String, context.Options.ClaimsIssuer))

        context.Principal <- ClaimsPrincipal(ClaimsIdentity(claims, context.Scheme.Name))
        context.Success()

    Task.CompletedTask

let private configureServices (config: ServeConfig) (services: IServiceCollection) =
    let authEvents = BasicAuthenticationEvents()
    authEvents.OnValidateCredentials <- validateCredentials

    services
        .AddAuthentication(BasicAuthenticationDefaults.AuthenticationScheme)
        .AddBasic(fun options ->
            options.Realm <- Realms.configured
            options.Events <- authEvents
            ())
    |> ignore

    services.AddSingleton<ServeConfig>(config).AddGiraffe() |> ignore

let serveHttpAsync (config: ServeConfig) =
    task {
        let builder = WebApplication.CreateBuilder()
        configureServices config builder.Services

        let app = builder.Build()
        app.UseAuthentication() |> ignore
        app.UseGiraffe webApp

        return! app.RunAsync()
    }
