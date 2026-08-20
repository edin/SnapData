using SnapData.Migrations.Cli;
using SnapData.Migrations.Cli.Commands;
using Spectre.Console.Cli;

var app = new CommandApp();
app.Configure(configuration =>
{
    configuration.SetApplicationName("snapdata");
    configuration.SetApplicationVersion(CliVersion.Value);
    configuration.AddCommand<ListCommand>("list")
        .WithDescription("List migrations discovered in the configured assembly.");
    configuration.AddCommand<StatusCommand>("status")
        .WithDescription("Show read-only migration status for the configured database.");
    configuration.AddCommand<VersionCommand>("version")
        .WithDescription("Print the SnapData migrations CLI version.");
});

return app.Run(args);
