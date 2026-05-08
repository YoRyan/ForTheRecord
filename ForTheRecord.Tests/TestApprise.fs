module Tests.Apprise

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
    Assert.Contains("This is an <strong>HTML</strong> message.", called.Message.HtmlBody)

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
    Assert.Contains("This is an <strong>HTML</strong> message.", called.Message.HtmlBody)

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
    Assert.Contains("This is a <strong>markdown</strong> message.", called.Message.HtmlBody)

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
