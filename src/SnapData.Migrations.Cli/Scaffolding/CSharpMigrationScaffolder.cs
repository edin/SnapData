using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using SnapData.Migrations.Cli.Configuration;

namespace SnapData.Migrations.Cli.Scaffolding;

internal sealed record CSharpMigrationScaffoldResult(
    string Id,
    string MigrationFile,
    string BundleFile);

internal sealed class CSharpMigrationScaffolder(
    MigrationIdGenerator? idGenerator = null)
{
    private static readonly UTF8Encoding Utf8WithoutBom = new(false);
    private readonly MigrationIdGenerator idGenerator = idGenerator ?? new();

    public CSharpMigrationScaffoldResult Create(
        MigrationCliConfiguration configuration,
        string migrationName)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        if (configuration.ProjectPath is null)
        {
            throw new InvalidOperationException(
                "C# migration creation requires Project to be configured. " +
                "A prebuilt Assembly cannot be edited safely.");
        }
        if (!File.Exists(configuration.ProjectPath))
        {
            throw new FileNotFoundException(
                $"Migration project '{configuration.ProjectPath}' does not exist.",
                configuration.ProjectPath);
        }

        var projectDirectory = Path.GetDirectoryName(configuration.ProjectPath)!;
        var migrationDirectory = configuration.MigrationsPath ?? projectDirectory;
        if (!Directory.Exists(migrationDirectory))
        {
            throw new DirectoryNotFoundException(
                $"C# migration directory '{migrationDirectory}' does not exist.");
        }

        var sources = LoadSources(projectDirectory);
        var bundle = FindBundle(sources, configuration.BundleType);
        var migrationNamespace = configuration.MigrationsNamespace
            ?? NamespaceOf(bundle.Declaration)
            ?? throw new InvalidOperationException(
                "MigrationsNamespace is not configured and could not be inferred from the bundle.");
        var name = MigrationName.Parse(migrationName);
        EnsureClassNameIsNew(sources, migrationNamespace, name.ClassName);
        EnsureNotRegistered(bundle.Declaration, name.ClassName);
        var id = idGenerator.Generate(name, ReadMigrationIds(sources));
        var migrationFile = Path.Combine(migrationDirectory, $"{name.ClassName}.cs");
        if (File.Exists(migrationFile))
        {
            throw new InvalidOperationException(
                $"Migration file '{migrationFile}' already exists. No files were changed.");
        }

        var updatedBundle = AddRegistration(
            bundle.Text,
            bundle.Declaration,
            name.ClassName);
        var migrationSource = MigrationTemplate(
            migrationNamespace,
            name.ClassName,
            id);
        var bundleTemp = $"{bundle.Path}.{Guid.NewGuid():N}.tmp";
        var migrationCreated = false;
        try
        {
            File.WriteAllText(bundleTemp, updatedBundle, bundle.Encoding);
            WriteNew(migrationFile, migrationSource);
            migrationCreated = true;
            File.Move(bundleTemp, bundle.Path, overwrite: true);
        }
        catch
        {
            if (File.Exists(bundleTemp))
            {
                File.Delete(bundleTemp);
            }
            if (migrationCreated && File.Exists(migrationFile))
            {
                File.Delete(migrationFile);
            }
            throw;
        }

        return new CSharpMigrationScaffoldResult(id, migrationFile, bundle.Path);
    }

    private static IReadOnlyList<SourceFile> LoadSources(string projectDirectory) =>
        Directory.GetFiles(projectDirectory, "*.cs", SearchOption.AllDirectories)
            .Where(path => !IsBuildOutput(path, projectDirectory))
            .Select(LoadSource)
            .ToArray();

    private static bool IsBuildOutput(string path, string projectDirectory)
    {
        var relative = Path.GetRelativePath(projectDirectory, path);
        var first = relative.Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            2)[0];
        return string.Equals(first, "bin", StringComparison.OrdinalIgnoreCase)
            || string.Equals(first, "obj", StringComparison.OrdinalIgnoreCase);
    }

    private static SourceFile LoadSource(string path)
    {
        using var reader = new StreamReader(
            path,
            Encoding.UTF8,
            detectEncodingFromByteOrderMarks: true);
        var text = reader.ReadToEnd();
        var encoding = reader.CurrentEncoding;
        var root = CSharpSyntaxTree.ParseText(text).GetCompilationUnitRoot();
        if (root.ContainsDiagnostics)
        {
            var error = root.GetDiagnostics().FirstOrDefault(diagnostic =>
                diagnostic.Severity == DiagnosticSeverity.Error);
            if (error is not null)
            {
                throw new InvalidOperationException(
                    $"C# source '{path}' could not be parsed: {error.GetMessage()}");
            }
        }
        return new SourceFile(path, text, encoding, root);
    }

    private static BundleSource FindBundle(
        IReadOnlyList<SourceFile> sources,
        string? requestedType)
    {
        var bundles = sources
            .SelectMany(source => source.Root.DescendantNodes()
                .OfType<ClassDeclarationSyntax>()
                .Where(IsMigrationBundle)
                .Select(declaration => new BundleSource(
                    source.Path,
                    source.Text,
                    source.Encoding,
                    declaration)))
            .ToArray();
        var matches = string.IsNullOrWhiteSpace(requestedType)
            ? bundles
            : bundles.Where(bundle =>
                string.Equals(
                    bundle.Declaration.Identifier.ValueText,
                    requestedType,
                    StringComparison.Ordinal)
                || string.Equals(
                    FullTypeName(bundle.Declaration),
                    requestedType,
                    StringComparison.Ordinal)).ToArray();
        return matches.Length switch
        {
            1 => matches[0],
            0 => throw new InvalidOperationException(
                requestedType is null
                    ? "No MigrationBundle class was found in the migration project."
                    : $"Migration bundle '{requestedType}' was not found in the migration project sources."),
            _ => throw new InvalidOperationException(
                "Multiple migration bundles matched. Configure Bundle using its full type name.")
        };
    }

    private static bool IsMigrationBundle(ClassDeclarationSyntax declaration) =>
        declaration.BaseList?.Types.Any(type =>
            type.Type.ToString().EndsWith("MigrationBundle", StringComparison.Ordinal)) == true;

    private static void EnsureClassNameIsNew(
        IEnumerable<SourceFile> sources,
        string migrationNamespace,
        string className)
    {
        var duplicate = sources.SelectMany(source => source.Root.DescendantNodes()
                .OfType<ClassDeclarationSyntax>())
            .FirstOrDefault(declaration =>
                string.Equals(
                    declaration.Identifier.ValueText,
                    className,
                    StringComparison.Ordinal)
                && string.Equals(
                    NamespaceOf(declaration),
                    migrationNamespace,
                    StringComparison.Ordinal));
        if (duplicate is not null)
        {
            throw new InvalidOperationException(
                $"Migration class '{migrationNamespace}.{className}' already exists. No files were changed.");
        }
    }

    private static void EnsureNotRegistered(
        ClassDeclarationSyntax bundle,
        string className)
    {
        var duplicate = ConfigureMethod(bundle).DescendantNodes()
            .OfType<GenericNameSyntax>()
            .Any(name =>
                string.Equals(name.Identifier.ValueText, "Add", StringComparison.Ordinal)
                && name.TypeArgumentList.Arguments.Any(argument =>
                    string.Equals(argument.ToString(), className, StringComparison.Ordinal)
                    || argument.ToString().EndsWith($".{className}", StringComparison.Ordinal)));
        if (duplicate)
        {
            throw new InvalidOperationException(
                $"Migration class '{className}' is already registered in the configured bundle. No files were changed.");
        }
    }

    private static IEnumerable<string> ReadMigrationIds(IEnumerable<SourceFile> sources) =>
        sources.SelectMany(source => source.Root.DescendantNodes()
            .OfType<PropertyDeclarationSyntax>()
            .Where(property => property.Identifier.ValueText == "Id")
            .Select(property => property.ExpressionBody?.Expression)
            .OfType<LiteralExpressionSyntax>()
            .Where(literal => literal.IsKind(SyntaxKind.StringLiteralExpression))
            .Select(literal => literal.Token.ValueText));

    private static string AddRegistration(
        string text,
        ClassDeclarationSyntax bundle,
        string className)
    {
        var method = ConfigureMethod(bundle);
        var parameter = method.ParameterList.Parameters.FirstOrDefault()?.Identifier.ValueText
            ?? "migrations";
        var newLine = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        if (method.Body is not null)
        {
            var closeBrace = method.Body.CloseBraceToken;
            var lineStart = text.LastIndexOf('\n', Math.Max(0, closeBrace.SpanStart - 1)) + 1;
            var closingIndent = text[lineStart..closeBrace.SpanStart];
            string insertion;
            if (closingIndent.All(character => character is ' ' or '\t'))
            {
                insertion =
                    $"    {parameter}.Add<{className}>();{newLine}{closingIndent}";
            }
            else
            {
                var methodLineStart = text.LastIndexOf(
                    '\n',
                    Math.Max(0, method.SpanStart - 1)) + 1;
                var methodIndent = text[methodLineStart..method.SpanStart];
                insertion =
                    $"{newLine}{methodIndent}    {parameter}.Add<{className}>();" +
                    $"{newLine}{methodIndent}";
            }
            return text.Insert(closeBrace.SpanStart, insertion);
        }
        if (method.ExpressionBody is not null)
        {
            var lineStart = text.LastIndexOf('\n', Math.Max(0, method.SpanStart - 1)) + 1;
            var methodIndent = text[lineStart..method.SpanStart];
            var expression = method.ExpressionBody.Expression.ToString();
            var replacement =
                $"{{{newLine}{methodIndent}    {expression};{newLine}" +
                $"{methodIndent}    {parameter}.Add<{className}>();{newLine}{methodIndent}}}";
            var start = method.ExpressionBody.ArrowToken.SpanStart;
            var end = method.SemicolonToken.Span.End;
            return text.Remove(start, end - start).Insert(start, replacement);
        }
        throw new InvalidOperationException(
            "The configured migration bundle Configure method has no body.");
    }

    private static MethodDeclarationSyntax ConfigureMethod(ClassDeclarationSyntax bundle)
    {
        var methods = bundle.Members.OfType<MethodDeclarationSyntax>()
            .Where(method => method.Identifier.ValueText == "Configure")
            .ToArray();
        return methods.Length switch
        {
            1 => methods[0],
            0 => throw new InvalidOperationException(
                "The configured migration bundle has no Configure method."),
            _ => throw new InvalidOperationException(
                "The configured migration bundle has multiple Configure methods.")
        };
    }

    private static string? NamespaceOf(SyntaxNode declaration)
    {
        var namespaces = declaration.Ancestors()
            .OfType<BaseNamespaceDeclarationSyntax>()
            .Reverse()
            .Select(item => item.Name.ToString());
        var value = string.Join('.', namespaces);
        return value.Length == 0 ? null : value;
    }

    private static string FullTypeName(ClassDeclarationSyntax declaration)
    {
        var ns = NamespaceOf(declaration);
        return ns is null
            ? declaration.Identifier.ValueText
            : $"{ns}.{declaration.Identifier.ValueText}";
    }

    private static void WriteNew(string path, string content)
    {
        using var stream = new FileStream(
            path,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None);
        using var writer = new StreamWriter(stream, Utf8WithoutBom);
        writer.Write(content);
        writer.WriteLine();
    }

    private static string MigrationTemplate(
        string migrationNamespace,
        string className,
        string id) => $$"""
        using SnapData.Migrations;

        namespace {{migrationNamespace}};

        public sealed class {{className}} : Migration
        {
            public override string Id => "{{id}}";

            public override void Up(MigrationPlan migration)
            {
                // Define the Up migration here.
            }

            public override void Down(MigrationPlan migration)
            {
                // Define the Down migration here.
            }
        }
        """;

    private sealed record SourceFile(
        string Path,
        string Text,
        Encoding Encoding,
        CompilationUnitSyntax Root);

    private sealed record BundleSource(
        string Path,
        string Text,
        Encoding Encoding,
        ClassDeclarationSyntax Declaration);
}
