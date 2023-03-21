namespace RedditChatBot

open System

open Microsoft.Azure.Functions.Extensions.DependencyInjection
open Microsoft.Extensions.Configuration

(* https://learn.microsoft.com/en-us/azure/azure-app-configuration/quickstart-azure-functions-csharp *)

/// Incorporates the Azure app configuration service into our Azure
/// functions app.
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
