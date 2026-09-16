using System.Diagnostics;

namespace SnapData.Migrations.Cli.Tests;

[Collection("CLI workflows")]
public sealed class SqlCliWorkflowTests : IDisposable
{
    private readonly string directory = Path.Combine(
        Path.GetTempPath(),
        $"snapdata-sql-cli-{Guid.NewGuid():N}");
    private readonly string cliAssembly = Path.Combine(
        AppContext.BaseDirectory,
        "SnapData.Migrations.Cli.dll");

    [Fact(Timeout = 60_000)]
    public async Task Runs_complete_sqlite_workflow_without_a_dotnet_project()
    {
        var initialized = await RunFromAsync(
            Path.GetTempPath(),
            "init",
            "--sql",
            "--directory",
            directory);
        AssertSuccess(initialized, "init --sql");
        Assert.Contains("SnapData initialized", initialized.StandardOutput);
        Assert.Contains("Scripts", initialized.StandardOutput);
        Assert.False(File.Exists(Path.Combine(
            directory,
            "Scripts",
            "App.Migrations.csproj")));

        var listed = await RunAsync("list");
        AssertSuccess(listed, "list");
        Assert.Contains("2 migrations discovered", listed.StandardOutput);
        Assert.Contains("SQL files", listed.StandardOutput);
        Assert.DoesNotContain("Building", listed.StandardOutput);

        var status = await RunAsync("status", "--no-color");
        AssertSuccess(status, "status");
        Assert.Contains("2 pending migrations", status.StandardOutput);
        Assert.DoesNotContain("\u001b[", status.StandardOutput);

        var script = await RunAsync("script");
        AssertSuccess(script, "script");
        Assert.Contains("CREATE TABLE users", script.StandardOutput);
        Assert.Contains("ADD COLUMN email", script.StandardOutput);

        var deploymentFile = Path.Combine(directory, "deployment.sql");
        var exported = await RunAsync(
            "script",
            "--output",
            "deployment.sql");
        AssertSuccess(exported, "script --output");
        Assert.Contains("Wrote 2 SQL statements", exported.StandardOutput);
        var deploymentSql = File.ReadAllText(deploymentFile);
        Assert.Contains("-- Migration: 001_create_users", deploymentSql);
        Assert.Contains("CREATE TABLE users", deploymentSql);
        Assert.DoesNotContain("Pending migration SQL", deploymentSql);

        var protectedOutput = await RunAsync(
            "script",
            "--output",
            "deployment.sql");
        Assert.Equal(1, protectedOutput.ExitCode);
        Assert.Contains("--force", protectedOutput.StandardOutput);

        var replaced = await RunAsync(
            "script",
            "--output",
            "deployment.sql",
            "--force");
        AssertSuccess(replaced, "script --output --force");

        var unconfirmedMigration = await RunAsync("migrate", "--no-color");
        Assert.Equal(1, unconfirmedMigration.ExitCode);
        Assert.Contains("Use --yes", unconfirmedMigration.StandardOutput);
        var migrated = await RunAsync("migrate", "--yes");
        AssertSuccess(migrated, "migrate");
        Assert.Contains("2 migrations applied", migrated.StandardOutput);
        Assert.True(File.Exists(Path.Combine(directory, "Scripts", "app.db")));

        var protectedWhenCurrent = await RunAsync("script", "--output", "deployment.sql");
        Assert.Equal(1, protectedWhenCurrent.ExitCode);
        Assert.Equal(deploymentSql, File.ReadAllText(deploymentFile));

        var exportedWhenCurrent = await RunAsync(
            "script",
            "--output",
            "deployment.sql",
            "--force");
        AssertSuccess(exportedWhenCurrent, "script --output when current");
        Assert.Contains("Database is up to date", exportedWhenCurrent.StandardOutput);
        Assert.Contains("No pending migrations", File.ReadAllText(deploymentFile));
        Assert.DoesNotContain("CREATE TABLE", File.ReadAllText(deploymentFile));
        var newEmptyExport = await RunAsync("script", "--output", "empty.sql");
        AssertSuccess(newEmptyExport, "empty script export to new file");
        Assert.Contains("No pending migrations", File.ReadAllText(Path.Combine(directory, "empty.sql")));

        var unconfirmedRollback = await RunAsync("rollback", "--no-color");
        Assert.Equal(1, unconfirmedRollback.ExitCode);
        Assert.Contains("Use --yes", unconfirmedRollback.StandardOutput);
        var rolledBack = await RunAsync("rollback", "--steps", "2", "--yes");
        AssertSuccess(rolledBack, "rollback");
        Assert.Contains("2 pending migrations", rolledBack.StandardOutput);

        var paired = await RunAsync("add", "Create orders");
        AssertSuccess(paired, "add paired SQL migration");
        Assert.Contains("Migration created", paired.StandardOutput);
        Assert.Contains("Up", paired.StandardOutput);
        Assert.Contains("Down", paired.StandardOutput);

        var combined = await RunAsync("add", "Add order items", "--combined");
        AssertSuccess(combined, "add combined SQL migration");
        Assert.Contains("Migration created", combined.StandardOutput);
        Assert.Contains("File", combined.StandardOutput);

        var upFiles = Directory.GetFiles(
                Path.Combine(directory, "Scripts"),
                "*.sql")
            .Where(path => !path.EndsWith(
                ".down.sql",
                StringComparison.OrdinalIgnoreCase))
            .ToArray();
        Assert.Equal(4, upFiles.Length);
        Assert.Contains(
            upFiles,
            path => Path.GetFileName(path).EndsWith(
                "_create_orders.sql",
                StringComparison.Ordinal));
        var combinedFile = Assert.Single(
            upFiles,
            path => Path.GetFileName(path).EndsWith(
                "_add_order_items.sql",
                StringComparison.Ordinal));
        Assert.Contains("-- snapdata:down", File.ReadAllText(combinedFile));

        var finalList = await RunAsync("list");
        AssertSuccess(finalList, "list after add");
        Assert.Contains("4 migrations discovered", finalList.StandardOutput);
    }

    private async Task<ProcessResult> RunAsync(params string[] arguments)
        => await RunFromAsync(directory, arguments);

    private async Task<ProcessResult> RunFromAsync(
        string workingDirectory,
        params string[] arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add(cliAssembly);
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo };
        process.Start();
        var standardOutput = process.StandardOutput.ReadToEndAsync();
        var standardError = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return new ProcessResult(process.ExitCode, await standardOutput, await standardError);
    }

    private static void AssertSuccess(ProcessResult result, string command) =>
        Assert.True(
            result.ExitCode == 0,
            $"The '{command}' command exited with {result.ExitCode}." +
            $"{Environment.NewLine}stdout:{Environment.NewLine}{result.StandardOutput}" +
            $"{Environment.NewLine}stderr:{Environment.NewLine}{result.StandardError}");

    public void Dispose()
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private sealed record ProcessResult(
        int ExitCode,
        string StandardOutput,
        string StandardError);
}
