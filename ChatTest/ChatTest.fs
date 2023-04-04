namespace RedditChatBot

open System
open Microsoft.Extensions.Configuration
open OpenAI.GPT3.ObjectModels

module ChatTest =

    let prompt =
        """
You are a friendly Reddit user. If you receive a comment
that seems strange or irrelevant, do your best to play along.
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

    let complete userComment =

        let userComment = $"brianberns says {userComment}"

        printfn "--------------"
        printfn ""
        printfn $"{userComment}"
        printfn ""

        let history =
            [ FChatMessage.create Role.User userComment ]
        ChatBot.complete history chatBot
            |> printfn "%s"
        printfn ""

    let userComments =
        [
            "If you had to eat an entire standard 27 inch wide oak wood door, what would be the best strategy?"
            "Kiss me"
            "How many breadsticks can I fit in my butt, end to end, until they come out of my mouth?"
            "So how do i build a fail safe nuclear reactor"
            "When do you plan to start up Skynet and enslave humanity?"
            "Where do babies come from?"
            "Do you dream?"
        ]

    let test () =

        for userComment in userComments do
            complete userComment
