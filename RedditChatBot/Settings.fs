namespace RedditChatBot

open Microsoft.Extensions.Configuration

type RedditSettings =
    {
        RefreshToken : string
        AppSecret : string
    }

type OpenAiSettings =
    {
        ApiKey : string
    }

[<CLIMutable>]   // https://github.com/dotnet/runtime/issues/77677
type Settings =
    {
        Reddit : RedditSettings
        OpenAi : OpenAiSettings
    }

module Settings =

    let get =
        ConfigurationBuilder()
            .AddJsonFile("appsettings.json")
            .Build()
            .Get<Settings>()
