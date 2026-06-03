module Tests.Fixtures

open System
open System.IO
open System.Net.Http
open System.Net.Http.Headers
open System.Text
open System.Text.Json
open System.Threading.Tasks

open Giraffe
open Meziantou.Framework.Http
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.TestHost
open Microsoft.Extensions.Hosting
open MailKit
open MimeKit
open Xunit

open ForTheRecord.Config
open ForTheRecord.Gmail
open ForTheRecord.Helpers
open ForTheRecord.Http
open ForTheRecord.Imap

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

let getTestApp (config: ServeConfig) =
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
        { Htpasswd = None
          HttpUrls = None
          Templates = Map.empty
          Inbox = Gmail(Set.empty, mock) }

    config, mock

let mockGmailWithHunter2Auth (user: string) (hasInsert: bool) =
    let mock = MockGmailInbox()

    let authSet yay =
        if yay then Set(Seq.singleton user) else Set.empty

    let config =
        { Htpasswd = Some(HtpasswdFile.Parse $"{user}:$apr1$nKTVHFsh$8gVerNz4iYOp211EbpBpJ0\n")
          HttpUrls = None
          Templates = Map.empty
          Inbox = Gmail(authSet hasInsert, mock) }

    config, mock

let mockImapWithoutAuth () =
    let mock = MockImapInbox()

    let config =
        { Htpasswd = None
          HttpUrls = None
          Templates = Map.empty
          Inbox = Imap(Set.empty, mock) }

    config, mock

let mockImapWithHunter2Auth (user: string) (hasInsert: bool) =
    let mock = MockImapInbox()

    let authSet yay =
        if yay then Set(Seq.singleton user) else Set.empty

    let config =
        { Htpasswd = Some(HtpasswdFile.Parse $"{user}:$apr1$nKTVHFsh$8gVerNz4iYOp211EbpBpJ0\n")
          HttpUrls = None
          Templates = Map.empty
          Inbox = Imap(authSet hasInsert, mock) }

    config, mock

let makeBasicAuth (user: string) (pass: string) =
    AuthenticationHeaderValue("Basic", $"{user}:{pass}" |> Encoding.UTF8.GetBytes |> Convert.ToBase64String)

let makeContent (mime: string) (s: string) =
    let content = new ByteArrayContent(Encoding.UTF8.GetBytes s)
    content.Headers.ContentType <- Headers.MediaTypeHeaderValue mime
    content

let makeTextContent = makeContent "text/plain"

let makeJsonContent (o: obj) =
    makeContent "application/json" (JsonSerializer.Serialize o)

let readEntity (e: MimeEntity) =
    use stream = new MemoryStream()
    e.WriteTo stream
    stream.ToArray()

let entityContainsBase64Of (e: MimeEntity) (data: byte array) =
    let s = e |> readEntity |> Encoding.UTF8.GetString |> _.Replace("\n", "")
    let b64 = Convert.ToBase64String data
    Assert.Contains(b64, s)

let downloadBytes (url: string) =
    task {
        let! response = httpClient.GetAsync url
        return! response.Content.ReadAsByteArrayAsync()
    }
    |> Async.AwaitTask
    |> Async.RunSynchronously
