namespace RedditChatBot

open System
open System.Threading

open Reddit.Controllers

module Footer =

    /// Horizontal rule markdown.
    let private hr = "---"

    /// Adds a footer to the given text.
    let add text =
        $"{text}\n\n{hr}\n\n^(The comment above was generated automatically. I am a bot based on [ChatGPT](https://openai.com/blog/chatgpt). You can find more information about me [here](https://www.reddit.com/user/friendly-chat-bot/comments/11nhqsj/about_me/).)"

    /// Removes the footer (if any) from the given text.
    let remove (text : string) =
        let idx = text.LastIndexOf(hr)
        if idx >= 0 then
            text.Substring(0, idx).TrimEnd()
        else text

module FriendlyChatBot =

    (*
     * The Reddit.NET API presents a very leaky abstraction. As a
     * general rule, we call Post.About() and Comment.Info()
     * defensively to make sure we have the full details of a thing.
     * Unfortunately, Comment.About() seems to have a race condition.
     *)

    /// Bot's user account.
    let bot = Reddit.client.User("friendly-chat-bot")

    /// Gets the role of the given author.
    let private getRole author =
        if author = bot.Name then Role.System
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
        let post = post.About()
        [
            Role.User, say post.Author post.Title
            if hasContent post.SelfText then
                Role.User, say post.Author post.SelfText
        ]

    /// Gets ancestor comments in chronological order.
    let private getCommentHistory comment =

        let rec loop (comment : Comment) =   // to-do: use fewer round-trips
            let comment = comment.Info()
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
                        yield! loop parent

                    | ThingType.Post ->
                        let post =
                            Reddit.client
                                .SelfPost(comment.ParentFullname)
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

    /// Submits a response comment to the given history.
    let private submitComment submit history =

            // get chat response
        let response = Chat.chat history
        printfn ""
        printfn $"Bot: {response}"

            // submit comment
        response
            |> Footer.add
            |> submit
            |> ignore

    /// Submits a top-level comment on the given post, if necessary.
    let private submitTopLevelComment (post : SelfPost) =

        let post = post.About()

            // don't comment on my own posts
        if getRole post.Author = Role.User then

                // has bot already replied to this comment?
            let handled =
                post.Comments.GetNew()   // to-do: what if the bot commented a long time ago?
                    |> Seq.exists (fun child ->
                        getRole child.Author = Role.System)

                // if not, begin to create a reply
            if not handled then

                    // get post as a history
                let history = getPostHistory post
                printDivider ()
                printfn $"Post title: {post.Title}"
                printfn $"Post text: {post.SelfText}"

                    // submit chat response
                submitComment post.Reply history

    /// Maximum number of nested bot replies in thread.
    let private maxDepth = 3

    /// Replies to the given comment, if necessary.
    let private submitReply (comment : Comment) =
        let comment = comment.Info()

            // ignore bot's own comments
        if getRole comment.Author <> Role.System
            && comment.Body <> "[deleted]" then   // no better way to check this?

                // has bot already replied to this comment?
            let handled =
                comment.Replies
                    |> Seq.exists (fun child ->
                        getRole child.Author = Role.System)

                // if not, begin to create a reply
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
                if nSystem >= maxDepth then
                    printfn ""
                    printfn "[Max depth exceeded]"
                else
                    submitComment comment.Reply history

    let rec private monitorReplies (post : Post) =

        let post = post.About()
        try
            let myCommentHistory = bot.GetCommentHistory()
            for myComment in myCommentHistory do
                if myComment.Created >= post.Created then
                    let myComment = myComment.Info()
                    if myComment.Root.Id = post.Id then
                        let userComments =
                            myComment.Replies
                                |> Seq.sortBy (fun reply -> reply.Created)
                                |> Seq.truncate 3
                        for userComment in userComments do
                            submitReply userComment

        with exn ->
            printDivider ()
            printfn $"{exn}"
            Thread.Sleep(10000)   // wait, then continue

        monitorReplies post

    /// Runs the bot.
    let run () =
        let post = Reddit.client.SelfPost("t3_11nhasc")
        submitTopLevelComment post
        monitorReplies post
