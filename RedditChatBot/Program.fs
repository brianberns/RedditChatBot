namespace RedditChatBot

open System
open Reddit.Controllers

module Program =

    let reply (comment : Comment) =

            // have I already replied to this comment?
        let handled =
            comment.Comments.GetComments()
                |> Seq.exists (fun child ->
                    child.Author = "EverydayChatBot")

            // if not, create a reply
        if not handled then
            (*
            let reply = Chat.chat comment.Body
            printfn $"{reply}"
            comment.Reply(reply) |> ignore
            *)
            ()

    [<EntryPoint>]
    let main args =

            // reply to top-level comments made on my posts
        let me = Reddit.client.Account.Me
        (*
        Reddit.monitorUserPosts me (fun post ->
            Reddit.monitorCommentStream post.Comments reply)
        *)

            // reply to comments made to my comments
        Reddit.monitorUserComments me (fun comment ->
            Reddit.monitorCommentStream comment.Comments reply)

        Console.ReadLine() |> ignore
        0
