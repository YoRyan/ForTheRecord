module Tests.Imap

open System.IO
open System.Text

open MailKit
open MimeKit.Text
open Xunit

open ForTheRecord.Imap

open Fixtures

[<Fact>]
let ``Whole message import works`` () =
    let _, mock = mockImapWithoutAuth ()

    let message =
        """From: me
To: me
Subject: Hello, World!
Content-Type: text/plain

Lorem ipsum dolor sit amet, consectetur adipiscing elit, sed do eiusmod tempor incididunt ut labore et dolore magna aliqua.
"""

    use stream = new MemoryStream(Encoding.UTF8.GetBytes message)

    importWholeMessageToImap mock stream
    |> Async.AwaitTask
    |> Async.RunSynchronously

    let called = Seq.last mock.CalledAppends

    Assert.Equal(
        "Lorem ipsum dolor sit amet, consectetur adipiscing elit, sed do eiusmod tempor incididunt ut labore et dolore magna aliqua.",
        TextFormat.Text |> called.Message.GetTextBody |> _.Trim()
    )

    Assert.Equal("Hello, World!", called.Message.Subject)
    Assert.Equal("text/plain", called.Message.Body.ContentType.MimeType)
    Assert.Equal(None, called.Folder)
    Assert.Equal(None, called.Flags)
    Assert.Equal(None, called.Keywords)

[<Fact>]
let ``Whole message import works with folders`` () =
    let _, mock = mockImapWithoutAuth ()

    let message =
        """From: me
To: me
Subject: Hello, World!
X-FTR-Imap-Folder: INBOX
X-FTR-Imap-Folder: CustomFolder1
X-FTR-Imap-Folder: CustomFolder2
Content-Type: text/plain

This is the label test message.
"""

    use stream = new MemoryStream(Encoding.UTF8.GetBytes message)

    importWholeMessageToImap mock stream
    |> Async.AwaitTask
    |> Async.RunSynchronously

    let called =
        mock.CalledAppends |> List.choose (fun call -> call.Folder) |> Set.ofList

    Assert.Equivalent(Set [ "INBOX"; "CustomFolder1"; "CustomFolder2" ], called)

[<Fact>]
let ``Whole message import works with flags`` () =
    let _, mock = mockImapWithoutAuth ()

    let message =
        """From: me
To: me
Subject: Hello, World!
X-FTR-Imap-Flag: Seen
X-FTR-Imap-Flag: flagged
X-FTR-Imap-Flag: RECENT
Content-Type: text/plain

This is the label test message.
"""

    use stream = new MemoryStream(Encoding.UTF8.GetBytes message)

    importWholeMessageToImap mock stream
    |> Async.AwaitTask
    |> Async.RunSynchronously

    let called = Seq.last mock.CalledAppends
    Assert.Equivalent(Some(MessageFlags.Seen ||| MessageFlags.Flagged ||| MessageFlags.Recent), called.Flags)

[<Fact>]
let ``Whole message import works with keywords`` () =
    let _, mock = mockImapWithoutAuth ()

    let message =
        """From: me
To: me
Subject: Hello, World!
X-FTR-Imap-Keyword: my
X-FTR-Imap-Keyword: awesome
X-FTR-Imap-Keyword: keyword
Content-Type: text/plain

This is the label test message.
"""

    use stream = new MemoryStream(Encoding.UTF8.GetBytes message)

    importWholeMessageToImap mock stream
    |> Async.AwaitTask
    |> Async.RunSynchronously

    let called = mock.CalledAppends |> Seq.last |> _.Keywords |> Option.map Set.ofList
    Assert.Equivalent(Some(Set [ "my"; "awesome"; "keyword" ]), called)
