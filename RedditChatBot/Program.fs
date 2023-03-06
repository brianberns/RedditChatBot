﻿namespace RedditChatBot

open System
open System.Threading
open Reddit.Controllers

module Program =

    let me = Reddit.client.User("EverydayChatBot")

    let private printDivider () =
        printfn ""
        printfn "----------------------------------------"
        printfn ""

    let private getRole (comment : Comment) =
        if comment.Author = me.Name then Role.System
        else Role.User

    let private getContent role (comment : Comment) =
        match role with
            | Role.User -> $"{comment.Author} says {comment.Body}"
            | _ -> comment.Body

    let private getContext comment =

        let rec loop depth comment =
            [
                let role = getRole comment
                let content = getContent role comment
                yield role, content

                if depth > 0 then
                    let parentFullname = comment.ParentFullname
                    if parentFullname.StartsWith("t1_") then
                        let parent = Reddit.client.Comment(parentFullname).About()
                        yield! loop (depth - 1) parent
            ]

        loop 5 comment
            |> List.rev

    /// Replies to the given comment, if necessary.
    let private reply (comment : Comment) =

        // let comment = comment.About()             // get full properties (expensive)

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

                    // construct user query
                let context = getContext comment
                assert(context |> Seq.last |> fst = Role.User)
                printDivider ()
                printfn $"Q: {context |> Seq.last |> snd}"

                    // get chat response
                let response = Chat.chat context
                printfn ""
                printfn $"A: {response}"
                comment.Reply(response) |> ignore

    /// Runs a chat session using the given post.
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
                        let myComment' = myComment.About()
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
        // let user = me
        let user = Reddit.client.User("bernsrite")
        user.GetPostHistory("submitted", sort="new", limit=1)   // get my latest post
            |> Seq.exactlyOne
            |> run
        0
