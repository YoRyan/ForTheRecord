module ForTheRecord.Helpers

open System.IO
open System.Reflection
open System.Text.Json

/// Attempt a downcast without raising an exception if it fails.
let tryDowncast<'T> (o: obj) =
    match o with
    | :? 'T as v -> Some v
    | _ -> None

/// Convenience function to transform return values from C#-style TryGet...()
/// functions into F# options.
let tryGetByref<'T> =
    function
    | (true, v: 'T) -> Some v
    | false, _ -> None

/// Attempt to read a property from a JSON element, checking that it's actually
/// an object first. This is most useful for the top-level element, which is
/// usually (but need not necessarily be) an object.
let tryJsonProperty (name: string) (js: JsonElement) : JsonElement option =
    match js.ValueKind with
    | JsonValueKind.Object -> js.TryGetProperty name |> tryGetByref
    | _ -> None

/// Attempt to read a JSON property as a string.
let tryJsonString (js: JsonElement) =
    match js.ValueKind with
    | JsonValueKind.String -> js |> _.GetString() |> Some
    | _ -> None

/// Read a text file embedded in the executing assembly.
let readEmbeddedText (fileName: string) =
    let assembly = Assembly.GetExecutingAssembly()
    let name = $"{assembly.GetName().Name}.{fileName}"
    use stream = assembly.GetManifestResourceStream name
    use reader = new StreamReader(stream)
    reader.ReadToEndAsync()
