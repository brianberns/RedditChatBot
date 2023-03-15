namespace RedditChatBot

open System
open System.Threading

open Reddit.Controllers

[<AutoOpen>]
module Prelude =

    /// Flips order of function arguments.
    let flip f a b = f b a

type Bot =
    {
        /// Bot's user account.
        User : User

        /// Maximum number of nested bot replies in thread.
        MaxDepth : int

        /// Minimum time between comments, to avoid Reddit's spam filter.
        MinCommentDelay : TimeSpan

        /// Time of the bot's most recent comment.
        LastCommentTime : DateTime
    }

module Bot =

    (*
     * The Reddit.NET API presents a very leaky abstraction. As a
     * general rule, we call Post.About() and Comment.Info()
     * defensively to make sure we have the full details of a thing.
     * (Unfortunately, Comment.About() seems to have a race condition.)
     *)

    /// Creates a bot with the given user name.
    let create name =
        {
            User = Reddit.client.User(name : string)
            MaxDepth = 3
            MinCommentDelay =
                TimeSpan(hours = 0, minutes = 5, seconds = 1)
            LastCommentTime = DateTime.Now
        }

    /// Determines the role of the given comment's author.
    let private getRole (comment : Comment) bot =
        if comment.Author = bot.User.Name then Role.System
        else Role.User

    /// Converts the given comment to a chat message.
    let private createChatMessage comment bot =
        let role = getRole comment bot
        let content =
            match role with
                | Role.User -> $"{comment.Author} says {comment.Body}"
                | _ -> comment.Body
        FChatMessage.create role content

    /// Gets ancestor comments in chronological order.
    let private getHistory comment bot : ChatHistory =

        let rec loop (comment : Comment) =   // to-do: use fewer round-trips
            let comment = comment.Info()
            [
                    // this comment
                yield createChatMessage comment bot

                    // ancestors
                match Thing.getType comment.ParentFullname with
                    | ThingType.Comment ->
                        let parent =
                            Reddit.client
                                .Comment(comment.ParentFullname)
                        yield! loop parent
                    | _ -> ()
            ]

        loop comment
            |> List.rev
            :> ChatHistory

    /// Delays the given bot, if necessary, to avoid spam filter.
    let private delay bot =
        let timeout =
            bot.LastCommentTime
                + bot.MinCommentDelay
                - DateTime.Now
        if timeout > TimeSpan.Zero then
            Thread.Sleep(timeout)

    /// Replies to the given comment, if necessary.
    let private submitReply (comment : Comment) bot =

            // ensure we have full details
        let comment = comment.Info()

            // ignore bot's own comments
        if getRole comment bot <> Role.System
            && comment.Body <> "[deleted]" then   // no better way to check this?

                // has bot already replied to this comment?
            let handled =
                comment.Replies
                    |> Seq.exists (fun child ->
                        getRole child bot = Role.System)

                // if not, begin to create a reply
            if handled then bot
            else
                    // get comment history
                let history = getHistory comment bot

                    // avoid deeply nested threads
                let nBot =
                    history
                        |> Seq.where (fun msg ->
                            msg.Role = Role.System)
                        |> Seq.length
                if nBot < bot.MaxDepth then

                        // sleep if necessary to avoid spam filter
                    delay bot

                        // reply with chat response
                    let response = Chat.complete history
                    comment.Reply(response) |> ignore

                        // update bot
                    { bot with LastCommentTime = DateTime.Now }

                else bot

        else bot

    /// Replies to the given comment safely, if necessary.
    let private submitReplySafe comment bot =
        try
            submitReply comment bot
        with exn ->
            printfn ""
            printfn $"{exn}"
            Thread.Sleep(10000)   // wait for problem to clear up, hopefully
            bot

    /// Runs a chat session in the given post.
    let rec private runPost (post : Post) bot =

        let rec loop bot =

                // get comments that we'll reply to
            let comments =
                [
                        // recent top-level comments in the post
                    yield! post.Comments.GetNew(limit = 100)

                        // replies to the bot's recent comments in this post
                    let botCommentHistory =
                        bot.User.GetCommentHistory(
                            context = 0,
                            limit = 100,
                            sort = "new")
                    for botComment in botCommentHistory do
                        if botComment.Created >= post.Created then
                            let botComment = botComment.Info()
                            if botComment.Root.Id = post.Id then
                                yield! botComment.Replies
                ]

                // reply to every comment, if necessary
            List.fold (flip submitReplySafe) bot comments
                |> loop

        loop bot |> ignore

    /// Runs the given bot.
    let run bot =

           // get bot's latest post
        let post =
            bot.User.GetPostHistory()   // must sort manually
                |> Seq.sortByDescending (fun pst -> pst.Created)
                |> Seq.head
        printfn $"{post.Title}"
        printfn $"{post.Created}"

            // run session in the post
        runPost post bot
