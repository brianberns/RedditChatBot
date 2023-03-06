namespace RedditChatBot

open OpenAI.GPT3
open OpenAI.GPT3.Managers
open OpenAI.GPT3.ObjectModels
open OpenAI.GPT3.ObjectModels.RequestModels

module Chat =

    let private settings = Settings.get.OpenAi

    let service =
        OpenAiOptions(ApiKey = settings.ApiKey)
            |> OpenAIService

    let chat content =

        let req =
            ChatCompletionCreateRequest(
                Messages =
                    ResizeArray [
                        ChatMessage.FromSystem("Reply in the style of a typical Reddit user")
                        ChatMessage.FromUser(content)
                    ],
                Model = Models.ChatGpt3_5Turbo)
        let resp =
            service.ChatCompletion.CreateCompletion(req).Result
        if resp.Successful then
            resp.Choices
                |> Seq.map (fun choice ->
                    choice.Message.Content)
                |> Seq.exactlyOne
        else
            failwith resp.Error.Message
