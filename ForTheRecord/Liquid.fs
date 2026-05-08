module ForTheRecord.Liquid

open System
open System.Reflection
open System.Text
open System.Text.Json
open System.Threading.Tasks

open Fluid
open Fluid.Values
open Markdig

open ForTheRecord.Helpers

/// Map of GitHub emoji codes to emojis.
let private gemojiMap =
    let assembly = Assembly.GetExecutingAssembly()

    use stream =
        assembly.GetManifestResourceStream $"{assembly.GetName().Name}.Gemoji.json"

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
