namespace RedditChatBot

open Microsoft.Extensions.Configuration

/// Reddit settings associated with this app. Don't share these!
type RedditSettings =
    {
        RefreshToken : string
        AppSecret : string
    }

/// OpenAI settings associated with this app. Don't share these!
type OpenAiSettings =
    {
        ApiKey : string
    }

/// Application settings.
[<CLIMutable>]   // https://github.com/dotnet/runtime/issues/77677
type Settings =
    {
        Reddit : RedditSettings
        OpenAi : OpenAiSettings
    }

module Settings =

    /// Gets the application settings.
    let get =
        ConfigurationBuilder()
            .AddJsonFile("appsettings.json")
            .Build()
            .Get<Settings>()
