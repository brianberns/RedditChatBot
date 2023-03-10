namespace RedditChatBot

open System
open System.Threading

open Reddit.Controllers

module Footer =

    /// Horizontal rule markdown.
    let private hr = "---"

    /// Adds a footer to the given body of text.
    let add body =
        $"{body}\n\n{hr}\n\n^(The comment above was generated automatically. I am a bot based on [ChatGPT](https://openai.com/blog/chatgpt). You can find more information about me [here](https://www.reddit.com/user/friendly-chat-bot/comments/11nhqsj/about_me/).)"

    /// Removes the footer from the given body of text.
    let remove (content : string) =
        let idx = content.LastIndexOf(hr)
        if idx >= 0 then
            content.Substring(0, idx).TrimEnd()
        else
            content

module FriendlyChatBot =

    /// My user account.
    let me = Reddit.client.User("friendly-chat-bot")

    /// Gets the role of the given author.
    let private getRole author =
        if author = me.Name then Role.System
        else Role.User

    /// Does the given text contain any content?
    let private hasContent =
        String.IsNullOrWhiteSpace >> not

    /// Says the given text as the given author.
    let private say author text =
        assert(hasContent author)
        assert(hasContent text)
        $"{author} says {text}"

    /// Converts the given post's content into a history.
    let private getPostHistory (post : SelfPost) =
        [
            Role.User, say post.Author post.Title
            if hasContent post.SelfText then
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
                        | _ -> Footer.remove comment.Body
                yield role, content

                    // ancestors
                match Thing.getType comment.ParentFullname with

                    | ThingType.Comment ->
                        let parent =
                            Reddit.client
                                .Comment(comment.ParentFullname)
                                .Info()
                        yield! loop parent

                    | ThingType.Post ->
                        let post =
                            Reddit.client
                                .SelfPost(comment.ParentFullname)
                                .About()
                        yield! getPostHistory post

                    | ThingType.Other ->
                        failwith $"Unexpected type: {comment.ParentFullname}"
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
                        |> Footer.add
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
            |> Footer.add
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
