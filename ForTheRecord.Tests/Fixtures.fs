module Tests.Fixtures

open System
open System.IO
open System.Net.Http
open System.Text
open System.Threading.Tasks

open Giraffe
open Meziantou.Framework.Http
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.TestHost
open Microsoft.Extensions.Hosting
open Microsoft.Extensions.Logging
open MailKit
open MimeKit

open ForTheRecord.Config
open ForTheRecord.Gmail
open ForTheRecord.Http
open ForTheRecord.Imap
open ForTheRecord.Smtp

type MockGmailInbox() =
    member val CalledImport: {| Message: MimeMessage
                                LabelIds: Set<string> option
                                InternalDateSource: InternalDateSourceEnum option
                                NeverMarkSpam: bool option
                                ProcessForCalendar: bool option
                                Deleted: bool option |} option = None with get, set

    interface IGmailInbox with
        member this.Import
            (
                message: Stream,
                ?labelIds: string list,
                ?internalDateSource: InternalDateSourceEnum,
                ?neverMarkSpam: bool,
                ?processForCalendar: bool,
                ?deleted: bool
            ) =
            this.CalledImport <-
                Some
                    {| Message = MimeMessage.Load message
                       LabelIds = labelIds |> Option.map Set.ofList
                       InternalDateSource = internalDateSource
                       NeverMarkSpam = neverMarkSpam
                       ProcessForCalendar = processForCalendar
                       Deleted = deleted |}

            Task.FromResult()

type MockImapInbox() =
    member val CalledAppends: {| Message: MimeMessage
                                 Folder: string option
                                 Flags: MessageFlags option
                                 Keywords: string list option
                                 Date: DateTime option |} list = [] with get, set

    interface IImapInbox with
        member this.Append
            (message: MimeMessage, ?folder: string, ?flags: MessageFlags, ?keywords: string list, ?date: DateTime)
            =
            // Need to copy the original message, which may get disposed.
            use stream = new MemoryStream()
            message.WriteTo stream
            stream.Seek(0, SeekOrigin.Begin) |> ignore
            let copy = MimeMessage.Load stream

            this.CalledAppends <-
                this.CalledAppends
                @ [ {| Message = copy
                       Folder = folder
                       Flags = flags
                       Keywords = keywords
                       Date = date |} ]

            Task.FromResult()

let private getTestApp (config: ServeConfig) =
    let builder = WebApplication.CreateBuilder()
    builder.WebHost.UseTestServer() |> ignore
    configureServices config builder.Services

    let app = builder.Build()
    app.UseGiraffe webApp
    app.Start()
    app

let testRequest (config: ServeConfig) (request: HttpRequestMessage) =
    task {
        use server = config |> getTestApp |> _.GetTestServer()
        use client = server.CreateClient()
        let! response = request |> client.SendAsync
        return response
    }
    |> Async.AwaitTask
    |> Async.RunSynchronously

let mockGmailWithoutAuth () =
    let mock = MockGmailInbox()

    let config =
        { LogLevel = LogLevel.None
          Htpasswd = None
          HttpUrls = None
          SmtpUrls = None
          Templates = Map.empty
          Inbox = Gmail(Set.empty, mock) }

    config, mock

let mockGmailWithHunter2Auth (user: string) (hasInsert: bool) =
    let mock = MockGmailInbox()

    let authSet yay =
        if yay then Set(Seq.singleton user) else Set.empty

    let config =
        { LogLevel = LogLevel.None
          Htpasswd = Some(HtpasswdFile.Parse $"{user}:$apr1$nKTVHFsh$8gVerNz4iYOp211EbpBpJ0\n")
          HttpUrls = None
          SmtpUrls = None
          Templates = Map.empty
          Inbox = Gmail(authSet hasInsert, mock) }

    config, mock

let mockImapWithoutAuth () =
    let mock = MockImapInbox()

    let config =
        { LogLevel = LogLevel.None
          Htpasswd = None
          HttpUrls = None
          SmtpUrls = None
          Templates = Map.empty
          Inbox = Imap(Set.empty, mock) }

    config, mock

let mockImapWithHunter2Auth (user: string) (hasInsert: bool) =
    let mock = MockImapInbox()

    let authSet yay =
        if yay then Set(Seq.singleton user) else Set.empty

    let config =
        { LogLevel = LogLevel.None
          Htpasswd = Some(HtpasswdFile.Parse $"{user}:$apr1$nKTVHFsh$8gVerNz4iYOp211EbpBpJ0\n")
          HttpUrls = None
          SmtpUrls = None
          Templates = Map.empty
          Inbox = Imap(authSet hasInsert, mock) }

    config, mock

let testSmtpServer (config: ServeConfig) (cancel: Threading.CancellationToken) =
    task {
        let runServer = serveTestSmtpAsync config cancel
        let mutable success = false

        while not success do
            let client = new Net.Smtp.SmtpClient()

            try
                do! client.ConnectAsync("localhost", testSmtpPort, cancellationToken = cancel)
                success <- true
            with _ ->
                do! Task.Delay 100

        return runServer
    }

let makeBasicAuth (user: string) (pass: string) =
    Headers.AuthenticationHeaderValue("Basic", $"{user}:{pass}" |> Encoding.UTF8.GetBytes |> Convert.ToBase64String)

let makeContent (mime: string) (s: string) =
    let content = new ByteArrayContent(Encoding.UTF8.GetBytes s)
    content.Headers.ContentType <- Headers.MediaTypeHeaderValue mime
    content

let makeTextContent = makeContent "text/plain"

let makeJsonContent (o: obj) =
    makeContent "application/json" (System.Text.Json.JsonSerializer.Serialize o)

let readEntity (e: MimeEntity) =
    use stream = new MemoryStream()
    e.WriteTo stream
    stream.ToArray()

let entityContainsBase64Of (e: MimeEntity) (data: byte array) =
    let s = e |> readEntity |> Encoding.UTF8.GetString |> _.Replace("\n", "")
    let b64 = Convert.ToBase64String data
    Xunit.Assert.Contains(b64, s)

let downloadBytes (url: string) =
    task {
        let! response = ForTheRecord.Helpers.httpClient.GetAsync url
        return! response.Content.ReadAsByteArrayAsync()
    }
    |> Async.AwaitTask
    |> Async.RunSynchronously

let internetAddress (addresses: string seq) =
    Seq.map (fun (a: string) -> InternetAddress.Parse a) addresses

let makeMimeEntity (mediaType: string, mediaSubtype: string) (content: string) =
    task {
        use stream = new MemoryStream(Encoding.UTF8.GetBytes content)
        let contentType = ContentType(mediaType, mediaSubtype)
        return! MimePart.LoadAsync(contentType, stream)
    }
