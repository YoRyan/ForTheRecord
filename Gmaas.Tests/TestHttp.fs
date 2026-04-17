module Tests.Http

open System
open System.Collections.Generic
open System.Net
open System.Net.Http
open System.Text
open System.Threading.Tasks

open Giraffe
open Google.Apis.Gmail.v1
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.TestHost
open Microsoft.Extensions.Hosting
open Xunit

open Gmaas.Config
open Gmaas.Gmail
open Gmaas.Helpers
open Gmaas.Http

type private MockGmailFs() =
    member val CalledMessage: Message option = None with get, set

    interface IGmailFs with
        member this.Import(msg: Message) : Task<Data.Message> =
            this.CalledMessage <- Some msg
            let whatever = Data.Message()
            Task.FromResult whatever

let private getTestApp (config: ServeConfig) =
    let builder = WebApplication.CreateBuilder()
    builder.WebHost.UseTestServer() |> ignore
    configureServices config builder.Services

    let app = builder.Build()
    app.UseGiraffe webApp
    app.Start()
    app

let private testRequest (config: ServeConfig) (request: HttpRequestMessage) =
    let resp =
        task {
            use server = getTestApp(config).GetTestServer()
            use client = server.CreateClient()
            let! response = request |> client.SendAsync
            return response
        }

    resp.Result


let private mockWithoutAuth () =
    let mock = MockGmailFs()

    let config =
        { Htpasswd = None
          AuthGmailInsert = Set.empty
          AuthGmailSend = Set.empty
          AppriseMiddleware = []
          ShoutrrrMiddleware = []
          HttpAddress = ""
          Gmail = mock }

    config, mock

let private makeContent (mime: string) (s: string) =
    let content = new ByteArrayContent(Encoding.UTF8.GetBytes s)
    content.Headers.ContentType <- Headers.MediaTypeHeaderValue mime
    content

let private makeTextContent = makeContent "text/plain"

[<Fact>]
let ``Easy curl import works`` () =
    let config, mock = mockWithoutAuth ()

    use content =
        makeTextContent
            "Lorem ipsum dolor sit amet, consectetur adipiscing elit, sed do eiusmod tempor incididunt ut labore et dolore magna aliqua."

    let request = new HttpRequestMessage(HttpMethod.Post, "/api/messages/import/ez")
    request.Content <- content

    let response = testRequest config request
    Assert.Equal(HttpStatusCode.OK, response.StatusCode)

    Assert.Equal(
        "Lorem ipsum dolor sit amet, consectetur adipiscing elit, sed do eiusmod tempor incididunt ut labore et dolore magna aliqua.",
        match mock.CalledMessage.Value.Body with
        | SinglePart s -> s
        | MultiPart(_ct, c, _a) -> c
    )

    let called = mock.CalledMessage.Value
    Assert.Equivalent([ "text/plain" ], getHeader "Content-Type" called.Headers)

[<Fact>]
let ``Easy curl import passes through headers`` () =
    let config, mock = mockWithoutAuth ()
    let request = new HttpRequestMessage(HttpMethod.Post, "/api/messages/import/ez")
    request.Headers.Add("To", "bob@example.com")
    request.Headers.Add("Subject", "Hello, World!")

    let response = testRequest config request
    Assert.Equal(HttpStatusCode.OK, response.StatusCode)

    let called = mock.CalledMessage.Value
    Assert.Equivalent([ "bob@example.com" ], getHeader "To" called.Headers)
    Assert.Equivalent([ "Hello, World!" ], getHeader "Subject" called.Headers)

[<Fact>]
let ``Easy curl import passes through Content-Type`` () =
    let config, mock = mockWithoutAuth ()
    let request = new HttpRequestMessage(HttpMethod.Post, "/api/messages/import/ez")
    request.Headers.Add("To", "bob@example.com")

    use content = makeTextContent "Now we have <strong>HTML</strong>!"
    content.Headers.ContentType <- Headers.MediaTypeHeaderValue "text/html"
    request.Content <- content

    let response = testRequest config request
    Assert.Equal(HttpStatusCode.OK, response.StatusCode)

    let called = mock.CalledMessage.Value
    Assert.Equivalent([ "text/html" ], getHeader "Content-Type" called.Headers)

[<Fact>]
let ``Curl import with application/x-www-form-urlencoded works`` () =
    let config, mock = mockWithoutAuth ()

    use content =
        new FormUrlEncodedContent(
            seq {
                "body",
                "Lorem ipsum dolor sit amet, consectetur adipiscing elit, sed do eiusmod tempor incididunt ut labore et dolore magna aliqua."

                "labelid", "INBOX"
                "labelid", "STARRED"
                "internaldatesource", "dateheader"
                "nevermarkspam", "true"
                "processforcalendar", "false"
            }
            |> Seq.map KeyValuePair
        )

    let request = new HttpRequestMessage(HttpMethod.Post, "/api/messages/import")
    request.Content <- content

    let response = testRequest config request
    Assert.Equal(HttpStatusCode.OK, response.StatusCode)

    let called = mock.CalledMessage.Value
    Assert.Equivalent(Some [ "INBOX"; "STARRED" ], called.LabelIds)
    Assert.Equivalent(Some InternalDateSourceEnum.DateHeader, called.InternalDateSource)
    Assert.Equivalent(Some true, called.NeverMarkSpam)
    Assert.Equivalent(Some false, called.ProcessForCalendar)
    Assert.Equivalent(None, called.Deleted)

    Assert.Equivalent(
        MultiPart(
            "text/plain",
            "Lorem ipsum dolor sit amet, consectetur adipiscing elit, sed do eiusmod tempor incididunt ut labore et dolore magna aliqua.",
            []
        ),
        called.Body
    )

[<Fact>]
let ``Curl import with application/x-www-form-urlencoded works with non-plain body`` () =
    let config, mock = mockWithoutAuth ()

    use content =
        new FormUrlEncodedContent(
            seq {
                "body", "Hello, <em>World!</em>"
                "bodytype", "text/html"
            }
            |> Seq.map KeyValuePair
        )

    let request = new HttpRequestMessage(HttpMethod.Post, "/api/messages/import")
    request.Content <- content

    let response = testRequest config request
    Assert.Equal(HttpStatusCode.OK, response.StatusCode)

    let called = mock.CalledMessage.Value
    Assert.Equivalent(MultiPart("text/html", "Hello, <em>World!</em>", []), called.Body)

[<Fact>]
let ``Curl import with multipart/form-data works`` () =
    let config, mock = mockWithoutAuth ()

    use content = new MultipartFormDataContent()

    for (k, v) in
        seq {
            "body",
            "Lorem ipsum dolor sit amet, consectetur adipiscing elit, sed do eiusmod tempor incididunt ut labore et dolore magna aliqua."

            "labelid", "INBOX"
            "labelid", "STARRED"
            "internaldatesource", "dateheader"
            "nevermarkspam", "true"
            "processforcalendar", "false"
        } do
        content.Add(makeTextContent v, k)

    let request = new HttpRequestMessage(HttpMethod.Post, "/api/messages/import")
    request.Content <- content

    let response = testRequest config request
    Assert.Equal(HttpStatusCode.OK, response.StatusCode)

    let called = mock.CalledMessage.Value
    Assert.Equivalent(Some [ "INBOX"; "STARRED" ], called.LabelIds)
    Assert.Equivalent(Some InternalDateSourceEnum.DateHeader, called.InternalDateSource)
    Assert.Equivalent(Some true, called.NeverMarkSpam)
    Assert.Equivalent(Some false, called.ProcessForCalendar)
    Assert.Equivalent(None, called.Deleted)

    Assert.Equivalent(
        MultiPart(
            "text/plain",
            "Lorem ipsum dolor sit amet, consectetur adipiscing elit, sed do eiusmod tempor incididunt ut labore et dolore magna aliqua.",
            []
        ),
        called.Body
    )

[<Fact>]
let ``Curl import with multipart/form-data works with attachments`` () =
    let config, mock = mockWithoutAuth ()

    use content = new MultipartFormDataContent()
    content.Add(makeTextContent "Hello, World!", "body")
    content.Add(makeContent "application/json" "{\"hello\": \"world\"}", "upload", "hello.json")
    content.Add(makeContent "text/html" "<p>hello <strong>world</strong></p>", "upload2", "hello.html")

    let request = new HttpRequestMessage(HttpMethod.Post, "/api/messages/import")
    request.Content <- content

    let response = testRequest config request
    Assert.Equal(HttpStatusCode.OK, response.StatusCode)

    let called = mock.CalledMessage.Value

    Assert.Equivalent(
        MultiPart(
            "text/plain",
            "Hello, World!",
            [ { ContentType = "application/json"
                Filename = "hello.json"
                Base64 = "{\"hello\": \"world\"}" |> Encoding.UTF8.GetBytes |> Convert.ToBase64String }
              { ContentType = "text/html"
                Filename = "hello.html"
                Base64 =
                  "<p>hello <strong>world</strong></p>"
                  |> Encoding.UTF8.GetBytes
                  |> Convert.ToBase64String } ]
        ),
        called.Body
    )

[<Fact>]
let ``Curl import with multipart/form-data works (sort of) with non-plain body`` () =
    let config, mock = mockWithoutAuth ()

    use content = new MultipartFormDataContent()
    content.Add(makeContent "text/html" "Hello, <em>World!</em>", "body")

    let request = new HttpRequestMessage(HttpMethod.Post, "/api/messages/import")
    request.Content <- content

    let response = testRequest config request
    Assert.Equal(HttpStatusCode.OK, response.StatusCode)

    let called = mock.CalledMessage.Value
    // This would ideally be text/html as originally submitted, but due to
    // limitations in ASP.NET's form-parsing API, that would require a custom
    // parser like https://andrewlock.net/reading-json-and-binary-data-from-multipart-form-data-sections-in-aspnetcore/.
    // Too much work--for now we're not concerned with this niche case.
    Assert.Equivalent(MultiPart("text/plain", "Hello, <em>World!</em>", []), called.Body)

[<Fact>]
let ``Curl import passes through headers`` () =
    let config, mock = mockWithoutAuth ()

    use content = new MultipartFormDataContent()
    content.Add(makeTextContent "Hello, World!", "body")

    let request = new HttpRequestMessage(HttpMethod.Post, "/api/messages/import")
    request.Content <- content
    request.Headers.Add("To", "bob@example.com")
    request.Headers.Add("Subject", "Hello, World!")

    let response = testRequest config request
    Assert.Equal(HttpStatusCode.OK, response.StatusCode)

    let called = mock.CalledMessage.Value
    Assert.Equivalent([ "bob@example.com" ], getHeader "To" called.Headers)
    Assert.Equivalent([ "Hello, World!" ], getHeader "Subject" called.Headers)
