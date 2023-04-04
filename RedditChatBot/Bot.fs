namespace RedditChatBot

open System
open System.Threading

open Microsoft.Extensions.Logging

open Reddit
open Reddit.Controllers

open OpenAI.GPT3.Managers

/// Application settings.
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

module Bot =

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

    (*
     * The Reddit.NET API presents a very leaky abstraction. As a
     * general rule, we call Post.About() and Comment.Info()
     * defensively to make sure we have the full details of a thing.
     * (Unfortunately, Comment.About() seems to have a race condition.)
     *)

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
                        if getRole post.Author bot = Role.User then
                            yield getPostMessage post bot

                    | _ -> ()
            ]

        loop comment
            |> List.rev

    /// Runs the given function repeatedly until it succeeds or
    /// we run out of tries.
    let private tryN numTries f =

        let rec loop numTriesRemaining =
            let success, value =
                let iTry = numTries - numTriesRemaining
                f iTry
            if not success && numTriesRemaining > 1 then
                loop (numTriesRemaining - 1)
            else value

        loop numTries

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

                // if not, begin to create a reply
            if handled then CommentResult.Ignored
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
                    if completion = "" then "#" else completion   // Reddit requires a non-empty string
                        |> comment.Reply
                        |> ignore
                    bot.Log.LogInformation("Comment submitted")
                    CommentResult.Replied

                else CommentResult.Ignored

        else CommentResult.Ignored

    /// Handles the given exception.
    let private handleException exn bot =

        let rec loop (exn : exn) =
            match exn with
                | :? AggregateException as aggExn ->
                    for innerExn in aggExn.InnerExceptions do
                        loop innerExn
                | _ ->
                    if isNull exn.InnerException then
                        bot.Log.LogError(exn, exn.Message)
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
                handleException exn bot
                false, CommentResult.Error)

    /// Runs the given bot.
    let private run bot =

            // get candidate messages that we might reply to
        let messages = RedditBot.getAllUnreadMessages bot.RedditBot
        bot.Log.LogInformation($"{messages.Length} unread message(s) found")

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

    /// Creates and runs a bot that monitors unread messages.
    let monitorUnreadMessages settings redditBotDef chatBotDef log =

            // initialize bot
        let bot = create settings redditBotDef chatBotDef log
        log.LogInformation("Bot initialized")

            // run bot
        run bot |> ignore
