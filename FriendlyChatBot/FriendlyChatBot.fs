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

/// A type of story consisting of N words.
type NumWordStoryDef =
    {
        /// Number of words in a story.
        NumWords : int

        /// Number name. (E.g. "six".)
        Name : string
    }

module NumWordStory =

    /// Creates a num-word story definition.
    let private define numWords name =
        {
            NumWords = numWords
            Name = name
        }

    /// Num-word story definitions.
    let private defs =
        [
            define 2 "two"
            define 5 "five"
            define 6 "six"
        ]

    /// Num-word story prompt.
    let private getPrompt def log =
        let seed = Post.getSeed log
        $"Using random seed {seed}, write a {def.Name}-word story to post on Reddit. Output as JSON: {{ \"Story\" : string }}."

    /// Structure of a completion. Must be public for serialization.
    type Completion = { Story : string }

    /// Tries to post a num-word story.
    let private tryPost def bot =
        Bot.tryN Post.numTries (fun _ ->
            let json =
                ChatBot.complete [] bot.ChatBot
            try
                let completion =
                    JsonSerializer.Deserialize<Completion>(json)
                if completion.Story.Split(' ').Length = def.NumWords then
                    true, Post.submit $"{def.Name}wordstories" completion.Story "" bot
                else
                    bot.Log.LogError($"Not a {def.Name}-word story: {completion.Story}")
                    false, None
            with exn ->
                bot.Log.LogError(json)
                Bot.handleException exn bot.Log
                false, None)

    /// Creates and runs a bot for the given number of words.
    let run numWords createBot log =
        let def =
            Seq.find (fun def -> def.NumWords = numWords) defs
        use bot =
            let prompt = getPrompt def log
            createBot prompt log
        tryPost def bot |> ignore

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
        let prompt = RandomThought.getPrompt log
        use bot = createBot prompt log
        RandomThought.tryPost bot
            |> ignore

    /// Posts a two-word story.
    [<FunctionName("PostTwoWordStory")>]
    member _.PostTwoWordStory(
        [<TimerTrigger("0 15 21 * * *")>]          // once a day at 21:15
        timer : TimerInfo,
        log : ILogger) =
        NumWordStory.run 2 createBot log

    /// Posts a five-word story.
    [<FunctionName("PostFiveWordStory")>]
    member _.PostFiveWordStory(
        [<TimerTrigger("0 15 22 * * *")>]          // once a day at 22:15
        timer : TimerInfo,
        log : ILogger) =
        NumWordStory.run 5 createBot log

    /// Posts a six-word story.
    [<FunctionName("PostSixWordStory")>]
    member _.PostSixWordStory(
        [<TimerTrigger("0 15 23 * * *")>]          // once a day at 23:15
        timer : TimerInfo,
        log : ILogger) =
        NumWordStory.run 6 createBot log
