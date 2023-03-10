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

/// Type of a reddit thing.
[<RequireQualifiedAccess>]
type ThingType =
    | Post
    | Comment
    | Other

module Thing =

    /// Answers the type of the thing with the given full name.
    (*
        t1_: Comment
        t2_: Account
        t3_: Link
        t4_: Message
        t5_: Subreddit
        t6_: Award
    *)
    let getType (fullname : string) =
        match fullname.Substring(0, 3) with
            | "t1_" -> ThingType.Comment
            | "t3_" -> ThingType.Post
            | _ -> ThingType.Other
