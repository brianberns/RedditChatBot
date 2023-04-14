namespace RedditChatBot

open System

open Microsoft.Azure.WebJobs
open Microsoft.Extensions.Configuration
open Microsoft.Extensions.Logging

open OpenAI.GPT3.ObjectModels

/// Azure function type for dependency injection.
type FriendlyChatBot(config : IConfiguration) =

    /// Reply prompt.
    let replyPrompt =
        """
You are a friendly Reddit user. If you receive a comment
that seems strange or irrelevant, do your best to play along.
        """

    /// Random thought prompt.
    let randomThoughtPrompt =
        """
Generate a random thought for the /r/RandomThoughts subreddit.
Specify the title with "Title:" and a one-sentence body with
"Body:".
        """

    /// Six word story prompt.
    let sixWordStoryPrompt =
        """
Generate a six-word story for the /r/sixwordstories subreddit.
        """

    /// Crazy idea prompt.
    let crazyIdeaPrompt =
        """
Generate a one-sentence crazy idea for the /r/CrazyIdeas subreddit.
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

    /// Trims the given prefix from the given string.
    let trimPrefix (prefix : string) (str : string) =
        if str.StartsWith(prefix) then
            str.Substring(prefix.Length).Trim()
        else failwith $"Missing prefix \"{prefix}\" in \"{str}\""

    /// Creates a random thought.
    let createRandomThought bot =
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

    /// # of retry attempts.
    let numTries = 3

    /// Submits a post
    let submitPost subredditName title body bot =
        Bot.tryN numTries (fun iTry ->
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

    /// Posts a random thought.
    let postRandomThought bot =
        let title, body = createRandomThought bot
        submitPost "RandomThoughts" title body bot

    /// Posts a six word story.
    let postSixWordStory bot =
        Bot.tryN numTries (fun _ ->
            let title = ChatBot.complete [] bot.ChatBot
            if title.Split(' ').Length = 6 then
                true, submitPost "sixwordstories" title "" bot
            else
                bot.Log.LogError($"Not a six-word story: {title}")
                false, None)

    /// Posts a crazy idea.
    let postCrazyIdea bot =
        let title = ChatBot.complete [] bot.ChatBot
        submitPost "CrazyIdeas" title "" bot

    /// Monitors unread messages.
    [<FunctionName("MonitorUnreadMessages")>]
    member _.MonitorUnreadMessages(
        [<TimerTrigger("0 */30 * * * *")>]   // every 30 minutes at :00 and :30 after the hour
        timer : TimerInfo,
        log : ILogger) =
        createBot replyPrompt log
            |> Bot.monitorUnreadMessages
            |> ignore

    /// Posts a random thought.
    [<FunctionName("PostRandomThought")>]
    member _.PostRandomThought(
        [<TimerTrigger("0 10 */12 * * *")>]   // every twelve hours at :10 after the hour
        timer : TimerInfo,
        log : ILogger) =
        createBot randomThoughtPrompt log
            |> postRandomThought
            |> ignore

    /// Posts a six word story.
    [<FunctionName("PostSixWordStory")>]
    member _.PostSixWordStory(
        [<TimerTrigger("0 20 0 * * *")>]     // every day at 00:20
        timer : TimerInfo,
        log : ILogger) =
        createBot sixWordStoryPrompt log
            |> postSixWordStory
            |> ignore

    /// Posts a crazy idea.
    [<FunctionName("PostCrazyIdea")>]
    member _.PostCrazyIdea(
        [<TimerTrigger("0 40 */12 * * *")>]   // every twelve hours at :40 after the hour
        timer : TimerInfo,
        log : ILogger) =
        createBot crazyIdeaPrompt log
            |> postCrazyIdea
            |> ignore
