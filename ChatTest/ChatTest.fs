namespace RedditChatBot

open System
open Microsoft.Extensions.Configuration
open OpenAI.ObjectModels

module ChatTest =

    let prompt =
        "Choose a random noun, then write a one-sentence thought about it to post on Reddit. The thought should be in the form of a statement, not a question. Output as JSON: { \"Noun\" : string, \"Story\" : string }."

    let chatBot =

        let settings =
            let cs =
                Environment.GetEnvironmentVariable("ConnectionString")
            ConfigurationBuilder()
                .AddAzureAppConfiguration(cs)
                .Build()
                .Get<AppSettings>()

        let botDef = ChatBotDef.create prompt Models.Gpt_4

        ChatBot.create settings.OpenAi botDef

    let test () =
        let history = []
        ChatBot.complete history chatBot
            |> printfn "%s"
