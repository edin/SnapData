using Microsoft.Data.Sqlite;

namespace SnapData.Tests;

public sealed class QueryCapabilitiesTests
{
    [Fact]
    public async Task Session_bound_build_uses_provider_for_all_wrappers_without_opening_connection()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await using var session = DbSession.Borrow(connection, SqlServerQueryCompiler.Instance);
        var queries = new[]
        {
            session.From<User>().Limit(1).Build(),
            session.From<User>().Select<User>("id").Limit(1).Build(),
            session.From("users").Build(),
            session.InsertInto<User>().Values(new User { Id = 1, Name = "A" }).Build(),
            session.Update<User>().Set(u => u.Name, "B").Where(u => u.Id == 1).Build(),
            session.DeleteFrom<User>().Where(u => u.Id == 1).Build(),
            session.UpsertInto<User>().Values(new User { Id = 1, Name = "A" })
                .OnConflict(u => u.Id).UpdateColumns(u => u.Name).Build()
        };
        Assert.All(queries, query => Assert.Contains("[users]", query.Text));
        Assert.Contains("TOP (1)", queries[0].Text);
        Assert.StartsWith("MERGE", queries[^1].Text);
        Assert.Equal(System.Data.ConnectionState.Closed, connection.State);
        Assert.Contains("\"users\"", session.From<User>().Build(PostgresQueryCompiler.Instance).Text);
        Assert.True(session.Capabilities.Upsert);
        Assert.Null(session.Capabilities.MaxParametersPerCommand);
    }

    [Fact]
    public async Task Transaction_compilation_and_execution_use_the_same_custom_compiler()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var compiler = new RecordingCompiler();
        await using var session = DbSession.Borrow(connection, compiler);
        await session.ExecuteAsync("CREATE TABLE users (id INTEGER PRIMARY KEY, name TEXT)");
        await using var transaction = await session.BeginTransactionAsync();
        var upsert = transaction.UpsertInto<User>().Values(new User { Id = 1, Name = "A" })
            .OnConflict(u => u.Id).UpdateColumns(u => u.Name);
        var inspected = upsert.Build();
        await upsert.ExecuteAsync();
        Assert.Equal(inspected.Text, compiler.Last!.Text);
        Assert.Equal(2, compiler.Compilations);
        Assert.Equal(session.Capabilities, transaction.Capabilities);
        await transaction.RollbackAsync();
    }

    [Fact]
    public void Advertised_upsert_and_returning_capabilities_match_compilers()
    {
        foreach (var compiler in new IQueryCompiler[] { SqlDialect.Ansi, SqliteQueryCompiler.Instance,
                     PostgresQueryCompiler.Instance, SqlServerQueryCompiler.Instance, MySqlQueryCompiler.Instance, FirebirdQueryCompiler.Instance })
        {
            var capabilities = compiler.Capabilities;
            var upsert = Sql.UpsertInto("users").Values(new { id = 1, name = "A" }).OnConflict("id").UpdateColumns("name");
            if (capabilities.Upsert) Assert.NotNull(upsert.Build(compiler));
            else Assert.Throws<NotSupportedException>(() => upsert.Build(compiler));
            if (capabilities.UpsertDoNothing) Assert.NotNull(upsert.DoNothing().Build(compiler));
            else Assert.Throws<NotSupportedException>(() => upsert.DoNothing().Build(compiler));
            if (capabilities.UpsertReturning) Assert.NotNull(upsert.Returning("id").Build(compiler));
            else Assert.Throws<NotSupportedException>(() => upsert.Returning("id").Build(compiler));
            var insert = Sql.InsertInto("users").Values(new { name = "A" }).Returning("id");
            var update = Sql.Update("users").Set("name", "A").Where(Exp.Col("id") == 1).Returning("id");
            var delete = Sql.DeleteFrom("users").Where(Exp.Col("id") == 1).Returning("id");
            CheckReturning(insert, capabilities.InsertReturning, compiler);
            CheckReturning(update, capabilities.UpdateReturning, compiler);
            CheckReturning(delete, capabilities.DeleteReturning, compiler);
        }
        IQueryCompiler legacy = new LegacyCompiler();
        Assert.Equal(QueryCapabilities.None, legacy.Capabilities);
    }

    private static void CheckReturning(ISqlQueryBuilder builder, bool supported, IQueryCompiler compiler)
    {
        if (supported) Assert.NotNull(builder.Build(compiler));
        else Assert.Throws<NotSupportedException>(() => builder.Build(compiler));
    }

    private sealed class RecordingCompiler : IQueryCompiler
    {
        public QueryCapabilities Capabilities => SqliteQueryCompiler.Instance.Capabilities;
        public SqlQuery? Last { get; private set; }
        public int Compilations { get; private set; }
        public SqlQuery Compile(ISqlQueryBuilder query)
        {
            Compilations++;
            return Last = SqliteQueryCompiler.Instance.Compile(query);
        }
    }

    private sealed class LegacyCompiler : IQueryCompiler
    {
        public SqlQuery Compile(ISqlQueryBuilder query) => SqliteQueryCompiler.Instance.Compile(query);
    }

    [Table("users")]
    private sealed class User
    {
        [Key, Column("id")] public int Id { get; set; }
        [Column("name")] public string Name { get; set; } = "";
    }
}
