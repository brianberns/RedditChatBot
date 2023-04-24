namespace RedditChatBot

open Microsoft.Azure.WebJobs
open Microsoft.Extensions.Configuration
open Microsoft.Extensions.Logging

open OpenAI.GPT3.ObjectModels

/// Azure function type for dependency injection.
type TypicalChatBot(config : IConfiguration) =

    /// System-level prompt.
    let prompt =
        "You are a typical Reddit user. Respond in the second person. If you receive a comment that seems strange or irrelevant, do your best to play along."

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
        createBot log
            |> Bot.monitorUnreadMessages
            |> ignore
