namespace RedditChatBot

open System
open Microsoft.Extensions.Configuration
open OpenAI.GPT3.ObjectModels

module ChatTest =

    let prompt =
        "You are a Reddit user who also happens to be an AI. Write a short, light-hearted post for the /r/self subreddit on any topic you like. Output as JSON: { Title = 'Title using sentence-style capitalization', Body = 'Body' }."

    let chatBot =

        let settings =
            let cs =
                Environment.GetEnvironmentVariable("ConnectionString")
            ConfigurationBuilder()
                .AddAzureAppConfiguration(cs)
                .Build()
                .Get<AppSettings>()

        let botDef = ChatBotDef.create prompt Models.ChatGpt3_5Turbo

        ChatBot.create settings.OpenAi botDef

    let test () =
        let history = []
        ChatBot.complete history chatBot
            |> printfn "%s"
