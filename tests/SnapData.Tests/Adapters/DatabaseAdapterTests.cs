using Microsoft.Data.Sqlite;

namespace SnapData.Tests;

public sealed class DatabaseAdapterTests
{
    [Fact]
    public async Task Opened_session_uses_its_adapters_query_compiler()
    {
        var compiler = new RecordingCompiler();
        var database = new SnapDatabase(new DatabaseAdapter(
            SqliteFactory.Instance,
            "Data Source=:memory:",
            compiler));

        await using var session = await database.OpenSessionAsync();
        await session.ExecuteAsync("CREATE TABLE values_table (value INTEGER NOT NULL)");
        var affected = await session.ExecuteAsync(
            Sql.InsertInto("values_table").Value("value", 1));

        Assert.Equal(1, affected);
        Assert.Single(compiler.CompiledBuilders);
        Assert.IsType<InsertQueryBuilder>(compiler.CompiledBuilders[0]);
    }

    [Fact]
    public async Task Borrowed_session_and_transaction_keep_the_adapter_compiler()
    {
        var compiler = new RecordingCompiler();
        var database = new SnapDatabase(new DatabaseAdapter(
            SqliteFactory.Instance,
            "Data Source=:memory:",
            compiler));
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var session = database.BorrowSession(connection);
        await session.ExecuteAsync("CREATE TABLE values_table (value INTEGER NOT NULL)");

        await using (var transaction = await session.BeginTransactionAsync())
        {
            await transaction.ExecuteAsync(
                Sql.InsertInto("values_table").Value("value", 1));
            await transaction.CommitAsync();
        }

        Assert.Single(compiler.CompiledBuilders);
        Assert.Equal(System.Data.ConnectionState.Open, connection.State);
    }

    [Fact]
    public void Provider_compilers_apply_provider_rules()
    {
        var mysql = Sql.Select("users.id")
            .From("users")
            .Where(Exp.Col("active") == true)
            .Limit(10)
            .Offset(20)
            .Build(MySqlQueryCompiler.Instance);
        var postgres = Sql.InsertInto("users")
            .Value("name", "Edin")
            .Returning("id")
            .Build(PostgresQueryCompiler.Instance);
        var sqlServerFirst = Sql.From("users u")
            .Select("u.id")
            .OrderBy("u.id")
            .Limit(1)
            .Build(SqlServerQueryCompiler.Instance);
        var sqlServerPage = Sql.From("users u")
            .Select("u.id")
            .OrderBy("u.id")
            .Limit(10)
            .Offset(20)
            .Build(SqlServerQueryCompiler.Instance);

        Assert.Equal(
            "SELECT `users`.`id` FROM `users` WHERE `active` = @p1 LIMIT 10 OFFSET 20",
            mysql.Text);
        Assert.Equal(
            "INSERT INTO \"users\" (\"name\") VALUES (@p1) RETURNING \"id\"",
            postgres.Text);
        Assert.Equal(
            "SELECT TOP (1) [u].[id] FROM [users] AS [u] ORDER BY [u].[id] ASC",
            sqlServerFirst.Text);
        Assert.Equal(
            "SELECT [u].[id] FROM [users] AS [u] ORDER BY [u].[id] ASC OFFSET 20 ROWS FETCH NEXT 10 ROWS ONLY",
            sqlServerPage.Text);
        Assert.True(SqliteQueryCompiler.Instance.SupportsReturning);
        Assert.True(PostgresQueryCompiler.Instance.SupportsReturning);
        Assert.False(MySqlQueryCompiler.Instance.SupportsReturning);
        Assert.False(SqlServerQueryCompiler.Instance.SupportsReturning);
        Assert.Throws<NotSupportedException>(() =>
            Sql.InsertInto("users")
                .Value("name", "Edin")
                .Returning("id")
                .Build(MySqlQueryCompiler.Instance));
    }

    private sealed class RecordingCompiler : IQueryCompiler
    {
        internal List<ISqlQueryBuilder> CompiledBuilders { get; } = [];

        public SqlQuery Compile(ISqlQueryBuilder query)
        {
            CompiledBuilders.Add(query);
            return SqlDialect.Ansi.Compile(query);
        }
    }
}
