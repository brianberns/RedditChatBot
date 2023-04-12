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
        let cs = Environment.GetEnvironmentVariable("ConnectionString")
        if isNull cs then
            failwith "ConnectionString environment variable must be set and point to an Azure app configuration"
        builder
            .ConfigurationBuilder
            .AddAzureAppConfiguration(cs)
            |> ignore

    override _.Configure(_ : IFunctionsHostBuilder) =
        ()

[<assembly: FunctionsStartup(typeof<Startup>)>]
do ()
