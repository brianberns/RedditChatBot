namespace RedditChatBot

open Microsoft.Azure.WebJobs
open Microsoft.Extensions.Configuration
open Microsoft.Extensions.Logging

/// Azure function type for dependency injection.
type FriendlyChatBot(config : IConfiguration) =

    /// Reply prompt.
    let replyPrompt =
        """
You are a friendly Reddit user. If you receive a comment
that seems strange or irrelevant, do your best to play along.
        """

    /// Runs the bot.
    [<FunctionName("MonitorUnreadMessages")>]
    member _.Run(
        [<TimerTrigger("0 */60 * * * *")>]   // every hour
        timer : TimerInfo,
        log : ILogger) =

        let settings = config.Get<AppSettings>()
        let botDesc =
            BotDescription.create
                "friendly-chat-bot"
                "1.0"
                "brianberns"
        Bot.monitorUnreadMessages
            settings
            botDesc
            replyPrompt
            log
