module ForTheRecord.Imap

open System
open System.IO
open System.Threading
open System.Threading.Tasks

open MailKit
open MailKit.Net.Imap
open MimeKit

type IImapInbox =
    abstract member Append:
        message: MimeMessage * ?folder: string * ?flags: MessageFlags * ?keywords: string list * ?date: DateTime ->
            Task<unit>

type ImapInbox(uri: Uri, username: string, password: string) =
    let client = new ImapClient()
    let clientSem = new SemaphoreSlim 1

    let ensureConnected () =
        task {
            if not client.IsConnected then
                do! client.ConnectAsync uri
                do! client.AuthenticateAsync(username, password)
        }

    interface IDisposable with
        member _.Dispose() =
            client.Dispose()
            clientSem.Dispose()

    interface IImapInbox with
        member _.Append
            (message: MimeMessage, ?folder: string, ?flags: MessageFlags, ?keywords: string list, ?date: DateTime)
            =
            task {
                let flags = flags |> Option.defaultValue MessageFlags.None

                do! clientSem.WaitAsync()
                do! ensureConnected ()

                let! folder =
                    match folder with
                    | Some path -> client.GetFolderAsync path
                    | None -> Task.FromResult client.Inbox

                let request =
                    match keywords, date with
                    | Some keywords, Some date -> AppendRequest(message, flags, keywords, date)
                    | Some keywords, None -> AppendRequest(message, flags, keywords)
                    | None, Some date -> AppendRequest(message, flags, date)
                    | None, None -> AppendRequest(message, flags)

                let! _ = folder.AppendAsync request

                clientSem.Release() |> ignore
            }

let parseImapFlags (flags: string list) : MessageFlags =
    let enums =
        flags
        |> Seq.map _.ToLowerInvariant()
        |> Seq.choose (function
            | "seen" -> Some MessageFlags.Seen
            | "answered" -> Some MessageFlags.Answered
            | "flagged" -> Some MessageFlags.Flagged
            | "deleted" -> Some MessageFlags.Deleted
            | "draft" -> Some MessageFlags.Draft
            | "recent" -> Some MessageFlags.Recent
            | "userdefined" -> Some MessageFlags.UserDefined
            | _ -> None)

    (0, enums)
    ||> Seq.fold (fun accum flag -> accum ||| int flag)
    |> LanguagePrimitives.EnumOfValue

/// Import a finalized message to Imap, using special headers to determine
/// which flags and metadata to apply.
let importWholeMessageToImap (inbox: IImapInbox) (message: Stream) =
    task {
        let! message = MimeMessage.LoadAsync message
        let headers = message.Headers

        let readList (field: string) =
            let field = field.ToLowerInvariant()

            (None, headers)
            ||> Seq.fold (fun state h ->
                if h.Field.ToLowerInvariant() = field then
                    match state with
                    | Some l -> l @ [ h.Value ]
                    | None -> [ h.Value ]
                    |> Some
                else
                    state)

        let folders = readList "x-ftr-imap-folder"
        let flags = readList "x-ftr-imap-flag" |> Option.map parseImapFlags
        let keywords = readList "x-ftr-imap-keyword"

        match folders with
        | Some folders ->
            return!
                folders
                |> Seq.map (fun f -> inbox.Append(message, f, ?flags = flags, ?keywords = keywords))
                |> Seq.cast<Task>
                |> Task.WhenAll
        | None -> return! inbox.Append(message, ?flags = flags, ?keywords = keywords)
    }
