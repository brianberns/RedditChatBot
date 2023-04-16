namespace RedditChatBot

open System
open Microsoft.Extensions.Configuration
open OpenAI.GPT3.ObjectModels

module ChatTest =

    let prompt =
        """
Generate a one-sentence showerthought for the /r/ShowerThoughts subreddit.
A showerthought is a miniature epiphany that offers a new way of looking
at something familiar. Showerthoughts call attention to perspective-shifting
details which have been overlooked or dismissed, but which seem obvious in
retrospect.
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
