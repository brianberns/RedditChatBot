namespace RedditChatBot

open Microsoft.Azure.WebJobs
open Microsoft.Extensions.Configuration
open Microsoft.Extensions.Logging

/// Azure function type for dependency injection.
type TypicalChatBot(config : IConfiguration) =

    /// Reply prompt.
    let replyPrompt =
        """
You are a typical Reddit user. If you receive a comment
that seems strange or irrelevant, do your best to play along.
        """

    /// Runs the bot.
    [<FunctionName("MonitorUnreadMessages")>]
    member _.Run(
        [<TimerTrigger("0 * * * * *")>]   // every minute at second 0 (probably too fast for Reddit's spam filter)
        timer : TimerInfo,
        log : ILogger) =

        let settings = config.Get<AppSettings>()
        let redditBotDef =
            RedditBotDef.create
                "typical-chat-bot"
                "1.0"
                "brianberns"
        Bot.monitorUnreadMessages
            settings
            redditBotDef
            OpenAI.GPT3.ObjectModels.Models.Gpt_4
            replyPrompt
            log
