namespace RedditChatBot

open System
open Microsoft.Extensions.Configuration

module RedditTest =

    let redditBot =

        let settings =
            let cs =
                Environment.GetEnvironmentVariable("ConnectionString")
            ConfigurationBuilder()
                .AddAzureAppConfiguration(cs)
                .Build()
                .Get<AppSettings>()
        let botDef =
            RedditBotDef.create
                "friendly-chat-bot"
                "1.0"
                "brianberns"
        RedditBot.create settings.Reddit botDef

    let rec getPost fullname =
        match Thing.getType fullname with
            | ThingType.Post ->
                redditBot.Client.Post(fullname).About()
            | ThingType.Comment ->
                let comment =
                    redditBot.Client.Comment(fullname).About()
                getPost comment.ParentFullname
            | _ -> failwith "Unexpected"

    let test () =
        let messages =
            RedditBot.getAllUnreadMessages redditBot
        printfn $"{messages.Length} unread message(s)"
        for message in messages do
            printfn ""
            printfn $"{message.Body}"
            printfn $"{message.CreatedUTC.ToLocalTime()}"
            printfn $"Score: {message.Score}"
            let post = getPost message.ParentId
            printfn $"{post.Title}"
            printfn $"https://www.reddit.com{message.Context}"
