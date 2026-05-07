module Tests.Templates

open System
open System.Net
open System.Net.Http
open System.Text

open MimeKit
open Xunit

open ForTheRecord.Config
open ForTheRecord.Gmail

open Fixtures

[<Fact>]
let ``Endpoints require Json request content`` () =
    let config, _ = mockGmailWithoutAuth ()

    let request = new HttpRequestMessage(HttpMethod.Post, "/apprise")
    use content = makeTextContent "Definitely not JSON!"
    request.Content <- content

    let response = testRequest config request
    Assert.Equal(HttpStatusCode.NotAcceptable, response.StatusCode)

[<Fact>]
let ``Endpoints require Gmail insert scope when Gmail is configured`` () =
    let config, _ = mockGmailWithHunter2Auth "AzureDiamond" false true
    let authHeader = makeBasicAuth "AzureDiamond" "hunter2"

    let request = new HttpRequestMessage(HttpMethod.Post, "/apprise")
    request.Headers.Authorization <- authHeader

    use content =
        {| title = "Title"
           message = "Whatever"
           ``type`` = "info"
           format = "text" |}
        |> makeJsonContent

    request.Content <- content

    let response = testRequest config request
    Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode)

[<Fact>]
let ``JSON import handles custom template with json key`` () =
    let mock = MockGmailInbox()

    let config =
        { Htpasswd = None
          HttpUrls = None
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
          Inbox = Gmail(Set.empty, Set.empty, mock) }

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
    let config, mock = mockGmailWithHunter2Auth "AzureDiamond" true false

    let config =
        { Htpasswd = config.Htpasswd
          HttpUrls = None
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
let ``JSON import handles missing template`` () =
    let mock = MockGmailInbox()

    let config =
        { Htpasswd = None
          HttpUrls = None
          Templates = Map.empty
          Inbox = Gmail(Set.empty, Set.empty, mock) }

    let request = new HttpRequestMessage(HttpMethod.Post, "/api/webhook")

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
    Assert.Equal(Some InternalDateSourceEnum.DateHeader, called.InternalDateSource)
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
           gmail_label_id = "Label_SomeID" |}
        |> makeJsonContent

    request.Content <- content

    let response = testRequest config request
    Assert.Equal(HttpStatusCode.OK, response.StatusCode)

    let called = mock.CalledImport.Value
    Assert.Equivalent(seq { "Label_SomeID" } |> Set.ofSeq, called.LabelIds.Value)

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

    Assert.Equivalent(
        seq {
            "INBOX"
            "STARRED"
        }
        |> Set.ofSeq,
        called.LabelIds.Value
    )

[<Fact>]
let ``Webhook import sets Gmail important label and custom label`` () =
    let config, mock = mockGmailWithoutAuth ()
    let request = new HttpRequestMessage(HttpMethod.Post, "/api/webhook")

    use content =
        {| title = "Test Title"
           message = "Test Message"
           gmail_label_id = "Label_SomeID"
           gmail_important = "true" |}
        |> makeJsonContent

    request.Content <- content

    let response = testRequest config request
    Assert.Equal(HttpStatusCode.OK, response.StatusCode)

    let called = mock.CalledImport.Value

    Assert.Equivalent(
        seq {
            "Label_SomeID"
            "IMPORTANT"
        }
        |> Set.ofSeq,
        called.LabelIds.Value
    )

[<Fact>]
let ``Apprise import works`` () =
    let config, mock = mockGmailWithoutAuth ()
    let request = new HttpRequestMessage(HttpMethod.Post, "/apprise")

    use content =
        {| title = "Hello, World!"
           message =
            "Lorem ipsum dolor sit amet, consectetur adipiscing elit, sed do eiusmod tempor incididunt ut labore et dolore magna aliqua."
           ``type`` = "info"
           format = "text" |}
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

    Assert.Contains("Hello, World!", called.Message.Subject)

[<Fact>]
let ``Apprise import handles 'text' input format`` () =
    let config, mock = mockGmailWithoutAuth ()
    let request = new HttpRequestMessage(HttpMethod.Post, "/apprise")

    use content =
        {| message = "This is a plain text message."
           ``type`` = "info"
           ftr_forceformat = "text" |}
        |> makeJsonContent

    request.Content <- content
    testRequest config request |> ignore

    let called = mock.CalledImport.Value
    let body = Seq.exactlyOne called.Message.BodyParts
    Assert.Contains("This is a plain text message.", body |> readEntity |> Encoding.UTF8.GetString)
    Assert.Equivalent(body.ContentType, ContentType("text", "plain"))

[<Fact>]
let ``Apprise import handles 'html' input format`` () =
    let config, mock = mockGmailWithoutAuth ()
    let request = new HttpRequestMessage(HttpMethod.Post, "/apprise")

    use content =
        {| title = ""
           message = "This is an <strong>HTML</strong> message."
           ``type`` = "info"
           ftr_forceformat = "html" |}
        |> makeJsonContent

    request.Content <- content
    testRequest config request |> ignore

    let called = mock.CalledImport.Value
    let body = Seq.exactlyOne called.Message.BodyParts
    Assert.Contains("This is an <strong>HTML</strong> message.", body |> readEntity |> Encoding.UTF8.GetString)
    Assert.Equivalent(body.ContentType, ContentType("text", "html"))

[<Fact>]
let ``Apprise import handles 'html' input format with the alternate field`` () =
    let config, mock = mockGmailWithoutAuth ()
    let request = new HttpRequestMessage(HttpMethod.Post, "/apprise")

    use content =
        {| message = "This is an <strong>HTML</strong> message."
           ``type`` = "info"
           format = "text"
           ftr_forcefarmot = "html" |}
        |> makeJsonContent

    request.Content <- content
    testRequest config request |> ignore

    let called = mock.CalledImport.Value
    let body = Seq.exactlyOne called.Message.BodyParts
    Assert.Contains("This is an <strong>HTML</strong> message.", body |> readEntity |> Encoding.UTF8.GetString)
    Assert.Equivalent(body.ContentType, ContentType("text", "html"))

[<Fact>]
let ``Apprise import handles 'markdown' input format`` () =
    let config, mock = mockGmailWithoutAuth ()
    let request = new HttpRequestMessage(HttpMethod.Post, "/apprise")

    use content =
        {| title = ""
           message = "This is a **markdown** message."
           ``type`` = "info"
           attachments = []
           ftr_forceformat = "markdown" |}
        |> makeJsonContent

    request.Content <- content
    testRequest config request |> ignore

    let called = mock.CalledImport.Value
    let body = Seq.exactlyOne called.Message.BodyParts
    Assert.Contains("This is a <strong>markdown</strong> message.", body |> readEntity |> Encoding.UTF8.GetString)
    Assert.Equivalent(body.ContentType, ContentType("text", "html"))

[<Fact>]
let ``Apprise import handles attachments`` () =
    let config, mock = mockGmailWithoutAuth ()
    let request = new HttpRequestMessage(HttpMethod.Post, "/apprise")

    let data =
        seq {
            "test.json", "application/json", "{\"hello\": \"world\"}"
            "test.html", "text/html", "<p>hello <strong>world</strong></p>"
        }
        |> Seq.map (fun (filename, mimetype, base64) ->
            filename, mimetype, base64 |> Encoding.UTF8.GetBytes |> Convert.ToBase64String)
        |> List.ofSeq

    use content =
        {| title = "Attachment Test Message"
           message = "This is a message with attachments."
           ``type`` = "info"
           attachments =
            List.map
                (fun (filename, mimetype, base64) ->
                    {| filename = filename
                       mimetype = mimetype
                       base64 = base64 |})
                data
           format = "text" |}
        |> makeJsonContent

    request.Content <- content
    testRequest config request |> ignore

    let called = mock.CalledImport.Value
    Assert.Contains("Attachment Test Message", called.Message.Subject)

    let body =
        called.Message.BodyParts
        |> Seq.filter (fun me ->
            let ct = me.ContentType
            ct.MediaType = "text" && ct.MediaSubtype = "plain")
        |> Seq.exactlyOne

    Assert.Contains("This is a message with attachments.", body |> readEntity |> Encoding.UTF8.GetString)

    let attachments = mock.CalledImport.Value.Message.Attachments |> Seq.toList
    Assert.Equal(2, attachments.Length)

    data
    |> Seq.iteri (fun i (filename, mimetype, base64) ->
        let attach = attachments[i]
        Assert.Equal(filename, attach.ContentDisposition.FileName)
        Assert.Equal(mimetype, attach.ContentType.MimeType)
        Assert.Contains(base64, attach |> readEntity |> Encoding.UTF8.GetString))

[<Fact>]
let ``Apprise import handles custom template`` () =
    let mock = MockGmailInbox()

    let config =
        { Htpasswd = None
          HttpUrls = None
          Templates =
            Map(
                seq {
                    "test",
                    """From: me
To: me
X-FTR-Gmail-LabelID: MyLabel
Subject: My Awesome Subject: {{ title }}

This is a test using custom templates.
{{ message }}
"""
                }
            )
          Inbox = Gmail(Set.empty, Set.empty, mock) }

    let request = new HttpRequestMessage(HttpMethod.Post, "/apprise")

    use content =
        {| title = "Hello, World!"
           message =
            "Lorem ipsum dolor sit amet, consectetur adipiscing elit, sed do eiusmod tempor incididunt ut labore et dolore magna aliqua."
           ``type`` = "info"
           format = "text"
           ftr_template = "test" |}
        |> makeJsonContent

    request.Content <- content

    let response = testRequest config request
    Assert.Equal(HttpStatusCode.OK, response.StatusCode)

    let called = mock.CalledImport.Value
    Assert.Equal("My Awesome Subject: Hello, World!", called.Message.Subject)
    Assert.Equivalent(seq { "MyLabel" } |> Set.ofSeq, called.LabelIds.Value)

    let body =
        called.Message.BodyParts
        |> Seq.exactlyOne
        |> readEntity
        |> Encoding.UTF8.GetString

    Assert.Contains("This is a test using custom templates.", body)

    Assert.Contains(
        "Lorem ipsum dolor sit amet, consectetur adipiscing elit, sed do eiusmod tempor incididunt ut labore et dolore magna aliqua.",
        body
    )

[<Fact>]
let ``Apprise import sets Gmail flags`` () =
    let config, mock = mockGmailWithoutAuth ()
    let request = new HttpRequestMessage(HttpMethod.Post, "/apprise")

    use content =
        {| title = "Test Title"
           message = "Test Message"
           ``type`` = "info"
           gmail_internal_date_source = "dateheader"
           gmail_never_mark_spam = "true"
           gmail_process_for_calendar = "false"
           gmail_deleted = "false" |}
        |> makeJsonContent

    request.Content <- content

    let response = testRequest config request
    Assert.Equal(HttpStatusCode.OK, response.StatusCode)

    let called = mock.CalledImport.Value
    Assert.Equal(Some InternalDateSourceEnum.DateHeader, called.InternalDateSource)
    Assert.Equal(Some true, called.NeverMarkSpam)
    Assert.Equal(Some false, called.ProcessForCalendar)
    Assert.Equal(Some false, called.Deleted)

[<Fact>]
let ``Apprise import sets Gmail label`` () =
    let config, mock = mockGmailWithoutAuth ()
    let request = new HttpRequestMessage(HttpMethod.Post, "/apprise")

    use content =
        {| title = "Test Title"
           message = "Test Message"
           ``type`` = "info"
           gmail_label_id = "Label_SomeID" |}
        |> makeJsonContent

    request.Content <- content

    let response = testRequest config request
    Assert.Equal(HttpStatusCode.OK, response.StatusCode)

    let called = mock.CalledImport.Value
    Assert.Equivalent(seq { "Label_SomeID" } |> Set.ofSeq, called.LabelIds.Value)

[<Fact>]
let ``Apprise import sets Gmail starred label`` () =
    let config, mock = mockGmailWithoutAuth ()
    let request = new HttpRequestMessage(HttpMethod.Post, "/apprise")

    use content =
        {| title = "Test Title"
           message = "Test Message"
           ``type`` = "info"
           gmail_starred = "true" |}
        |> makeJsonContent

    request.Content <- content

    let response = testRequest config request
    Assert.Equal(HttpStatusCode.OK, response.StatusCode)

    let called = mock.CalledImport.Value

    Assert.Equivalent(
        seq {
            "INBOX"
            "STARRED"
        }
        |> Set.ofSeq,
        called.LabelIds.Value
    )

[<Fact>]
let ``Apprise import sets Gmail important label and custom label`` () =
    let config, mock = mockGmailWithoutAuth ()
    let request = new HttpRequestMessage(HttpMethod.Post, "/apprise")

    use content =
        {| title = "Test Title"
           message = "Test Message"
           ``type`` = "info"
           gmail_label_id = "Label_SomeID"
           gmail_important = "true" |}
        |> makeJsonContent

    request.Content <- content

    let response = testRequest config request
    Assert.Equal(HttpStatusCode.OK, response.StatusCode)

    let called = mock.CalledImport.Value

    Assert.Equivalent(
        seq {
            "Label_SomeID"
            "IMPORTANT"
        }
        |> Set.ofSeq,
        called.LabelIds.Value
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

/// https://containrrr.dev/shoutrrr/v0.8/services/generic/
[<Fact>]
let ``Shoutrrr import works`` () =
    let config, mock = mockGmailWithoutAuth ()
    let request = new HttpRequestMessage(HttpMethod.Post, "/shoutrrr")

    use content = makeTextContent "Hello world (or slack channel) !"
    // Shoutrrr defaults to JSON content type for whatever reason.
    content.Headers.ContentType <- Headers.MediaTypeHeaderValue "application/json"
    request.Content <- content

    let response = testRequest config request
    Assert.Equal(HttpStatusCode.OK, response.StatusCode)

    let called = mock.CalledImport.Value

    Assert.Contains(
        "Hello world (or slack channel) !",
        called.Message.BodyParts
        |> Seq.exactlyOne
        |> readEntity
        |> Encoding.UTF8.GetString
    )

    Assert.Equal("Notification", called.Message.Subject)

[<Fact>]
let ``Shoutrrr import handles custom template`` () =
    let mock = MockGmailInbox()

    let config =
        { Htpasswd = None
          HttpUrls = None
          Templates =
            Map(
                seq {
                    "test",
                    """From: me
To: me
Subject: Test Subject

Here's my message! {{ message }}
"""
                }
            )
          Inbox = Gmail(Set.empty, Set.empty, mock) }

    let request = new HttpRequestMessage(HttpMethod.Post, "/shoutrrr?ftr_template=test")
    use content = makeTextContent "Hello, World!"
    content.Headers.ContentType <- Headers.MediaTypeHeaderValue "application/json"
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

    Assert.Contains("Here's my message! Hello, World!", body)

[<Fact>]
let ``Shoutrrr import handles custom template with reference to user`` () =
    let config, mock = mockGmailWithHunter2Auth "AzureDiamond" true false

    let config =
        { Htpasswd = config.Htpasswd
          HttpUrls = None
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

    let request = new HttpRequestMessage(HttpMethod.Post, "/shoutrrr?ftr_template=test")
    request.Headers.Authorization <- makeBasicAuth "AzureDiamond" "hunter2"

    use content = makeTextContent "Hello, World!"
    content.Headers.ContentType <- Headers.MediaTypeHeaderValue "application/json"
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
let ``Shoutrrr JSON import works`` () =
    let config, mock = mockGmailWithoutAuth ()
    let request = new HttpRequestMessage(HttpMethod.Post, "/shoutrrr/json")

    use content =
        {| title = "Amazing opportunities!"
           message = "New map book available for purchase." |}
        |> makeJsonContent

    request.Content <- content

    let response = testRequest config request
    Assert.Equal(HttpStatusCode.OK, response.StatusCode)

    let called = mock.CalledImport.Value

    Assert.Contains(
        "New map book available for purchase.",
        called.Message.BodyParts
        |> Seq.exactlyOne
        |> readEntity
        |> Encoding.UTF8.GetString
    )

    Assert.Equal("Amazing opportunities!", called.Message.Subject)

[<Fact>]
let ``Shoutrrr JSON import sets Gmail flags`` () =
    let config, mock = mockGmailWithoutAuth ()
    let request = new HttpRequestMessage(HttpMethod.Post, "/shoutrrr/json")

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
    Assert.Equal(Some InternalDateSourceEnum.DateHeader, called.InternalDateSource)
    Assert.Equal(Some true, called.NeverMarkSpam)
    Assert.Equal(Some false, called.ProcessForCalendar)
    Assert.Equal(Some false, called.Deleted)

[<Fact>]
let ``Shoutrrr JSON import sets Gmail label`` () =
    let config, mock = mockGmailWithoutAuth ()
    let request = new HttpRequestMessage(HttpMethod.Post, "/shoutrrr/json")

    use content =
        {| title = "Test Title"
           message = "Test Message"
           gmail_label_id = "Label_SomeID" |}
        |> makeJsonContent

    request.Content <- content

    let response = testRequest config request
    Assert.Equal(HttpStatusCode.OK, response.StatusCode)

    let called = mock.CalledImport.Value
    Assert.Equivalent(seq { "Label_SomeID" } |> Set.ofSeq, called.LabelIds.Value)

[<Fact>]
let ``Shoutrrr JSON import sets Gmail starred label and custom label`` () =
    let config, mock = mockGmailWithoutAuth ()
    let request = new HttpRequestMessage(HttpMethod.Post, "/shoutrrr/json")

    use content =
        {| title = "Test Title"
           message = "Test Message"
           ``type`` = "info"
           gmail_label_id = "Label_SomeID"
           gmail_starred = "true" |}
        |> makeJsonContent

    request.Content <- content

    let response = testRequest config request
    Assert.Equal(HttpStatusCode.OK, response.StatusCode)

    let called = mock.CalledImport.Value

    Assert.Equivalent(
        seq {
            "Label_SomeID"
            "STARRED"
        }
        |> Set.ofSeq,
        called.LabelIds.Value
    )

[<Fact>]
let ``Shoutrrr JSON import sets Gmail important label`` () =
    let config, mock = mockGmailWithoutAuth ()
    let request = new HttpRequestMessage(HttpMethod.Post, "/shoutrrr/json")

    use content =
        {| title = "Test Title"
           message = "Test Message"
           ``type`` = "info"
           gmail_important = "true" |}
        |> makeJsonContent

    request.Content <- content

    let response = testRequest config request
    Assert.Equal(HttpStatusCode.OK, response.StatusCode)

    let called = mock.CalledImport.Value

    Assert.Equivalent(
        seq {
            "INBOX"
            "IMPORTANT"
        }
        |> Set.ofSeq,
        called.LabelIds.Value
    )
