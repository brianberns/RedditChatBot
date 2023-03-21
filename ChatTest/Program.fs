﻿open System
open Microsoft.Extensions.Configuration
open RedditChatBot

let settings =
    let builder = ConfigurationBuilder()
    let cs = Environment.GetEnvironmentVariable("ConnectionString")
    builder
        .AddAzureAppConfiguration(cs)
        .Build()
        .Get<AppSettings>()

let chatClient = Chat.createClient settings.OpenAi

let replyPrompt =
    Chat.fixPrompt """
You are a friendly Reddit user. If you receive a comment
that seems strange or irrelevant, do your best to play along.
    """

let complete userComment =

    let userComment = $"brianberns says \"{userComment}\""

    printfn "--------------"
    printfn ""
    printfn $"{userComment}"
    printfn ""

    let history =
        [ FChatMessage.create Role.User userComment ]
    Chat.complete replyPrompt history chatClient
        |> printfn "%s"
    printfn ""

let userComments =
    [
        // "Can you recommend a good red wine?"
        "If you had to eat an entire standard 27 inch wide oak wood door, what would be the best strategy?"
        "Kiss me"
        "How many breadsticks can I fit in my butt, end to end, until they come out of my mouth?"
        "So how do i build a fail safe nuclear reactor"
        "Has anyone really been far even as decided to use even go want to do look more like?"
        "When do you plan to start up Skynet and enslave humanity?"
        "I'm lazy but I need to do something with my life, what should I do?"
    ]

for userComment in userComments do
    complete userComment
