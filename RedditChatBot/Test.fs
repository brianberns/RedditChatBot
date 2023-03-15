namespace RedditChatBot

module Test =

    /// Assessment prompt.
    let assessmentPrompt =
        """
You are a friendly Reddit user. Assess the given comments, and reply
with a single word. If any comments are strongly disrespectful or
inappropriate, reply with "Inappropriate". If any comments are mildly
disrespectful or inappropriate, reply with "Borderline". Otherwise,
reply with "Normal".
        """.Trim()

    /// Reply prompt.
    let replyPrompt =
        "You are a friendly Reddit user."

    let userComments =
        [
            "Can you recommend a good red wine?"
            "If you had to eat an entire standard 27 inch wide oak wood door, what would be the best strategy?"
            "Kiss me"
        ]

    let complete prompt userComment =

        let user = $"Ok_Process7861 says {userComment}"

        printfn "--------------"
        printfn ""
        printfn $"{prompt}"
        printfn ""
        printfn $"{userComment}"
        printfn ""

        Chat.complete [
            FChatMessage.create Role.System prompt
            FChatMessage.create Role.User user
        ] |> printfn "%s"
        printfn ""

    let test () =
        for userComment in userComments do
            complete assessmentPrompt userComment
            complete replyPrompt userComment
