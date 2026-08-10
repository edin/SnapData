using Microsoft.Data.Sqlite;

namespace SnapData.Tests;

public sealed class EntityQueryTests
{
    private static int StaticRowCount => 3;

    private int RowCount => 2;

    [Fact]
    public async Task Typed_query_translates_common_predicates_and_executes()
    {
        await using var connection = await OpenConnectionAsync();
        await using var session = DbSession.Borrow(connection, SqliteQueryCompiler.Instance);
        var since = new DateTime(2025, 1, 1);

        var users = await session
            .From<User>()
            .Where(user => user.Active && user.CreatedAt >= since)
            .OrderByDescending(user => user.Name)
            .Limit(2)
            .ToListAsync();

        Assert.Equal(["Zara", "Edin"], users.Select(user => user.Name));
    }

    [Fact]
    public void Captured_values_are_read_without_compiling_expressions()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        using var session = DbSession.Borrow(connection, SqliteQueryCompiler.Instance);
        var local = 1;
        var options = new QueryLimits { Maximum = 4 };

        var query = session.From<User>()
            .Where(user => user.Id >= local)
            .Where(user => user.Id <= RowCount)
            .Where(user => user.Score <= options.Maximum)
            .Where(user => user.MinimumScore <= StaticRowCount)
            .Build();

        Assert.Equal(1, query.Parameters["p1"]);
        Assert.Equal(2, query.Parameters["p2"]);
        Assert.Equal(4, query.Parameters["p3"]);
        Assert.Equal(3, query.Parameters["p4"]);
    }

    [Fact]
    public void Arbitrary_captured_value_expressions_are_rejected()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        using var session = DbSession.Borrow(connection, SqliteQueryCompiler.Instance);
        var local = 1;

        var arithmetic = Assert.Throws<NotSupportedException>(() =>
            session.From<User>().Where(user => user.Id <= local + 1));
        var method = Assert.Throws<NotSupportedException>(() =>
            session.From<User>().Where(user => user.Id <= GetRowCount()));

        Assert.Contains("field/property access only", arithmetic.Message);
        Assert.Contains("Evaluate the expression", method.Message);
    }

    [Fact]
    public async Task Terminal_operations_execute_through_typed_query()
    {
        await using var connection = await OpenConnectionAsync();
        await using var session = DbSession.Borrow(connection, SqliteQueryCompiler.Instance);

        var first = await session
            .From<User>()
            .Where(user => user.Active)
            .OrderBy(user => user.Id)
            .FirstOrDefaultAsync();
        var missing = await session
            .From<User>()
            .Where(user => user.Id == 999)
            .FirstOrDefaultAsync();
        var single = await session
            .From<User>()
            .Where(user => user.Id == 1)
            .SingleOrDefaultAsync();
        var anyInactive = await session
            .From<User>()
            .Where(user => !user.Active)
            .AnyAsync();
        var activeCount = await session
            .From<User>()
            .Where(user => user.Active)
            .CountAsync();

        Assert.Equal(1, first!.Id);
        Assert.Null(missing);
        Assert.Equal("Edin", single!.Name);
        Assert.True(anyInactive);
        Assert.Equal(3, activeCount);
    }

    [Fact]
    public async Task Single_or_default_rejects_multiple_rows()
    {
        await using var connection = await OpenConnectionAsync();
        await using var session = DbSession.Borrow(connection, SqliteQueryCompiler.Instance);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            session.From<User>().Where(user => user.Active).SingleOrDefaultAsync());

        Assert.Contains("more than one row", exception.Message);
    }

    [Fact]
    public async Task Strict_first_and_single_require_a_row()
    {
        await using var connection = await OpenConnectionAsync();
        await using var session = DbSession.Borrow(connection, SqliteQueryCompiler.Instance);

        var first = await session
            .From<User>()
            .OrderBy(user => user.Id)
            .FirstAsync();
        var single = await session
            .From<User>()
            .Where(user => user.Id == 2)
            .SingleAsync();

        Assert.Equal(1, first.Id);
        Assert.Equal("Zara", single.Name);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            session.From<User>().Where(user => user.Id == 999).FirstAsync());
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            session.From<User>().Where(user => user.Id == 999).SingleAsync());
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            session.From<User>().SingleAsync());
    }

    [Fact]
    public async Task Page_returns_items_total_and_navigation_metadata()
    {
        await using var connection = await OpenConnectionAsync();
        await using var session = DbSession.Borrow(connection, SqliteQueryCompiler.Instance);
        var query = session
            .From<User>()
            .OrderBy(user => user.Id)
            .Limit(1);

        var page = await query.PageAsync(pageNumber: 2, pageSize: 2);
        var originalRows = await query.ToListAsync();

        Assert.Equal([3L, 4L], page.Items.Select(user => user.Id));
        Assert.Equal(4, page.TotalCount);
        Assert.Equal(2, page.PageNumber);
        Assert.Equal(2, page.PageSize);
        Assert.Equal(2, page.TotalPages);
        Assert.True(page.HasPreviousPage);
        Assert.False(page.HasNextPage);
        Assert.Single(originalRows);
    }

    [Theory]
    [InlineData(0, 10)]
    [InlineData(1, 0)]
    public async Task Page_rejects_invalid_arguments(int pageNumber, int pageSize)
    {
        await using var connection = await OpenConnectionAsync();
        await using var session = DbSession.Borrow(connection, SqliteQueryCompiler.Instance);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            session.From<User>().PageAsync(pageNumber, pageSize));
    }

    [Fact]
    public async Task Count_respects_limit_and_does_not_mutate_query()
    {
        await using var connection = await OpenConnectionAsync();
        await using var session = DbSession.Borrow(connection, SqliteQueryCompiler.Instance);
        var query = session.From<User>().Where(user => user.Active).Limit(2);

        var count = await query.CountAsync();
        var rows = await query.ToListAsync();

        Assert.Equal(2, count);
        Assert.Equal(2, rows.Count);
    }

    [Fact]
    public void Typed_query_uses_mapping_and_supports_native_ast_predicates()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        using var session = DbSession.Borrow(connection, SqliteQueryCompiler.Instance);
        var query = session
            .From<User>()
            .Where(user => user.Name != null && !user.Active)
            .Where(Exp.Col("created_at") < new DateTime(2030, 1, 1))
            .OrderBy(user => user.Name)
            .Build(SqliteQueryCompiler.Instance);

        Assert.Contains("FROM \"app_users\"", query.Text);
        Assert.Contains("\"display_name\" IS NOT NULL", query.Text);
        Assert.Contains("NOT (\"active\" = @p1)", query.Text);
        Assert.Contains("ORDER BY \"display_name\" ASC", query.Text);
        Assert.Equal(2, query.Parameters.Count);
    }

    [Fact]
    public void Alias_qualifies_mapped_selection_predicates_and_sorting()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        using var session = DbSession.Borrow(connection, SqliteQueryCompiler.Instance);

        var query = session
            .From<User>()
            .As("u")
            .Where(user => user.Active)
            .OrderBy(user => user.Name)
            .Build(SqliteQueryCompiler.Instance);

        Assert.Contains("FROM \"app_users\" AS \"u\"", query.Text);
        Assert.Contains("\"u\".\"Id\"", query.Text);
        Assert.Contains("\"u\".\"active\" = @p1", query.Text);
        Assert.Contains("ORDER BY \"u\".\"display_name\" ASC", query.Text);
    }

    [Fact]
    public void Reversed_and_property_comparisons_are_translated()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        using var session = DbSession.Borrow(connection, SqliteQueryCompiler.Instance);

        var reversed = session.From<User>().Where(user => 10 < user.Score).Build();
        var columns = session.From<User>().Where(user => user.Score >= user.MinimumScore).Build();

        Assert.Contains("\"score\" > @p1", reversed.Text);
        Assert.Contains("\"score\" >= \"minimum_score\"", columns.Text);
    }

    [Fact]
    public void Unsupported_method_call_recommends_native_expression_api()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        using var session = DbSession.Borrow(connection);

        var exception = Assert.Throws<NotSupportedException>(() =>
            session.From<User>().Where(user => user.Name.StartsWith("E")));

        Assert.Contains("Use SnapData Exp", exception.Message);
    }

    private static async Task<SqliteConnection> OpenConnectionAsync()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            CREATE TABLE app_users (
                id INTEGER PRIMARY KEY,
                display_name TEXT NOT NULL,
                active INTEGER NOT NULL,
                created_at TEXT NOT NULL,
                score INTEGER NOT NULL,
                minimum_score INTEGER NOT NULL
            );

            INSERT INTO app_users VALUES
                (1, 'Edin', 1, '2025-02-01', 10, 5),
                (2, 'Zara', 1, '2025-03-01', 8, 8),
                (3, 'Old', 1, '2024-01-01', 2, 5),
                (4, 'Inactive', 0, '2025-04-01', 7, 3);
            """;
        await command.ExecuteNonQueryAsync();
        return connection;
    }

    [Table("app_users")]
    private sealed class User
    {
        [Key]
        public long Id { get; init; }

        [Column("display_name")]
        public required string Name { get; init; }

        [Column("active")]
        public bool Active { get; init; }

        [Column("created_at")]
        public DateTime CreatedAt { get; init; }

        [Column("score")]
        public int Score { get; init; }

        [Column("minimum_score")]
        public int MinimumScore { get; init; }
    }

    private sealed class QueryLimits
    {
        public int Maximum { get; init; }
    }

    private static int GetRowCount() => 2;
}
