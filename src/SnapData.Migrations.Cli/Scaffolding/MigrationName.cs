using System.Text.RegularExpressions;

namespace SnapData.Migrations.Cli.Scaffolding;

internal sealed partial record MigrationName(string ClassName, string Slug)
{
    private const int MaximumClassNameLength = 128;

    public static MigrationName Parse(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        value = value.Trim();
        if (!ValidCharacters().IsMatch(value))
        {
            throw new ArgumentException(
                "Migration names may contain only ASCII letters, numbers, spaces, hyphens, and underscores.",
                nameof(value));
        }

        var words = Words().Matches(value)
            .Select(match => match.Value)
            .ToArray();
        if (words.Length == 0 || !char.IsAsciiLetter(words[0][0]))
        {
            throw new ArgumentException(
                "A migration name must start with an ASCII letter.",
                nameof(value));
        }

        var normalizedWords = words
            .Select(word => word.ToLowerInvariant())
            .ToArray();
        var className = string.Concat(normalizedWords.Select(ToPascalCase));
        if (className.Length > MaximumClassNameLength)
        {
            throw new ArgumentException(
                $"Migration names must produce class names no longer than {MaximumClassNameLength} characters.",
                nameof(value));
        }

        return new MigrationName(className, string.Join('_', normalizedWords));
    }

    private static string ToPascalCase(string word) =>
        char.ToUpperInvariant(word[0]) + word[1..];

    [GeneratedRegex(@"^[A-Za-z0-9 _-]+$")]
    private static partial Regex ValidCharacters();

    [GeneratedRegex(@"[A-Z]+(?=[A-Z][a-z]|[0-9]|\b)|[A-Z]?[a-z]+|[0-9]+")]
    private static partial Regex Words();
}
