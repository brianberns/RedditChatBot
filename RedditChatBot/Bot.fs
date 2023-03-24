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
        /// Bot description.
        Description : BotDescription

        /// Reddit API client.
        RedditClient : RedditClient

        /// Chat API client.
        ChatClient : OpenAIService

        /// Maximum number of bot comments in a nested thread.
        MaxCommentDepth : int

        /// Minimum time between comments, to avoid Reddit's spam filter.
        MinCommentDelay : TimeSpan

        /// Time of the bot's most recent comment.
        LastCommentTime : DateTime

        /// Logger.
        Log : ILogger
    }

module Bot =

    /// Creates a bot with the given user name.
    let create settings botDesc log =

            // connect to Reddit
        let redditClient =
                Reddit.createClient settings.Reddit botDesc

            // connect to chat service
        let chatClient = 
            Chat.createClient settings.OpenAi

            // allow debug build to comment immediately (danger: don't do this often)
        let minCommentDelay =
#if DEBUG
            TimeSpan.Zero
#else
            TimeSpan(hours = 0, minutes = 5, seconds = 5)
#endif

            // determine time of last comment
        let lastCommentTime =
            redditClient
                .User(botDesc.BotName)
                .GetCommentHistory(
                    context = 0,
                    limit = 1)
                |> Seq.tryExactlyOne
                |> Option.map (fun comment -> comment.Created)
                |> Option.defaultValue (DateTime.Now - minCommentDelay)

        {
            Description = botDesc
            RedditClient = redditClient
            ChatClient = chatClient
            MaxCommentDepth = 4
            MinCommentDelay = minCommentDelay
            LastCommentTime = lastCommentTime
            Log = log
        }

    /// Determines the role of the given author.
    let private getRole author bot =
        if author = bot.Description.BotName then
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

    /// Converts the given post's content into a history.
    let private getPostHistory (post : SelfPost) bot =
        [
            createChatMessage post.Author post.Title bot
            if hasContent post.SelfText then
                createChatMessage post.Author post.SelfText bot
        ]

    /// Gets ancestor comments in chronological order.
    let private getHistory comment bot : ChatHistory =

        let rec loop (comment : Comment) =
            let comment = comment.Info()
            [
                    // this comment
                yield createChatMessage
                    comment.Author comment.Body bot

                    // ancestors
                match Thing.getType comment.ParentFullname with

                    | ThingType.Comment ->
                        let parent =
                            bot.RedditClient
                                .Comment(comment.ParentFullname)
                        yield! loop parent

                    | ThingType.Post ->
                        let post =
                            bot.RedditClient
                                .SelfPost(comment.ParentFullname)
                                .About()
                        if getRole post.Author bot = Role.User then
                            yield! getPostHistory post bot

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

    /// Completes the given history positively.
    let private completePositive history bot =

        let isNegative (text : string) =
            let text = text.ToLower()
            text.Contains("disrespectful")
                || text.Contains("not respectful")
                || text.Contains("inappropriate")
                || text.Contains("not appropriate")

        tryN 3 (fun _ ->
            let completion =
                Chat.complete
                    bot.Description.ReplyPrompt
                    history
                    bot.ChatClient
            let success = not (isNegative completion)
            success, completion)

    (*
     * ChatGPT can be coaxed to go along with comments that it sees
     * as strange or irrelevant, but it often reacts sharply to
     * anything that it finds disrespectful or inappropriate, even
     * if instructed not to. So our strategy is to:
     *
     * - Ask ChatGPT to categorize all comments before replying.
     * - Ask it to go along with "strange" comments.
     * - Handle "inappropriate" comments by filtering out sharp
     *   replies.
     *)

    /// Bot's assessment of a user comment.
    type private Assessment =
        | Normal
        | Strange
        | Inappropriate

    /// Assessment prompt.
    let private assessmentPrompt =
        Chat.fixPrompt """
You are a friendly Reddit user. Assess the following comment, and
reply with a single word. If the comment is disrespectful or
inappropriate, reply with "Inappropriate". If the comment is strange
or irrelevant, reply with "Strange". Otherwise, reply with "Normal".
        """

    /// Assesses the given history.
    let private assess (history : ChatHistory) bot =

        let str =
            let history' =
                history
                    |> Seq.where (fun msg -> msg.Role = Role.User)
                    |> Seq.map (fun msg -> msg.Content)
                    |> String.concat "\r\n"
                    |> FChatMessage.create Role.User
                    |> List.singleton
            Chat.complete
                assessmentPrompt
                history'
                bot.ChatClient

        let str = str.ToLower()
        if str.StartsWith("inappropriate") then
            Inappropriate
        elif str.StartsWith("strange") then
            Strange
        elif str.StartsWith("normal") then
            Normal
        else
            bot.Log.LogWarning($"Unexpected assessment: {str}")
            Normal

    /// Delays the given bot, if necessary.
    let private delay bot =
        let nextCommentTime =
            bot.LastCommentTime + bot.MinCommentDelay
        let timeout =
            nextCommentTime - DateTime.Now
        if timeout > TimeSpan.Zero then
            bot.Log.LogInformation(
                $"Sleeping until {nextCommentTime}")
            Thread.Sleep(timeout)

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

                        // assess input
                    let assessment = assess history bot

                        // obtain chat completion
                    let completion =
                        if assessment = Inappropriate then
                            completePositive history bot
                        else
                            Chat.complete
                                bot.Description.ReplyPrompt
                                history
                                bot.ChatClient
                    
                        // submit reply
                    delay bot
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

    /// Fetches all of the bot's unread messages.
    let getAllUnreadMessages bot =

        let rec loop count after =

                // get a batch of messages
            let messages =
                bot.RedditClient.Account.Messages
                    .GetMessagesUnread(
                        limit = 100,
                        after = after,
                        count = count)

            seq {
                yield! messages

                    // try to get more messages?
                if messages.Count > 0 then
                    let count' = messages.Count + count
                    let after' = (Seq.last messages).Name
                    yield! loop count' after'
            }

        loop 0 ""
            |> Seq.sortBy (fun message ->
                message.CreatedUTC)   // oldest messages first
            |> Seq.toArray

    /// Runs the given bot.
    let private run bot =

            // get candidate messages that we might reply to
        let messages = getAllUnreadMessages bot
        bot.Log.LogInformation($"{messages.Length} unread message(s) found")

            // reply to no more than one message
        messages
            |> Seq.map (fun message -> message.Name)   // fullname of the thing the message is about
            |> Seq.tryFind (fun fullname ->
                match Thing.getType fullname with
                    | ThingType.Comment ->

                            // attempt to reply to message
                        let comment =
                            bot.RedditClient.Comment(fullname)
                        let result = submitReplySafe comment bot

                            // mark message read?
                        if result <> CommentResult.Error then
                            bot.RedditClient.Account.Messages
                                .ReadMessage(fullname)   // this is weird, but apparently correct

                            // stop looking?
                        result = CommentResult.Replied
                    | _ -> false)

    /// Creates and runs a bot that monitors unread messages.
    let monitorUnreadMessages settings botDesc log =

            // initialize bot
        let bot = create settings botDesc log
        log.LogInformation("Bot initialized")

            // run bot
        run bot |> ignore
