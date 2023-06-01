namespace RedditChatBot

open System
open System.Threading

open Microsoft.Extensions.Logging

open Reddit.Controllers

/// Application settings. The fields of this type should
/// correspond to the keys of the Azure app configuration.
/// E.g. Key "OpenAi:ApiKey" corresponds to field
/// AppSettings.OpenAi.ApiKey.
[<CLIMutable>]   // https://github.com/dotnet/runtime/issues/77677
type AppSettings =
    {
        /// Reddit settings.
        Reddit : RedditSettings

        /// OpenAI settings.
        OpenAi : OpenAiSettings
    }

/// A Reddit chat bot.
type Bot =
    {
        /// Reddit bot.
        RedditBot : RedditBot

        /// Chat bot.
        ChatBot : ChatBot

        /// Maximum number of bot comments in a nested thread.
        MaxCommentDepth : int

        /// Logger.
        Log : ILogger
    }

    member bot.Dispose() = bot.ChatBot.Dispose()

    interface IDisposable with
        member bot.Dispose() = bot.Dispose()

module Bot =

    (*
     * The Reddit.NET API presents a very leaky abstraction. As a
     * general rule, we call Post.About() and Comment.Info()
     * defensively to make sure we have the full details of a thing.
     * (Unfortunately, Comment.About() seems to have a race condition.)
     *)

    /// Creates a bot with the given values.
    let create settings redditBotDef chatBotDef log =

            // connect to Reddit
        let redditBot =
            RedditBot.create settings.Reddit redditBotDef

            // connect to chat service
        let chatBot = 
            ChatBot.create settings.OpenAi chatBotDef

        {
            RedditBot = redditBot
            ChatBot = chatBot
            MaxCommentDepth = 4
            Log = log
        }

    /// Determines the role of the given author.
    let private getRole author bot =
        if author = bot.RedditBot.BotDef.BotName then
            Role.Assistant
        else Role.User

    /// Does the given text contain any content?
    let private hasContent =
        String.IsNullOrWhiteSpace >> not

    /// Says the given text as the given author.
    let private say author text =
        assert(hasContent author)
        assert(hasContent text)
        $"{author} says {text}"

    /// Converts the given text to a chat message based on its
    /// author's role.
    let private createChatMessage author text bot =
        let role = getRole author bot
        let content =
            match role with
                | Role.User -> say author text
                | _ -> text
        FChatMessage.create role content

    /// Subreddit details.
    type private SubredditDetail =
        {
            /// Bot can post autonomously?
            AutonomousPost : bool

            /// Comment prompt, if any.
            CommentPromptOpt : Option<string>

            /// Reply to /u/AutoModerator?
            ReplyToAutoModerator : bool
        }

    /// Subreddit detail map.
    type private SubredditDetailMap =
        Map<string, SubredditDetail>

    module private SubredditDetailMap =

        /// Bot can post autonomously?
        let isAutonomousPost subreddit (map : SubredditDetailMap) =
            map
                |> Map.tryFind subreddit
                |> Option.map (fun detail ->
                    detail.AutonomousPost)
                |> Option.defaultValue false

        /// Gets comment prompt for the given subreddit, if any.
        let tryGetCommentPrompt subreddit (map : SubredditDetailMap) =
            map
                |> Map.tryFind subreddit
                |> Option.bind (fun detail ->
                    detail.CommentPromptOpt)

        /// Bot should reply to comment?
        let shouldReply (comment : Comment) (map : SubredditDetailMap) =
            if comment.Author = "AutoModerator" then
                map
                    |> Map.tryFind comment.Subreddit
                    |> Option.map (fun detail ->
                        detail.ReplyToAutoModerator)
                    |> Option.defaultValue true
            else true

    /// Subreddit details.
    let private subredditDetailMap : SubredditDetailMap =
        let autonomous =
            {
                AutonomousPost = true
                CommentPromptOpt = None
                ReplyToAutoModerator = true
            }
        Map [
            "RandomThoughts",
                { autonomous with ReplyToAutoModerator = false }
            "self", autonomous
            "sixwordstories",
                {
                    autonomous with
                        CommentPromptOpt =
                            Some "It is customary, but not mandatory, to write a six-word response."
                }
            "testingground4bots", autonomous
        ]

    /// Creates messages describing the given subreddit.
    let private getSubredditMessages subreddit =
        let createMsg = FChatMessage.create Role.User
        seq {
                // subreddit name
            yield createMsg $"Subreddit: {subreddit}"

                // comment prompt?
            match SubredditDetailMap.tryGetCommentPrompt subreddit subredditDetailMap with
                | Some prompt -> yield createMsg prompt
                | None -> ()
        }

    /// Converts the given post's content into a message.
    let private getPostMessage (post : SelfPost) bot =
        let content =
            if hasContent post.SelfText then
                post.Title + Environment.NewLine + post.SelfText
            else
                post.Title
        createChatMessage post.Author content bot

    /// Gets ancestor comments in chronological order.
    let private getHistory comment bot : ChatHistory =

        let rec loop (comment : Comment) =
            let comment = comment.Info()
            [
                    // this comment
                let body = comment.Body.Trim()
                let botName = bot.RedditBot.BotDef.BotName
                if body <> $"/u/{botName}" && body <> $"u/{botName}" then   // skip summons if there's no other content
                    yield createChatMessage
                        comment.Author body bot

                    // ancestors
                match Thing.getType comment.ParentFullname with

                    | ThingType.Comment ->
                        let parent =
                            bot.RedditBot.Client
                                .Comment(comment.ParentFullname)
                        yield! loop parent

                    | ThingType.Post ->
                        let post =
                            bot.RedditBot.Client
                                .SelfPost(comment.ParentFullname)
                                .About()
                        let isUserPost = getRole post.Author bot = Role.User
                        let isAutonomousSubreddit =
                            SubredditDetailMap.isAutonomousPost
                                post.Subreddit
                                subredditDetailMap

                        if isUserPost || isAutonomousSubreddit then
                            yield! List.rev [   // will be unreversed at the end
                                yield! getSubredditMessages post.Subreddit
                                yield getPostMessage post bot
                            ]

                    | _ -> ()
            ]

        loop comment
            |> List.rev

    /// Result of attempting submitting a reply comment.
    [<RequireQualifiedAccess>]
    type private CommentResult =

        /// Reply submitted successfully.
        | Replied

        /// No need to reply.
        | Ignored

        /// Error while attempting to submit a reply.
        | Error

    /// Replies to the given comment, if necessary.
    let private submitReply (comment : Comment) bot =

            // ensure we have full, current details
        let comment = comment.Info()

            // don't reply to bot's own comments
        if getRole comment.Author bot <> Role.Assistant
            && comment.Body <> "[deleted]" then   // no better way to check this?

                // has bot already replied to this comment?
            let handled =
                comment.Replies
                    |> Seq.exists (fun child ->
                        getRole child.Author bot = Role.Assistant)

                // should reply to comment?
            let shouldReply =
                SubredditDetailMap.shouldReply comment subredditDetailMap

                // begin to create a reply?
            if handled || not shouldReply then
                CommentResult.Ignored
            else
                    // avoid deeply nested threads
                let history = getHistory comment bot
                let nBot =
                    history
                        |> Seq.where (fun msg ->
                            msg.Role = Role.Assistant)
                        |> Seq.length
                if nBot < bot.MaxCommentDepth then

                        // obtain chat completion
                    let completion =
                        ChatBot.complete history bot.ChatBot
                    
                        // submit reply
                    let body =
                        if completion = "" then "#"   // Reddit requires a non-empty string
                        else completion
                    comment.Reply(body) |> ignore
                    bot.Log.LogWarning($"Comment submitted: {body}")   // use warning for emphasis in log
                    CommentResult.Replied

                else CommentResult.Ignored

        else CommentResult.Ignored

    /// Runs the given function repeatedly until it succeeds or
    /// we run out of tries.
    let tryN numTries f =

        let rec loop numTriesRemaining =
            let success, value =
                let iTry = numTries - numTriesRemaining
                f iTry
            if not success && numTriesRemaining > 1 then
                loop (numTriesRemaining - 1)
            else value

        loop numTries

    /// Handles the given exception.
    let handleException exn (log : ILogger) =

        let rec loop (exn : exn) =
            match exn with
                | :? AggregateException as aggExn ->
                    for innerExn in aggExn.InnerExceptions do
                        loop innerExn
                | _ ->
                    if isNull exn.InnerException then
                        log.LogError(exn, exn.Message)
                    else
                        loop exn.InnerException

        loop exn
        Thread.Sleep(10000)   // wait for problem to clear up, hopefully

    /// Replies safely to the given comment, if necessary.
    let private submitReplySafe comment bot =
        let numTries = 3
        tryN numTries (fun iTry ->
            try
                true, submitReply comment bot
            with exn ->
                bot.Log.LogError($"Error on reply attempt #{iTry+1} of {numTries}")
                handleException exn bot.Log
                false, CommentResult.Error)

    /// Monitors unread messages.
    let monitorUnreadMessages bot =

            // get candidate messages that we might reply to
        let messages = RedditBot.getAllUnreadMessages bot.RedditBot
        let logger =
            if messages.Length > 0 then bot.Log.LogWarning   // use warning for emphasis in log
            else bot.Log.LogInformation
        logger $"{messages.Length} unread message(s) found"

            // reply to no more than one message
        messages
            |> Seq.map (fun message -> message.Name)   // fullname of the thing the message is about
            |> Seq.tryFind (fun fullname ->
                match Thing.getType fullname with
                    | ThingType.Comment ->

                            // attempt to reply to message
                        let comment =
                            bot.RedditBot.Client.Comment(fullname)
                        let result = submitReplySafe comment bot

                            // mark message read?
                        if result <> CommentResult.Error then
                            bot.RedditBot.Client.Account.Messages
                                .ReadMessage(fullname)   // this is weird, but apparently correct

                            // stop looking?
                        result = CommentResult.Replied
                    | _ -> false)
