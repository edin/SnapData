namespace SnapData.Migrations.Cli.Build;

internal sealed class MigrationProjectBuilder(IProcessRunner? processRunner = null)
{
    private readonly IProcessRunner processRunner = processRunner ?? new ProcessRunner();

    public async Task<string> BuildAsync(
        string projectPath,
        string configuration,
        string? targetFramework,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(configuration);
        var fullProjectPath = Path.GetFullPath(projectPath);
        if (!File.Exists(fullProjectPath))
        {
            throw new FileNotFoundException(
                $"Migration project '{fullProjectPath}' does not exist.",
                fullProjectPath);
        }

        var workingDirectory = Path.GetDirectoryName(fullProjectPath)
            ?? throw new InvalidOperationException(
                $"Migration project path '{fullProjectPath}' has no parent directory.");
        var frameworks = await ReadTargetFrameworksAsync(
            fullProjectPath,
            workingDirectory,
            cancellationToken).ConfigureAwait(false);
        var selectedFramework = SelectTargetFramework(frameworks, targetFramework);

        var buildArguments = new List<string>
        {
            "build",
            fullProjectPath,
            "--nologo",
            "--configuration",
            configuration,
            "--verbosity",
            "minimal"
        };
        if (selectedFramework is not null)
        {
            buildArguments.Add("--framework");
            buildArguments.Add(selectedFramework);
        }

        var build = await processRunner.RunAsync(
            "dotnet",
            buildArguments,
            workingDirectory,
            cancellationToken).ConfigureAwait(false);
        EnsureSuccess(build, $"Building migration project '{fullProjectPath}' failed.");

        var targetPathArguments = new List<string>
        {
            "msbuild",
            fullProjectPath,
            "-nologo",
            "-getProperty:TargetPath",
            $"-property:Configuration={configuration}"
        };
        if (selectedFramework is not null)
        {
            targetPathArguments.Add($"-property:TargetFramework={selectedFramework}");
        }

        var targetPathResult = await processRunner.RunAsync(
            "dotnet",
            targetPathArguments,
            workingDirectory,
            cancellationToken).ConfigureAwait(false);
        EnsureSuccess(
            targetPathResult,
            $"Could not determine the output assembly for '{fullProjectPath}'.");
        var targetPath = targetPathResult.StandardOutput.Trim();
        if (string.IsNullOrWhiteSpace(targetPath))
        {
            throw new InvalidOperationException(
                $"MSBuild returned an empty TargetPath for migration project '{fullProjectPath}'.");
        }

        targetPath = Path.GetFullPath(targetPath, workingDirectory);
        if (!File.Exists(targetPath))
        {
            throw new FileNotFoundException(
                $"The built migration assembly '{targetPath}' does not exist.",
                targetPath);
        }
        return targetPath;
    }

    private async Task<IReadOnlyList<string>> ReadTargetFrameworksAsync(
        string projectPath,
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        var result = await processRunner.RunAsync(
            "dotnet",
            ["msbuild", projectPath, "-nologo", "-getProperty:TargetFrameworks"],
            workingDirectory,
            cancellationToken).ConfigureAwait(false);
        EnsureSuccess(result, $"Could not inspect migration project '{projectPath}'.");
        var value = result.StandardOutput.Trim();
        if (string.IsNullOrWhiteSpace(value))
        {
            result = await processRunner.RunAsync(
                "dotnet",
                ["msbuild", projectPath, "-nologo", "-getProperty:TargetFramework"],
                workingDirectory,
                cancellationToken).ConfigureAwait(false);
            EnsureSuccess(result, $"Could not inspect migration project '{projectPath}'.");
            value = result.StandardOutput.Trim();
        }

        return value
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private static string? SelectTargetFramework(
        IReadOnlyList<string> frameworks,
        string? requestedFramework)
    {
        if (!string.IsNullOrWhiteSpace(requestedFramework))
        {
            var match = frameworks.FirstOrDefault(framework => string.Equals(
                framework,
                requestedFramework,
                StringComparison.OrdinalIgnoreCase));
            if (match is null && frameworks.Count > 0)
            {
                throw new InvalidOperationException(
                    $"Target framework '{requestedFramework}' is not declared by the migration project. " +
                    $"Available frameworks: {string.Join(", ", frameworks)}.");
            }
            return match ?? requestedFramework;
        }

        return frameworks.Count switch
        {
            0 => null,
            1 => frameworks[0],
            _ => throw new InvalidOperationException(
                "The migration project targets multiple frameworks. " +
                $"Set TargetFramework to one of: {string.Join(", ", frameworks)}.")
        };
    }

    private static void EnsureSuccess(ProcessResult result, string message)
    {
        if (result.ExitCode == 0)
        {
            return;
        }

        var details = string.IsNullOrWhiteSpace(result.StandardError)
            ? result.StandardOutput.Trim()
            : result.StandardError.Trim();
        throw new InvalidOperationException(
            string.IsNullOrWhiteSpace(details) ? message : $"{message}{Environment.NewLine}{details}");
    }
}
