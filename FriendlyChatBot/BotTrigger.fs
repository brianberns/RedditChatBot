namespace RedditChatBot

open Microsoft.Azure.WebJobs
open Microsoft.Extensions.Configuration
open Microsoft.Extensions.Logging

/// Azure function trigger.
type BotTrigger(config : IConfiguration) =

    /// Runs the bot.
    [<FunctionName("MonitorUnreadMessages")>]
    member _.Run(
        [<TimerTrigger("0 * * * * *")>]   // every minute at second 0
        timer : TimerInfo,
        log : ILogger) =

        let settings = config.Get<AppSettings>()
        let botDesc =
            BotDescription.create
                "friendly-chat-bot"
                "1.0"
                "brianberns"
        Bot.monitorUnreadMessages settings botDesc log
