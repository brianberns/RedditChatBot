namespace RedditChatBot

open Reddit

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

/// Reddit bot definition.
type RedditBotDef =
    {
        /// Bot's Reddit account name.
        BotName : string

        /// Bot version. E.g. "1.0".
        Version : string

        /// Bot author's Reddit account name.
        AuthorName : string
    }

module RedditBotDef =

    /// Creates a Reddit bot definition.
    let create botName version authorName =
        {
            BotName = botName
            Version = version
            AuthorName = authorName
        }

    /// Bot's user agent string, in format suggested by Reddit (more
    /// or less).
    let toUserAgent botDef =
        $"{botDef.BotName}:v{botDef.Version} (by /u/{botDef.AuthorName})"

/// A Reddit bot.
type RedditBot =
    {
        /// Bot definition.
        BotDef : RedditBotDef

        /// Reddit API client.
        Client : RedditClient
    }

module RedditBot =

    /// Creates a Reddit bot.
    let create settings botDef =
        {
            BotDef = botDef
            Client =
                RedditClient(
                    appId = settings.ApiKey,
                    refreshToken = settings.RefreshToken,
                    appSecret = settings.AppSecret,
                    userAgent = RedditBotDef.toUserAgent botDef)
        }

    /// Fetches all of the bot's unread messages.
    let getAllUnreadMessages bot =

        let rec loop count after =

                // get a batch of messages
            let messages =
                bot.Client.Account.Messages
                    .GetMessagesUnread(
                        limit = 100,
                        after = after,
                        count = count)

            seq {
                yield! messages

                    // try to get more messages?
                if messages.Count > 0 then
                    let count' = messages.Count + count
                    let after' = (Seq.last messages).Name
                    yield! loop count' after'
            }

        loop 0 ""
            |> Seq.sortBy (fun message ->
                -message.Score, message.CreatedUTC)
            |> Seq.toArray

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
