namespace RedditChatBot

open System.Threading
open Reddit.Controllers

module Program =

    /// My user account.
    let me = Reddit.client.User("EverydayChatBot")

    /// Determines the role of the given comment's author.
    let private getRole (comment : Comment) =
        if comment.Author = me.Name then Role.System
        else Role.User

    /// Converts the given comment into chat content.
    let private getContent role (comment : Comment) =
        match role with
            | Role.User -> $"{comment.Author} says {comment.Body}"
            | _ -> comment.Body

    /// Gets ancestor comments for context. The given comment
    /// has depth 0, so a full context will have N+1 entries,
    /// where N is the given depth.
    let private getContext depth comment =

        let rec loop depth comment =   // to-do: use fewer round-trips
            [
                    // this comment
                let role = getRole comment
                let content = getContent role comment
                yield role, content

                    // ancestor comments
                if depth > 0 then
                    let parentFullname = comment.ParentFullname
                    if parentFullname.StartsWith("t1_") then
                        let parent =
                            Reddit.client.Comment(parentFullname).About()
                        yield! loop (depth - 1) parent
            ]

        comment
            |> loop depth
            |> List.rev

    /// Maximum context depth.
    let private maxDepth = 5

    /// Prints a divider to the screen.
    let private printDivider () =
        printfn ""
        printfn "----------------------------------------"
        printfn ""

    /// Replies to the given comment, if necessary.
    let private reply (comment : Comment) =

        let comment = comment.About()   // make sure we have full details

            // ignore my own comments
        if getRole comment <> Role.System
            && comment.Body <> "[deleted]" then   // no better way to check this?

                // have I already replied to this comment?
            let handled =
                comment.Replies
                    |> Seq.exists (fun child ->
                        getRole child = Role.System)

                // if not, create a reply
            if not handled then

                    // get comment context
                let context = getContext maxDepth comment
                assert(context |> Seq.last |> fst = Role.User)
                printDivider ()
                printfn $"Q: {context |> Seq.last |> snd}"

                    // avoid deeply nested threads
                if context.Length > maxDepth then
                    printfn "[Max depth exceeded]"
                else
                        // get chat response
                    let response = Chat.chat context
                    printfn ""
                    printfn $"A: {response}"
                    comment.Reply(response) |> ignore

    /// Runs a chat session in the given post.
    let private run (post : Post) =

        let rec loop () =   // run in a simple loop for now
            try
                    // reply to any top-level comments in the post
                for comment in post.Comments.GetNew() do
                    reply comment

                    // reply to replies to my recent comments on this post
                let commentHistory =
                    me.GetCommentHistory(
                        context = 0,
                        sort = "new")
                for myComment in commentHistory do
                    if myComment.Created >= post.Created then
                        let myComment' = myComment.Info()   // to-do: use About() if race condition can be avoided?
                        if myComment'.Root.Id = post.Id then
                            for comment in myComment'.Replies do
                                reply comment

            with exn ->
                printDivider ()
                printfn $"{exn}"
                Thread.Sleep(10000)   // wait, then continue

            loop ()

        loop ()

    [<EntryPoint>]
    let main args =
        me.GetPostHistory("submitted", sort="new", limit=1)   // get my latest post
            |> Seq.exactlyOne
            |> run
        0
