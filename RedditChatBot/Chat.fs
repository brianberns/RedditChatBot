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
    | Assistant

/// F# chat message type.
type FChatMessage =
    {
        /// Role of message originator.
        Role : Role

        /// Message content.
        Content : string
    }

module FChatMessage =

    /// Creates a chat message.
    let create role content =
        {
            Role = role
            Content = content
        }

    /// Converts a chat message to native format.
    let toNative msg =
        let create =
            match msg.Role with
                | Role.System -> ChatMessage.FromSystem
                | Role.User -> ChatMessage.FromUser
                | Role.Assistant -> ChatMessage.FromAssistant
        create msg.Content

/// Chonological sequence of chat messages.
type ChatHistory = List<FChatMessage>

module Chat =

    /// Chat settings.
    let private settings = Settings.get.OpenAi

    /// Chat service.
    let service =
        OpenAiOptions(ApiKey = settings.ApiKey)
            |> OpenAIService

    /// Gets a response to the given chat history.
    let complete (history : ChatHistory) =

            // build the request
        let req =
            let msgs =
                history
                    |> Seq.map FChatMessage.toNative
                    |> Seq.toArray
            ChatCompletionCreateRequest(
                Messages = msgs,
                Model = Models.ChatGpt3_5Turbo)

            // wait for the response (single-threaded, no point in getting fancy)
        let resp =
            service.ChatCompletion.CreateCompletion(req).Result
        if resp.Successful then
            let choice = Seq.exactlyOne resp.Choices
            choice.Message.Content.Trim()   // some responses start with whitespace - why?
        else
            failwith $"{resp.Error.Message}"
