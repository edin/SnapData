using System.Reflection;

namespace SnapData.Migrations.Cli;

internal static class CliVersion
{
    public static string Value { get; } =
        typeof(CliVersion).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion
            .Split('+', 2)[0]
        ?? typeof(CliVersion).Assembly.GetName().Version?.ToString()
        ?? "unknown";
}
