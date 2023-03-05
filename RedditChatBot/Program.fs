namespace RedditChatBot

open Reddit.Controllers

module Program =

    let userName = "EverydayChatBot"

    /// Replies to the given comment, if necessary.
    let private reply (comment : Comment) =

        let comment = comment.About()             // get full properties

            // ignore my own comments
        if comment.Author <> userName
            && comment.Depth < 5                  // avoid infinite recursion with another bot
            && comment.Body <> "[deleted]" then   // no better way to check this?

                // have I already replied to this comment?
            let handled =
                comment.Replies
                    |> Seq.exists (fun child ->
                        child.Author = userName)

                // if not, create a reply
            if not handled then
                printfn ""
                printfn $"User says: {comment.Body}"
                let replyBody = Chat.chat comment.Body
                printfn $"I say: {replyBody}"
                comment.Reply(replyBody) |> ignore

    [<EntryPoint>]
    let main args =

        let me = Reddit.client.User(userName)
           
            // reply to any top-level comments in my posts
        for post in me.PostHistory do
            for comment in post.Comments.GetNew() do
                reply comment

            // reply to replies to my recent comments
        for myComment in me.CommentHistory do
            for comment in myComment.About().Replies do
                reply comment

        0
