namespace RedditChatBot

open OpenAI.GPT3
open OpenAI.GPT3.Managers
open OpenAI.GPT3.ObjectModels
open OpenAI.GPT3.ObjectModels.RequestModels

[<RequireQualifiedAccess>]
type Role =

    /// System-level instruction.
    | System

    /// User query.
    | User

    /// ChatGPT response.
    | Assistance

module Role =

    /// Creates chat message depending on role.
    let createMessage = function
        | Role.System -> ChatMessage.FromSystem
        | Role.User -> ChatMessage.FromUser
        | Role.Assistance -> ChatMessage.FromAssistance

module Chat =

    let private settings = Settings.get.OpenAi

    let service =
        OpenAiOptions(ApiKey = settings.ApiKey)
            |> OpenAIService

    /// Initial prompt.
    let private prompt =
        Role.createMessage
            Role.System
            "Reply in the style of a typical Reddit user"

    /// Gets a reponse to the given message context.
    let chat context =
        let req =
            ChatCompletionCreateRequest(
                Messages =
                    ResizeArray [
                        prompt
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
