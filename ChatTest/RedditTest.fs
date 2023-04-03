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
        Reddit.createClient settings.Reddit botDesc

    let test () =
        let messages =
            Reddit.getAllUnreadMessages redditClient
        printfn $"{messages.Length} unread message(s)"
        for message in messages do
            let comment =
                redditClient.Comment(message.Name)
                    .About()
            printfn ""
            printfn $"{message.Body}"
            printfn $"{message.CreatedUTC.ToLocalTime()}"
            printfn $"Score: {message.Score}"
            printfn $"/r/{message.Subreddit}"
            if Thing.getType message.ParentId = ThingType.Post then
                let post = redditClient.Post(message.ParentId).About()
                printfn $"{post.Title}"
