namespace RedditChatBot

open OpenAI.GPT3
open OpenAI.GPT3.Managers
open OpenAI.GPT3.ObjectModels
open OpenAI.GPT3.ObjectModels.RequestModels

/// OpenAI settings associated with this app. Don't share these!
[<CLIMutable>]   // https://github.com/dotnet/runtime/issues/77677
type OpenAiSettings =
    {
        ApiKey : string
    }

/// Message roles.
[<RequireQualifiedAccess>]
type Role =

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
                | Role.User -> ChatMessage.FromUser
                | Role.Assistant -> ChatMessage.FromAssistant
        create msg.Content

/// Chonological sequence of chat messages.
type ChatHistory = List<FChatMessage>

module Chat =

    /// Creates a chat client.
    let createClient settings =
        OpenAiOptions(ApiKey = settings.ApiKey)
            |> OpenAIService

    /// Fixes prompt whitespace.
    let fixPrompt (prompt : string) =
        prompt
            .Replace("\r", "")
            .Replace("\n", " ")
            .Trim()

    /// Gets a response to the given chat history.
    let complete prompt (history : ChatHistory) (client : OpenAIService) =

            // build the request
        let req =
            let messages =
                [|
                    ChatMessage.FromSystem prompt
                    for msg in history do
                        FChatMessage.toNative msg
                |]
            ChatCompletionCreateRequest(
                Messages = messages,
                Model = Models.Gpt_4)

            // wait for the response (single-threaded, no point in getting fancy)
        let resp = client.ChatCompletion.CreateCompletion(req).Result
        if resp.Successful then
            let choice = Seq.exactlyOne resp.Choices
            choice.Message.Content.Trim()                       // some responses start with whitespace - why?
        elif resp.Error.Code = "context_length_exceeded" then   // e.g. "This model's maximum context length is 4097 tokens. However, your messages resulted in 4174 tokens. Please reduce the length of the messages."
            "Sorry, we've exceeded ChatGPT's maximum context length. Please start a new thread."
        else
            failwith $"{resp.Error.Message}"
