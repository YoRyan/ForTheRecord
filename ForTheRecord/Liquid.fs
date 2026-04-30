module ForTheRecord.Liquid

open System
open System.Text
open System.Text.Json
open System.Threading.Tasks

open Fluid
open Fluid.Values

type Filters() =
    /// Encodes an UTF-8 string inline with base64 so that it is safe for use in
    /// email headers.
    static member encodeBase64 (input: FluidValue) (_arguments: FilterArguments) (_context: TemplateContext) =
        input
        |> _.ToStringValue()
        |> Encoding.UTF8.GetBytes
        |> Convert.ToBase64String
        |> fun b64 -> $"=?UTF-8?B?{b64}?="
        |> StringValue
        |> fun sv -> sv :> FluidValue
        |> ValueTask.FromResult

/// Create a template options object with our custom filters configured.
let createTemplateOptions () =
    let options = TemplateOptions()

    for name, d in seq { "ftr_encode_base64", Filters.encodeBase64 } do
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
