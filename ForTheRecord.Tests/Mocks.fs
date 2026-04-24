module Tests.Mocks

open System
open System.IO
open System.Threading.Tasks

open Google.Apis.Gmail.v1
open MimeKit

open ForTheRecord.Gmail
open ForTheRecord.Imap

type MockGmailInbox() =
    member val CalledImport: {| Message: MimeMessage
                                LabelIds: string list option
                                InternalDateSource: InternalDateSourceEnum option
                                NeverMarkSpam: bool option
                                ProcessForCalendar: bool option
                                Deleted: bool option |} option = None with get, set

    interface IGmailInbox with
        member this.Import
            (
                message: ReadOnlySpan<byte>,
                ?labelIds: string list,
                ?internalDateSource: InternalDateSourceEnum,
                ?neverMarkSpam: bool,
                ?processForCalendar: bool,
                ?deleted: bool
            ) : Task<unit> =
            use stream = new MemoryStream(message.ToArray())
            let message = MimeMessage.Load stream

            this.CalledImport <-
                Some
                    {| Message = message
                       LabelIds = labelIds
                       InternalDateSource = internalDateSource
                       NeverMarkSpam = neverMarkSpam
                       ProcessForCalendar = processForCalendar
                       Deleted = deleted |}

            Task.FromResult()

        member this.Send(message: ReadOnlySpan<byte>) : Task<Data.Message> = failwith "Not Implemented"

type MockImapInbox() =
    interface IImapInbox with
        member this.Append(message: ReadOnlySpan<byte>, flag: string list option, date: DateTime option) : Task<unit> =
            failwith "Not Implemented"
