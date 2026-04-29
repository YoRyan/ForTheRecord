module ForTheRecord.Liquid

open System.Text.Json

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
