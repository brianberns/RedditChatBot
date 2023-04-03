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

    let rec getPost fullname =
        match Thing.getType fullname with
            | ThingType.Post ->
                redditClient.Post(fullname).About()
            | ThingType.Comment ->
                let comment = redditClient.Comment(fullname).About()
                getPost comment.ParentFullname
            | _ -> failwith "Unexpected"

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
            let post = getPost message.ParentId
            printfn $"{post.Title}"
            printfn $"https://www.reddit.com{message.Context}"
