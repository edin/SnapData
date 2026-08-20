using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;

namespace SnapData.Migrations.Cli.Configuration;

internal sealed partial class IniConfigurationLoader
{
    private const string FileName = "snap.ini";
    private readonly Func<string, string?> environmentVariable;

    public IniConfigurationLoader(Func<string, string?>? environmentVariable = null)
    {
        this.environmentVariable = environmentVariable
            ?? Environment.GetEnvironmentVariable;
    }

    public MigrationCliConfiguration Load(
        string startDirectory,
        string? profile = null,
        string? configFile = null)
    {
        var resolvedConfigFile = configFile is null
            ? FindConfigFile(startDirectory)
            : ResolveExplicitConfigFile(configFile, startDirectory);
        var configDirectory = Path.GetDirectoryName(resolvedConfigFile)
            ?? throw new InvalidOperationException(
                $"Configuration path '{resolvedConfigFile}' has no parent directory.");
        var root = new ConfigurationBuilder()
            .SetBasePath(configDirectory)
            .AddIniFile(
                Path.GetFileName(resolvedConfigFile),
                optional: false,
                reloadOnChange: false)
            .Build();
        var baseSection = root.GetSection("Migration");
        if (!HasValues(baseSection))
        {
            throw new InvalidOperationException(
                $"Configuration file '{resolvedConfigFile}' does not contain a [Migration] section.");
        }

        IConfigurationSection? profileSection = null;
        if (!string.IsNullOrWhiteSpace(profile))
        {
            profileSection = root.GetSection($"Migration:{profile}");
            if (!HasValues(profileSection))
            {
                throw new InvalidOperationException(
                    $"Configuration profile '{profile}' was not found in '{resolvedConfigFile}'. " +
                    $"Add a [Migration:{profile}] section or select another profile.");
            }
        }

        string? Value(string name, params string[] aliases)
        {
            foreach (var key in new[] { name }.Concat(aliases))
            {
                var value = profileSection?[key];
                if (value is not null)
                {
                    return ExpandEnvironment(value);
                }
            }
            foreach (var key in new[] { name }.Concat(aliases))
            {
                var value = baseSection[key];
                if (value is not null)
                {
                    return ExpandEnvironment(value);
                }
            }
            return null;
        }

        return new MigrationCliConfiguration(
            resolvedConfigFile,
            string.IsNullOrWhiteSpace(profile) ? null : profile,
            Value("Provider", "Dialect") ?? string.Empty,
            Value("Connection") ?? string.Empty,
            ResolveOptionalPath(Value("Assembly"), configDirectory),
            ResolveOptionalPath(Value("Project"), configDirectory),
            NullIfWhiteSpace(Value("Configuration")) ?? "Debug",
            NullIfWhiteSpace(Value("TargetFramework", "Framework")),
            NullIfWhiteSpace(Value("Bundle", "MigrationBundle")),
            ResolveOptionalPath(Value("MigrationsPath"), configDirectory),
            NullIfWhiteSpace(Value("MigrationsNamespace")),
            NullIfWhiteSpace(Value("HistoryTable", "MigrationsTable"))
                ?? "__snapdata_migrations",
            ParseTimeout(Value("Timeout")));
    }

    internal static string FindConfigFile(string startDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(startDirectory);
        var fullStart = Path.GetFullPath(startDirectory);
        if (!Directory.Exists(fullStart))
        {
            throw new DirectoryNotFoundException(
                $"Configuration search directory '{fullStart}' does not exist.");
        }

        for (var directory = new DirectoryInfo(fullStart);
             directory is not null;
             directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, FileName);
            if (File.Exists(candidate))
            {
                return Path.GetFullPath(candidate);
            }
        }
        throw new FileNotFoundException(
            $"Could not find '{FileName}' in '{fullStart}' or any parent directory.");
    }

    private static string ResolveExplicitConfigFile(
        string configFile,
        string startDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configFile);
        var path = Path.IsPathRooted(configFile)
            ? configFile
            : Path.Combine(startDirectory, configFile);
        path = Path.GetFullPath(path);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                $"Configuration file '{path}' does not exist.", path);
        }
        return path;
    }

    private string ExpandEnvironment(string value) =>
        EnvironmentPattern().Replace(value, match =>
        {
            var name = match.Groups[1].Value;
            return environmentVariable(name)
                ?? throw new InvalidOperationException(
                    $"Environment variable '{name}' referenced by the migration configuration is not set.");
        });

    private static TimeSpan ParseTimeout(string? value)
    {
        if (value is null)
        {
            return TimeSpan.FromSeconds(30);
        }
        if (!double.TryParse(
                value,
                NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture,
                out var seconds) || seconds <= 0)
        {
            throw new InvalidOperationException(
                $"Migration Timeout must be a positive number of seconds, but was '{value}'.");
        }
        return TimeSpan.FromSeconds(seconds);
    }

    private static string? ResolveOptionalPath(string? value, string configDirectory)
    {
        value = NullIfWhiteSpace(value);
        if (value is null)
        {
            return null;
        }
        return Path.GetFullPath(Path.IsPathRooted(value)
            ? value
            : Path.Combine(configDirectory, value));
    }

    private static string? NullIfWhiteSpace(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;

    private static bool HasValues(IConfigurationSection section) =>
        section.GetChildren().Any(child => child.Value is not null);

    [GeneratedRegex(@"\$\{env\.([A-Za-z_][A-Za-z0-9_]*)\}", RegexOptions.IgnoreCase)]
    private static partial Regex EnvironmentPattern();
}
