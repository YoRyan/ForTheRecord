module Tests.Webhook

open System.Net
open System.Net.Http
open System.Text

open MailKit
open Microsoft.Extensions.Logging
open Xunit

open ForTheRecord.Config

open Fixtures

[<Theory>]
[<InlineData("/api/webhook")>]
[<InlineData("/go/notify")>]
[<InlineData("/apprise")>]
[<InlineData("/shoutrrr/json")>]
[<InlineData("/ntfy")>]
[<InlineData("/ntfy_template/test")>]
let ``JSON import requires Json request content`` (uri: string) =
    let config, _ = mockGmailWithoutAuth ()

    let request = new HttpRequestMessage(HttpMethod.Post, uri)
    use content = makeTextContent "Definitely not JSON!"
    request.Content <- content

    let response = testRequest config request
    Assert.Equal(HttpStatusCode.NotAcceptable, response.StatusCode)

[<Theory>]
[<InlineData("/api/webhook")>]
[<InlineData("/go/notify")>]
[<InlineData("/apprise")>]
[<InlineData("/shoutrrr/json")>]
[<InlineData("/ntfy")>]
[<InlineData("/ntfy_template/test")>]
let ``JSON import requires Gmail insert scope when Gmail is configured`` (uri: string) =
    let config, _ = mockGmailWithHunter2Auth "AzureDiamond" false
    let authHeader = makeBasicAuth "AzureDiamond" "hunter2"

    let request = new HttpRequestMessage(HttpMethod.Post, uri)
    request.Headers.Authorization <- authHeader

    use content =
        {| title = "Title"
           message = "Whatever"
           ``type`` = "info" |}
        |> makeJsonContent

    request.Content <- content

    let response = testRequest config request
    Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode)

[<Theory>]
[<InlineData("/api/webhook")>]
[<InlineData("/go/notify")>]
[<InlineData("/apprise")>]
[<InlineData("/shoutrrr/json")>]
[<InlineData("/ntfy")>]
[<InlineData("/ntfy_template/test")>]
let ``JSON import requires IMAP insert scope when IMAP is configured`` (uri: string) =
    let config, _ = mockImapWithHunter2Auth "AzureDiamond" false
    let authHeader = makeBasicAuth "AzureDiamond" "hunter2"

    let request = new HttpRequestMessage(HttpMethod.Post, uri)
    request.Headers.Authorization <- authHeader

    use content =
        {| title = "Title"
           message = "Whatever"
           ``type`` = "info" |}
        |> makeJsonContent

    request.Content <- content

    let response = testRequest config request
    Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode)

[<Fact>]
let ``JSON import handles custom template with json key`` () =
    let mock = MockGmailInbox()

    let config =
        { LogLevel = LogLevel.None
          Htpasswd = None
          HttpUrls = None
          SmtpUrls = None
          Templates =
            Map(
                seq {
                    "test",
                    """From: me
To: me
Subject: Test Subject

{{ key1 }} {{ json.key2 }} {{ key3.value }} {{ json.key4.value }}
"""
                }
            )
          Inbox = Gmail(Set.empty, mock) }

    let request = new HttpRequestMessage(HttpMethod.Post, "/api/webhook")

    use content =
        {| key1 = "one"
           key2 = "two"
           key3 = {| value = "three" |}
           key4 = {| value = "four" |}
           ftr_template = "test" |}
        |> makeJsonContent

    request.Content <- content

    let response = testRequest config request
    Assert.Equal(HttpStatusCode.OK, response.StatusCode)

    let called = mock.CalledImport.Value
    Assert.Equal("Test Subject", called.Message.Subject)

    let body =
        called.Message.BodyParts
        |> Seq.exactlyOne
        |> readEntity
        |> Encoding.UTF8.GetString

    Assert.Contains("one two three four", body)

[<Fact>]
let ``JSON import handles custom template with reference to user`` () =
    let config, mock = mockGmailWithHunter2Auth "AzureDiamond" true

    let config =
        { LogLevel = LogLevel.None
          Htpasswd = config.Htpasswd
          HttpUrls = None
          SmtpUrls = None
          Templates =
            Map(
                seq {
                    "test",
                    """From: me
To: me
Subject: Test Subject

{{ ftr.user }} says "{{ message }}"
"""
                }
            )
          Inbox = config.Inbox }

    let request = new HttpRequestMessage(HttpMethod.Post, "/api/webhook")
    request.Headers.Authorization <- makeBasicAuth "AzureDiamond" "hunter2"

    use content =
        {| message = "Hello, World!"
           ftr_template = "test" |}
        |> makeJsonContent

    request.Content <- content

    let response = testRequest config request
    Assert.Equal(HttpStatusCode.OK, response.StatusCode)

    let called = mock.CalledImport.Value
    Assert.Equal("Test Subject", called.Message.Subject)

    let body =
        called.Message.BodyParts
        |> Seq.exactlyOne
        |> readEntity
        |> Encoding.UTF8.GetString

    Assert.Contains("AzureDiamond says \"Hello, World!\"", body)

[<Fact>]
let ``JSON import handles reference to gmail`` () =
    let mock = MockGmailInbox()

    let config =
        { LogLevel = LogLevel.None
          Htpasswd = None
          HttpUrls = None
          SmtpUrls = None
          Templates =
            Map(
                seq {
                    "test",
                    """From: me
To: me
Subject: Test Subject

{% if ftr.is_gmail -%}
{{ message }}
{% endif -%}
"""
                }
            )
          Inbox = Gmail(Set.empty, mock) }

    let request = new HttpRequestMessage(HttpMethod.Post, "/api/webhook")

    use content =
        {| message = "Hello, World!"
           ftr_template = "test" |}
        |> makeJsonContent

    request.Content <- content

    let response = testRequest config request
    Assert.Equal(HttpStatusCode.OK, response.StatusCode)

    let called = mock.CalledImport.Value
    Assert.Equal("Test Subject", called.Message.Subject)

    let body =
        called.Message.BodyParts
        |> Seq.exactlyOne
        |> readEntity
        |> Encoding.UTF8.GetString

    Assert.Contains("Hello, World!", body)

[<Fact>]
let ``JSON import handles reference to imap`` () =
    let mock = MockImapInbox()

    let config =
        { LogLevel = LogLevel.None
          Htpasswd = None
          HttpUrls = None
          SmtpUrls = None
          Templates =
            Map(
                seq {
                    "test",
                    """From: me
To: me
Subject: Test Subject

{% if ftr.is_imap -%}
{{ message }}
{% endif -%}
"""
                }
            )
          Inbox = Imap(Set.empty, mock) }

    let request = new HttpRequestMessage(HttpMethod.Post, "/api/webhook")

    use content =
        {| message = "Hello, World!"
           ftr_template = "test" |}
        |> makeJsonContent

    request.Content <- content

    let response = testRequest config request
    Assert.Equal(HttpStatusCode.OK, response.StatusCode)

    let called = Seq.last mock.CalledAppends
    Assert.Equal("Test Subject", called.Message.Subject)

    let body =
        called.Message.BodyParts
        |> Seq.exactlyOne
        |> readEntity
        |> Encoding.UTF8.GetString

    Assert.Contains("Hello, World!", body)

[<Theory>]
[<InlineData("/api/webhook")>]
[<InlineData("/go/notify")>]
[<InlineData("/apprise")>]
[<InlineData("/shoutrrr/json")>]
let ``JSON import handles missing template`` (uri: string) =
    let config, mock = mockGmailWithoutAuth ()
    let request = new HttpRequestMessage(HttpMethod.Post, uri)

    use content =
        {| message = "whatever"
           ftr_template = "test" |}
        |> makeJsonContent

    request.Content <- content

    let response = testRequest config request
    Assert.Equal(HttpStatusCode.UnprocessableContent, response.StatusCode)

[<Fact>]
let ``Webhook import works`` () =
    let config, mock = mockGmailWithoutAuth ()
    let request = new HttpRequestMessage(HttpMethod.Post, "/api/webhook")

    use content =
        {| title = "Hello, World!"
           message =
            "Lorem ipsum dolor sit amet, consectetur adipiscing elit, sed do eiusmod tempor incididunt ut labore et dolore magna aliqua." |}
        |> makeJsonContent

    request.Content <- content

    let response = testRequest config request
    Assert.Equal(HttpStatusCode.OK, response.StatusCode)

    let called = mock.CalledImport.Value

    Assert.Contains(
        "Lorem ipsum dolor sit amet, consectetur adipiscing elit, sed do eiusmod tempor incididunt ut labore et dolore magna aliqua.",
        called.Message.BodyParts
        |> Seq.exactlyOne
        |> readEntity
        |> Encoding.UTF8.GetString
    )

    Assert.Equal("Hello, World!", called.Message.Subject)

[<Fact>]
let ``Webhook import works with alternate subject field`` () =
    let config, mock = mockGmailWithoutAuth ()
    let request = new HttpRequestMessage(HttpMethod.Post, "/api/webhook")

    use content =
        {| subject = "Hello, World!"
           message =
            "Lorem ipsum dolor sit amet, consectetur adipiscing elit, sed do eiusmod tempor incididunt ut labore et dolore magna aliqua." |}
        |> makeJsonContent

    request.Content <- content

    let response = testRequest config request
    Assert.Equal(HttpStatusCode.OK, response.StatusCode)

    let called = mock.CalledImport.Value

    Assert.Contains(
        "Lorem ipsum dolor sit amet, consectetur adipiscing elit, sed do eiusmod tempor incididunt ut labore et dolore magna aliqua.",
        called.Message.BodyParts
        |> Seq.exactlyOne
        |> readEntity
        |> Encoding.UTF8.GetString
    )

    Assert.Equal("Hello, World!", called.Message.Subject)

[<Fact>]
let ``Webhook import sets Gmail flags`` () =
    let config, mock = mockGmailWithoutAuth ()
    let request = new HttpRequestMessage(HttpMethod.Post, "/api/webhook")

    use content =
        {| title = "Test Title"
           message = "Test Message"
           gmail_internal_date_source = "dateheader"
           gmail_never_mark_spam = "true"
           gmail_process_for_calendar = "false"
           gmail_deleted = "false" |}
        |> makeJsonContent

    request.Content <- content

    let response = testRequest config request
    Assert.Equal(HttpStatusCode.OK, response.StatusCode)

    let called = mock.CalledImport.Value
    Assert.Equal(Some ForTheRecord.Gmail.InternalDateSourceEnum.DateHeader, called.InternalDateSource)
    Assert.Equal(Some true, called.NeverMarkSpam)
    Assert.Equal(Some false, called.ProcessForCalendar)
    Assert.Equal(Some false, called.Deleted)

[<Fact>]
let ``Webhook import sets Gmail label`` () =
    let config, mock = mockGmailWithoutAuth ()
    let request = new HttpRequestMessage(HttpMethod.Post, "/api/webhook")

    use content =
        {| title = "Test Title"
           message = "Test Message"
           gmail_label_ids = [ "Label_SomeID" ] |}
        |> makeJsonContent

    request.Content <- content

    let response = testRequest config request
    Assert.Equal(HttpStatusCode.OK, response.StatusCode)

    let called = mock.CalledImport.Value
    Assert.Equivalent([ "Label_SomeID" ] |> Set, called.LabelIds.Value)

[<Fact>]
let ``Webhook import sets Gmail starred label`` () =
    let config, mock = mockGmailWithoutAuth ()
    let request = new HttpRequestMessage(HttpMethod.Post, "/api/webhook")

    use content =
        {| title = "Test Title"
           message = "Test Message"
           gmail_starred = "true" |}
        |> makeJsonContent

    request.Content <- content

    let response = testRequest config request
    Assert.Equal(HttpStatusCode.OK, response.StatusCode)

    let called = mock.CalledImport.Value

    Assert.Equivalent([ "INBOX"; "STARRED" ] |> Set, called.LabelIds.Value)

[<Fact>]
let ``Webhook import sets Gmail important label and custom label`` () =
    let config, mock = mockGmailWithoutAuth ()
    let request = new HttpRequestMessage(HttpMethod.Post, "/api/webhook")

    use content =
        {| title = "Test Title"
           message = "Test Message"
           gmail_label_ids = [ "Label_SomeID" ]
           gmail_important = "true" |}
        |> makeJsonContent

    request.Content <- content

    let response = testRequest config request
    Assert.Equal(HttpStatusCode.OK, response.StatusCode)

    let called = mock.CalledImport.Value

    Assert.Equivalent([ "Label_SomeID"; "IMPORTANT" ] |> Set, called.LabelIds.Value)

[<Fact>]
let ``Webhook import sets IMAP flags`` () =
    let config, mock = mockImapWithoutAuth ()
    let request = new HttpRequestMessage(HttpMethod.Post, "/api/webhook")

    use content =
        {| title = "Test Title"
           message = "Test Message"
           imap_folders = "Inbox,CustomFolder,OtherCustomFolder"
           imap_flags = "seen,answered"
           imap_keywords = "keyword1,keyword2" |}
        |> makeJsonContent

    request.Content <- content

    let response = testRequest config request
    Assert.Equal(HttpStatusCode.OK, response.StatusCode)

    Assert.Equivalent(
        [ "Inbox"; "CustomFolder"; "OtherCustomFolder" ] |> Set,
        mock.CalledAppends |> List.choose (fun call -> call.Folder) |> Set
    )

    for called in mock.CalledAppends do
        Assert.Equivalent(Some(MessageFlags.Seen ||| MessageFlags.Answered), called.Flags)

        Assert.Equivalent(
            [ "keyword1"; "keyword2" ] |> Set,
            called.Keywords |> Option.map Set |> Option.defaultValue Set.empty
        )

/// https://github.com/nikoksr/notify/tree/main/service/http
[<Fact>]
let ``Notify import works`` () =
    let config, mock = mockGmailWithoutAuth ()
    let request = new HttpRequestMessage(HttpMethod.Post, "/go/notify")

    use content =
        {| subject = "Testing new features"
           message = "Notify's HTTP service is here." |}
        |> makeJsonContent

    request.Content <- content

    let response = testRequest config request
    Assert.Equal(HttpStatusCode.OK, response.StatusCode)

    let called = mock.CalledImport.Value

    Assert.Contains(
        "Notify's HTTP service is here.",
        called.Message.BodyParts
        |> Seq.exactlyOne
        |> readEntity
        |> Encoding.UTF8.GetString
    )

    Assert.Equal("Testing new features", called.Message.Subject)
