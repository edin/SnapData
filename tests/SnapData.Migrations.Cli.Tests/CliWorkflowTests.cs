using System.Diagnostics;

namespace SnapData.Migrations.Cli.Tests;

[Collection("CLI workflows")]
public sealed class CliWorkflowTests : IDisposable
{
    private readonly string directory;
    private readonly string cliAssembly;

    public CliWorkflowTests()
    {
        var repository = FindRepositoryRoot();
        directory = Path.Combine(
            repository,
            "artifacts",
            "test-workspaces",
            $"cli-{Guid.NewGuid():N}");
        cliAssembly = Path.Combine(
            AppContext.BaseDirectory,
            "SnapData.Migrations.Cli.dll");
    }

    [Fact(Timeout = 180_000)]
    public async Task Runs_complete_sqlite_migration_workflow()
    {
#if !NET10_0
        // The workflow builds a generated net8.0 project. Run it once from the
        // newest test target to avoid concurrent MSBuild processes contending
        // for the same referenced project outputs.
        await Task.CompletedTask;
        return;
#else
        var initialized = await RunAsync(
            Directory.GetParent(directory)!.FullName,
            "init",
            "--directory",
            directory);
        AssertSuccess(initialized, "init");
        Assert.Contains("SnapData initialized", initialized.StandardOutput);

        var added = await RunAsync(directory, "add", "Create orders");
        AssertSuccess(added, "add C# migration");
        Assert.Contains("Migration created", added.StandardOutput);
        Assert.True(File.Exists(Path.Combine(
            directory,
            "Migrations",
            "CreateOrders.cs")));
        Assert.Contains(
            "Add<CreateOrders>()",
            File.ReadAllText(Path.Combine(
                directory,
                "Migrations",
                "AppMigrationBundle.cs")));

        var listed = await RunAsync(directory, "list");
        AssertSuccess(listed, "list");
        Assert.Contains("3 migrations discovered", listed.StandardOutput);
        UseBuiltAssembly();

        var initialStatus = await RunAsync(directory, "status");
        AssertSuccess(initialStatus, "status");
        Assert.Contains("3 pending migrations", initialStatus.StandardOutput);

        var script = await RunAsync(directory, "script");
        AssertSuccess(script, "script");
        Assert.Contains("Pending migration SQL", script.StandardOutput);
        Assert.Contains("CREATE TABLE", script.StandardOutput, StringComparison.OrdinalIgnoreCase);

        var migrated = await RunAsync(directory, "migrate", "--yes");
        AssertSuccess(migrated, "migrate");
        Assert.Contains("Database is up to date; 3 migrations applied", migrated.StandardOutput);
        Assert.True(File.Exists(Path.Combine(directory, "Migrations", "app.db")));

        var migratedStatus = await RunAsync(directory, "status");
        AssertSuccess(migratedStatus, "status after migrate");
        Assert.Contains("Database is up to date; 3 migrations applied", migratedStatus.StandardOutput);

        var rolledBack = await RunAsync(
            directory,
            "rollback",
            "--steps",
            "3",
            "--yes");
        AssertSuccess(rolledBack, "rollback");
        Assert.Contains("3 pending migrations", rolledBack.StandardOutput);

        var rolledBackStatus = await RunAsync(directory, "status");
        AssertSuccess(rolledBackStatus, "status after rollback");
        Assert.Contains("3 pending migrations", rolledBackStatus.StandardOutput);
#endif
    }

    private void UseBuiltAssembly()
    {
        var configFile = Path.Combine(directory, "snap.ini");
        var lines = File.ReadAllLines(configFile)
            .Where(line => !line.StartsWith(
                "TargetFramework=",
                StringComparison.OrdinalIgnoreCase))
            .Select(line => line.StartsWith(
                    "Project=",
                    StringComparison.OrdinalIgnoreCase)
                ? "Assembly=Migrations/bin/Debug/net8.0/App.Migrations.dll"
                : line);
        File.WriteAllLines(configFile, lines);
    }

    private async Task<ProcessResult> RunAsync(
        string workingDirectory,
        params string[] arguments)
    {
        Directory.CreateDirectory(workingDirectory);
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
        return new ProcessResult(
            process.ExitCode,
            await standardOutput,
            await standardError);
    }

    private static void AssertSuccess(ProcessResult result, string command)
    {
        Assert.True(
            result.ExitCode == 0,
            $"The '{command}' command exited with {result.ExitCode}." +
            $"{Environment.NewLine}stdout:{Environment.NewLine}{result.StandardOutput}" +
            $"{Environment.NewLine}stderr:{Environment.NewLine}{result.StandardError}");
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "SnapData.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException("Could not locate the SnapData repository root.");
    }

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
