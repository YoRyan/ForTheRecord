module ForTheRecord.Helpers

open System.Text.Json

/// Attempt a downcast without raising an exception if it fails.
let tryDowncast<'T> (o: obj) =
    match o with
    | :? 'T as v -> Some v
    | _ -> None

/// Convenience function to transform return values from C#-style TryGet...()
/// functions into F# options.
let tryGetOption<'T> =
    function
    | (true, v: 'T) -> Some v
    | false, _ -> None

/// Attempt to read a property from a JSON element, checking that it's actually
/// an object first. This is most useful for the top-level element, which is
/// usually (but need not necessarily be) an object.
let tryJsonProperty (name: string) (js: JsonElement) : JsonElement option =
    match js.ValueKind with
    | JsonValueKind.Object -> js.TryGetProperty name |> tryGetOption
    | _ -> None
