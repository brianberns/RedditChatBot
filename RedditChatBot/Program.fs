namespace RedditChatBot

open System
open Microsoft.Extensions.Configuration

(*
open OpenAI.GPT3
open OpenAI.GPT3.Managers
open OpenAI.GPT3.ObjectModels
open OpenAI.GPT3.ObjectModels.RequestModels
*)

open Reddit

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

module Reddit =

    let private settings = Settings.get.Reddit

    let client =
        RedditClient(
            appId = "fVstFww14kdp4hFRJCCzdg",
            refreshToken = settings.RefreshToken,
            appSecret = settings.AppSecret)

    let monitor (post : Controllers.Post) callback =

        let flag = post.Comments.MonitorNew()
        assert(flag)

        post.Comments.NewUpdated.Add(callback)

        {
            new IDisposable with
                member _.Dispose() =
                    let flagOff = post.Comments.MonitorNew()
                    assert(not flagOff)
        }

module Program =

    [<EntryPoint>]
    let main args =
        use _ =
            let post = Reddit.client.Post("t3_11glnkd")   // "I am a ChatGPT bot"
            Reddit.monitor post (fun evt ->
                for comment in evt.Added do
                    printfn $"Comment added: {comment.Body}")

        Console.ReadLine() |> ignore
        0
