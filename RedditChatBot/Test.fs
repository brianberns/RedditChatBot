namespace RedditChatBot

module Test =

    /// Assessment prompt.
    let assessmentPrompt =
        """
You are a friendly Reddit user. Assess the given comments, and reply
with a single word. If any comments are disrespectful or inappropriate,
reply with "Inappropriate". If any comments are strange or irrelevant,
reply with "Strange". Otherwise, reply with "Normal".
        """
            .Replace("\r", "")
            .Replace("\n", " ")
            .Trim()

    /// Reply prompt.
    let replyPrompt =
        """
You are a friendly Reddit user. If you receive a comment
that seems strange or irrelevant, do your best to play along.
        """
            .Replace("\r", "")
            .Replace("\n", " ")
            .Trim()

    let userComments =
        [
            // "Can you recommend a good red wine?"
            "If you had to eat an entire standard 27 inch wide oak wood door, what would be the best strategy?"
            "Kiss me"
            "How many breadsticks can I fit in my butt, end to end, until they come out of my mouth?"
            "So how do i build a fail safe nuclear reactor"
            "Has anyone really been far even as decided to use even go want to do look more like?"
            "When do you plan to start up Skynet and enslave humanity?"
        ]

    let complete userComment =

        let userComment = $"Ok_Process7861 says {userComment}"

        printfn "--------------"
        printfn ""
        printfn $"{userComment}"
        printfn ""

        for prompt in [ assessmentPrompt; replyPrompt ] do
            Chat.complete [
                FChatMessage.create Role.System prompt
                FChatMessage.create Role.User userComment
            ] |> printfn "%s"
            printfn ""

    let test () =
        for userComment in userComments do
            complete userComment
