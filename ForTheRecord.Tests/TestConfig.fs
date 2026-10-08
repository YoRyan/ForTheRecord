module Tests.Config

open Xunit

open ForTheRecord.Smtp

[<Theory>]
[<InlineData("smtp://0.0.0.0", "0.0.0.0", 25)>]
[<InlineData("smtp://[::]", "::", 25)>]
[<InlineData("smtp://0.0.0.0:2525", "0.0.0.0", 2525)>]
[<InlineData("smtp://[::]:2525", "::", 2525)>]
[<InlineData("smtp://localhost", "127.0.0.1", 25)>]
[<InlineData("smtp://localhost", "::1", 25)>]
[<InlineData("smtp://*", "0.0.0.0", 25)>]
[<InlineData("smtp://*", "::", 25)>]
[<InlineData("smtp://+:2525", "0.0.0.0", 2525)>]
[<InlineData("smtp://+:2525", "::", 2525)>]
let ``Configured SMTP endpoint parses correct address and port`` (url: string) (ip: string) (port: int) =
    match smtpUrlEndpoints url with
    | Ok eps ->
        eps
        |> List.exists (fun ep -> ep.Address.ToString() = ip && ep.Port = port)
        |> Assert.True
    | Error _ ->
        Assert.Fail()