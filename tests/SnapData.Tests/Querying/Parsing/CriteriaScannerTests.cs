namespace SnapData.Tests;

public sealed class CriteriaScannerTests
{
    [Fact]
    public void Scans_join_criteria_using_attribute_metadata()
    {
        var result = new CriteriaScanner(
            "u.role_id = r.id AND r.active = @active").Scan();

        Assert.True(result.Succeeded);
        Assert.Equal(
            [
                CriteriaTokenKind.Identifier,
                CriteriaTokenKind.Dot,
                CriteriaTokenKind.Identifier,
                CriteriaTokenKind.Equal,
                CriteriaTokenKind.Identifier,
                CriteriaTokenKind.Dot,
                CriteriaTokenKind.Identifier,
                CriteriaTokenKind.And,
                CriteriaTokenKind.Identifier,
                CriteriaTokenKind.Dot,
                CriteriaTokenKind.Identifier,
                CriteriaTokenKind.Equal,
                CriteriaTokenKind.Parameter,
                CriteriaTokenKind.EndOfFile
            ],
            result.Tokens.Select(token => token.Kind));
        Assert.Equal("@active", result.Tokens[^2].Value);
    }

    [Fact]
    public void Keywords_are_case_insensitive_and_symbols_use_longest_match()
    {
        var result = new CriteriaScanner(
            "score >= 10 and score <> 20 OR score != 30").Scan();

        Assert.Empty(result.Diagnostics);
        Assert.Contains(result.Tokens, token => token.Kind == CriteriaTokenKind.GreaterThanOrEqual);
        Assert.Contains(result.Tokens, token => token.Kind == CriteriaTokenKind.NotEqualSql);
        Assert.Contains(result.Tokens, token => token.Kind == CriteriaTokenKind.NotEqual);
        Assert.Contains(result.Tokens, token => token.Kind == CriteriaTokenKind.And);
        Assert.Contains(result.Tokens, token => token.Kind == CriteriaTokenKind.Or);
    }

    [Fact]
    public void Scans_sql_strings_numbers_parameters_and_quoted_identifiers()
    {
        var result = new CriteriaScanner(
            "\"user\".name = 'O''Brien' AND amount >= 12.5e-2 AND id = :id").Scan();

        Assert.True(result.Succeeded);
        Assert.Contains(result.Tokens, token =>
            token.Kind == CriteriaTokenKind.QuotedIdentifier && token.Value == "\"user\"");
        Assert.Contains(result.Tokens, token =>
            token.Kind == CriteriaTokenKind.String && token.Value == "'O''Brien'");
        Assert.Contains(result.Tokens, token =>
            token.Kind == CriteriaTokenKind.Number && token.Value == "12.5e-2");
        Assert.Contains(result.Tokens, token =>
            token.Kind == CriteriaTokenKind.Parameter && token.Value == ":id");
    }

    [Fact]
    public void Trivia_is_skipped_and_locations_track_multiple_lines()
    {
        var result = new CriteriaScanner(
            """
            active = true -- current users
            AND /* required */ role_id IS NOT NULL
            """).Scan();

        Assert.True(result.Succeeded);
        Assert.DoesNotContain(result.Tokens, token => token.Kind == CriteriaTokenKind.Comment);
        var and = Assert.Single(result.Tokens, token => token.Kind == CriteriaTokenKind.And);
        Assert.Equal(2, and.Span.Line);
        Assert.Equal(1, and.Span.Column);
    }

    [Theory]
    [InlineData("name = 'unfinished", "Unterminated string literal")]
    [InlineData("name = \"unfinished", "Unterminated quoted identifier")]
    [InlineData("active = true /* unfinished", "Unterminated block comment")]
    [InlineData("active = #bad", "Unexpected character '#'")]
    public void Reports_scanner_diagnostics_with_source_spans(
        string source,
        string expectedMessage)
    {
        var result = new CriteriaScanner(source).Scan();

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Contains(expectedMessage, diagnostic.Message);
        Assert.InRange(diagnostic.Span.Position, 0, source.Length);
        Assert.True(diagnostic.Span.Length > 0);
    }

    [Fact]
    public void Every_token_kind_has_attribute_metadata()
    {
        Assert.Equal(
            Enum.GetValues<CriteriaTokenKind>().Length,
            CriteriaTokenMetadataProvider.All.Count);
        Assert.All(
            CriteriaTokenMetadataProvider.All,
            metadata => Assert.NotNull(
                CriteriaTokenMetadataProvider.ByKind[metadata.Kind]));
    }
}
