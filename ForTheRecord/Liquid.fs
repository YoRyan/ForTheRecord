module ForTheRecord.Liquid

open System
open System.Collections.Generic
open System.Reflection
open System.Text
open System.Text.Json
open System.Text.RegularExpressions
open System.Threading.Tasks

open Fluid
open Fluid.Values
open Markdig
open Microsoft.AspNetCore.Http

open ForTheRecord.Helpers

/// A Liquid template compiled into the program's assembly.
type AssemblyTemplate =
    | Apprise
    | Ntfy
    | Shoutrrr
    | Webhook

/// Read one of the Liquid templates compiled into the program's assembly.
let readAssemblyTemplate (template: AssemblyTemplate) =
    task {
        let name =
            match template with
            | Apprise -> "Apprise"
            | Ntfy -> "Ntfy"
            | Shoutrrr -> "Shoutrrr"
            | Webhook -> "Webhook"

        return! readEmbeddedText $"Liquid.{name}.liquid"
    }

/// Map of GitHub emoji codes to emojis.
let private gemojiMap =
    let assembly = Assembly.GetExecutingAssembly()

    use stream =
        assembly.GetManifestResourceStream $"{assembly.GetName().Name}.emoji.json"

    stream
    |> JsonSerializer.Deserialize<
        {| emoji: string
           aliases: string list |} list
        >
    |> Seq.ofList
    |> Seq.map (fun e -> e.aliases |> Seq.ofList |> Seq.map (fun alias -> alias, e.emoji))
    |> Seq.concat
    |> Map.ofSeq

type Filters() =
    /// Encodes an UTF-8 string inline so that it is safe for use in email
    /// headers.
    static member encodeUtf8 (input: FluidValue) (_arguments: FilterArguments) (_context: TemplateContext) =
        input
        |> _.ToStringValue()
        |> Encoding.UTF8.GetBytes
        |> Convert.ToBase64String
        |> fun b64 -> $"=?UTF-8?B?{b64}?="
        |> StringValue
        |> ValueTask.FromResult<FluidValue>

    /// Converts Markdown to HTML.
    static member markdown (input: FluidValue) (_arguments: FilterArguments) (_context: TemplateContext) =
        input
        |> _.ToStringValue()
        |> Markdown.ToHtml
        |> StringValue
        |> ValueTask.FromResult<FluidValue>

    /// Convert a GitHub emoji code to its corresponding emoji.
    static member gemoji (input: FluidValue) (_arguments: FilterArguments) (_context: TemplateContext) =
        input
        |> _.ToStringValue()
        |> gemojiMap.TryGetValue
        |> tryGetByref
        |> Option.defaultValue null
        |> StringValue
        |> ValueTask.FromResult<FluidValue>

    /// Escape a string for use in a single-quoted command-line argument.
    static member singleShellEscape (input: FluidValue) (_arguments: FilterArguments) (_context: TemplateContext) =
        input
        |> _.ToStringValue()
        |> _.Replace("'", "'\"'\"'")
        |> StringValue
        |> ValueTask.FromResult<FluidValue>

    /// Download a URL, returning the obtained Content-Type header and base64 file content.
    static member download (input: FluidValue) (_arguments: FilterArguments) (context: TemplateContext) =
        task {
            let url = input.ToStringValue()
            let! response = httpClient.GetAsync url

            if response.IsSuccessStatusCode then
                let! bytes = response.Content.ReadAsByteArrayAsync()

                let contentType =
                    match response.Content.Headers.ContentType with
                    | null -> null
                    | hv -> hv.MediaType

                return
                    seq {
                        "content_type", contentType |> StringValue
                        "base64", bytes |> Convert.ToBase64String |> StringValue
                    }
                    |> dict
                    |> fun d -> FluidValue.Create(d, context.Options)
            else
                return null
        }
        |> ValueTask<FluidValue>

/// Create a template options object with our custom filters configured.
let createTemplateOptions () =
    let options = TemplateOptions()

    for name, d in
        seq {
            "ftr_encode_utf8", Filters.encodeUtf8
            "ftr_markdown", Filters.markdown
            "ftr_gemoji", Filters.gemoji
            "ftr_single_shell_escape", Filters.singleShellEscape
            "ftr_download", Filters.download
        } do
        options.Filters.AddFilter(name, FilterDelegate d)

    options

/// Map a top-level JSON object into a dictionary suitable for use as a Liquid
/// model.
let rec jsonLiquidModel (js: JsonElement) : obj =
    match js.ValueKind with
    | JsonValueKind.Object ->
        let d =
            js.EnumerateObject()
            |> Seq.map (fun prop -> prop.Name, jsonLiquidModel prop.Value)
            |> dict

        d
    | JsonValueKind.Array ->
        let l = js.EnumerateArray() |> Seq.map jsonLiquidModel |> List.ofSeq
        l
    | JsonValueKind.String -> js.GetString()
    | JsonValueKind.Number -> js.GetDecimal()
    | JsonValueKind.True -> true
    | JsonValueKind.False -> false
    | JsonValueKind.Undefined
    | JsonValueKind.Null
    | _ -> null

let private parseNtfyPriority (v: string) =
    v
    |> _.Trim()
    |> _.ToLowerInvariant()
    |> function
        | "5"
        | "max"
        | "urgent" -> 5
        | "4"
        | "high" -> 4
        | "2"
        | "low" -> 2
        | "1"
        | "min" -> 1
        | "3"
        | "default"
        | _ -> 3

let private parseNtfyTags (v: string) =
    v |> _.Split(",") |> List.ofArray |> List.map _.Trim()

let private parseNtfyBool (v: string) =
    v
    |> _.Trim()
    |> _.ToLowerInvariant()
    |> function
        | "true"
        | "1"
        | "yes" -> true
        | _ -> false

/// A string value, with an optional name, in the [name=]value format.
type private NtfySimpleValue = string option * string

/// A value or ; separator token. These are strung together to define actions.
type private NtfySimpleWord =
    | Value of NtfySimpleValue
    | Separator

let private parseNtfyActions (v: string) =
    /// Read a sequence of values in the extras.\<param\>= and
    /// headers.\<header\>= formats.
    let mapOfStrings (name: string) (values: NtfySimpleValue list) =
        values
        |> Seq.choose (fun (v_name, v_value) ->
            match v_name |> Option.map _.Split(".", 2) with
            | Some [| m_name; m_key |] when m_name = name -> Some(m_key, v_value)
            | _ -> None)
        |> tryNonEmptyDict

    /// Parse an action from the "simple" non-JSON format for use in headers and
    /// query strings.
    let oneAction (values: NtfySimpleValue list) : IDictionary<string, obj> option =
        // Despite the name, there is nothing "simple" about this format. We
        // have to account for both the short and long versions and we have to
        // coerce values into maps and booleans where required.
        match values with
        | (Some "action", "view") :: (Some "label", label) :: (Some "url", url) :: rest
        | (None, "view") :: (None, label) :: (None, url) :: rest ->
            seq {
                "action", "view" :> obj
                "label", label
                "url", url

                yield!
                    rest
                    |> Seq.choose (fun (name, value) ->
                        match name with
                        | Some "clear" -> Some("clear", parseNtfyBool value :> obj)
                        | _ -> None)
            }
        | (Some "action", "broadcast") :: (Some "label", label) :: rest
        | (None, "broadcast") :: (None, label) :: rest ->
            seq {
                "action", "broadcast"
                "label", label

                yield!
                    rest
                    |> Seq.choose (fun (name, value) ->
                        match name with
                        | Some "intent" -> Some("intent", value :> obj)
                        | Some "clear" -> Some("clear", parseNtfyBool value)
                        | _ -> None)

                match mapOfStrings "extras" rest with
                | Some d -> "extras", d
                | None -> ()
            }
        | (Some "action", "http") :: (Some "label", label) :: (Some "url", url) :: rest
        | (None, "http") :: (None, label) :: (None, url) :: rest ->
            seq {
                "action", "http"
                "label", label
                "url", url

                yield!
                    rest
                    |> Seq.choose (fun (name, value) ->
                        match name with
                        | Some "method" -> Some("method", value :> obj)
                        | Some "body" -> Some("body", value)
                        | Some "clear" -> Some("clear", parseNtfyBool value)
                        | _ -> None)

                match mapOfStrings "headers" rest with
                | Some d -> "headers", d
                | None -> ()
            }
        | (Some "action", "copy") :: (Some "label", label) :: (Some "value", value) :: rest
        | (None, "copy") :: (None, label) :: (None, value) :: rest ->
            seq {
                "action", "copy"
                "label", label
                "value", value

                yield!
                    rest
                    |> Seq.choose (fun (name, value) ->
                        match name with
                        | Some "clear" -> Some("clear", parseNtfyBool value :> obj)
                        | _ -> None)
            }
        | _ -> Seq.empty
        |> tryNonEmptyDict

    let rec readWords (v: string) =
        match v with
        | "" -> []
        | _ ->
            let groups =
                Regex.Match(
                    v,
                    @"^\s*(?:(?<name>[\w.]+)=)?(?:'(?<value>[^']*)'|""(?<value>[^""]*)""|(?<value>[^,;]*))\s*(?<term>,|;|$)(?<rest>.*)"
                )
                |> _.Groups

            let name =
                groups.TryGetValue "name"
                |> tryGetByref
                |> Option.map _.Value
                |> Option.filter (not << String.IsNullOrEmpty)

            let value = groups["value"].Value
            let term = groups["term"].Value
            let rest = groups["rest"].Value

            [ Value(name, value)
              if term = ";" then
                  Separator ]
            @ readWords rest

    let asSimple (v: string) =
        ([], readWords v)
        // Split the list of words into sub-lists divided by ; separators.
        ||> List.fold (fun accum word ->
            let headAction, tailActions =
                match accum with
                | [] -> [], []
                | h :: t -> h, t

            match word with
            | Value value -> [ headAction @ [ value ] ] @ tailActions
            | Separator -> [ [] ] @ [ headAction ] @ tailActions)
        |> List.rev
        |> List.choose oneAction

    // Per the Ntfy spec, the actions field can be a straight-up JSON array, so
    // try parsing it as one first.
    let asJson =
        try
            Some(JsonDocument.Parse v)
        with _ ->
            None

    match asJson with
    | Some doc -> jsonLiquidModel doc.RootElement
    | None -> asSimple v

/// Map header and query string values from a publish request to a JSON-like
/// model for rendering by a template. See
/// https://docs.ntfy.sh/publish/#list-of-all-parameters and
/// https://docs.ntfy.sh/publish/#publish-as-json.
let ntfyModelFromRequest (request: HttpRequest) : IDictionary<string, obj> =
    seq {
        yield! request.Headers
        yield! request.Query
    }
    |> Seq.map (|KeyValue|)
    // Flatten StringValues into single strings. This is okay because no Ntfy
    // parameter relies on repetition of the same key.
    |> Seq.map (fun (k, sv) -> k, Seq.last sv)
    |> Seq.map (fun (k, v) -> k.ToLowerInvariant(), v)
    |> Seq.choose (fun (key, value) ->
        match key with
        | "x-message"
        | "message"
        | "m" -> Some("message", value :> obj)
        | "x-title"
        | "title"
        | "t" -> Some("title", value)
        | "x-priority"
        | "priority"
        | "prio"
        | "p" -> Some("priority", parseNtfyPriority value)
        | "x-tags"
        | "tags"
        | "tag"
        | "ta" -> Some("tags", parseNtfyTags value)
        | "x-actions"
        | "actions"
        | "action" -> Some("actions", parseNtfyActions value)
        | "x-click"
        | "click" -> Some("click", value)
        | "x-attach"
        | "attach"
        | "a" -> Some("attach", value)
        | "x-markdown"
        | "markdown"
        | "md" -> Some("markdown", parseNtfyBool value)
        | "x-icon"
        | "icon" -> Some("icon", value)
        | "x-filename"
        | "filename"
        | "file"
        | "f" -> Some("filename", value)
        | "x-email"
        | "x-e-mail"
        | "email"
        | "e-mail"
        | "mail"
        | "e" -> Some("email", value)
        | "x-call"
        | "call" -> Some("call", value)
        | "x-sequence-id"
        | "sequence-id"
        | "sid" -> Some("sequence_id", value)
        | _ -> None)
    |> dict
