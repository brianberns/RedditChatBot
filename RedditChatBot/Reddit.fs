namespace RedditChatBot

open System
open Reddit

module Reddit =

    let private settings = Settings.get.Reddit

    let client =
        RedditClient(
            appId = "fVstFww14kdp4hFRJCCzdg",
            refreshToken = settings.RefreshToken,
            appSecret = settings.AppSecret)

    let monitor (post : Controllers.Post) callback =

        let flag = post.Comments.MonitorNew()
        assert(flag)

        post.Comments.NewUpdated.Add(fun evt ->
            for comment in evt.Added do
                callback comment)

        {
            new IDisposable with
                member _.Dispose() =
                    let flag = post.Comments.MonitorNew()
                    assert(not flag)
        }
