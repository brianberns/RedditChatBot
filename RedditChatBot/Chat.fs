namespace RedditChatBot

open OpenAI.GPT3
open OpenAI.GPT3.Managers
open OpenAI.GPT3.ObjectModels
open OpenAI.GPT3.ObjectModels.RequestModels

/// Message roles.
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

/// Messages in chronological order.
type History = seq<Role * string>

module Chat =

    /// Chat settings.
    let private settings = Settings.get.OpenAi

    /// Chat service.
    let service =
        OpenAiOptions(ApiKey = settings.ApiKey)
            |> OpenAIService

    /// Initial prompt.
    let private prompt =
        Role.createMessage
            Role.System
            "Reply in the style of a kind Reddit user"

    /// Gets a reponse to the given message history.
    let chat (history : History) =

            // build the request
        let req =
            ChatCompletionCreateRequest(
                Messages =
                    ResizeArray [
                        prompt
                        for (role, content) in history do
                            Role.createMessage role content
                    ],
                Model = Models.ChatGpt3_5Turbo)

            // wait for the response (single-threaded, no point in getting fancy)
        let resp =
            service.ChatCompletion.CreateCompletion(req).Result
        if resp.Successful then
            resp.Choices
                |> Seq.map (fun choice ->
                    choice.Message.Content)
                |> Seq.exactlyOne
        else
            failwith resp.Error.Message
