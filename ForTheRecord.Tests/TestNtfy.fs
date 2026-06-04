module Tests.Ntfy

open System.Net
open System.Net.Http
open System.Text

open MailKit
open Xunit

open ForTheRecord.Config

open Fixtures

/// https://docs.ntfy.sh/publish/#publish-as-json
[<Fact>]
let ``Ntfy JSON import works`` () =
    let config, mock = mockGmailWithoutAuth ()
    let request = new HttpRequestMessage(HttpMethod.Post, "/ntfy")

    // Don't want any privacy extensions making the requests return different results.
    let attachUrl = "https://ipv4.icanhazip.com"

    use content =
        {| topic = "INBOX"
           title = "Low disk space alert"
           message = "Disk space is low at 5.1 GB"
           tags = [ "warning"; "cd" ]
           priority = 4
           attach = attachUrl
           filename = "diskspace.jpg"
           click = "https://homecamera.lan/xasds1h2xsSsa/"
           actions =
            [ {| action = "view"
                 label = "Admin panel"
                 url = "https://filesrv.lan/admin" |} ] |}
        |> makeJsonContent

    request.Content <- content

    let response = testRequest config request
    Assert.Equal(HttpStatusCode.OK, response.StatusCode)

    let called = mock.CalledImport.Value

    // Subject must contain title and tags.
    Assert.Contains("Low disk space alert", called.Message.Subject)
    Assert.Contains("⚠️", called.Message.Subject)
    Assert.Contains("💿️", called.Message.Subject)

    // Priority level 4 sets the important label.
    Assert.Equivalent([ "IMPORTANT"; "INBOX" ] |> Set, called.LabelIds.Value)

    // Body must contain the attachment.
    let attachment = called.Message.Attachments |> Seq.exactlyOne
    Assert.Equal("diskspace.jpg", attachment.ContentType.Name)
    Assert.Equal("diskspace.jpg", attachment.ContentDisposition.FileName)
    entityContainsBase64Of attachment (downloadBytes attachUrl)

    // Body text must contain the message, click action, and action button.
    let body = called.Message.HtmlBody
    Assert.Contains("Disk space is low at 5.1 GB", body)
    Assert.Contains("href=\"https://homecamera.lan/xasds1h2xsSsa/\"", body)
    Assert.Contains("Admin panel", body)
    Assert.Contains("href=\"https://filesrv.lan/admin\"", body)

[<Fact>]
let ``Ntfy JSON import rejects message without topic`` () =
    let config, mock = mockGmailWithoutAuth ()
    let request = new HttpRequestMessage(HttpMethod.Post, "/ntfy")

    use content =
        {| title = "Low disk space alert"
           message = "Disk space is low at 5.1 GB"
           tags = [ "warning"; "cd" ]
           priority = 4
           attach = "https://filesrv.lan/space.jpg"
           filename = "diskspace.jpg"
           click = "https://homecamera.lan/xasds1h2xsSsa/"
           actions =
            [ {| action = "view"
                 label = "Admin panel"
                 url = "https://filesrv.lan/admin" |} ] |}
        |> makeJsonContent

    request.Content <- content

    let response = testRequest config request
    Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode)

[<Fact>]
let ``Ntfy JSON import sets Gmail starred and important labels for priority 5`` () =
    let config, mock = mockGmailWithoutAuth ()
    let request = new HttpRequestMessage(HttpMethod.Post, "/ntfy")

    use content =
        {| topic = "INBOX"
           message = "Whatever"
           priority = 5
           filename = "diskspace.jpg" |}
        |> makeJsonContent

    request.Content <- content

    let response = testRequest config request
    Assert.Equal(HttpStatusCode.OK, response.StatusCode)

    let called = mock.CalledImport.Value

    Assert.Equivalent([ "STARRED"; "IMPORTANT"; "INBOX" ] |> Set, called.LabelIds.Value)

    let body = called.Message.HtmlBody
    Assert.Contains("Whatever", body)

[<Fact>]
let ``Ntfy JSON import sets Gmail important labels for priority 4`` () =
    let config, mock = mockGmailWithoutAuth ()
    let request = new HttpRequestMessage(HttpMethod.Post, "/ntfy")

    use content =
        {| topic = "INBOX"
           message = "Whatever"
           priority = 4
           filename = "diskspace.jpg" |}
        |> makeJsonContent

    request.Content <- content

    let response = testRequest config request
    Assert.Equal(HttpStatusCode.OK, response.StatusCode)

    let called = mock.CalledImport.Value

    Assert.Equivalent([ "IMPORTANT"; "INBOX" ] |> Set, called.LabelIds.Value)

    let body = called.Message.HtmlBody
    Assert.Contains("Whatever", body)

[<Fact>]
let ``Ntfy JSON import sets no additional Gmail labels for priority 3`` () =
    let config, mock = mockGmailWithoutAuth ()
    let request = new HttpRequestMessage(HttpMethod.Post, "/ntfy")

    use content =
        {| topic = "INBOX"
           message = "Whatever"
           priority = 4
           filename = "diskspace.jpg" |}
        |> makeJsonContent

    request.Content <- content

    let response = testRequest config request
    Assert.Equal(HttpStatusCode.OK, response.StatusCode)

    let called = mock.CalledImport.Value
    Assert.Equivalent([ "INBOX" ] |> Set, called.LabelIds.Value)

    let body = called.Message.HtmlBody
    Assert.Contains("Whatever", body)

[<Theory>]
[<InlineData(2)>]
[<InlineData(1)>]
let ``Ntfy JSON import removes Gmail INBOX label for low priority`` (priority: uint) =
    let config, mock = mockGmailWithoutAuth ()
    let request = new HttpRequestMessage(HttpMethod.Post, "/ntfy")

    use content =
        {| topic = "INBOX"
           message = "Whatever"
           priority = priority
           filename = "diskspace.jpg" |}
        |> makeJsonContent

    request.Content <- content

    let response = testRequest config request
    Assert.Equal(HttpStatusCode.OK, response.StatusCode)

    let called = mock.CalledImport.Value
    Assert.Equivalent(Set.empty, called.LabelIds.Value)

    let body = called.Message.HtmlBody
    Assert.Contains("Whatever", body)

[<Fact>]
let ``Ntfy JSON import sets IMAP recent and flagged flag for priority 5`` () =
    let config, mock = mockImapWithoutAuth ()
    let request = new HttpRequestMessage(HttpMethod.Post, "/ntfy")

    use content =
        {| topic = "Inbox"
           message = "Whatever"
           priority = 5
           filename = "diskspace.jpg" |}
        |> makeJsonContent

    request.Content <- content

    let response = testRequest config request
    Assert.Equal(HttpStatusCode.OK, response.StatusCode)

    let called = Seq.last mock.CalledAppends
    Assert.Equivalent(Some(MessageFlags.Recent ||| MessageFlags.Flagged), called.Flags)

    let body = called.Message.HtmlBody
    Assert.Contains("Whatever", body)

[<Fact>]
let ``Ntfy JSON import sets IMAP recent flag for priority 4`` () =
    let config, mock = mockImapWithoutAuth ()
    let request = new HttpRequestMessage(HttpMethod.Post, "/ntfy")

    use content =
        {| topic = "Inbox"
           message = "Whatever"
           priority = 4
           filename = "diskspace.jpg" |}
        |> makeJsonContent

    request.Content <- content

    let response = testRequest config request
    Assert.Equal(HttpStatusCode.OK, response.StatusCode)

    let called = Seq.last mock.CalledAppends
    Assert.Equivalent(Some MessageFlags.Recent, called.Flags)

    let body = called.Message.HtmlBody
    Assert.Contains("Whatever", body)

[<Fact>]
let ``Ntfy JSON import sets no IMAP flags for priority 3`` () =
    let config, mock = mockImapWithoutAuth ()
    let request = new HttpRequestMessage(HttpMethod.Post, "/ntfy")

    use content =
        {| topic = "Inbox"
           message = "Whatever"
           priority = 3
           filename = "diskspace.jpg" |}
        |> makeJsonContent

    request.Content <- content

    let response = testRequest config request
    Assert.Equal(HttpStatusCode.OK, response.StatusCode)

    let called = Seq.last mock.CalledAppends
    Assert.Equivalent(None, called.Flags)

    let body = called.Message.HtmlBody
    Assert.Contains("Whatever", body)

[<Theory>]
[<InlineData(2)>]
[<InlineData(1)>]
let ``Ntfy JSON import sets IMAP answered flag for low priority`` (priority: uint) =
    let config, mock = mockImapWithoutAuth ()
    let request = new HttpRequestMessage(HttpMethod.Post, "/ntfy")

    use content =
        {| topic = "Inbox"
           message = "Whatever"
           priority = priority
           filename = "diskspace.jpg" |}
        |> makeJsonContent

    request.Content <- content

    let response = testRequest config request
    Assert.Equal(HttpStatusCode.OK, response.StatusCode)

    let called = Seq.last mock.CalledAppends
    Assert.Equivalent(Some MessageFlags.Answered, called.Flags)

    let body = called.Message.HtmlBody
    Assert.Contains("Whatever", body)

[<Fact>]
let ``Ntfy JSON import sets IMAP keywords`` () =
    let config, mock = mockImapWithoutAuth ()
    let request = new HttpRequestMessage(HttpMethod.Post, "/ntfy")

    use content =
        {| topic = "Inbox"
           message = "Whatever"
           priority = 3
           filename = "diskspace.jpg"
           tags = [ "warning"; "cd" ] |}
        |> makeJsonContent

    request.Content <- content

    let response = testRequest config request
    Assert.Equal(HttpStatusCode.OK, response.StatusCode)

    let called = Seq.last mock.CalledAppends
    Assert.Equivalent([ "warning"; "cd" ] |> Set |> Some, called.Keywords |> Option.map Set)

    let body = called.Message.HtmlBody
    Assert.Contains("Whatever", body)

[<Fact>]
let ``Ntfy JSON import passes through non-emoji tags`` () =
    let config, mock = mockGmailWithoutAuth ()
    let request = new HttpRequestMessage(HttpMethod.Post, "/ntfy")

    use content =
        {| topic = "INBOX"
           message = "Whatever"
           tags = [ "smile"; "grin"; "home-server"; "fortherecord" ] |}
        |> makeJsonContent

    request.Content <- content

    let response = testRequest config request
    Assert.Equal(HttpStatusCode.OK, response.StatusCode)

    let called = mock.CalledImport.Value
    let subject = called.Message.Subject
    Assert.Contains("😄", subject)
    Assert.Contains("😁", subject)
    Assert.Contains("home-server", subject)
    Assert.Contains("fortherecord", subject)

    let body = called.Message.HtmlBody
    Assert.Contains("Whatever", body)

[<Fact>]
let ``Ntfy JSON import handles notification icon`` () =
    let config, mock = mockGmailWithoutAuth ()
    let request = new HttpRequestMessage(HttpMethod.Post, "/ntfy")

    use content =
        {| topic = "INBOX"
           message = "Whatever"
           icon = "https://styles.redditmedia.com/t5_32uhe/styles/communityIcon_xnt6chtnr2j21.png" |}
        |> makeJsonContent

    request.Content <- content

    let response = testRequest config request
    Assert.Equal(HttpStatusCode.OK, response.StatusCode)

    let called = mock.CalledImport.Value
    let body = called.Message.HtmlBody
    Assert.Contains("src=\"https://styles.redditmedia.com/t5_32uhe/styles/communityIcon_xnt6chtnr2j21.png\"", body)
    Assert.Contains("Whatever", body)

[<Fact>]
let ``Ntfy JSON import handles markdown`` () =
    let config, mock = mockGmailWithoutAuth ()
    let request = new HttpRequestMessage(HttpMethod.Post, "/ntfy")

    use content =
        {| topic = "INBOX"
           message = "A really **bold** message!"
           markdown = true
           icon = "https://styles.redditmedia.com/t5_32uhe/styles/communityIcon_xnt6chtnr2j21.png" |}
        |> makeJsonContent

    request.Content <- content

    let response = testRequest config request
    Assert.Equal(HttpStatusCode.OK, response.StatusCode)

    let called = mock.CalledImport.Value
    Assert.Contains("A really <strong>bold</strong> message!", called.Message.HtmlBody)

[<Fact>]
let ``Ntfy JSON import handles view action button`` () =
    let config, mock = mockGmailWithoutAuth ()
    let request = new HttpRequestMessage(HttpMethod.Post, "/ntfy")

    use content =
        {| topic = "myhome"
           message = "You left the house. Turn down the A/C?"
           actions =
            [ {| action = "view"
                 label = "Open portal"
                 url = "https://home.nest.com/"
                 clear = true |} ] |}
        |> makeJsonContent

    request.Content <- content

    let response = testRequest config request
    Assert.Equal(HttpStatusCode.OK, response.StatusCode)

    let called = mock.CalledImport.Value
    let body = called.Message.HtmlBody
    Assert.Contains("Open portal", body)
    Assert.Contains("href=\"https://home.nest.com/\"", body)
    Assert.Contains("You left the house. Turn down the A/C?", body)

[<Fact>]
let ``Ntfy JSON import handles http action button`` () =
    let config, mock = mockGmailWithoutAuth ()
    let request = new HttpRequestMessage(HttpMethod.Post, "/ntfy")

    use content =
        {| topic = "myhome"
           message = "Garage door has been open for 15 minutes. Close it?"
           actions =
            [ {| action = "http"
                 label = "Close door"
                 url = "https://api.mygarage.lan/"
                 method = "PUT"
                 headers = {| Authorization = "Bearer zAzsx1sk.." |}
                 body = "{\"action\": \"close\"}" |} ] |}
        |> makeJsonContent

    request.Content <- content

    let response = testRequest config request
    Assert.Equal(HttpStatusCode.OK, response.StatusCode)

    let called = mock.CalledImport.Value
    let body = called.Message.HtmlBody
    Assert.Contains("Close door", body)
    Assert.Contains("curl", body)
    Assert.Contains("PUT", body)
    Assert.Contains("https://api.mygarage.lan/", body)
    Assert.Contains("Authorization: Bearer zAzsx1sk..", body)
    Assert.Contains("{\"action\": \"close\"}", body)
    Assert.Contains("Garage door has been open for 15 minutes. Close it?", body)

[<Fact>]
let ``Ntfy JSON import handles http copy button`` () =
    let config, mock = mockGmailWithoutAuth ()
    let request: HttpRequestMessage = new HttpRequestMessage(HttpMethod.Post, "/ntfy")

    use content =
        {| topic = "myhome"
           message = "Your one-time passcode is 123456"
           actions =
            [ {| action = "copy"
                 label = "Copy code"
                 value = "123456" |} ] |}
        |> makeJsonContent

    request.Content <- content

    let response = testRequest config request
    Assert.Equal(HttpStatusCode.OK, response.StatusCode)

    let called = mock.CalledImport.Value
    let body = called.Message.HtmlBody
    Assert.Contains("Copy code", body)
    Assert.Contains("123456", body)
    Assert.Contains("Your one-time passcode is 123456", body)

[<Fact>]
let ``Ntfy JSON import handles custom template`` () =
    let mock = MockGmailInbox()

    let config =
        { Htpasswd = None
          HttpUrls = None
          SmtpUrls = None
          Templates =
            Map(
                seq {
                    "test",
                    """From: me
To: me
Subject: Test Subject

Here's my amazing message: {{ message }}
"""
                }
            )
          Inbox = Gmail(Set.empty, mock) }

    let request = new HttpRequestMessage(HttpMethod.Post, "/ntfy_template/test")

    use content =
        {| topic = "myhome"
           message = "Hello, World!" |}
        |> makeJsonContent

    request.Content <- content

    let response = testRequest config request
    Assert.Equal(HttpStatusCode.OK, response.StatusCode)

    let called = mock.CalledImport.Value
    Assert.Contains("Test Subject", called.Message.Subject)

    let body =
        called.Message.BodyParts
        |> Seq.exactlyOne
        |> readEntity
        |> Encoding.UTF8.GetString

    Assert.Contains("Here's my amazing message: Hello, World!", body)

[<Fact>]
let ``Ntfy JSON import handles missing template`` () =
    let config, mock = mockGmailWithoutAuth ()
    let request = new HttpRequestMessage(HttpMethod.Post, "/ntfy_template/test")

    use content =
        {| topic = "myhome"
           message = "Hello, World!" |}
        |> makeJsonContent

    request.Content <- content

    let response = testRequest config request
    Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode)

[<Fact>]
let ``Ntfy simple import example 1`` () =
    let config, mock = mockGmailWithoutAuth ()
    let request = new HttpRequestMessage(HttpMethod.Post, "/ntfy/INBOX")
    request.Content <- makeTextContent "Backup successful 😀"

    let response = testRequest config request
    Assert.Equal(HttpStatusCode.OK, response.StatusCode)

    let called = mock.CalledImport.Value
    let body = called.Message.HtmlBody
    Assert.Contains("Backup successful &#128512;", body)

[<Fact>]
let ``Ntfy simple import example 2`` () =
    let config, mock = mockGmailWithoutAuth ()
    let request = new HttpRequestMessage(HttpMethod.Post, "/ntfy/INBOX")
    request.Headers.Add("Title", "Unauthorized access detected")
    request.Headers.Add("Priority", "urgent")
    request.Headers.Add("Tags", "warning,skull")
    request.Content <- makeTextContent "Remote access to phils-laptop detected. Act right away."

    let response = testRequest config request
    Assert.Equal(HttpStatusCode.OK, response.StatusCode)

    let called = mock.CalledImport.Value
    Assert.Contains("Unauthorized access detected", called.Message.Subject)
    Assert.Contains("⚠️", called.Message.Subject)
    Assert.Contains("💀️", called.Message.Subject)

    Assert.Equivalent([ "STARRED"; "IMPORTANT"; "INBOX" ] |> Set, called.LabelIds.Value)

    let body = called.Message.HtmlBody
    Assert.Contains("Remote access to phils-laptop detected. Act right away", body)

[<Fact>]
let ``Ntfy simple import example 3`` () =
    let config, mock = mockGmailWithoutAuth ()
    let request = new HttpRequestMessage(HttpMethod.Post, "/ntfy/INBOX")
    request.Headers.Add("Click", "https://home.nest.com/")
    request.Headers.Add("Attach", "https://nest.com/view/yAxkasd.jpg")
    request.Headers.Add("Actions", "http, Open door, https://api.nest.com/open/yAxkasd, clear=true")

    request.Content <-
        makeTextContent
            "There's someone at the door. 🐶\n\nPlease check if it's a good boy or a hooman.\nDoggies have been known to ring the doorbell."

    let response = testRequest config request
    Assert.Equal(HttpStatusCode.OK, response.StatusCode)

    let called = mock.CalledImport.Value
    Assert.Equivalent([ "INBOX" ] |> Set, called.LabelIds.Value)

    let body = called.Message.HtmlBody
    Assert.Contains("There&#39;s someone at the door. &#128054;", body)
    Assert.Contains("Please check if it&#39;s a good boy or a hooman.", body)
    Assert.Contains("Doggies have been known to ring the doorbell.", body)

[<Fact>]
let ``Ntfy simple import works with markdown`` () =
    let config, mock = mockGmailWithoutAuth ()
    let request = new HttpRequestMessage(HttpMethod.Post, "/ntfy/INBOX")
    request.Headers.Add("Markdown", "yes")
    request.Content <- makeTextContent "Look ma, **bold text**, *italics*"

    let response = testRequest config request
    Assert.Equal(HttpStatusCode.OK, response.StatusCode)

    let called = mock.CalledImport.Value
    Assert.Equivalent([ "INBOX" ] |> Set, called.LabelIds.Value)

    let body = called.Message.HtmlBody
    Assert.Contains("Look ma, <strong>bold text</strong>, <em>italics</em>", body)

[<Fact>]
let ``Ntfy simple import handles view action button`` () =
    let config, mock = mockGmailWithoutAuth ()

    let request: HttpRequestMessage =
        new HttpRequestMessage(HttpMethod.Post, "/ntfy/INBOX")

    request.Headers.Add("Actions", "view, Open Twitter, https://twitter.com/binwiederhier/status/1467633927951163392")
    request.Content <- makeTextContent "Somebody retweeted your tweet."

    let response = testRequest config request
    Assert.Equal(HttpStatusCode.OK, response.StatusCode)

    let called = mock.CalledImport.Value
    let body = called.Message.HtmlBody
    Assert.Contains("Somebody retweeted your tweet.", body)
    Assert.Contains("Open Twitter", body)
    Assert.Contains("href=\"https://twitter.com/binwiederhier/status/1467633927951163392\"", body)

[<Fact>]
let ``Ntfy simple import handles http action button`` () =
    let config, mock = mockGmailWithoutAuth ()
    let request = new HttpRequestMessage(HttpMethod.Post, "/ntfy/INBOX")

    request.Headers.Add(
        "Actions",
        "http, Close door, https://api.mygarage.lan/, method=PUT, headers.Authorization=Bearer zAzsx1sk.., body={\"action\": \"close\"}"
    )

    request.Content <- makeTextContent "Garage door has been open for 15 minutes. Close it?"

    let response = testRequest config request
    Assert.Equal(HttpStatusCode.OK, response.StatusCode)

    let called = mock.CalledImport.Value
    let body = called.Message.HtmlBody
    Assert.Contains("Close door", body)
    Assert.Contains("curl", body)
    Assert.Contains("PUT", body)
    Assert.Contains("https://api.mygarage.lan/", body)
    Assert.Contains("Authorization: Bearer zAzsx1sk..", body)
    Assert.Contains("{\"action\": \"close\"}", body)
    Assert.Contains("Garage door has been open for 15 minutes. Close it?", body)

[<Fact>]
let ``Ntfy simple import handles http copy button`` () =
    let config, mock = mockGmailWithoutAuth ()

    let request: HttpRequestMessage =
        new HttpRequestMessage(HttpMethod.Post, "/ntfy/INBOX")

    request.Headers.Add("Actions", "copy, Copy code, 123456")
    request.Content <- makeTextContent "Your one-time passcode is 123456"

    let response = testRequest config request
    Assert.Equal(HttpStatusCode.OK, response.StatusCode)

    let called = mock.CalledImport.Value
    let body = called.Message.HtmlBody
    Assert.Contains("Copy code", body)
    Assert.Contains("123456", body)
    Assert.Contains("Your one-time passcode is 123456", body)

[<Fact>]
let ``Ntfy simple import handles multiple actions`` () =
    let config, mock = mockGmailWithoutAuth ()

    let request: HttpRequestMessage =
        new HttpRequestMessage(HttpMethod.Post, "/ntfy/INBOX")

    request.Headers.Add(
        "Actions",
        "view, Open portal, https://home.nest.com/, clear=true; http, Turn down, https://api.nest.com/, body='{\"temperature\": 65}'"
    )

    request.Content <- makeTextContent "You left the house. Turn down the A/C?"

    let response = testRequest config request
    Assert.Equal(HttpStatusCode.OK, response.StatusCode)

    let called = mock.CalledImport.Value
    let body = called.Message.HtmlBody
    Assert.Contains("Open portal", body)
    Assert.Contains("href=\"https://home.nest.com/\"", body)
    Assert.Contains("Turn down", body)
    Assert.Contains("https://api.nest.com/", body)
    Assert.Contains("{\"temperature\": 65}", body)

[<Fact>]
let ``Ntfy simple import handles action with long format`` () =
    let config, mock = mockGmailWithoutAuth ()

    let request: HttpRequestMessage =
        new HttpRequestMessage(HttpMethod.Post, "/ntfy/INBOX")

    request.Headers.Add(
        "Actions",
        "action=view, label=Open Twitter, url=https://twitter.com/binwiederhier/status/1467633927951163392"
    )

    request.Content <- makeTextContent "Somebody retweeted your tweet."

    let response = testRequest config request
    Assert.Equal(HttpStatusCode.OK, response.StatusCode)

    let called = mock.CalledImport.Value
    let body = called.Message.HtmlBody
    Assert.Contains("Somebody retweeted your tweet.", body)
    Assert.Contains("Open Twitter", body)
    Assert.Contains("href=\"https://twitter.com/binwiederhier/status/1467633927951163392\"", body)

[<Theory>]
[<InlineData(",")>]
[<InlineData(";")>]
let ``Ntfy simple import handles action with separator character inside value`` (sep: string) =
    let config, mock = mockGmailWithoutAuth ()

    let request: HttpRequestMessage =
        new HttpRequestMessage(HttpMethod.Post, "/ntfy/INBOX")

    request.Headers.Add(
        "Actions",
        $"action=view, label=\"Open {sep} Twitter\", url=https://twitter.com/binwiederhier/status/1467633927951163392"
    )

    request.Content <- makeTextContent "Somebody retweeted your tweet."

    let response = testRequest config request
    Assert.Equal(HttpStatusCode.OK, response.StatusCode)

    let called = mock.CalledImport.Value
    let body = called.Message.HtmlBody
    Assert.Contains("Somebody retweeted your tweet.", body)
    Assert.Contains($"Open {sep} Twitter", body)
    Assert.Contains("href=\"https://twitter.com/binwiederhier/status/1467633927951163392\"", body)

[<Fact>]
let ``Ntfy simple import handles custom template`` () =
    let mock = MockGmailInbox()

    let config =
        { Htpasswd = None
          HttpUrls = None
          SmtpUrls = None
          Templates =
            Map(
                seq {
                    "test",
                    """From: me
To: me
Subject: Test Subject

Here's my amazing message: {{ message }}
"""
                }
            )
          Inbox = Gmail(Set.empty, mock) }

    let request = new HttpRequestMessage(HttpMethod.Post, "/ntfy_template/test/INBOX")
    request.Content <- makeTextContent "Hello, World!"

    let response = testRequest config request
    Assert.Equal(HttpStatusCode.OK, response.StatusCode)

    let called = mock.CalledImport.Value
    Assert.Contains("Test Subject", called.Message.Subject)

    let body =
        called.Message.BodyParts
        |> Seq.exactlyOne
        |> readEntity
        |> Encoding.UTF8.GetString

    Assert.Contains("Here's my amazing message: Hello, World!", body)

[<Fact>]
let ``Ntfy simple import handles missing template`` () =
    let config, mock = mockGmailWithoutAuth ()
    let request = new HttpRequestMessage(HttpMethod.Post, "/ntfy_template/test/INBOX")
    request.Content <- makeTextContent "Hello, World!"

    let response = testRequest config request
    Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode)
