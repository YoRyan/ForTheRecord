module ForTheRecord.Smtp

open System
open System.Net
open System.Threading

open Microsoft.Extensions.Logging
open SmtpServer

open ForTheRecord.Config

[<Literal>]
let testSmtpPort = 12525

type private MessageStore(config: ServeConfig, logger: ILogger) =
    inherit Storage.MessageStore()

    override _.SaveAsync
        (
            context: ISessionContext,
            transaction: IMessageTransaction,
            buffer: Buffers.ReadOnlySequence<byte>,
            cancellationToken: CancellationToken
        ) =
        task {
            use stream = System.IO.Pipelines.PipeReader.Create(buffer).AsStream false

            try
                match config.Inbox with
                | Gmail(_, inbox) -> do! Gmail.importWholeMessageToGmail inbox stream
                | Imap(_, inbox) -> do! Imap.importWholeMessageToImap inbox stream

                return Protocol.SmtpResponse.Ok
            with ex ->
                logger.LogError(ex, "Exception when processing mail")
                return Protocol.SmtpResponse.TransactionFailed
        }

type private UserAuthenticator(config: ServeConfig, logger: ILogger) =
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

                match credentialsOk, hasScope with
                | true, true -> return true
                | true, false ->
                    logger.LogInformation(
                        "User {} does not have the appropriate permission granted to upload mail",
                        user,
                        password
                    )

                    return false
                | false, _ ->
                    logger.LogInformation("Rejecting unknown user {}, password {}", user, password)
                    return false
            | None -> return true
        }

    interface Authentication.IUserAuthenticator

let serveSmtpAsync (config: ServeConfig) =
    task {
        use loggerFactory = LoggerFactory.Create(configureLogging config)
        let logger = loggerFactory.CreateLogger "ForTheRecord.Smtp"

        let builder = SmtpServerOptionsBuilder().ServerName "ForTheRecord"

        for url in config.SmtpUrls.Value do
            let uri = Uri url

            let endpoint =
                match uri.HostNameType with
                | UriHostNameType.IPv4
                | UriHostNameType.IPv6 ->
                    EndpointDefinitionBuilder()
                        .Endpoint(IPEndPoint(IPAddress.Parse uri.Host, if uri.IsDefaultPort then 2525 else uri.Port))
                        .AllowUnsecureAuthentication(true)
                        .Build()
                | _ -> failwithf "Invalid SMTP listening address (must use an IP address for the host): %s" url

            builder.Endpoint endpoint |> ignore

        let provider = ComponentModel.ServiceProvider()
        provider.Add(MessageStore(config, logger))
        provider.Add(UserAuthenticator(config, logger))

        let server = SmtpServer.SmtpServer(builder.Build(), provider)
        return! server.StartAsync CancellationToken.None
    }

let serveTestSmtpAsync (config: ServeConfig) (cancel: CancellationToken) =
    task {
        let logger = Abstractions.NullLogger.Instance

        let options =
            SmtpServerOptionsBuilder()
                .ServerName("ForTheRecord")
                .Endpoint(fun builder ->
                    builder
                    |> _.Port(testSmtpPort, false)
                    |> _.AllowUnsecureAuthentication(true)
                    |> ignore)
                .Build()

        let provider = ComponentModel.ServiceProvider()
        provider.Add(MessageStore(config, logger))
        provider.Add(UserAuthenticator(config, logger))

        let server = SmtpServer.SmtpServer(options, provider)
        return! server.StartAsync cancel
    }
