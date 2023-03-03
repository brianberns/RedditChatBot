namespace RedditChatBot

open System

open Reddit
open Reddit.Controllers

module Reddit =

    let private settings = Settings.get.Reddit

    let client =
        RedditClient(
            appId = "fVstFww14kdp4hFRJCCzdg",
            refreshToken = settings.RefreshToken,
            appSecret = settings.AppSecret)

    /// Monitors for new posts by the given user.
    let monitorUserPosts (user : User) callback =

        let flag = user.MonitorPostHistory()
        assert(flag)

        user.PostHistoryUpdated.Add(fun evt ->
            for post in evt.Added do
                printfn $"monitorUserPosts: {post.Title}"
                callback post)

    /// Monitors for new comments by the given user.
    let monitorUserComments (user : User) callback =

        let flag = user.MonitorCommentHistory()
        assert(flag)

        user.CommentHistoryUpdated.Add(fun evt ->
            for comment in evt.Added do
                printfn $"monitorUserComments: {comment.Body}"
                callback comment)

    /// Monitor for new comments in the given stream.
    let monitorCommentStream (stream : Comments) callback =

        let flag = stream.MonitorNew()
        assert(flag)

        stream.NewUpdated.Add(fun evt ->
            for comment in evt.Added do
                printfn $"monitorCommentStream: {comment.Body}"
                callback comment)
