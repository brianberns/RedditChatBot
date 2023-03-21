namespace RedditChatBot

open System

open Microsoft.Azure.Functions.Extensions.DependencyInjection
open Microsoft.Extensions.Configuration

/// Azure functions statup type.
type Startup() =
    inherit FunctionsStartup()

    /// Connects to Azure app configuration.
    override _.ConfigureAppConfiguration(builder) =
        Environment.GetEnvironmentVariable("ConnectionString")
            |> builder.ConfigurationBuilder.AddAzureAppConfiguration
            |> ignore

    override _.Configure(_ : IFunctionsHostBuilder) =
        ()

[<assembly: FunctionsStartup(typeof<Startup>)>]
do ()
