open System.IO
open System.Threading.Tasks

open Tomlyn.Model

open ForTheRecord.Config
open ForTheRecord.Http

let runWithConfig (fn: TomlTable -> Task<unit>) (file: FileInfo) : Task =
    task {
        use file = file.OpenRead()
        let config = Tomlyn.TomlSerializer.Deserialize<TomlTable> file
        do! fn config
    }

let serveGmail (config: TomlTable) =
    task {
        let! serveConfig = loadServeConfig loadGmailConfig config
        return! serveHttpAsync serveConfig
    }

let serveImap (config: TomlTable) =
    task {
        let! serveConfig = loadServeConfig loadImapConfig config
        return! serveHttpAsync serveConfig
    }

[<EntryPoint>]
let main arg =
    task {
        let config = System.CommandLine.Option<FileInfo> "--config"
        config.Description <- "Path to TOML configuration file"
        config.Required <- true
        config.Recursive <- true

        let root =
            System.CommandLine.RootCommand "ForTheRecord: Import notifications into your webmail inbox."

        root.Add config

        root.Subcommands.Add(
            let cmd =
                System.CommandLine.Command(
                    "test-gmail",
                    "Test the connection to Gmail, obtaining tokens from Google if necessary"
                )

            cmd.SetAction(fun result -> runWithConfig testGmail (result.GetRequiredValue config))
            cmd
        )

        root.Subcommands.Add(
            let cmd =
                System.CommandLine.Command("serve-gmail", "Run the service using the configured Gmail inbox")

            cmd.SetAction(fun result -> runWithConfig serveGmail (result.GetRequiredValue config))
            cmd
        )

        root.Subcommands.Add(
            let cmd =
                System.CommandLine.Command("test-imap", "Test the connection to the configured IMAP server")

            cmd.SetAction(fun result -> runWithConfig testImap (result.GetRequiredValue config))
            cmd
        )

        root.Subcommands.Add(
            let cmd =
                System.CommandLine.Command("serve-imap", "Run the service using the configured IMAP inbox")

            cmd.SetAction(fun result -> runWithConfig serveImap (result.GetRequiredValue config))
            cmd
        )

        return arg |> root.Parse |> _.Invoke()
    }
    |> Async.AwaitTask
    |> Async.RunSynchronously
