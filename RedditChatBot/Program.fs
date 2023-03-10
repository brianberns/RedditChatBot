namespace RedditChatBot

module Program =

    /// Runs the bot.
    [<EntryPoint>]
    let main args =
        FriendlyChatBot.run ()
        0
