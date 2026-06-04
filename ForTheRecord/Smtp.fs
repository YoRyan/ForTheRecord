module ForTheRecord.Smtp

open System
open System.Buffers
open System.IO.Pipelines
open System.Threading
open System.Net

open SmtpServer
open SmtpServer.Protocol

open ForTheRecord.Config
open ForTheRecord.Gmail
open ForTheRecord.Imap

[<Literal>]
let testSmtpPort = 12525

type private MessageStore(config: ServeConfig) =
    inherit Storage.MessageStore()

    override _.SaveAsync
        (
            context: ISessionContext,
            transaction: IMessageTransaction,
            buffer: ReadOnlySequence<byte>,
            cancellationToken: CancellationToken
        ) =
        task {
            use stream = PipeReader.Create(buffer).AsStream false

            match config.Inbox with
            | Gmail(_, inbox) -> do! importWholeMessageToGmail inbox stream
            | Imap(_, inbox) -> do! importWholeMessageToImap inbox stream

            return SmtpResponse.Ok
        }

type private UserAuthenticator(config: ServeConfig) =
    inherit Authentication.UserAuthenticator()

    override _.AuthenticateAsync
        (context: ISessionContext, user: string, password: string, cancellationToken: CancellationToken)
        =
        task {
            match config.Htpasswd with
            | Some htpasswd ->
                let credentialsOk = htpasswd.VerifyCredentials(user, password)

                let hasScope =
                    user
                    |> match config.Inbox with
                       | Gmail(authInsert, _) -> authInsert.Contains
                       | Imap(authAppend, _) -> authAppend.Contains

                return credentialsOk && hasScope
            | None -> return true
        }

    interface Authentication.IUserAuthenticator

let serveSmtpAsync (config: ServeConfig) =
    task {
        let builder = SmtpServerOptionsBuilder() |> _.ServerName("ForTheRecord")

        for url in config.SmtpUrls.Value do
            let uri = Uri url

            let endpoint =
                match uri.HostNameType with
                | UriHostNameType.IPv4
                | UriHostNameType.IPv6 ->
                    EndpointDefinitionBuilder()
                    |> _.Endpoint(IPEndPoint(IPAddress.Parse uri.Host, if uri.IsDefaultPort then 2525 else uri.Port))
                    |> _.AllowUnsecureAuthentication(true)
                    |> _.Build()
                | _ -> failwithf "Invalid SMTP listening address (must use an IP address for the host): %s" url

            builder.Endpoint endpoint |> ignore

        let provider = ComponentModel.ServiceProvider()
        provider.Add(MessageStore config)
        provider.Add(UserAuthenticator config)

        let server = SmtpServer.SmtpServer(builder.Build(), provider)
        return! server.StartAsync CancellationToken.None
    }

let serveTestSmtpAsync (config: ServeConfig) (cancel: CancellationToken) =
    task {
        let options =
            SmtpServerOptionsBuilder()
            |> _.ServerName("ForTheRecord")
            |> _.Endpoint(fun builder ->
                builder
                |> _.Port(testSmtpPort, false)
                |> _.AllowUnsecureAuthentication(true)
                |> ignore)
            |> _.Build()

        let provider = ComponentModel.ServiceProvider()
        provider.Add(MessageStore config)
        provider.Add(UserAuthenticator config)

        let server = SmtpServer.SmtpServer(options, provider)
        return! server.StartAsync cancel
    }
