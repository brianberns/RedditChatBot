﻿namespace RedditChatBot

open System
open Microsoft.Extensions.Configuration
open OpenAI.GPT3.ObjectModels

module ChatTest =

    let prompt =
        "Write a suprising six-word story that is not about aliens or animals."

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
