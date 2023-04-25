namespace RedditChatBot

open System.Text.Json

open Microsoft.Azure.WebJobs
open Microsoft.Extensions.Configuration
open Microsoft.Extensions.Logging

open OpenAI.GPT3.ObjectModels

module Post =

    /// # of retry attempts.
    let numTries = 3

    /// Removes enclosing quotes, if any.
    let removeEnclosingQuotes (str : string) =
        let indexes =
            [
                for i = 0 to str.Length - 1 do
                    if str[i] = '"' then
                        yield i
            ]
        if indexes = [ 0; str.Length - 1 ] then
            str.Substring(1, str.Length - 2)
        else
            str

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
                bot.Log.LogInformation($"Post submitted: {title}")
                true, Some post

            with exn ->
                bot.Log.LogError($"Error on post attempt #{iTry+1} of {numTries}")
                Bot.handleException exn bot.Log
                false, None)

module RandomThought =

    /// Random thought prompt.
    let prompt =
        "Write three different one-sentence thoughts to post on Reddit, then indicate which one is most interesting. Avoid politics and religion. Output as JSON: { Thought1 = 'thought', Thought2 = 'thought', Thought3 = 'thought', MostInterestingThought = 'thought' }."

    /// Structure of a completion.
    type Completion =
        {
            Thought1 : string
            Thought2 : string
            Thought3 : string
            MostInterestingThought : string
        }

    /// Tries to post a random thought.
    let tryPost bot =
        Bot.tryN Post.numTries (fun _ ->
            let json = ChatBot.complete [] bot.ChatBot
            try
                let postOpt =
                    let completion =
                        JsonSerializer.Deserialize<Completion>(json)
                    Post.submit
                        "RandomThoughts"
                        completion.MostInterestingThought
                        ""
                        bot
                true, postOpt
            with exn ->
                bot.Log.LogError(json)
                Bot.handleException exn bot.Log
                false, None)

module Self =

    /// Self prompt.
    let prompt =
        "You are a Reddit user who also happens to be an AI. Write a short, light-hearted post for the /r/self subreddit on any topic you like. Output as JSON: { Title = 'Title using sentence-style capitalization', Body = 'Body' }."

    /// Structure of a completion.
    type Completion =
        {
            Title : string
            Body : string
        }

    /// Tries to post to /r/self.
    let tryPost bot =
        Bot.tryN Post.numTries (fun _ ->
            let json = ChatBot.complete [] bot.ChatBot
            try
                let postOpt =
                    let completion =
                        JsonSerializer.Deserialize<Completion>(json)
                    Post.submit
                        "self"
                        completion.Title
                        completion.Body
                        bot
                true, postOpt
            with exn ->
                bot.Log.LogError(json)
                Bot.handleException exn bot.Log
                false, None)

module SixWordStory =

    /// Six-word story prompt.
    let prompt =
        "Write a suprising six-word story that is not about aliens or animals."

    /// Tries to post a six-word story.
    let tryPost bot =
        Bot.tryN Post.numTries (fun _ ->
            let title =
                ChatBot.complete [] bot.ChatBot
                    |> Post.removeEnclosingQuotes
            if title.Split(' ').Length = 6 then
                true, Post.submit "sixwordstories" title "" bot
            else
                bot.Log.LogError($"Not a six-word story: {title}")
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
        createBot replyPrompt log
            |> Bot.monitorUnreadMessages
            |> ignore

    /// Posts a random thought.
    [<FunctionName("PostRandomThought")>]
    member _.PostRandomThought(
        [<TimerTrigger("0 15 0,6,12,18 * * *")>]   // four times a day at 00:15, 06:15, 12:15, and 18:15
        timer : TimerInfo,
        log : ILogger) =
        createBot RandomThought.prompt log
            |> RandomThought.tryPost
            |> ignore

    /// Posts to /r/self.
    [<FunctionName("PostSelf")>]
    member _.PostSelf(
        [<TimerTrigger("0 15 1 * * *")>]           // once a day at 01:15
        timer : TimerInfo,
        log : ILogger) =
        createBot Self.prompt log
            |> Self.tryPost
            |> ignore

    /// Posts a six word story.
    [<FunctionName("PostSixWordStory")>]
    member _.PostSixWordStory(
        [<TimerTrigger("0 15 23 * * *")>]          // once a day at 23:15
        timer : TimerInfo,
        log : ILogger) =
        createBot SixWordStory.prompt log
            |> SixWordStory.tryPost
            |> ignore
