namespace RedditChatBot

open System
open System.Threading

open Reddit
open Reddit.Controllers

type Bot =
    {
        /// Reddit API client.
        RedditClient : RedditClient

        /// Bot's Reddit user account.
        User : User

        /// Maximum number of bot comments in a nested thread.
        MaxCommentDepth : int

        /// Minimum time between comments, to avoid Reddit's spam filter.
        MinCommentDelay : TimeSpan

        /// Time of the bot's most recent comment.
        LastCommentTime : DateTime
    }

module Bot =

    /// Creates a bot with the given user name.
    let create name =
        let client = Reddit.createClient ()                    // always create a fresh client, since Reddit occasionally invalidates existing one
        let minCommentDelay =
            TimeSpan(hours = 0, minutes = 5, seconds = 5)
        {
            RedditClient = client
            User = client.User(name : string)
            MaxCommentDepth = 4
            MinCommentDelay = minCommentDelay
            LastCommentTime = DateTime.Now - minCommentDelay   // allow first comment immediately
        }

    /// Determines the role of the given comment's author.
    let private getRole (comment : Comment) bot =
        if comment.Author = bot.User.Name then Role.Assistant
        else Role.User

    /// Converts the given comment to a chat message based on its
    /// author's role.
    let private createChatMessage comment bot =
        let role = getRole comment bot
        let content =
            match role with
                | Role.User -> $"{comment.Author} says {comment.Body}"
                | _ -> comment.Body
        FChatMessage.create role content

    (*
     * The Reddit.NET API presents a very leaky abstraction. As a
     * general rule, we call Post.About() and Comment.Info()
     * defensively to make sure we have the full details of a thing.
     * (Unfortunately, Comment.About() seems to have a race condition.)
     *)

    /// Gets ancestor comments in chronological order.
    let private getHistory comment bot : ChatHistory =

        let rec loop (comment : Comment) =
            let comment = comment.Info()
            [
                    // this comment
                yield createChatMessage comment bot

                    // ancestors
                match Thing.getType comment.ParentFullname with
                    | ThingType.Comment ->
                        let parent =
                            bot.RedditClient
                                .Comment(comment.ParentFullname)
                        yield! loop parent
                    | _ -> ()
            ]

        loop comment
            |> List.rev

    /// Runs the given function repeatedly until it succeeds or
    /// we run out of tries.
    let rec private tryN numTries f =
        let success, value = f ()
        if not success && numTries > 1 then
            tryN (numTries - 1) f
        else value

    /// Completes the given history positively, using the given
    /// system-level prompt.
    let private completePositive prompt history =

        let isNegative (text : string) =
            let text = text.ToLower()
            text.Contains("disrespectful")
                || text.Contains("not respectful")
                || text.Contains("inappropriate")
                || text.Contains("not appropriate")

        tryN 3 (fun () ->
            let completion = Chat.complete prompt history
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

    /// Fixes prompt whitespace.
    let private fixPrompt (prompt : string) =
        prompt
            .Replace("\r", "")
            .Replace("\n", " ")
            .Trim()

    /// Bot's assessment of a user comment.
    type private Assessment =
        | Normal
        | Strange
        | Inappropriate

    /// Assessment prompt.
    let private assessmentPrompt =
        fixPrompt """
You are a friendly Reddit user. Assess the following comment, and
reply with a single word. If the comment is disrespectful or
inappropriate, reply with "Inappropriate". If the comment is strange
or irrelevant, reply with "Strange". Otherwise, reply with "Normal".
        """

    /// Parses the given assessment string.
    let private parseAssessment (str : string) =
        let str = str.ToLower()
        if str.StartsWith("inappropriate") then
            Inappropriate
        elif str.StartsWith("strange") then
            Strange
        elif str.StartsWith("normal") then
            Normal
        else
            printfn $"Unexpected assessment: {str}"
            Normal

    /// Assesses the given history.
    let private assess (history : ChatHistory) =
        history
            |> Seq.where (fun msg -> msg.Role = Role.User)
            |> Seq.map (fun msg -> msg.Content)
            |> String.concat "\r\n"
            |> FChatMessage.create Role.User
            |> List.singleton
            |> Chat.complete assessmentPrompt
            |> parseAssessment

    /// Reply prompt.
    let private replyPrompt =
        fixPrompt """
You are a friendly Reddit user. If you receive a comment
that seems strange or irrelevant, do your best to play along.
        """

    /// Delays the given bot, if necessary.
    let private delay bot =
        let nextCommentTime =
            bot.LastCommentTime + bot.MinCommentDelay
        let timeout =
            nextCommentTime - DateTime.Now
        if timeout > TimeSpan.Zero then
            printfn $"Sleeping until {nextCommentTime}"
            Thread.Sleep(timeout)

    /// Replies to the given comment, if necessary.
    let private submitReply (comment : Comment) bot =

            // ensure we have full details
        assert(isNull comment.Body |> not)

            // don't reply to bot's own comments
        if getRole comment bot <> Role.Assistant
            && comment.Body <> "[deleted]" then   // no better way to check this?

                // has bot already replied to this comment?
            let handled =
                comment.Replies
                    |> Seq.exists (fun child ->
                        getRole child bot = Role.Assistant)

                // if not, begin to create a reply
            if handled then
                false, bot
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
                    let assessment = assess history

                        // obtain chat completion
                    let completion =
                        if assessment = Inappropriate then
                            completePositive replyPrompt history
                        else
                            Chat.complete replyPrompt history
                    
                        // submit reply
                    delay bot
                    if completion = "" then "#" else completion   // Reddit requires a non-empty string
                        |> comment.Reply
                        |> ignore
                    true, { bot with LastCommentTime = DateTime.Now }

                else false, bot

        else false, bot

    /// Handles the given exception.
    let private handleException (exn : exn) =

        let dump (exn : exn) =
            printfn ""
            printfn $"{exn}"
            printfn ""

        match exn with
            | :? AggregateException as aggExn ->
                for innerExn in aggExn.InnerExceptions do
                    dump innerExn
            | _ -> dump exn

        Thread.Sleep(10000)   // wait for problem to clear up, hopefully

    /// Replies safely to the given comment, if necessary.
    let private submitReplySafe comment bot =
        tryN 3 (fun () ->
            try
                true, submitReply comment bot
            with exn ->
                handleException exn
                false, (false, bot))

    /// Monitors and replies to incoming messages.
    let rec monitorMessages bot =

            // get candidate messages that we might reply to
        let messages =
            bot.RedditClient.Account.Messages.GetMessagesInbox()
                |> Seq.sortBy (fun message -> message.CreatedUTC)

            // generate replies
        (bot, messages)
            ||> Seq.fold (fun bot message ->
                match Thing.getType message.Name with
                    | ThingType.Comment ->
                        let comment =
                            bot.RedditClient
                                .Comment(message.Name)
                                .Info()
                        let flag, bot' = submitReplySafe comment bot
                        if flag then printfn "Reply submitted"
                        bot'
                    | _ -> bot)
            |> monitorMessages

    /// Runs a bot.
    let rec run () =

        try
            create "friendly-chat-bot"
                |> monitorMessages

        with exn ->

            handleException exn

            printfn ""
            printfn "*** Restarting ***"
            printfn ""
            run ()   // restart from scratch
