using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;

namespace SnapData.Migrations.Cli.Runtime;

internal static class RoslynSdkResolver
{
    private static string? roslynDirectory;

    public static void Initialize()
    {
        AssemblyLoadContext.Default.Resolving += Resolve;
    }

    private static Assembly? Resolve(
        AssemblyLoadContext context,
        AssemblyName assemblyName)
    {
        if (assemblyName.Name is not (
            "Microsoft.CodeAnalysis" or
            "Microsoft.CodeAnalysis.CSharp"))
        {
            return null;
        }

        roslynDirectory ??= FindRoslynDirectory();
        var path = Path.Combine(roslynDirectory, $"{assemblyName.Name}.dll");
        return File.Exists(path) ? context.LoadFromAssemblyPath(path) : null;
    }

    private static string FindRoslynDirectory()
    {
        var runtimeDirectory = RuntimeEnvironment.GetRuntimeDirectory();
        var dotnetRoot = Path.GetFullPath(Path.Combine(
            runtimeDirectory,
            "..",
            "..",
            ".."));
        var sdkRoot = Path.Combine(dotnetRoot, "sdk");
        if (Directory.Exists(sdkRoot))
        {
            var candidate = Directory.GetDirectories(sdkRoot)
                .OrderByDescending(path => Directory.GetLastWriteTimeUtc(path))
                .Select(path => Path.Combine(path, "Roslyn", "bincore"))
                .FirstOrDefault(path => File.Exists(Path.Combine(
                    path,
                    "Microsoft.CodeAnalysis.CSharp.dll")));
            if (candidate is not null)
            {
                return candidate;
            }
        }

        throw new InvalidOperationException(
            "C# migration scaffolding requires a .NET SDK with Roslyn compiler assemblies.");
    }
}
