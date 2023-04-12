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
Generate a strange post for the /r/RandomThoughts subreddit.
The title must not be a question. Specify the title with
"Title:" and a one-sentence body with "Body:".
        """

    /// Six word story prompt.
    let sixWordStoryPrompt =
        "Write a six word story with a twist ending"

    /// Creates a bot.
    let createBot prompt model log =
        let settings = config.Get<AppSettings>()
        let redditBotDef =
            RedditBotDef.create
                "friendly-chat-bot"
                "1.0"
                "brianberns"
        let chatBotDef =
            ChatBotDef.create prompt model
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
    let nTries = 3

    /// Submits a post
    let submitPost subredditName title body bot =
        Bot.tryN nTries (fun iTry ->
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
                bot.Log.LogError($"Error on post attempt #{iTry+1} of {nTries}")
                Bot.handleException exn bot.Log
                false, None)

    /// Posts a random thought.
    let postRandomThought bot =
        let title, body = createRandomThought bot
        submitPost "RandomThoughts" title body bot

    /// Posts a six word story.
    let postSixWordStory bot =
        let title = ChatBot.complete [] bot.ChatBot
        submitPost "sixwordstories" title "" bot

    /// Monitors unread messages.
    [<FunctionName("MonitorUnreadMessages")>]
    member _.MonitorUnreadMessages(
        [<TimerTrigger("0 */30 * * * *")>]   // twice an hour
        timer : TimerInfo,
        log : ILogger) =
        createBot replyPrompt Models.Gpt_4 log
            |> Bot.monitorUnreadMessages
            |> ignore

    /// Posts a random thought.
    [<FunctionName("PostRandomThought")>]
    member _.PostRandomThought(
        [<TimerTrigger("0 20 */3 * * *")>]   // every three hours
        timer : TimerInfo,
        log : ILogger) =
        createBot randomThoughtPrompt Models.ChatGpt3_5Turbo log
            |> postRandomThought
            |> ignore

    /// Posts a six word story.
    [<FunctionName("PostSixWordStory")>]
    member _.PostSixWordStory(
        [<TimerTrigger("0 40 0 * * *")>]   // every day
        timer : TimerInfo,
        log : ILogger) =
        createBot sixWordStoryPrompt Models.ChatGpt3_5Turbo log
            |> postSixWordStory
            |> ignore
