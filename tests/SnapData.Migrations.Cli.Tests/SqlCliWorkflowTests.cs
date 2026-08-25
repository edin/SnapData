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

        var status = await RunAsync("status");
        AssertSuccess(status, "status");
        Assert.Contains("2 pending migrations", status.StandardOutput);

        var script = await RunAsync("script");
        AssertSuccess(script, "script");
        Assert.Contains("CREATE TABLE users", script.StandardOutput);
        Assert.Contains("ADD COLUMN email", script.StandardOutput);

        var migrated = await RunAsync("migrate", "--yes");
        AssertSuccess(migrated, "migrate");
        Assert.Contains("2 migrations applied", migrated.StandardOutput);
        Assert.True(File.Exists(Path.Combine(directory, "Scripts", "app.db")));

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
