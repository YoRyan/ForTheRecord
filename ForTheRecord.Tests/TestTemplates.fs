module Tests.Templates

open System.Net
open System.Net.Http

open Xunit

open Fixtures

[<Fact>]
let ``Endpoints require Json request content`` () =
    let config, mock = mockGmailWithoutAuth ()

    let request = new HttpRequestMessage(HttpMethod.Post, "/apprise")

    use content = makeTextContent "Definitely not JSON!"
    request.Content <- content

    let response = testRequest config request
    Assert.Equal(HttpStatusCode.NotAcceptable, response.StatusCode)

[<Fact>]
let ``Endpoints require Gmail insert scope when Gmail is configured`` () =
    let config, mock = mockGmailWithHunter2Auth "AzureDiamond" false true
    let authHeader = makeBasicAuth "AzureDiamond" "hunter2"

    let request = new HttpRequestMessage(HttpMethod.Post, "/apprise")
    request.Headers.Authorization <- authHeader

    use content =
        { title = "Title"
          message = "Whatever"
          ``type`` = "info"
          attachments = []
          ftr_forcefarmot = None
          ftr_forceformat = None }
        |> makeJsonContent

    request.Content <- content

    let response = testRequest config request
    Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode)
