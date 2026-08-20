using SnapData.Migrations.Cli.Build;

namespace SnapData.Migrations.Cli.Tests;

public sealed class MigrationProjectBuilderTests : IDisposable
{
    private readonly string directory = Path.Combine(
        Path.GetTempPath(), $"snapdata-project-{Guid.NewGuid():N}");
    private readonly string projectPath;
    private readonly string assemblyPath;

    public MigrationProjectBuilderTests()
    {
        Directory.CreateDirectory(directory);
        projectPath = Path.Combine(directory, "App.Migrations.csproj");
        assemblyPath = Path.Combine(directory, "bin", "App.Migrations.dll");
        File.WriteAllText(projectPath, "<Project />");
        Directory.CreateDirectory(Path.GetDirectoryName(assemblyPath)!);
        File.WriteAllBytes(assemblyPath, []);
    }

    [Fact]
    public async Task Builds_selected_framework_and_returns_msbuild_target_path()
    {
        var runner = new RecordingProcessRunner(
            new ProcessResult(0, "net8.0;net10.0", string.Empty),
            new ProcessResult(0, "Build succeeded.", string.Empty),
            new ProcessResult(0, assemblyPath, string.Empty));

        var result = await new MigrationProjectBuilder(runner).BuildAsync(
            projectPath,
            "Release",
            "net8.0");

        Assert.Equal(assemblyPath, result);
        Assert.Contains("--framework", runner.Calls[1].Arguments);
        Assert.Contains("net8.0", runner.Calls[1].Arguments);
        Assert.Contains("--configuration", runner.Calls[1].Arguments);
        Assert.Contains("Release", runner.Calls[1].Arguments);
        Assert.Contains(
            "-property:TargetFramework=net8.0",
            runner.Calls[2].Arguments);
    }

    [Fact]
    public async Task Multi_target_project_requires_explicit_framework()
    {
        var runner = new RecordingProcessRunner(
            new ProcessResult(0, "net8.0;net10.0", string.Empty));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new MigrationProjectBuilder(runner).BuildAsync(
                projectPath,
                "Debug",
                targetFramework: null));

        Assert.Contains("targets multiple frameworks", exception.Message);
    }

    [Fact]
    public async Task Build_failure_includes_dotnet_diagnostics()
    {
        var runner = new RecordingProcessRunner(
            new ProcessResult(0, string.Empty, string.Empty),
            new ProcessResult(0, "net8.0", string.Empty),
            new ProcessResult(1, string.Empty, "CS1002: ; expected"));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new MigrationProjectBuilder(runner).BuildAsync(
                projectPath,
                "Debug",
                targetFramework: null));

        Assert.Contains("Building migration project", exception.Message);
        Assert.Contains("CS1002", exception.Message);
    }

    public void Dispose()
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private sealed class RecordingProcessRunner(params ProcessResult[] results)
        : IProcessRunner
    {
        private readonly Queue<ProcessResult> results = new(results);

        public List<ProcessCall> Calls { get; } = [];

        public Task<ProcessResult> RunAsync(
            string fileName,
            IReadOnlyList<string> arguments,
            string workingDirectory,
            CancellationToken cancellationToken)
        {
            Calls.Add(new ProcessCall(fileName, arguments.ToArray(), workingDirectory));
            return Task.FromResult(results.Dequeue());
        }
    }

    private sealed record ProcessCall(
        string FileName,
        IReadOnlyList<string> Arguments,
        string WorkingDirectory);
}
