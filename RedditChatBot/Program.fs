namespace RedditChatBot

open System

module Program =

    /// My user account.
    let me = Reddit.client.User("AI-chat-bot")

    /// Runs the bot.
    [<EntryPoint>]
    let main args =

        let subreddit = Reddit.client.Subreddit("AskReddit")

        subreddit.Posts.RisingUpdated.Add(fun evt ->
            printfn ""
            printfn $"{DateTime.Now}"
            let post =
                evt.NewPosts
                    |> Seq.sortByDescending (fun post ->
                        post.Score)
                    |> Seq.head
            printfn $"   {post.Title}: {post.Score}")

        let flag = subreddit.Posts.MonitorRising()
        assert(flag)

        Console.ReadLine() |> ignore

        0
