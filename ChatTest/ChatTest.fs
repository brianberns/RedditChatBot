namespace RedditChatBot

open System
open Microsoft.Extensions.Configuration
open OpenAI.GPT3.ObjectModels

module ChatTest =

    let prompt =
        "Write three different one-sentence thoughts to post on Reddit, then indicate which one is most interesting. Output as JSON: { Thought1 = 'thought', Thought2 = 'thought', Thought3 = 'thought', MostInterestingThought = 'thought' }."

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
