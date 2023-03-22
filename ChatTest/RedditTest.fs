namespace RedditChatBot

open System
open Microsoft.Extensions.Configuration

module RedditTest =

    let redditClient =

        let settings =
            let cs =
                Environment.GetEnvironmentVariable("ConnectionString")
            ConfigurationBuilder()
                .AddAzureAppConfiguration(cs)
                .Build()
                .Get<AppSettings>()
        let botDesc =
            BotDescription.create
                "friendly-chat-bot"
                "1.0"
                "brianberns"
                ""
        Reddit.createClient settings.Reddit botDesc

    let test () =
        let messages =
            redditClient.Account.Messages
                .GetMessagesUnread(limit = 1000)
        printfn $"{messages.Count}"
