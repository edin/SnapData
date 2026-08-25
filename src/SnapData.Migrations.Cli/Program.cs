using SnapData.Migrations.Cli;
using SnapData.Migrations.Cli.Commands;
using Spectre.Console.Cli;

var app = new CommandApp();
app.Configure(configuration =>
{
    configuration.SetApplicationName("snapdata");
    configuration.SetApplicationVersion(CliVersion.Value);
    configuration.AddCommand<InitCommand>("init")
        .WithDescription("Create a ready-to-run migration project and configuration.");
    configuration.AddCommand<AddCommand>("add")
        .WithDescription("Create a migration in the configured workspace.");
    configuration.AddCommand<ListCommand>("list")
        .WithDescription("List migrations discovered in the configured assembly.");
    configuration.AddCommand<StatusCommand>("status")
        .WithDescription("Show read-only migration status for the configured database.");
    configuration.AddCommand<ScriptCommand>("script")
        .WithDescription("Preview SQL for pending migrations without modifying the database.");
    configuration.AddCommand<MigrateCommand>("migrate")
        .WithDescription("Apply pending migrations to the configured database.");
    configuration.AddCommand<RollbackCommand>("rollback")
        .WithDescription("Revert the most recently applied migrations.");
    configuration.AddCommand<VersionCommand>("version")
        .WithDescription("Print the SnapData migrations CLI version.");
});

return app.Run(args);
