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
        appId = "fVstFww14kdp4hFRJCCzdg",
        refreshToken = settings.Reddit.RefreshToken,
        appSecret = settings.Reddit.AppSecret)

let selfPost = reddit.SelfPost("t3_11glnkd")   // "I am a ChatGPT bot"

let flagOn = selfPost.Comments.MonitorNew()
assert(flagOn)

selfPost.Comments.NewUpdated.Add(fun evt ->
    for comment in evt.Added do
        printfn $"Comment added: {comment.Body}")

System.Console.ReadLine() |> ignore

let flagOff = selfPost.Comments.MonitorNew()
assert(not flagOff)
