using System.Reflection;
using System.Runtime.Loader;
using SnapData.Migrations;

namespace SnapData.Migrations.Cli.Discovery;

internal sealed class MigrationAssemblyCatalog
{
    public MigrationCatalog Load(
        string assemblyPath,
        string? migrationsNamespace = null,
        string? bundleType = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assemblyPath);
        var fullPath = Path.GetFullPath(assemblyPath);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException(
                $"Migration assembly '{fullPath}' does not exist. Build the migration project or correct the Assembly setting.",
                fullPath);
        }

        var loadContext = new MigrationAssemblyLoadContext(fullPath);
        try
        {
            var assembly = loadContext.LoadFromAssemblyPath(fullPath);
            var types = assembly.GetTypes();
            var bundle = FindBundle(types, migrationsNamespace, bundleType);
            IEnumerable<Migration> migrations = bundle is null
                ? new MigrationCollection().ScanTypes(
                    types,
                    type => IsInNamespace(type, migrationsNamespace))
                : bundle;
            return new MigrationCatalog(
                migrations.ToArray(),
                bundle?.GetType().FullName);
        }
        catch (ReflectionTypeLoadException exception)
        {
            var loaderMessages = exception.LoaderExceptions
                .Where(item => item is not null)
                .Select(item => item!.Message)
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            var details = loaderMessages.Length == 0
                ? string.Empty
                : $" {string.Join(" ", loaderMessages)}";
            throw new InvalidOperationException(
                $"Migration types could not be loaded from '{fullPath}'.{details}",
                exception);
        }
    }

    private static MigrationBundle? FindBundle(
        IEnumerable<Type> types,
        string? migrationsNamespace,
        string? requestedType)
    {
        var bundles = types
            .Where(type =>
                typeof(MigrationBundle).IsAssignableFrom(type)
                && !type.IsAbstract
                && !type.ContainsGenericParameters)
            .ToArray();
        Type? selected;
        if (!string.IsNullOrWhiteSpace(requestedType))
        {
            var matches = bundles.Where(type =>
                    string.Equals(type.FullName, requestedType, StringComparison.Ordinal)
                    || string.Equals(type.Name, requestedType, StringComparison.Ordinal))
                .ToArray();
            selected = matches.Length switch
            {
                0 => throw new InvalidOperationException(
                    $"Migration bundle '{requestedType}' was not found in the configured assembly."),
                1 => matches[0],
                _ => throw new InvalidOperationException(
                    $"Migration bundle name '{requestedType}' is ambiguous. Use its full type name.")
            };
        }
        else
        {
            var candidates = bundles
                .Where(type => IsInNamespace(type, migrationsNamespace))
                .ToArray();
            selected = candidates.Length switch
            {
                0 => null,
                1 => candidates[0],
                _ => throw new InvalidOperationException(
                    "Multiple migration bundles were discovered: " +
                    $"{string.Join(", ", candidates.Select(type => type.FullName))}. " +
                    "Set Bundle to the full type name to select one.")
            };
        }

        if (selected is null)
        {
            return null;
        }
        if (selected.GetConstructor(Type.EmptyTypes) is null)
        {
            throw new InvalidOperationException(
                $"Migration bundle type '{selected.FullName}' needs a public parameterless constructor.");
        }
        return (MigrationBundle)(Activator.CreateInstance(selected)
            ?? throw new InvalidOperationException(
                $"Migration bundle type '{selected.FullName}' could not be created."));
    }

    private static bool IsInNamespace(Type type, string? migrationsNamespace)
    {
        if (string.IsNullOrWhiteSpace(migrationsNamespace))
        {
            return true;
        }

        return string.Equals(
                type.Namespace,
                migrationsNamespace,
                StringComparison.Ordinal)
            || type.Namespace?.StartsWith(
                $"{migrationsNamespace}.",
                StringComparison.Ordinal) == true;
    }

    private sealed class MigrationAssemblyLoadContext(string componentAssemblyPath)
        : AssemblyLoadContext(isCollectible: false)
    {
        private readonly AssemblyDependencyResolver resolver =
            new(componentAssemblyPath);

        protected override Assembly? Load(AssemblyName assemblyName)
        {
            var shared = Default.Assemblies.FirstOrDefault(assembly =>
                AssemblyName.ReferenceMatchesDefinition(
                    assembly.GetName(),
                    assemblyName));
            if (shared is not null)
            {
                return shared;
            }

            var path = resolver.ResolveAssemblyToPath(assemblyName);
            return path is null ? null : LoadFromAssemblyPath(path);
        }

        protected override nint LoadUnmanagedDll(string unmanagedDllName)
        {
            var path = resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
            return path is null ? nint.Zero : LoadUnmanagedDllFromPath(path);
        }
    }
}
