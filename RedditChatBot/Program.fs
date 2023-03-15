namespace RedditChatBot

module Program =

    /// Runs the bot.
    [<EntryPoint>]
    let main args =
        { Bot.create "friendly-chat-bot"
            with MinCommentDelay = System.TimeSpan.Zero }
            |> Bot.run
        0
