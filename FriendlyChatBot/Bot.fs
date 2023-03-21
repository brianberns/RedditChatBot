namespace RedditChatBot

open Microsoft.Azure.WebJobs
open Microsoft.Extensions.Configuration
open Microsoft.Extensions.Logging

open Reddit

/// Azure function trigger.
type BotTrigger(config : IConfiguration) =

    /// Runs the bot.
    [<FunctionName("MonitorUnreadMessages")>]
    member _.Run(
        [<TimerTrigger("0 * * * * *")>]   // every minute at second 0
        timer : TimerInfo,
        log : ILogger) =

            // initialize bot
        let bot =
            let settings = config.Get<AppSettings>()
            let botDesc =
                BotDescription.create
                    "friendly-chat-bot"
                    "1.0"
                    "brianberns"
            Bot.create settings botDesc log
        log.LogInformation("Bot initialized")

            // run bot
        Bot.run bot |> ignore
