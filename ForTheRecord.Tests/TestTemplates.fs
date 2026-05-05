module Tests.Templates

open System
open System.Net
open System.Net.Http
open System.Text

open MimeKit
open Xunit

open ForTheRecord.Config

open Fixtures

type ApprisePayload =
    { title: string
      message: string
      ``type``: string
      format: string
      attachments:
          {| mimetype: string
             filename: string
             base64: string |} list
      ftr_template: string option
      ftr_forcefarmot: string option
      ftr_forceformat: string option }

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
        { title = "Title"
          message = "Whatever"
          ``type`` = "info"
          format = "text"
          attachments = []
          ftr_template = None
          ftr_forcefarmot = None
          ftr_forceformat = None }
        |> makeJsonContent

    request.Content <- content

    let response = testRequest config request
    Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode)

[<Fact>]
let ``Apprise import works`` () =
    let config, mock = mockGmailWithoutAuth ()
    let request = new HttpRequestMessage(HttpMethod.Post, "/apprise")

    use content =
        { title = "Hello, World!"
          message =
            "Lorem ipsum dolor sit amet, consectetur adipiscing elit, sed do eiusmod tempor incididunt ut labore et dolore magna aliqua."
          ``type`` = "info"
          format = "text"
          attachments = []
          ftr_template = None
          ftr_forcefarmot = None
          ftr_forceformat = None }
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
        { title = ""
          message = "This is a plain text message."
          ``type`` = "info"
          format = "text"
          attachments = []
          ftr_template = None
          ftr_forcefarmot = None
          ftr_forceformat = None }
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
        { title = ""
          message = "This is an <strong>HTML</strong> message."
          ``type`` = "info"
          format = "html"
          attachments = []
          ftr_template = None
          ftr_forcefarmot = None
          ftr_forceformat = None }
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
        { title = ""
          message = "This is an <strong>HTML</strong> message."
          ``type`` = "info"
          attachments = []
          format = "text"
          ftr_template = None
          ftr_forcefarmot = None
          ftr_forceformat = Some "html" }
        |> makeJsonContent

    request.Content <- content
    testRequest config request |> ignore

    let called = mock.CalledImport.Value
    let body = Seq.exactlyOne called.Message.BodyParts
    Assert.Contains("This is an <strong>HTML</strong> message.", body |> readEntity |> Encoding.UTF8.GetString)
    Assert.Equivalent(body.ContentType, ContentType("text", "html"))

[<Fact>]
let ``Apprise import handles 'html' input format with the second alternate field`` () =
    let config, mock = mockGmailWithoutAuth ()
    let request = new HttpRequestMessage(HttpMethod.Post, "/apprise")

    use content =
        { title = ""
          message = "This is an <strong>HTML</strong> message."
          ``type`` = "info"
          attachments = []
          format = "text"
          ftr_template = None
          ftr_forcefarmot = Some "html"
          ftr_forceformat = None }
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
        { title = ""
          message = "This is a **markdown** message."
          ``type`` = "info"
          attachments = []
          format = "markdown"
          ftr_template = None
          ftr_forcefarmot = None
          ftr_forceformat = None }
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
        { title = "Attachment Test Message"
          message = "This is a message with attachments."
          ``type`` = "info"
          attachments =
            List.map
                (fun (filename, mimetype, base64) ->
                    {| filename = filename
                       mimetype = mimetype
                       base64 = base64 |})
                data
          format = "text"
          ftr_template = None
          ftr_forcefarmot = None
          ftr_forceformat = None }
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
          AppriseTemplates =
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
        { title = "Hello, World!"
          message =
            "Lorem ipsum dolor sit amet, consectetur adipiscing elit, sed do eiusmod tempor incididunt ut labore et dolore magna aliqua."
          ``type`` = "info"
          format = "text"
          attachments = []
          ftr_template = Some "test"
          ftr_forcefarmot = None
          ftr_forceformat = None }
        |> makeJsonContent

    request.Content <- content

    let response = testRequest config request
    Assert.Equal(HttpStatusCode.OK, response.StatusCode)

    let called = mock.CalledImport.Value
    Assert.Equal("My Awesome Subject: Hello, World!", called.Message.Subject)
    Assert.Equivalent([ "MyLabel" ], called.LabelIds.Value)

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
