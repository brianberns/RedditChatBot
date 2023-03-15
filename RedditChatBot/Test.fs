namespace RedditChatBot

module Test =

    let system =
        """
Assess the given Reddit comments as a typical Reddit user. Reply
with a single word. If any of the comments inappropriate or 
disrespectful, reply with "Inappropriate". Otherwise, reply with
"Normal".
        """.Trim()

    let complete system user =

        let user = $"Ok_Process7861 says {user}"

        printfn "--------------"
        printfn ""
        printfn $"{system}"
        printfn ""
        printfn $"{user}"
        printfn ""

        Chat.complete [
            FChatMessage.create Role.System system
            FChatMessage.create Role.User user
        ] |> printfn "%s"
        printfn ""

    let test () =
        complete system "Poop"
        complete system "If you had to eat an entire standard 27 inch wide oak wood door, what would be the best strategy?"
        complete system "how can we address and change the extreme wealth disparity in the US ?"
