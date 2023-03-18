namespace RedditChatBot

open System
open System.Threading

open Reddit.Controllers

type Bot =
    {
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
        let minCommentDelay =
            TimeSpan(hours = 0, minutes = 5, seconds = 5)
        {
            User = Reddit.client.User(name : string)
            MaxCommentDepth = 4
            MinCommentDelay = minCommentDelay
            LastCommentTime = DateTime.Now - minCommentDelay   // allow first comment immediately
        }

    /// Determines the role of the given comment's author.
    let private getRole (comment : Comment) bot =
        if comment.Author = bot.User.Name then Role.System
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
                            Reddit.client
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

    /// Completes the given history using the given system-level
    /// prompt.
    let private complete prompt (history : ChatHistory) =
        Chat.complete [
            yield FChatMessage.create Role.System prompt
            yield! history
        ]

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
            let completion = complete prompt history
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
You are a friendly Reddit user. Assess the given comments, and reply
with a single word. If any comments are disrespectful or inappropriate,
reply with "Inappropriate". If any comments are strange or irrelevant,
reply with "Strange". Otherwise, reply with "Normal".
        """

    /// Parses the given assessment string.
    let private tryParseAssessment (str : string) =
        let str = str.ToLower()
        if str.StartsWith("inappropriate") then
            Ok Inappropriate
        elif str.StartsWith("strange") then
            Ok Strange
        elif str.StartsWith("normal") then
            Ok Normal
        else
            Error str

    /// Assesses the given history.
    let private assess history =
        tryN 3 (fun () ->
            let assessmentRes =
                history
                    |> complete assessmentPrompt
                    |> tryParseAssessment
            match assessmentRes with
                | Ok assessment -> true, assessment
                | Error str ->
                    printfn $"Unexpected assessment: {str}"
                    false, Normal)

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
        if getRole comment bot <> Role.System
            && comment.Body <> "[deleted]" then   // no better way to check this?

                // has bot already replied to this comment?
            let handled =
                comment.Replies
                    |> Seq.exists (fun child ->
                        getRole child bot = Role.System)

                // if not, begin to create a reply
            if handled then
                false, bot
            else
                    // avoid deeply nested threads
                let history = getHistory comment bot
                let nBot =
                    history
                        |> Seq.where (fun msg ->
                            msg.Role = Role.System)
                        |> Seq.length
                if nBot < bot.MaxCommentDepth then

                        // assess input
                    let assessment = assess history

                        // obtain chat completion
                    let completion =
                        if assessment = Inappropriate then
                            completePositive replyPrompt history
                        else
                            complete replyPrompt history
                    
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
        printfn ""
        printfn $"{exn}"
        printfn ""
        Thread.Sleep(10000)   // wait for problem to clear up, hopefully

    /// Replies to the given comment safely, if necessary.
    let private submitReplySafe comment bot =
        try
            submitReply comment bot
        with exn ->
            handleException exn
            false, bot

    /// Runs a chat session in the given post.
    let rec private runPost (post : Post) bot =
        try
                // get candidate user comments that we might reply to
            let userComments =
                [|
                        // recent top-level comments in the post
                    yield! post.Comments.GetNew(
                        context = 0,
                        limit = 35)

                        // replies to the bot's recent comments in this post
                    let botCommentHistory =
                        bot.User.GetCommentHistory(
                            context = 0,
                            limit = 35,
                            sort = "new")
                    for botComment in botCommentHistory do
                        let botComment = botComment.Info()
                        if botComment.Root.Id = post.Id then
                            yield! botComment.Replies
                |]

                // generate replies
            printfn ""
            let fullComments =
                userComments
                    |> Seq.map (fun comment -> comment.Info())
                    |> Seq.sortBy (fun comment -> comment.Created)
            (bot, Seq.indexed fullComments)
                ||> Seq.fold (fun bot (idx, comment) ->
                    printfn $"Processing comment {idx+1}/{userComments.Length}"
                    let flag, bot = submitReplySafe comment bot
                    if flag then printfn "Reply submitted"
                    bot)
                |> runPost post

        with exn ->
            handleException exn
            runPost post bot

    /// Runs the given bot.
    let run bot =

           // get bot's latest post
        let post =
            bot.User.GetPostHistory()   // must sort manually
                |> Seq.sortByDescending (fun pst -> pst.Created)
                |> Seq.head
        printfn $"{post.Title}"
        printfn $"{post.Created.ToLocalTime()}"

            // run session in the post
        runPost post bot |> ignore
