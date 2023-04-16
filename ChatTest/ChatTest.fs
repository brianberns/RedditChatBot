namespace RedditChatBot

open System
open Microsoft.Extensions.Configuration
open OpenAI.GPT3.ObjectModels

module ChatTest =

    let prompt =
        """
Generate a one-sentence post for the /r/ShowerThoughts subreddit.
        """

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
