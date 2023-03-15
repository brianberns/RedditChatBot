namespace RedditChatBot

module Program =

    /// Runs the bot.
    [<EntryPoint>]
    let main args =
        Bot.create "friendly-chat-bot"
            |> Bot.run
        0
