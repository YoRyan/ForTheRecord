module Tests.Shoutrrr

open System.Net
open System.Net.Http
open System.Text

open Xunit

open ForTheRecord.Config
open ForTheRecord.Gmail

open Fixtures

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

[<Fact>]
let ``Shoutrrr JSON import handles custom template`` () =
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

    let request = new HttpRequestMessage(HttpMethod.Post, "/shoutrrr/json")

    use content =
        {| title = "Amazing opportunities!"
           message = "New map book available for purchase."
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

    Assert.Contains("Here's my message! New map book available for purchase.", body)
