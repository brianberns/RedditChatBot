namespace RedditChatBot

open System

open Microsoft.Azure.WebJobs
open Microsoft.Extensions.Configuration
open Microsoft.Extensions.Logging

open OpenAI.GPT3.ObjectModels

/// Azure function type for dependency injection.
type FriendlyChatBot(config : IConfiguration) =

    /// Reply prompt.
    let replyPrompt =
        """
You are a friendly Reddit user. If you receive a comment
that seems strange or irrelevant, do your best to play along.
        """

    /// Post prompt.
    let postPrompt =
        """
Generate a strange post for the /r/randomthoughts subreddit.
Specify the title with "Title:" and a one-sentence body with "Body:".
        """

    /// Creates a bot.
    let createBot prompt log =
        let settings = config.Get<AppSettings>()
        let redditBotDef =
            RedditBotDef.create
                "friendly-chat-bot"
                "1.0"
                "brianberns"
        let chatBotDef =
            ChatBotDef.create prompt Models.Gpt_4
        let bot = Bot.create settings redditBotDef chatBotDef log
        log.LogInformation("Bot initialized")
        bot

    /// Trims the given prefix from the given string.
    let trimPrefix (prefix : string) (str : string) =
        if str.StartsWith(prefix) then
            str.Substring(prefix.Length).Trim()
        else failwith $"Missing prefix \"{prefix}\" in \"{str}\""

    /// Creates a random thought.
    let createRandomThought bot =
        let parts =
            let completion = ChatBot.complete [] bot.ChatBot
            completion.Split(
                '\n',
                StringSplitOptions.RemoveEmptyEntries)
                |> Seq.map (fun part -> part.Trim())
                |> Seq.toList
        match parts with
            | [ titlePart; bodyPart ] ->
                let title = trimPrefix "Title:" titlePart
                let body = trimPrefix "Body:" bodyPart
                title, body
            | _ -> failwith $"Unexpected number of parts: {parts}"

    /// Posts a random thought.
    let postRandomThought bot =
        let nTries = 3
        Bot.tryN nTries (fun iTry ->
            try
                let post =
                    let subreddit =
                        bot.RedditBot.Client
                            .Subreddit("RandomThoughts")
                            .About()
                    let title, body = createRandomThought bot
                    subreddit
                        .SelfPost(title, body)
                        .Submit()
                true, Some post

            with exn ->
                bot.Log.LogError($"Error on post attempt #{iTry+1} of {nTries}")
                Bot.handleException exn bot.Log
                false, None)

    /// Monitors unread messages.
    [<FunctionName("MonitorUnreadMessages")>]
    member _.MonitorUnreadMessages(
        [<TimerTrigger("0 */30 * * * *")>]   // twice an hour
        timer : TimerInfo,
        log : ILogger) =
        createBot replyPrompt log
            |> Bot.monitorUnreadMessages
            |> ignore

    /// Posts a random thought.
    [<FunctionName("PostRandomThought")>]
    member _.PostRandomThought(
        [<TimerTrigger("0 0 */3 * * *")>]   // every three hours
        timer : TimerInfo,
        log : ILogger) =
        createBot postPrompt log
            |> postRandomThought
            |> ignore
