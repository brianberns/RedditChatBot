namespace RedditChatBot

open Microsoft.Azure.WebJobs
open Microsoft.Extensions.Configuration
open Microsoft.Extensions.Logging

open OpenAI.GPT3.ObjectModels

/// Azure function type for dependency injection.
type FriendlyChatBot(config : IConfiguration) =

    /// System-level prompt.
    let prompt =
        """
You are a friendly Reddit user. If you receive a comment
that seems strange or irrelevant, do your best to play along.
        """

    /// Runs the bot.
    [<FunctionName("MonitorUnreadMessages")>]
    member _.Run(
        [<TimerTrigger("0 */30 * * * *")>]   // twice an hour
        timer : TimerInfo,
        log : ILogger) =

            // initialize bot
        let bot =
            let settings = config.Get<AppSettings>()
            let redditBotDef =
                RedditBotDef.create
                    "friendly-chat-bot"
                    "1.0"
                    "brianberns"
            let chatBotDef =
                ChatBotDef.create prompt Models.Gpt_4
            Bot.create settings redditBotDef chatBotDef log
        log.LogInformation("Bot initialized")

            // run bot
        Bot.monitorUnreadMessages bot |> ignore
