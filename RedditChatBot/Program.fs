open Microsoft.Extensions.Configuration

(*
open OpenAI.GPT3
open OpenAI.GPT3.Managers
open OpenAI.GPT3.ObjectModels
open OpenAI.GPT3.ObjectModels.RequestModels
*)

open Reddit

type RedditSettings =
    {
        RefreshToken : string
        AppSecret : string
    }

type OpenAiSettings =
    {
        ApiKey : string
    }

[<CLIMutable>]   // https://github.com/dotnet/runtime/issues/77677
type Settings =
    {
        Reddit : RedditSettings
        OpenAi : OpenAiSettings
    }

let settings =
    ConfigurationBuilder()
        .AddJsonFile("appsettings.json")
        .Build()
        .Get<Settings>()

(*
let service =
    OpenAiOptions(ApiKey = settings.OpenAi.ApiKey)
        |> OpenAIService

let req =
    ChatCompletionCreateRequest(
        Messages =
            ResizeArray [
                ChatMessage.FromUser("Are you conscious?")
            ],
        Model = Models.ChatGpt3_5Turbo)
let resp =
    service.ChatCompletion.CreateCompletion(req).Result
if resp.Successful then
    for choice in resp.Choices do
        printfn $"{choice.Message.Content}"
else
    printfn $"Error: {resp.Error.Message}"
*)

let reddit =
    RedditClient(
        appId = "TQfEWCI3esJvFE95y0SxIw",
        refreshToken = settings.Reddit.RefreshToken,
        appSecret = settings.Reddit.AppSecret)

printfn $"Username: {reddit.Account.Me.Name}"
printfn $"Cake Day: {reddit.Account.Me.Created}"
