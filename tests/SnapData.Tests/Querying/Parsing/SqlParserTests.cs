namespace SnapData.Tests;

public sealed class SqlParserTests
{
    [Fact]
    public void Criteria_parser_preserves_boolean_precedence_and_parentheses()
    {
        var predicate = SqlParser.ParseCriteria(
            "active = true OR (score >= @minimum AND NOT deleted)",
            new { minimum = 10 });
        var query = Sql.From("users").Where(predicate).Build();

        Assert.Equal(
            "SELECT * FROM \"users\" WHERE (\"active\" = @p1 OR (\"score\" >= @p2 AND NOT (\"deleted\" = @p3)))",
            query.Text);
        Assert.Equal(true, query.Parameters["p1"]);
        Assert.Equal(10, query.Parameters["p2"]);
        Assert.Equal(true, query.Parameters["p3"]);
    }

    [Fact]
    public void Criteria_parser_supports_null_like_between_and_in()
    {
        var query = Sql.From("users")
            .Where(SqlParser.ParseCriteria(
                "name IS NOT NULL AND name LIKE @search AND score BETWEEN 10 AND 20 AND status NOT IN ('blocked', 'deleted')",
                new { search = "Ed%" }))
            .Build();

        Assert.Equal(
            "SELECT * FROM \"users\" WHERE (((\"name\" IS NOT NULL AND \"name\" LIKE @p1) AND \"score\" BETWEEN @p2 AND @p3) AND NOT (\"status\" IN (@p4, @p5)))",
            query.Text);
        Assert.Equal("Ed%", query.Parameters["p1"]);
        Assert.Equal(10L, query.Parameters["p2"]);
        Assert.Equal("blocked", query.Parameters["p4"]);
    }

    [Fact]
    public void Join_parser_returns_structured_table_and_predicate()
    {
        var parsed = SqlParser.ParseJoin(
            "app.orders o ON o.user_id = u.id AND o.created_at >= @since",
            new { since = new DateTime(2026, 1, 1) });
        var query = Sql.From("app.users u")
            .LeftJoin(parsed.Table, parsed.Predicate)
            .Build();

        Assert.Equal("orders", parsed.Table.Name);
        Assert.Equal("app", parsed.Table.Schema);
        Assert.Equal("o", parsed.Table.Alias);
        Assert.Equal(
            "SELECT * FROM \"app\".\"users\" AS \"u\" LEFT JOIN \"app\".\"orders\" AS \"o\" ON (\"o\".\"user_id\" = \"u\".\"id\" AND \"o\".\"created_at\" >= @p1)",
            query.Text);
    }

    [Fact]
    public void From_first_and_compact_join_builders_use_parser()
    {
        var query = Sql
            .From("users u")
            .LeftJoin(
                "orders o ON o.user_id = u.id AND o.total >= @minimum",
                new { minimum = 100m })
            .Select("u.id", "o.total")
            .Where("u.active = @active", new { active = true })
            .Build(PostgresQueryCompiler.Instance);

        Assert.Equal(
            "SELECT \"u\".\"id\", \"o\".\"total\" FROM \"users\" AS \"u\" LEFT JOIN \"orders\" AS \"o\" ON (\"o\".\"user_id\" = \"u\".\"id\" AND \"o\".\"total\" >= @p1) WHERE \"u\".\"active\" = @p2",
            query.Text);
        Assert.Equal(100m, query.Parameters["p1"]);
        Assert.Equal(true, query.Parameters["p2"]);
    }

    [Fact]
    public void Typed_query_accepts_parsed_string_criteria()
    {
        using var connection = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=:memory:");
        using var session = DbSession.Borrow(connection);

        var query = session.From<ParsedUser>()
            .Where("active = @active", new { active = true })
            .Build();

        Assert.Contains("WHERE \"active\" = @p1", query.Text);
    }

    [Fact]
    public void Parsed_or_where_combines_with_existing_conditions()
    {
        var query = Sql.From("users")
            .Where("active = true")
            .Where("score >= 10")
            .OrWhere("is_admin = true")
            .Build();

        Assert.Equal(
            "SELECT * FROM \"users\" WHERE ((\"active\" = @p1 AND \"score\" >= @p2) OR \"is_admin\" = @p3)",
            query.Text);
    }

    [Theory]
    [InlineData("active = @missing", "No value was supplied")]
    [InlineData("active =", "Expected a column, parameter, or literal")]
    [InlineData("active AND", "Expected a column name")]
    [InlineData("active = true trailing", "Unexpected token")]
    public void Parser_reports_precise_errors(string criteria, string message)
    {
        var exception = Assert.Throws<SqlParseException>(() =>
            SqlParser.ParseCriteria(criteria));

        Assert.Contains(message, exception.Message);
        Assert.True(exception.Line >= 1);
        Assert.True(exception.Column >= 1);
    }

    private sealed class ParsedUser
    {
        public long Id { get; init; }

        [Column("active")]
        public bool Active { get; init; }
    }
}
