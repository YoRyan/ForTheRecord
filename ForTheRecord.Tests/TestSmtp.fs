module Tests.Smtp

open System.Threading

open MailKit.Net.Smtp
open MailKit.Security
open MimeKit
open MimeKit.Text
open Xunit

open ForTheRecord.Smtp

open Fixtures

// All SMTP tests must run sequentially because the test server uses a fixed
// port. Place all SMTP tests in this module; xUnit interprets it as a "test
// collection" within which tests will not be parallelized.
// https://xunit.net/docs/running-tests-in-parallel#parallelism-in-xunitnet-v2v3

[<Fact>]
let ``SMTP server works with Gmail without authentication`` () =
    task {
        use cts = new CancellationTokenSource()
        let config, mock = mockGmailWithoutAuth ()
        let! runServer = testSmtpServer config cts.Token

        let client = new SmtpClient()
        do! client.ConnectAsync("localhost", testSmtpPort)

        use! body = makeMimeEntity ("text", "plain") "Hello, World!"

        use message =
            new MimeMessage(
                [ "alice@example.com" ] |> internetAddress,
                [ "bob@example.com" ] |> internetAddress,
                "Test Subject",
                body
            )

        let! response = client.SendAsync message
        Assert.Equal("Ok", response)

        let called = mock.CalledImport.Value
        Assert.Equal("alice@example.com", called.Message.From |> Seq.exactlyOne |> _.ToString())
        Assert.Equal("bob@example.com", called.Message.To |> Seq.exactlyOne |> _.ToString())
        Assert.Equal("Test Subject", called.Message.Subject)
        Assert.Equal("text/plain", called.Message.Body.ContentType.MimeType)
        Assert.Equal("Hello, World!", TextFormat.Text |> called.Message.GetTextBody |> _.Trim())

        do! cts.CancelAsync()
        return! runServer
    }

[<Fact>]
let ``SMTP server works with Gmail with authentication`` () =
    task {
        use cts = new CancellationTokenSource()
        let config, mock = mockGmailWithHunter2Auth "AzureDiamond" true
        let! runServer = testSmtpServer config cts.Token

        let client = new SmtpClient()
        do! client.ConnectAsync("localhost", testSmtpPort)
        do! client.AuthenticateAsync("AzureDiamond", "hunter2")

        use! body = makeMimeEntity ("text", "plain") "Hello, World!"

        use message =
            new MimeMessage(
                [ "alice@example.com" ] |> internetAddress,
                [ "bob@example.com" ] |> internetAddress,
                "Test Subject",
                body
            )

        let! response = client.SendAsync message
        Assert.Equal("Ok", response)

        let called = mock.CalledImport.Value
        Assert.Equal("alice@example.com", called.Message.From |> Seq.exactlyOne |> _.ToString())
        Assert.Equal("bob@example.com", called.Message.To |> Seq.exactlyOne |> _.ToString())
        Assert.Equal("Test Subject", called.Message.Subject)
        Assert.Equal("text/plain", called.Message.Body.ContentType.MimeType)
        Assert.Equal("Hello, World!", TextFormat.Text |> called.Message.GetTextBody |> _.Trim())

        do! cts.CancelAsync()
        return! runServer
    }

[<Fact>]
let ``SMTP server with Gmail fails bad credentials`` () =
    task {
        use cts = new CancellationTokenSource()
        let config, mock = mockGmailWithHunter2Auth "AzureDiamond" true
        let! runServer = testSmtpServer config cts.Token

        let client = new SmtpClient()
        do! client.ConnectAsync("localhost", testSmtpPort)

        let! _ =
            Assert.ThrowsAsync<AuthenticationException>(fun () -> client.AuthenticateAsync("AzureDiamond", "whatever"))

        do! cts.CancelAsync()
        return! runServer
    }

[<Fact>]
let ``SMTP server with Gmail requires insert scope`` () =
    task {
        use cts = new CancellationTokenSource()
        let config, mock = mockGmailWithHunter2Auth "AzureDiamond" false
        let! runServer = testSmtpServer config cts.Token

        let client = new SmtpClient()
        do! client.ConnectAsync("localhost", testSmtpPort)

        let! _ =
            Assert.ThrowsAsync<AuthenticationException>(fun () -> client.AuthenticateAsync("AzureDiamond", "hunter2"))

        do! cts.CancelAsync()
        return! runServer
    }

[<Fact>]
let ``SMTP server works with IMAP without authentication`` () =
    task {
        use cts = new CancellationTokenSource()
        let config, mock = mockImapWithoutAuth ()
        let! runServer = testSmtpServer config cts.Token

        let client = new SmtpClient()
        do! client.ConnectAsync("localhost", testSmtpPort)

        use! body = makeMimeEntity ("text", "plain") "Hello, World!"

        use message =
            new MimeMessage(
                [ "alice@example.com" ] |> internetAddress,
                [ "bob@example.com" ] |> internetAddress,
                "Test Subject",
                body
            )

        let! response = client.SendAsync message
        Assert.Equal("Ok", response)

        let called = Seq.last mock.CalledAppends
        Assert.Equal("alice@example.com", called.Message.From |> Seq.exactlyOne |> _.ToString())
        Assert.Equal("bob@example.com", called.Message.To |> Seq.exactlyOne |> _.ToString())
        Assert.Equal("Test Subject", called.Message.Subject)
        Assert.Equal("text/plain", called.Message.Body.ContentType.MimeType)
        Assert.Equal("Hello, World!", TextFormat.Text |> called.Message.GetTextBody |> _.Trim())

        do! cts.CancelAsync()
        return! runServer
    }

[<Fact>]
let ``SMTP server works with IMAP with authentication`` () =
    task {
        use cts = new CancellationTokenSource()
        let config, mock = mockImapWithHunter2Auth "AzureDiamond" true
        let! runServer = testSmtpServer config cts.Token

        let client = new SmtpClient()
        do! client.ConnectAsync("localhost", testSmtpPort)
        do! client.AuthenticateAsync("AzureDiamond", "hunter2")

        use! body = makeMimeEntity ("text", "plain") "Hello, World!"

        use message =
            new MimeMessage(
                [ "alice@example.com" ] |> internetAddress,
                [ "bob@example.com" ] |> internetAddress,
                "Test Subject",
                body
            )

        let! response = client.SendAsync message
        Assert.Equal("Ok", response)

        let called = Seq.last mock.CalledAppends
        Assert.Equal("alice@example.com", called.Message.From |> Seq.exactlyOne |> _.ToString())
        Assert.Equal("bob@example.com", called.Message.To |> Seq.exactlyOne |> _.ToString())
        Assert.Equal("Test Subject", called.Message.Subject)
        Assert.Equal("text/plain", called.Message.Body.ContentType.MimeType)
        Assert.Equal("Hello, World!", TextFormat.Text |> called.Message.GetTextBody |> _.Trim())

        do! cts.CancelAsync()
        return! runServer
    }

[<Fact>]
let ``SMTP server with IMAP requires append permission`` () =
    task {
        use cts = new CancellationTokenSource()
        let config, mock = mockImapWithHunter2Auth "AzureDiamond" false
        let! runServer = testSmtpServer config cts.Token

        let client = new SmtpClient()
        do! client.ConnectAsync("localhost", testSmtpPort)

        let! _ =
            Assert.ThrowsAsync<AuthenticationException>(fun () -> client.AuthenticateAsync("AzureDiamond", "hunter2"))

        do! cts.CancelAsync()
        return! runServer
    }
