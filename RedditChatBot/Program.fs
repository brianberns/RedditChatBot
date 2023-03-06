namespace RedditChatBot

open System.Threading
open Reddit.Controllers

module Program =

    let me = Reddit.client.User("EverydayChatBot")

    /// Replies to the given comment, if necessary.
    let private reply (comment : Comment) =

        let comment = comment.About()             // get full properties

            // ignore my own comments
        if comment.Author <> me.Name
            && comment.Depth < 5                  // avoid infinite recursion with another bot
            && comment.Body <> "[deleted]" then   // no better way to check this?

                // have I already replied to this comment?
            let handled =
                comment.Replies
                    |> Seq.exists (fun child ->
                        child.Author = me.Name)

                // if not, create a reply
            if not handled then

                    // construct user input
                let query = $"{comment.Author} says {comment.Body}"
                printfn ""
                printfn "----------------------------------------"
                printfn $"Q: {query}"

                    // get chat response
                let response = Chat.chat query
                printfn ""
                printfn $"A: {response}"
                comment.Reply(response) |> ignore

    /// Runs a chat session.
    let rec run () =

        try
                // get my latest post
            let post =
                me.GetPostHistory("submitted", sort="new", limit=1)
                    |> Seq.exactlyOne
           
                // reply to any top-level comments in the post
            for comment in post.Comments.GetNew() do
                reply comment

                // reply to replies to my recent comments
            for myComment in me.GetCommentHistory(sort="new") do
                if myComment.Created >= post.Created then   // ignore comments from previous posts
                    for comment in myComment.About().Replies do
                        reply comment
        with exn ->
            printfn $"{exn}"
            Thread.Sleep(10000)   // wait, then continue

            // loop (for now)
        run ()

    run ()
