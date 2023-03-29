namespace RedditChatBot

open Reddit

/// Reddit bot description.
type BotDescription =
    {
        /// Bot's Reddit account name.
        BotName : string

        /// Bot version. E.g. "1.0".
        Version : string

        /// Bot author's Reddit account name.
        AuthorName : string
    }

module BotDescription =

    /// Creates a bot description.
    let create botName version authorName =
        {
            BotName = botName
            Version = version
            AuthorName = authorName
        }

    /// Bot's user agent string, in format suggested by Reddit (more
    /// or less).
    let toUserAgent botDesc =
        $"{botDesc.BotName}:v{botDesc.Version} (by /u/{botDesc.AuthorName})"

(*
 * To create a Reddit bot:
 *
 * - Use https://ssl.reddit.com/prefs/apps/ to create app ID and
 *   secret.
 *
 * - Use https://not-an-aardvark.github.io/reddit-oauth-helper/
 *   to create refresh token. Choose desired scopes and make
 *   permanent.
 *)

/// Reddit settings associated with this app. Don't share these!
[<CLIMutable>]   // https://github.com/dotnet/runtime/issues/77677
type RedditSettings =
    {
        /// App unique identifier.
        ApiKey : string

        /// App secret.
        AppSecret : string

        /// App authentication refresh token.
        RefreshToken : string
    }

module Reddit =

    /// Creates a Reddit API client.
    let createClient settings botDesc =
        RedditClient(
            appId = settings.ApiKey,
            refreshToken = settings.RefreshToken,
            appSecret = settings.AppSecret,
            userAgent = BotDescription.toUserAgent botDesc)

/// Type of a reddit thing.
[<RequireQualifiedAccess>]
type ThingType =

    /// A post (aka "link").
    | Post

    /// A comment.
    | Comment

    /// Some other thing.
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
