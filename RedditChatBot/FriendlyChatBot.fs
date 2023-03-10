namespace RedditChatBot

open System
open System.Threading

open Reddit.Controllers

module FriendlyChatBot =

    /// My user account.
    let me = Reddit.client.User("friendly-chat-bot")

    let private getRole author =
        if author = me.Name then Role.System
        else Role.User

    let isNonEmpty =
        String.IsNullOrWhiteSpace >> not

    let private say author text =
        assert(isNonEmpty author)
        assert(isNonEmpty text)
        $"{author} says {text}"

    let private hr = "---"

    let private addFooter body =
        $"{body}\n\n{hr}\n\n^(I am a bot based on ChatGPT. This comment was generated automatically.)"

    let private removeFooter (content : string) =
        let idx = content.LastIndexOf(hr)
        if idx >= 0 then
            content.Substring(0, idx).TrimEnd()
        else
            content

    let private getPostHistory (post : SelfPost) =
        [
            Role.User, say post.Author post.Title
            if isNonEmpty post.SelfText then
                Role.User, say post.Author post.SelfText
        ]

    /// Gets ancestor comments in chronological order.
    let private getCommentHistory comment =

        let rec loop (comment : Comment) =   // to-do: use fewer round-trips
            [
                    // this comment
                let role = getRole comment.Author
                let content =
                    match role with
                        | Role.User -> say comment.Author comment.Body
                        | _ -> removeFooter comment.Body
                yield role, content

                    // ancestors
                let parentFullname = comment.ParentFullname
                match parentFullname.Substring(0, 3) with

                        // comment
                    | "t1_" ->
                        let parent =
                            Reddit.client
                                .Comment(parentFullname)
                                .Info()
                        yield! loop parent

                        // post
                    | "t3_" ->
                        let post =
                            Reddit.client
                                .SelfPost(parentFullname)
                                .About()
                        yield! getPostHistory post

                        // other
                    | prefix -> failwith $"Unexpected type: {prefix}"
            ]

        loop comment
            |> List.rev

    /// Prints a divider to the screen.
    let private printDivider () =
        printfn ""
        printfn "----------------------------------------"
        printfn ""

    /// Replies to the given comment, if necessary.
    let private reply (comment : Comment) =

        let comment = comment.Info()   // make sure we have full details (Comment.About seems to have a race condition)

            // ignore my own comments
        if getRole comment.Author <> Role.System
            && comment.Body <> "[deleted]" then   // no better way to check this?

                // have I already replied to this comment?
            let handled =
                comment.Replies
                    |> Seq.exists (fun child ->
                        getRole child.Author = Role.System)

                // if not, create a reply
            if not handled then

                    // get comment history
                let history = getCommentHistory comment
                assert(history |> Seq.last |> fst = Role.User)
                printDivider ()
                printfn $"User: {history |> Seq.last |> snd}"

                    // avoid deeply nested threads
                let nSystem =
                    history
                        |> Seq.where (fun (role, _) ->
                            role = Role.System)
                        |> Seq.length
                if nSystem >= 3 then
                    printfn ""
                    printfn "Bot: [Max depth exceeded]"
                else
                        // get chat response
                    let response = Chat.chat history
                    printfn ""
                    printfn $"Bot: {response}"
                    response
                        |> addFooter
                        |> comment.Reply
                        |> ignore

    let private submitTopLevelComment (post : SelfPost) =
        assert (getRole post.Author = Role.User)

        printDivider ()
        printfn $"Post title: {post.Title}"
        printfn $"Post text: {post.SelfText}"

            // comment on post
        let response =
            getPostHistory post
                |> Chat.chat
        printfn ""
        printfn $"Bot: {response}"
        response
            |> addFooter
            |> post.Reply
            |> ignore

    let rec private monitorReplies (post : Post) =

        try
            let myCommentHistory = me.GetCommentHistory()
            for myComment in myCommentHistory do
                if myComment.Created >= post.Created then
                    let myComment = myComment.Info()   // make sure we have full details
                    if myComment.Root.Id = post.Id then
                        let userComments =
                            myComment.Replies
                                |> Seq.sortBy (fun reply -> reply.Created)
                                |> Seq.truncate 3
                        for userComment in userComments do
                            reply userComment

        with exn ->
            printDivider ()
            printfn $"{exn}"
            Thread.Sleep(10000)   // wait, then continue

        monitorReplies post

    /// Runs the bot.
    let run () =
        let post = Reddit.client.SelfPost("t3_11nhasc").About()
        submitTopLevelComment post
        monitorReplies post
