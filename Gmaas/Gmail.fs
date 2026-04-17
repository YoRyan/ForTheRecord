module Gmaas.Gmail

open System
open System.Buffers.Text
open System.Collections.Generic
open System.Threading.Tasks

open Google.Apis.Gmail.v1

type InternalDateSourceEnum = UsersResource.MessagesResource.ImportRequest.InternalDateSourceEnum

let parseInternalDateSource (s: string) =
    match s.ToLowerInvariant() with
    | "receivedtime" -> Ok InternalDateSourceEnum.ReceivedTime
    | "dateheader" -> Ok InternalDateSourceEnum.DateHeader
    | _ -> Error(sprintf "unknown internal date source value: %s" s)

type IGmailInbox =
    abstract member Import:
        message: ReadOnlySpan<byte> *
        ?labelIds: string list *
        ?internalDateSource: InternalDateSourceEnum *
        ?neverMarkSpam: bool *
        ?processForCalendar: bool *
        ?deleted: bool ->
            Task<Data.Message>

    abstract member Send: message: ReadOnlySpan<byte> -> Task<Data.Message>

type GmailInbox(service: GmailService) =
    interface IGmailInbox with
        member _.Import
            (
                message: ReadOnlySpan<byte>,
                ?labelIds: string list,
                ?internalDateSource: InternalDateSourceEnum,
                ?neverMarkSpam: bool,
                ?processForCalendar: bool,
                ?deleted: bool
            ) : Task<Data.Message> =
            let data = Data.Message()
            data.Raw <- Base64Url.EncodeToString message
            data.LabelIds <- labelIds |> Option.defaultValue [ "INBOX" ] |> List.insertAt 0 "UNREAD" |> List

            let request = service.Users.Messages.Import(data, "me")
            request.NeverMarkSpam <- neverMarkSpam |> Option.defaultValue true
            request.ProcessForCalendar <- processForCalendar |> Option.defaultValue false
            request.Deleted <- deleted |> Option.defaultValue false
            request.InternalDateSource <- internalDateSource |> Option.defaultValue InternalDateSourceEnum.ReceivedTime
            request.ExecuteAsync()

        member _.Send(message: ReadOnlySpan<byte>) : Task<Data.Message> = failwith "Not Implemented"
