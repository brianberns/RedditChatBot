namespace RedditChatBot

open System
open System.Threading

open Reddit.Controllers

type Bot =
    {
        /// Bot's user account.
        User : User

        /// Maximum number of nested bot replies in thread.
        MaxDepth : int

        /// Minimum time between comments, to avoid Reddit's spam filter.
        MinCommentDelay : TimeSpan

        /// Time of the bot's most recent comment.
        LastCommentTime : DateTime
    }

module Bot =

    (*
     * The Reddit.NET API presents a very leaky abstraction. As a
     * general rule, we call Post.About() and Comment.Info()
     * defensively to make sure we have the full details of a thing.
     * (Unfortunately, Comment.About() seems to have a race condition.)
     *)

    /// Creates a bot with the given user name.
    let create name =
        {
            User = Reddit.client.User(name : string)
            MaxDepth = 3
            MinCommentDelay =
                TimeSpan(hours = 0, minutes = 5, seconds = 5)
            LastCommentTime = DateTime.Now
        }

    type private Assessment =
        | Normal
        | Inappropriate
        | Strange

    module private Assessment =

        let parse = function
            | "Inappropriate" -> Inappropriate
            | "Strange" -> Strange
            | _ -> Normal

    /// Determines the role of the given comment's author.
    let private getRole (comment : Comment) bot =
        if comment.Author = bot.User.Name then Role.System
        else Role.User

    /// Converts the given comment to a chat message.
    let private createChatMessage comment bot =
        let role = getRole comment bot
        let content =
            match role with
                | Role.User -> $"{comment.Author} says {comment.Body}"
                | _ -> comment.Body
        FChatMessage.create role content

    /// Gets ancestor comments in chronological order.
    let private getHistory comment bot : ChatHistory =

        let rec loop (comment : Comment) =   // to-do: use fewer round-trips
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

    /// System-level prompt.
    let private systemPrompt =
        """
Reply in the style of an enthusiastic, friendly Reddit user. At the end
of your reply include a keyword in square brackets that describes your
assessment of the last message in your input. Keyword "Normal" indicates
a normal message. Keyword "Inappropriate" indicates an inappropriate or
disrespectful message. Keyword "Strange" indicates a strange, nonsensical,
or irrelevant message.
        """.Trim()

    /// Parses the given completion.
    let private parseCompletion (completion : string) =
        let iStart = completion.LastIndexOf('[')
        let iEnd = completion.LastIndexOf(']')
        if iStart >= 0 && iEnd >= 0 && iEnd > iStart then
            let content =
                completion
                    .Substring(0, iStart)
                    .Trim()
            let assessment =
                completion
                    .Substring(
                        iStart + 1,
                        iEnd - iStart - 1)
                    .Trim()
                    |> Assessment.parse
            content, assessment
        else
            completion, Normal

    /// Delays the given bot, if necessary, to avoid spam filter.
    let private delay bot =
        let timeout =
            bot.LastCommentTime
                + bot.MinCommentDelay
                - DateTime.Now
        if timeout > TimeSpan.Zero then
            Thread.Sleep(timeout)

    /// Replies to the given comment, if necessary.
    let private submitReply (comment : Comment) bot =

            // ensure we have full details
        let comment = comment.Info()

            // ignore bot's own comments
        if getRole comment bot <> Role.System
            && comment.Body <> "[deleted]" then   // no better way to check this?

                // has bot already replied to this comment?
            let handled =
                comment.Replies
                    |> Seq.exists (fun child ->
                        getRole child bot = Role.System)

                // if not, begin to create a reply
            if handled then bot
            else
                    // get comment history
                let history = getHistory comment bot

                    // avoid deeply nested threads
                let nBot =
                    history
                        |> Seq.where (fun msg ->
                            msg.Role = Role.System)
                        |> Seq.length
                if nBot < bot.MaxDepth then

                        // obtain chat completion
                    let content, assessment =
                        let completion =
                            Chat.complete [
                                yield FChatMessage.create
                                    Role.System systemPrompt
                                yield! history
                            ]
                        parseCompletion completion
                    
                        // submit reply
                    let body =
                        if assessment = Normal then content
                        else "No comment."
                    delay bot
                    comment.Reply(body) |> ignore
                    { bot with LastCommentTime = DateTime.Now }

                else bot

        else bot

    /// Replies to the given comment safely, if necessary.
    let private submitReplySafe comment bot =
        try
            submitReply comment bot
        with exn ->
            printfn ""
            printfn $"{exn}"
            Thread.Sleep(10000)   // wait for problem to clear up, hopefully
            bot

    /// Runs a chat session in the given post.
    let private runPost (post : Post) bot =

        let rec loop bot =

                // get candidate comments that we'll reply to
            let comments =
                [
                        // recent top-level comments in the post
                    yield! post.Comments.GetNew(limit = 100)

                        // replies to the bot's recent comments in this post
                    let botCommentHistory =
                        bot.User.GetCommentHistory(
                            context = 0,
                            limit = 100,
                            sort = "new")
                    for botComment in botCommentHistory do
                        if botComment.Created >= post.Created then
                            let botComment = botComment.Info()
                            if botComment.Root.Id = post.Id then
                                yield! botComment.Replies
                ]

                // generate replies
            printfn ""
            printfn $"{DateTime.Now}: Found {comments.Length} comment(s)"
            (bot, Seq.indexed comments)
                ||> Seq.fold (fun bot (idx, comment) ->
                    printfn $"{DateTime.Now}: Comment {idx+1}/{comments.Length}"
                    submitReplySafe comment bot)
                |> loop

        loop bot |> ignore

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
        runPost post bot
