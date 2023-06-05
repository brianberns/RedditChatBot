namespace RedditChatBot

open System
open System.Text.Json

open Microsoft.Azure.WebJobs
open Microsoft.Extensions.Configuration
open Microsoft.Extensions.Logging

open OpenAI.ObjectModels

module Post =

    /// Gets a random seed.
    let getSeed (log : ILogger) =
        let seed = DateTime.Now.Ticks % 1000000L
        log.LogWarning($"Seed: {seed}")
        seed

    /// # of retry attempts.
    let numTries = 3

    /// Submits a post
    let submit subredditName title body bot =
        Bot.tryN 3 (fun iTry ->
            try
                let post =
                    let subreddit =
                        bot.RedditBot.Client
                            .Subreddit(subredditName : string)
                            .About()
                    subreddit
                        .SelfPost(title, body)
                        .Submit()
                bot.Log.LogWarning($"Post submitted: {title}")   // use warning for emphasis in log
                true, Some post

            with exn ->
                bot.Log.LogError($"Error on post attempt #{iTry+1} of {numTries}")
                Bot.handleException exn bot.Log
                false, None)

module RandomThought =

    /// Random thought prompt.
    let getPrompt log =
        let seed = Post.getSeed log
        $"Using random seed {seed}, write a one-sentence thought to post on Reddit. Avoid politics and religion. The thought should be in the form of a statement, not a question. Output as JSON: {{ \"Thought\" : string }}."

    /// Structure of a completion. Must be public for serialization.
    type Completion = { Thought : string }

    /// Tries to post a random thought.
    let tryPost bot =
        Bot.tryN Post.numTries (fun _ ->
            let json = ChatBot.complete [] bot.ChatBot
            try
                let completion =
                    JsonSerializer.Deserialize<Completion>(json)
                let thought = completion.Thought.ToLower()
                if thought.Contains("random") || thought.Contains("seed") then
                    bot.Log.LogError($"Not a random thought: {completion.Thought}")
                    false, None
                else
                    true, Post.submit "RandomThoughts" completion.Thought "" bot
            with exn ->
                bot.Log.LogError(json)
                Bot.handleException exn bot.Log
                false, None)

module SixWordStory =

    /// Num-word story prompt.
    let getPrompt log =
        let seed = Post.getSeed log
        $"Using random seed {seed}, write a six-word story to post on Reddit. Output as JSON: {{ \"Story\" : string }}."

    /// Structure of a completion. Must be public for serialization.
    type Completion = { Story : string }

    /// Tries to post a six-word story.
    let tryPost bot =
        Bot.tryN Post.numTries (fun _ ->
            let json =
                ChatBot.complete [] bot.ChatBot
            try
                let completion =
                    JsonSerializer.Deserialize<Completion>(json)
                if completion.Story.Split(' ').Length = 6 then
                    true, Post.submit $"sixwordstories" completion.Story "" bot
                else
                    bot.Log.LogError($"Not a six-word story: {completion.Story}")
                    false, None
            with exn ->
                bot.Log.LogError(json)
                Bot.handleException exn bot.Log
                false, None)

/// Azure function type for dependency injection.
type FriendlyChatBot(config : IConfiguration) =

    /// Reply prompt.
    let replyPrompt =
        "You are a friendly Reddit user. Respond to the last user in the following thread. If you receive a comment that seems strange or irrelevant, do your best to play along."

    /// Creates a bot.
    let createBot prompt log =
        let settings = config.Get<AppSettings>()
        let redditBotDef =
            RedditBotDef.create
                "friendly-chat-bot"
                "1.0"
                "brianberns"
        let chatBotDef =
            ChatBotDef.create prompt Models.Gpt_4
        let bot = Bot.create settings redditBotDef chatBotDef log
        log.LogInformation("Bot initialized")
        bot

    /// Monitors unread messages.
    [<FunctionName("MonitorUnreadMessages")>]
    member _.MonitorUnreadMessages(
        [<TimerTrigger("0 */30 * * * *")>]         // every 30 minutes at :00 and :30 after the hour
        timer : TimerInfo,
        log : ILogger) =
        use bot = createBot replyPrompt log
        Bot.monitorUnreadMessages bot
            |> ignore

    /// Posts a random thought.
    [<FunctionName("PostRandomThought")>]
    member _.PostRandomThought(
        [<TimerTrigger("0 15 0,6,12,18 * * *")>]   // four times a day at 00:15, 06:15, 12:15, and 18:15
        timer : TimerInfo,
        log : ILogger) =
        use bot =
            let prompt = RandomThought.getPrompt log
            createBot prompt log
        RandomThought.tryPost bot
            |> ignore

    /// Posts a six-word story.
    [<FunctionName("PostSixWordStory")>]
    member _.PostSixWordStory(
        [<TimerTrigger("0 15 23 * * *")>]          // once a day at 23:15
        timer : TimerInfo,
        log : ILogger) =
        use bot =
            let prompt = SixWordStory.getPrompt log
            createBot prompt log
        SixWordStory.tryPost bot
            |> ignore
