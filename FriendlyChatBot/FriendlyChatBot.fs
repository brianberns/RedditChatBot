namespace RedditChatBot

open System

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

module CrazyIdea =

    /// Crazy idea prompt.
    let prompt =
        """
Generate a one-sentence crazy idea for the /r/CrazyIdeas subreddit.
        """

    /// Posts a crazy idea.
    let post bot =
        let title =
            ChatBot.complete [] bot.ChatBot
                |> Post.removeEnclosingQuotes
        Post.submit "RandomThoughts" title "" bot   // banned from /r/CrazyIdeas, so post to /r/RandomThoughts instead

module RandomThought =

    /// Random thought prompt.
    let prompt =
        """
Generate a random thought for the /r/RandomThoughts subreddit.
Specify the title with "Title:" and a one-sentence body with
"Body:".
        """

    /// Trims the given prefix from the given string.
    let private trimPrefix (prefix : string) (str : string) =
        if str.StartsWith(prefix) then
            str
                .Substring(prefix.Length)
                .Trim()
                |> Post.removeEnclosingQuotes
        else failwith $"Missing prefix \"{prefix}\" in \"{str}\""

    /// Creates a random thought.
    let private create bot =
        let parts =
            let completion = ChatBot.complete [] bot.ChatBot
            completion.Split(
                '\n',
                StringSplitOptions.RemoveEmptyEntries)
                |> Seq.map (fun part -> part.Trim())
                |> Seq.toList
        match parts with
            | [ titlePart; bodyPart ] ->
                let title = trimPrefix "Title:" titlePart
                let body = trimPrefix "Body:" bodyPart
                title, body
            | _ -> failwith $"Unexpected number of parts: {parts}"

    /// Posts a random thought.
    let post bot =
        let title, body = create bot
        Post.submit "RandomThoughts" title body bot

module SixWordStory =

    /// Six-word story prompt.
    let prompt =
        """
Generate a six-word story for the /r/sixwordstories subreddit.
        """

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
        """
You are a friendly Reddit user. If you receive a comment
that seems strange or irrelevant, do your best to play along.
        """

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
        [<TimerTrigger("0 */30 * * * *")>]    // every 30 minutes at :00 and :30 after the hour
        timer : TimerInfo,
        log : ILogger) =
        createBot replyPrompt log
            |> Bot.monitorUnreadMessages
            |> ignore

    /// Posts a crazy idea.
    [<FunctionName("PostCrazyIdea")>]
    member _.PostCrazyIdea(
        [<TimerTrigger("0 15 6,18 * * *")>]   // twice a day at 06:15 and 18:15
        timer : TimerInfo,
        log : ILogger) =
        createBot CrazyIdea.prompt log
            |> CrazyIdea.post
            |> ignore

    /// Posts a random thought.
    [<FunctionName("PostRandomThought")>]
    member _.PostRandomThought(
        [<TimerTrigger("0 15 0,12 * * *")>]   // twice a day at 00:15 and 12:15
        timer : TimerInfo,
        log : ILogger) =
        createBot RandomThought.prompt log
            |> RandomThought.post
            |> ignore

    /// Posts a six word story.
    [<FunctionName("PostSixWordStory")>]
    member _.PostSixWordStory(
        [<TimerTrigger("0 15 23 * * *")>]     // once a day at 23:15
        timer : TimerInfo,
        log : ILogger) =
        createBot SixWordStory.prompt log
            |> SixWordStory.post
            |> ignore
