namespace RedditChatBot

open Reddit

module Reddit =

    /// Reddit application settings.
    let private settings = Settings.get.Reddit

    /// Reddit client.
    let client =
        RedditClient(
            appId = settings.ApiKey,
            refreshToken = settings.RefreshToken,
            appSecret = settings.AppSecret,
            userAgent = "AI-chat-bot:v1.0 (by /u/brianberns)")   // this is the format that Reddit suggests (more or less)
