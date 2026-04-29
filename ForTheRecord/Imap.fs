module ForTheRecord.Imap

open System
open System.Threading.Tasks
open System.IO

type IImapInbox =
    abstract member Append: message: Stream * ?flag: string list * ?date: DateTime -> Task<unit>
