module Tests.Gmail

open System
open System.Collections.Generic
open System.IO
open System.Net
open System.Net.Http
open System.Text

open MimeKit.Text
open Xunit

open ForTheRecord.Config
open ForTheRecord.Gmail

open Fixtures

[<Fact>]
let ``Endpoints not available when Gmail is not configured`` () =
    let mock = MockImapInbox()

    let config =
        { Htpasswd = None
          HttpUrls = None
          AppriseTemplates = Map.empty
          Inbox = Imap(Set.empty, mock) }

    let request =
        new HttpRequestMessage(HttpMethod.Post, "/api/gmail/messages/import/ez")

    let response = testRequest config request
    Assert.Equal(HttpStatusCode.Gone, response.StatusCode)

    use content = new FormUrlEncodedContent(seq { "body", "" } |> Seq.map KeyValuePair)
    let request = new HttpRequestMessage(HttpMethod.Post, "/api/gmail/messages/import")
    request.Content <- content
    let response = testRequest config request
    Assert.Equal(HttpStatusCode.Gone, response.StatusCode)

[<Fact>]
let ``Authenticated endpoints work when authentication is disabled`` () =
    let config, _ = mockGmailWithoutAuth ()

    let request =
        new HttpRequestMessage(HttpMethod.Post, "/api/gmail/messages/import/ez")

    let response = testRequest config request
    Assert.Equal(HttpStatusCode.OK, response.StatusCode)

    use content = new FormUrlEncodedContent(seq { "body", "" } |> Seq.map KeyValuePair)
    let request = new HttpRequestMessage(HttpMethod.Post, "/api/gmail/messages/import")
    request.Content <- content
    let response = testRequest config request
    Assert.Equal(HttpStatusCode.OK, response.StatusCode)

[<Fact>]
let ``Authenticated endpoints require authentication`` () =
    let config, _ = mockGmailWithHunter2Auth "AzureDiamond" true true

    let request =
        new HttpRequestMessage(HttpMethod.Post, "/api/gmail/messages/import/ez")

    let response = testRequest config request
    Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode)

    use content = new FormUrlEncodedContent(seq { "body", "" } |> Seq.map KeyValuePair)
    let request = new HttpRequestMessage(HttpMethod.Post, "/api/gmail/messages/import")
    request.Content <- content
    let response = testRequest config request
    Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode)

[<Fact>]
let ``Authenticated endpoints work with basic authentication`` () =
    let config, _ = mockGmailWithHunter2Auth "AzureDiamond" true true
    let authHeader = makeBasicAuth "AzureDiamond" "hunter2"

    let request =
        new HttpRequestMessage(HttpMethod.Post, "/api/gmail/messages/import/ez")

    request.Headers.Authorization <- authHeader
    let response = testRequest config request
    Assert.Equal(HttpStatusCode.OK, response.StatusCode)

    use content = new FormUrlEncodedContent(seq { "body", "" } |> Seq.map KeyValuePair)
    let request = new HttpRequestMessage(HttpMethod.Post, "/api/gmail/messages/import")
    request.Headers.Authorization <- authHeader
    request.Content <- content
    let response = testRequest config request
    Assert.Equal(HttpStatusCode.OK, response.StatusCode)

[<Fact>]
let ``Authenticated endpoints fail with bad authentication`` () =
    let config, _ = mockGmailWithHunter2Auth "AzureDiamond" true true
    let authHeader = makeBasicAuth "AzureDiamond" "whatever"

    let request =
        new HttpRequestMessage(HttpMethod.Post, "/api/gmail/messages/import/ez")

    request.Headers.Authorization <- authHeader
    let response = testRequest config request
    Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode)

    use content = new FormUrlEncodedContent(seq { "body", "" } |> Seq.map KeyValuePair)
    let request = new HttpRequestMessage(HttpMethod.Post, "/api/gmail/messages/import")
    request.Headers.Authorization <- authHeader
    request.Content <- content
    let response = testRequest config request
    Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode)

[<Fact>]
let ``Authenticated import endpoints require the insert scope`` () =
    let config, _ = mockGmailWithHunter2Auth "AzureDiamond" false true
    let authHeader = makeBasicAuth "AzureDiamond" "hunter2"

    let request =
        new HttpRequestMessage(HttpMethod.Post, "/api/gmail/messages/import/ez")

    request.Headers.Authorization <- authHeader
    let response = testRequest config request
    Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode)

    use content = new FormUrlEncodedContent(seq { "body", "" } |> Seq.map KeyValuePair)
    let request = new HttpRequestMessage(HttpMethod.Post, "/api/gmail/messages/import")
    request.Headers.Authorization <- authHeader
    request.Content <- content
    let response = testRequest config request
    Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode)

[<Fact>]
let ``Minimal import call produces a valid message`` () =
    let config, mock = mockGmailWithoutAuth ()

    let request =
        new HttpRequestMessage(HttpMethod.Post, "/api/gmail/messages/import/ez")

    let response = testRequest config request
    Assert.Equal(HttpStatusCode.OK, response.StatusCode)

    let called = mock.CalledImport.Value
    Assert.NotEqual(0, called.Message.From.Count)

[<Fact>]
let ``Easy curl import works`` () =
    let config, mock = mockGmailWithoutAuth ()

    use content =
        makeTextContent
            "Lorem ipsum dolor sit amet, consectetur adipiscing elit, sed do eiusmod tempor incididunt ut labore et dolore magna aliqua."

    let request =
        new HttpRequestMessage(HttpMethod.Post, "/api/gmail/messages/import/ez")

    request.Content <- content

    let response = testRequest config request
    Assert.Equal(HttpStatusCode.OK, response.StatusCode)

    let called = mock.CalledImport.Value

    Assert.Equal(
        "Lorem ipsum dolor sit amet, consectetur adipiscing elit, sed do eiusmod tempor incididunt ut labore et dolore magna aliqua.",
        TextFormat.Text |> called.Message.GetTextBody |> _.Trim()
    )

    Assert.Equal("text/plain", called.Message.Body.ContentType.MimeType)

[<Fact>]
let ``Easy curl import passes through headers`` () =
    let config, mock = mockGmailWithoutAuth ()

    let request =
        new HttpRequestMessage(HttpMethod.Post, "/api/gmail/messages/import/ez")

    request.Headers.Add("To", "bob@example.com")
    request.Headers.Add("Subject", "Hello, World!")

    let response = testRequest config request
    Assert.Equal(HttpStatusCode.OK, response.StatusCode)

    let called = mock.CalledImport.Value
    Assert.Equal("bob@example.com", called.Message.To.ToString())
    Assert.Equal("Hello, World!", called.Message.Subject)

[<Fact>]
let ``Easy curl import passes through Content-Type`` () =
    let config, mock = mockGmailWithoutAuth ()

    let request =
        new HttpRequestMessage(HttpMethod.Post, "/api/gmail/messages/import/ez")

    request.Headers.Add("To", "bob@example.com")

    use content = makeTextContent "Now we have <strong>HTML</strong>!"
    content.Headers.ContentType <- Headers.MediaTypeHeaderValue "text/html"
    request.Content <- content

    let response = testRequest config request
    Assert.Equal(HttpStatusCode.OK, response.StatusCode)

    let called = mock.CalledImport.Value
    Assert.Equal("text/html", called.Message.Body.ContentType.MimeType)

[<Fact>]
let ``Curl import with application/x-www-form-urlencoded works`` () =
    let config, mock = mockGmailWithoutAuth ()

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

    let request = new HttpRequestMessage(HttpMethod.Post, "/api/gmail/messages/import")
    request.Content <- content

    let response = testRequest config request
    Assert.Equal(HttpStatusCode.OK, response.StatusCode)

    let called = mock.CalledImport.Value
    Assert.Equivalent(Some [ "INBOX"; "STARRED" ], called.LabelIds)
    Assert.Equivalent(Some InternalDateSourceEnum.DateHeader, called.InternalDateSource)
    Assert.Equivalent(Some true, called.NeverMarkSpam)
    Assert.Equivalent(Some false, called.ProcessForCalendar)
    Assert.Equivalent(None, called.Deleted)

    Assert.Equal(
        "Lorem ipsum dolor sit amet, consectetur adipiscing elit, sed do eiusmod tempor incididunt ut labore et dolore magna aliqua.",
        TextFormat.Plain |> called.Message.GetTextBody |> _.Trim()
    )

[<Fact>]
let ``Curl import with application/x-www-form-urlencoded works with non-plain body`` () =
    let config, mock = mockGmailWithoutAuth ()

    use content =
        new FormUrlEncodedContent(
            seq {
                "body", "Hello, <em>World!</em>"
                "bodytype", "text/html"
            }
            |> Seq.map KeyValuePair
        )

    let request = new HttpRequestMessage(HttpMethod.Post, "/api/gmail/messages/import")
    request.Content <- content

    let response = testRequest config request
    Assert.Equal(HttpStatusCode.OK, response.StatusCode)

    let called = mock.CalledImport.Value
    Assert.Equal("Hello, <em>World!</em>", TextFormat.Html |> called.Message.GetTextBody |> _.Trim())

[<Fact>]
let ``Curl import with multipart/form-data works`` () =
    let config, mock = mockGmailWithoutAuth ()

    use content = new MultipartFormDataContent()

    for k, v in
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

    let request = new HttpRequestMessage(HttpMethod.Post, "/api/gmail/messages/import")
    request.Content <- content

    let response = testRequest config request
    Assert.Equal(HttpStatusCode.OK, response.StatusCode)

    let called = mock.CalledImport.Value
    Assert.Equivalent(Some [ "INBOX"; "STARRED" ], called.LabelIds)
    Assert.Equivalent(Some InternalDateSourceEnum.DateHeader, called.InternalDateSource)
    Assert.Equivalent(Some true, called.NeverMarkSpam)
    Assert.Equivalent(Some false, called.ProcessForCalendar)
    Assert.Equivalent(None, called.Deleted)

    Assert.Equal(
        "Lorem ipsum dolor sit amet, consectetur adipiscing elit, sed do eiusmod tempor incididunt ut labore et dolore magna aliqua.",
        TextFormat.Plain |> called.Message.GetTextBody |> _.Trim()
    )

[<Fact>]
let ``Curl import with multipart/form-data works with attachments`` () =
    let config, mock = mockGmailWithoutAuth ()

    use content = new MultipartFormDataContent()
    content.Add(makeTextContent "Hello, World!", "body")
    use jsonContent = makeJsonContent {| hello = "world" |}
    content.Add(jsonContent, "upload", "hello.json")
    content.Add(makeContent "text/html" "<p>hello <strong>world</strong></p>", "upload2", "hello.html")

    let request = new HttpRequestMessage(HttpMethod.Post, "/api/gmail/messages/import")
    request.Content <- content

    let response = testRequest config request
    Assert.Equal(HttpStatusCode.OK, response.StatusCode)

    let called = mock.CalledImport.Value
    Assert.Equal("Hello, World!", TextFormat.Plain |> called.Message.GetTextBody |> _.Trim())

    let attachments = mock.CalledImport.Value.Message.Attachments |> Seq.toList
    Assert.Equal(2, attachments.Length)
    Assert.Equal("application/json", attachments[0].ContentType.MimeType)
    Assert.Equal("hello.json", attachments[0].ContentType.Name)
    Assert.Equal("text/html", attachments[1].ContentType.MimeType)
    Assert.Equal("hello.html", attachments[1].ContentType.Name)

    Assert.Contains(
        jsonContent
        |> _.ReadAsStringAsync()
        |> Async.AwaitTask
        |> Async.RunSynchronously
        |> Encoding.UTF8.GetBytes
        |> Convert.ToBase64String,
        attachments[0] |> readEntity |> Encoding.UTF8.GetString
    )

    Assert.Contains(
        "<p>hello <strong>world</strong></p>"
        |> Encoding.UTF8.GetBytes
        |> Convert.ToBase64String,
        attachments[1] |> readEntity |> Encoding.UTF8.GetString
    )

[<Fact>]
let ``Curl import with multipart/form-data works (sort of) with non-plain body`` () =
    let config, mock = mockGmailWithoutAuth ()

    use content = new MultipartFormDataContent()
    content.Add(makeContent "text/html" "Hello, <em>World!</em>", "body")

    let request = new HttpRequestMessage(HttpMethod.Post, "/api/gmail/messages/import")
    request.Content <- content

    let response = testRequest config request
    Assert.Equal(HttpStatusCode.OK, response.StatusCode)

    let called = mock.CalledImport.Value
    // This would ideally be text/html as originally submitted, but due to
    // limitations in ASP.NET's form-parsing API, that would require a custom
    // parser like https://andrewlock.net/reading-json-and-binary-data-from-multipart-form-data-sections-in-aspnetcore/.
    // Too much work--for now we're not concerned with this niche case.
    Assert.Equal("Hello, <em>World!</em>", TextFormat.Text |> called.Message.GetTextBody |> _.Trim())

[<Fact>]
let ``Curl import passes through headers`` () =
    let config, mock = mockGmailWithoutAuth ()

    use content = new MultipartFormDataContent()
    content.Add(makeTextContent "Hello, World!", "body")

    let request = new HttpRequestMessage(HttpMethod.Post, "/api/gmail/messages/import")
    request.Content <- content
    request.Headers.Add("To", "bob@example.com")
    request.Headers.Add("Subject", "Hello, World!")

    let response = testRequest config request
    Assert.Equal(HttpStatusCode.OK, response.StatusCode)

    let called = mock.CalledImport.Value
    Assert.Equal("bob@example.com", called.Message.To.ToString())
    Assert.Equal("Hello, World!", called.Message.Subject)

[<Fact>]
let ``Whole message import works`` () =
    let _, mock = mockGmailWithoutAuth ()

    let message =
        """From: me
To: me
Subject: Hello, World!
Content-Type: text/plain

Lorem ipsum dolor sit amet, consectetur adipiscing elit, sed do eiusmod tempor incididunt ut labore et dolore magna aliqua.
"""

    use stream = new MemoryStream(Encoding.UTF8.GetBytes message)

    importWholeMessageToGmail mock stream
    |> Async.AwaitTask
    |> Async.RunSynchronously

    let called = mock.CalledImport.Value

    Assert.Equal(
        "Lorem ipsum dolor sit amet, consectetur adipiscing elit, sed do eiusmod tempor incididunt ut labore et dolore magna aliqua.",
        TextFormat.Text |> called.Message.GetTextBody |> _.Trim()
    )

    Assert.Equal("Hello, World!", called.Message.Subject)
    Assert.Equal("text/plain", called.Message.Body.ContentType.MimeType)

[<Fact>]
let ``Whole message import works with labels`` () =
    let _, mock = mockGmailWithoutAuth ()

    let message =
        """From: me
To: me
Subject: Hello, World!
X-FTR-Gmail-LabelID: INBOX
X-FTR-Gmail-LabelID: MyAwesomeLabel
X-FTR-Gmail-LabelID: MyAwesomeLabel2
Content-Type: text/plain

This is the label test message.
"""

    use stream = new MemoryStream(Encoding.UTF8.GetBytes message)

    importWholeMessageToGmail mock stream
    |> Async.AwaitTask
    |> Async.RunSynchronously

    let called = mock.CalledImport.Value

    let expected =
        seq {
            "INBOX"
            "MyAwesomeLabel"
            "MyAwesomeLabel2"
        }
        |> Set.ofSeq

    Assert.Equivalent(expected, called.LabelIds |> Option.defaultValue [] |> Set.ofList)
    Assert.Equal(None, called.InternalDateSource)
    Assert.Equal(None, called.NeverMarkSpam)
    Assert.Equal(None, called.ProcessForCalendar)
    Assert.Equal(None, called.Deleted)

[<Fact>]
let ``Whole message import works with flags`` () =
    let _, mock = mockGmailWithoutAuth ()

    let message =
        """From: me
To: me
Subject: Hello, World!
X-FTR-Gmail-InternalDateSource: dateheader
X-FTR-Gmail-NeverMarkSpam: true
X-FTR-Gmail-ProcessForCalendar: nonsensevalue
X-FTR-Gmail-Deleted: false
Content-Type: text/plain

This is the label test message.
"""

    use stream = new MemoryStream(Encoding.UTF8.GetBytes message)

    importWholeMessageToGmail mock stream
    |> Async.AwaitTask
    |> Async.RunSynchronously

    let called = mock.CalledImport.Value

    Assert.Equal(Some InternalDateSourceEnum.DateHeader, called.InternalDateSource)
    Assert.Equal(Some true, called.NeverMarkSpam)
    Assert.Equal(None, called.ProcessForCalendar)
    Assert.Equal(Some false, called.Deleted)
    Assert.Equivalent(Set.empty, called.LabelIds |> Option.defaultValue [] |> Set.ofList)
