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
    let createBot log =
        let settings = config.Get<AppSettings>()
        let redditBotDef =
            RedditBotDef.create
                "friendly-chat-bot"
                "1.0"
                "brianberns"
        let chatBotDef =
            ChatBotDef.create replyPrompt Models.Gpt_4
        let bot = Bot.create settings redditBotDef chatBotDef log
        log.LogInformation("Bot initialized")
        bot

    /// Posts a random thought.
    let postRandomThought bot =

        let bot =
            let chatBot =
                let chatBotDef =
                    { bot.ChatBot.BotDef with Prompt = postPrompt }
                { bot.ChatBot with BotDef = chatBotDef }
            { bot with ChatBot = chatBot }

        let nTries = 3
        Bot.tryN nTries (fun iTry ->
            try
                let post =
                    let subreddit =
                        bot.RedditBot.Client
                            .Subreddit("RandomThoughts")
                            .About()
                    let title, body =
                        let parts =
                            let completion = ChatBot.complete [] bot.ChatBot
                            completion.Split(
                                '\n',
                                StringSplitOptions.RemoveEmptyEntries)
                                |> Seq.map (fun part -> part.Trim())
                                |> Seq.toList
                        match parts with
                            | [ titlePart; bodyPart ] ->
                                let title =
                                    let prefix = "Title:"
                                    if titlePart.StartsWith(prefix) then
                                        titlePart.Substring(prefix.Length).Trim()
                                    else failwith "Unexpected title"
                                let body =
                                    let prefix = "Body:"
                                    if bodyPart.StartsWith(prefix) then
                                        bodyPart.Substring(prefix.Length).Trim()
                                    else failwith "Unexpected body"
                                title, body
                            | _ -> failwith "Unexpected number of parts"
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
        createBot log
            |> Bot.monitorUnreadMessages
            |> ignore

    /// Posts a random thought.
    [<FunctionName("PostRandomThought")>]
    member _.PostRandomThought(
        [<TimerTrigger("0 */180 * * * *")>]   // every three hours
        timer : TimerInfo,
        log : ILogger) =
        createBot log
            |> postRandomThought
