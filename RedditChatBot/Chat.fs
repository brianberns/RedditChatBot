namespace RedditChatBot

open OpenAI.GPT3
open OpenAI.GPT3.Managers
open OpenAI.GPT3.ObjectModels
open OpenAI.GPT3.ObjectModels.RequestModels

[<RequireQualifiedAccess>]
type Role =
    | System
    | User
    | Assistance

module Role =

    /// Creates chat message depending on role.
    let createMessage = function
        | System -> ChatMessage.FromSystem
        | User -> ChatMessage.FromUser
        | Assistance -> ChatMessage.FromAssistance

module Chat =

    let private settings = Settings.get.OpenAi

    let service =
        OpenAiOptions(ApiKey = settings.ApiKey)
            |> OpenAIService

    let chat context =

        let req =
            ChatCompletionCreateRequest(
                Messages =
                    ResizeArray [
                        Role.createMessage
                            Role.System
                            "Reply in the style of a typical Reddit user"
                        for (role, content) in context do
                            Role.createMessage role content
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
