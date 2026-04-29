module Tests.Mocks

open System
open System.IO
open System.Threading.Tasks

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
                message: Stream,
                ?labelIds: string list,
                ?internalDateSource: InternalDateSourceEnum,
                ?neverMarkSpam: bool,
                ?processForCalendar: bool,
                ?deleted: bool
            ) : Task<unit> =
            this.CalledImport <-
                Some
                    {| Message = MimeMessage.Load message
                       LabelIds = labelIds
                       InternalDateSource = internalDateSource
                       NeverMarkSpam = neverMarkSpam
                       ProcessForCalendar = processForCalendar
                       Deleted = deleted |}

            Task.FromResult()

        member this.Send(message: Stream) : Task<unit> = failwith "Not Implemented"

type MockImapInbox() =
    interface IImapInbox with
        member this.Append(message: Stream, flag: string list option, date: DateTime option) : Task<unit> =
            failwith "Not Implemented"
