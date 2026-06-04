module ForTheRecord.Config

open System
open System.IO
open System.Threading.Tasks

open Google.Apis.Gmail.v1
open Google.Apis.Services
open Meziantou.Framework.Http
open Microsoft.Extensions.Logging
open Tomlyn.Model

open ForTheRecord.Gmail
open ForTheRecord.Helpers
open ForTheRecord.Imap

[<Literal>]
let private applicationName = "ForTheRecord"

type ConfiguredInbox =
    | Gmail of authInsert: Set<string> * inbox: IGmailInbox
    | Imap of authAppend: Set<string> * inbox: IImapInbox

type ServeConfig =
    { LogLevel: LogLevel
      Htpasswd: HtpasswdFile option
      HttpUrls: string list option
      SmtpUrls: string list option
      Templates: Map<string, string>
      Inbox: ConfiguredInbox }

// We use DOM access because F# records do not serialize well without the
// CLIMutable attribute. This requires named record types, which would mean a
// lot of ceremony.

let private inTable<'T> k (t: TomlTable) =
    t.TryGetValue k
    |> tryGetByref
    |> Option.map (function
        | :? 'T as v -> v
        | v -> failwithf "Expected a %s: %s" (typeof<'T>.ToString()) (v.ToString()))

let private asList<'T> (a: TomlArray) : 'T list =
    a
    |> Seq.map (function
        | :? 'T as v -> v
        | v -> failwithf "Expected a %s: %s" (typeof<'T>.ToString()) (v.ToString()))
    |> Seq.toList

let private asTableList (ta: TomlTableArray) : TomlTable list = ta |> Seq.toList

let private asMap<'V> (t: TomlTable) : Map<string, 'V> =
    t
    |> Seq.map (|KeyValue|)
    |> Seq.map (fun (k, v) ->
        match v with
        | :? 'V as v -> k, v
        | v -> failwithf "Expected a %s: %s" (typeof<'V>.ToString()) (v.ToString()))
    |> Map.ofSeq

let getGmailInbox (config: ServeConfig) =
    match config.Inbox with
    | Gmail(_authInsert, inbox) -> inbox
    | _ -> raise (InvalidOperationException())

let getImapInbox (config: ServeConfig) =
    match config.Inbox with
    | Imap(_authAppend, inbox) -> inbox
    | _ -> raise (InvalidOperationException())

let loadServeConfig (loadInboxConfig: TomlTable -> Task<ConfiguredInbox>) (t: TomlTable) =
    task {
        let! inbox = loadInboxConfig t

        return
            { LogLevel =
                t
                |> inTable<string> "log_level"
                |> Option.map _.ToLowerInvariant()
                |> Option.map (fun s ->
                    match s with
                    | "trace" -> LogLevel.Trace
                    | "debug" -> LogLevel.Debug
                    | "information" -> LogLevel.Information
                    | "warning" -> LogLevel.Warning
                    | "error" -> LogLevel.Error
                    | "critical" -> LogLevel.Critical
                    | _ -> failwithf "Invalid log level: %s" s)
                |> Option.defaultValue LogLevel.Information
              Htpasswd =
                t
                |> inTable "auth"
                |> Option.bind (inTable<string> "htpasswd")
                |> Option.map HtpasswdFile.Parse
              HttpUrls = t |> inTable "http" |> Option.bind (inTable "listen_urls") |> Option.map asList
              SmtpUrls = t |> inTable "smtp" |> Option.bind (inTable "listen_urls") |> Option.map asList
              Templates = t |> inTable "templates" |> Option.map asMap |> Option.defaultValue Map.empty
              Inbox = inbox }
    }

let configureLogging (config: ServeConfig) (builder: ILoggingBuilder) =
    builder
    |> _.AddConsole()
    |> _.AddDebug()
    |> _.SetMinimumLevel(config.LogLevel)
    |> ignore

let loadGmailConfig (t: TomlTable) =
    task {
        let google = t |> inTable "google"
        let scopes = google |> Option.bind (inTable "scopes")

        let credentialsFile =
            match google |> Option.bind (inTable "credentials") with
            | Some path -> FileInfo path
            | None -> failwith "Missing path to Google credentials file."

        let credentialsStore =
            match google |> Option.bind (inTable "tokens_store") with
            | Some path -> DirectoryInfo path
            | None -> failwith "Missing path to Google tokens directory."

        let! credentials = loadCredentials (CowardlyCodeReceiver()) credentialsFile credentialsStore
        let initializer = BaseClientService.Initializer()
        initializer.HttpClientInitializer <- credentials
        initializer.ApplicationName <- applicationName
        let service = new GmailService(initializer)

        let authInsert =
            scopes
            |> Option.bind (inTable "gmail")
            |> Option.bind (inTable "insert")
            |> Option.map asList
            |> Option.defaultValue []
            |> Set.ofList

        return Gmail(authInsert, GmailInbox service)
    }

let testGmail (t: TomlTable) =
    task {
        let google = t |> inTable "google"

        let credentialsFile =
            match google |> Option.bind (inTable "credentials") with
            | Some path -> FileInfo path
            | None -> failwith "Missing path to Google credentials file."

        let credentialsStore =
            match google |> Option.bind (inTable "tokens_store") with
            | Some path -> DirectoryInfo path
            | None -> failwith "Missing path to Google tokens directory."

        let! credentials = loadCredentials (TerminalCodeReceiver()) credentialsFile credentialsStore
        let initializer = BaseClientService.Initializer()
        initializer.HttpClientInitializer <- credentials
        initializer.ApplicationName <- applicationName

        new GmailService(initializer) |> ignore

        printfn "Successfully logged into Gmail."
    }

let loadImapConfig (t: TomlTable) =
    let imap = t |> inTable "imap"
    let perms = imap |> Option.bind (inTable "permissions")

    let uri =
        match imap |> Option.bind (inTable "connect_url") with
        | Some url -> Uri url
        | None -> failwith "Missing imap:// or imaps:// connection URL."

    let username, password =
        match imap |> Option.bind (inTable "username"), imap |> Option.bind (inTable "password") with
        | Some username, Some password -> username, password
        | _ -> failwith "Missing IMAP username and password."

    let authAppend =
        perms
        |> Option.bind (inTable "append")
        |> Option.map asList
        |> Option.defaultValue []
        |> Set.ofList

    Imap(authAppend, new ImapInbox(uri, username, password)) |> Task.FromResult

let testImap (t: TomlTable) =
    task {
        let imap = t |> inTable "imap"

        let uri =
            match imap |> Option.bind (inTable "connect_url") with
            | Some url -> Uri url
            | None -> failwith "Missing imap:// or imaps:// connection URL."

        let username, (password: string) =
            match imap |> Option.bind (inTable "username"), imap |> Option.bind (inTable "password") with
            | Some username, Some password -> username, password
            | _ -> failwith "Missing IMAP username and password."

        use client = new MailKit.Net.Imap.ImapClient()
        do! client.ConnectAsync uri
        do! client.AuthenticateAsync(username, password)

        printfn "Successfully logged into IMAP server."
    }
