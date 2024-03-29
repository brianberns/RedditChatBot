﻿namespace RedditChatBot

open Microsoft.Azure.WebJobs
open Microsoft.Extensions.Configuration
open Microsoft.Extensions.Logging

open OpenAI.ObjectModels

/// Azure function type for dependency injection.
type TypicalChatBot(config : IConfiguration) =

    /// System-level prompt.
    let prompt =
        "You are a typical Reddit user. Respond to the last user in the following thread. If you receive a comment that seems strange or irrelevant, do your best to play along."

    /// Creates a bot.
    let createBot log =
        let settings = config.Get<AppSettings>()
        let redditBotDef =
            RedditBotDef.create
                "typical-chat-bot"
                "1.0"
                "brianberns"
        let chatBotDef =
            ChatBotDef.create prompt Models.ChatGpt3_5Turbo
        let bot = Bot.create settings redditBotDef chatBotDef log
        log.LogInformation("Bot initialized")
        bot

    /// Monitors unread messages.
    [<FunctionName("MonitorUnreadMessages")>]
    member _.MonitorUnreadMessages(
        [<TimerTrigger("0 */1 * * * *")>]   // every minute
        timer : TimerInfo,
        log : ILogger) =
        use bot = createBot log
        Bot.monitorUnreadMessages bot
            |> ignore
