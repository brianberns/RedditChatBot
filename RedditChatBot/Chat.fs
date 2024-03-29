﻿namespace RedditChatBot

open System

open OpenAI
open OpenAI.Managers
open OpenAI.ObjectModels.RequestModels

/// OpenAI settings associated with this app. Don't share these!
[<CLIMutable>]   // https://github.com/dotnet/runtime/issues/77677
type OpenAiSettings =
    {
        ApiKey : string
    }

/// Chat bot definition.
type ChatBotDef =
    {
        /// System-level prompt.
        Prompt : string

        /// GPT model.
        Model : string
    }

module ChatBotDef =

    /// Creates a chat bot definition.
    let create prompt model =
        {
            Prompt = prompt
            Model = model
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

/// A chat bot.
type ChatBot =
    {
        /// Bot definition.
        BotDef : ChatBotDef

        /// OpenAI API client.
        Client : OpenAIService
    }

    member bot.Dispose() = bot.Client.Dispose()

    interface IDisposable with
        member bot.Dispose() = bot.Dispose()

module ChatBot =

    /// Creates a chat bot.
    let create settings botDef =
        let client =
            let options = OpenAiOptions(ApiKey = settings.ApiKey)
            new OpenAIService(options)
        {
            BotDef = botDef
            Client = client
        }

    /// Gets a response to the given chat history.
    let complete (history : ChatHistory) bot =

            // build the request
        let req =
            let messages =
                [|
                    ChatMessage.FromSystem bot.BotDef.Prompt
                    for msg in history do
                        FChatMessage.toNative msg
                |]
            ChatCompletionCreateRequest(
                Messages = messages,
                Model = bot.BotDef.Model,
                Temperature = 1.0f)

            // wait for the response (single-threaded, no point in getting fancy)
        let resp =
            bot.Client.ChatCompletion
                .CreateCompletion(req)
                .Result
        if resp.Successful then
            let choice = Seq.exactlyOne resp.Choices
            choice.Message.Content.Trim()                       // some responses start with whitespace - why?
        elif resp.Error.Code = "context_length_exceeded" then   // e.g. "This model's maximum context length is 4097 tokens. However, your messages resulted in 4174 tokens. Please reduce the length of the messages."
            "Sorry, we've exceeded ChatGPT's maximum context length. Please start a new thread."
        else
            failwith $"{resp.Error.Message}"
