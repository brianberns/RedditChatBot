namespace RedditChatBot

open System

open Microsoft.Azure.Functions.Extensions.DependencyInjection
open Microsoft.Extensions.Configuration

type Startup() =
    inherit FunctionsStartup()

    override _.ConfigureAppConfiguration(builder) =
        let cs = Environment.GetEnvironmentVariable("ConnectionString")
        assert(not (isNull cs))
        builder.ConfigurationBuilder.AddAzureAppConfiguration(cs)
            |> ignore

    override _.Configure(_ : IFunctionsHostBuilder) =
        ()

[<assembly: FunctionsStartup(typeof<Startup>)>]
do ()
