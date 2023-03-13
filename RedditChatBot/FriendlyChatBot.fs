namespace RedditChatBot

open System
open System.Threading

open Reddit.Controllers

module FriendlyChatBot =

    (*
     * The Reddit.NET API presents a very leaky abstraction. As a
     * general rule, we call Post.About() and Comment.Info()
     * defensively to make sure we have the full details of a thing.
     * (Unfortunately, Comment.About() seems to have a race condition.)
     *)

    /// Bot's user account.
    let bot = Reddit.client.User("friendly-chat-bot")

    /// Determines the role of the given comment's author.
    let private getRole (comment : Comment) =
        if comment.Author = bot.Name then Role.System
        else Role.User

    /// Gets ancestor comments in chronological order.
    let private getHistory comment =

        let rec loop (comment : Comment) =   // to-do: use fewer round-trips
            let comment = comment.Info()
            [
                    // this comment
                let role = getRole comment
                let content =
                    match role with
                        | Role.User -> $"{comment.Author} says {comment.Body}"
                        | _ -> comment.Body
                yield role, content

                    // ancestors
                let thingType = Thing.getType comment.ParentFullname
                if thingType = ThingType.Comment then
                    let parent =
                        Reddit.client
                            .Comment(comment.ParentFullname)
                    yield! loop parent
            ]

        loop comment
            |> List.rev

    /// Maximum number of nested bot replies in thread.
    let private maxDepth = 3

    /// Timestamp of last bot comment.
    let mutable private dtLastComment = DateTime.Now

    /// Minimum time between comments, to avoid Reddit's spam filter.
    let private commentDelay =
        TimeSpan(hours = 0, minutes = 5, seconds = 1)

    /// Prints a divider to the screen.
    let private printDivider () =
        printfn ""
        printfn "----------------------------------------"
        printfn ""

    /// Replies to the given comment, if necessary.
    let private submitReply (comment : Comment) =
        let comment = comment.Info()

            // ignore bot's own comments
        if getRole comment <> Role.System
            && comment.Body <> "[deleted]" then   // no better way to check this?

                // has bot already replied to this comment?
            let handled =
                comment.Replies
                    |> Seq.exists (fun child ->
                        getRole child = Role.System)

                // if not, begin to create a reply
            if not handled then

                    // get comment history
                let history = getHistory comment
                assert(history |> Seq.last |> fst = Role.User)

                    // avoid deeply nested threads
                let nSystem =
                    history
                        |> Seq.where (fun (role, _) ->
                            role = Role.System)
                        |> Seq.length
                if nSystem < maxDepth then

                        // sleep if necessary to avoid spam filter
                    let sleep = dtLastComment + commentDelay - DateTime.Now
                    if sleep > TimeSpan.Zero then
                        printDivider ()
                        printfn $"Sleeping for {sleep}"
                        Thread.Sleep(sleep)

                        // reply with chat response
                    let response = Chat.chat history
                    comment.Reply(response) |> ignore
                    dtLastComment <- DateTime.Now

                        // log interaction
                    printDivider ()
                    printfn $"User: {history |> Seq.last |> snd}"
                    printfn ""
                    printfn $"Bot: {response}"

    /// Runs a chat session in the given post.
    let rec private runPost (post : Post) =
        try
                // reply to any top-level comments in the post
            let userComments = post.Comments.GetNew()
            for userComment in userComments do
                submitReply userComment

                // reply to replies to bot's recent comments on this post
            let botCommentHistory =
                bot.GetCommentHistory(
                    context = 0,
                    limit = 100,
                    sort = "new")
            for botComment in botCommentHistory do
                if botComment.Created >= post.Created then
                    let botComment = botComment.Info()
                    if botComment.Root.Id = post.Id then
                        for userComment in botComment.Replies do
                            submitReply userComment

        with exn ->
            printDivider ()
            printfn $"{exn}"
            Thread.Sleep(10000)   // wait, then continue

        runPost post   // loop for now

    /// Runs the bot.
    let run () =

           // get bot's latest post
        let post =
            bot.GetPostHistory()   // must sort manually
                |> Seq.sortByDescending (fun pst -> pst.Created)
                |> Seq.head
        printfn $"{post.Title}"
        printfn $"{post.Created}"

            // run session in the post
        runPost post
