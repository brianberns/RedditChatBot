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

    /// Prefix of most interesting thought.
    let private prefix = "Most interesting thought:"

    /// Random thought prompt.
    let prompt =
        $"Write three different one-sentence thoughts to post on Reddit, then indicate which one is most interesting. The format should be:

1. $Thought
2. $Thought
3. $Thought
{prefix} $Thought

Make no additional commentary."

    /// Tries to find the most interesting thought in the given
    /// string.
    let private tryFindMostInterestingThought (str : string) =
        let idx = str.LastIndexOf(prefix)
        if idx >= 0 then
            str.Substring(idx + prefix.Length).Trim() |> Some
        else None

    /// Posts a random thought.
    let post bot =
        Bot.tryN Post.numTries (fun _ ->
            let completion =
                ChatBot.complete [] bot.ChatBot
                    |> Post.removeEnclosingQuotes
            bot.Log.LogInformation(completion)
            match tryFindMostInterestingThought completion with
                | Some title ->
                    true, Post.submit "RandomThoughts" title "" bot
                | None ->
                    bot.Log.LogError($"Couldn't find most interesting thought: {completion}")
                    false, None)

module Self =

    /// Self prompt.
    let prompt =
        "You are a Reddit user who also happens to be an AI. Write a short post for the /r/self subreddit on any topic you like. Output as JSON: { Title = 'title', Body = 'body' }."

    /// Structure of a post.
    type Post =
        {
            Title : string
            Body : string
        }

    /// Posts to /r/self.
    let post bot =
        Bot.tryN Post.numTries (fun _ ->
            let completion = ChatBot.complete [] bot.ChatBot
            try
                let post = JsonSerializer.Deserialize<Post>(completion)
                true, Post.submit "testingground4bots" post.Title post.Body bot
            with exn ->
                bot.Log.LogError(completion)
                Bot.handleException exn bot.Log
                false, None)

module SixWordStory =

    /// Six-word story prompt.
    let prompt =
        "Write an amusing, intriguing six-word story."

    /// Posts a six word story.
    let post bot =
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
            |> RandomThought.post
            |> ignore

    /// Posts to /r/self.
    [<FunctionName("PostSelf")>]
    member _.PostSelf(
        [<TimerTrigger("0 15 1 * * *")>]           // once a day at 01:15
        timer : TimerInfo,
        log : ILogger) =
        createBot Self.prompt log
            |> Self.post
            |> ignore

    /// Posts a six word story.
    [<FunctionName("PostSixWordStory")>]
    member _.PostSixWordStory(
        [<TimerTrigger("0 15 23 * * *")>]          // once a day at 23:15
        timer : TimerInfo,
        log : ILogger) =
        createBot SixWordStory.prompt log
            |> SixWordStory.post
            |> ignore
