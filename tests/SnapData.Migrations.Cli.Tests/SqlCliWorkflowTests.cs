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
