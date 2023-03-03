namespace RedditChatBot

open System

module Program =

    [<EntryPoint>]
    let main args =
        use _ =
            let post = Reddit.client.Post("t3_11glnkd")   // "I am a ChatGPT bot"
            Reddit.monitor post (fun comment ->
                let handled =
                    comment.Comments.GetComments()
                        |> Seq.exists (fun child ->
                            child.Author = "EverydayChatBot")
                if not handled then
                    Chat.chat comment.Body
                        |> comment.Reply
                        |> ignore)

        Console.ReadLine() |> ignore
        0
