using Microsoft.Data.Sqlite;

namespace SnapData.Tests;

public sealed class UpsertTests
{
    [Fact]
    public void Compiles_parameterized_upsert_for_sqlite_and_postgres()
    {
        foreach (var compiler in new IQueryCompiler[] { SqliteQueryCompiler.Instance, PostgresQueryCompiler.Instance })
        {
            var query = Sql.UpsertInto("app.users")
                .Values(new { tenant = 1, email = "a@example.com", name = "O'Reilly" })
                .OnConflict("tenant", "email").UpdateColumns("name").Build(compiler);
            Assert.Equal("INSERT INTO \"app\".\"users\" (\"tenant\", \"email\", \"name\") VALUES (@p1, @p2, @p3) ON CONFLICT (\"tenant\", \"email\") DO UPDATE SET \"name\" = excluded.\"name\"", query.Text);
            Assert.Equal("O'Reilly", query.Parameters["p3"]);
            Assert.DoesNotContain("O'Reilly", query.Text);
        }
    }

    [Fact]
    public void Unsupported_providers_fail_explicitly()
    {
        foreach (var compiler in new IQueryCompiler[] { SqlDialect.Ansi, MySqlQueryCompiler.Instance, FirebirdQueryCompiler.Instance })
            Assert.Throws<NotSupportedException>(() => Valid().Build(compiler));
    }

    [Fact]
    public void Requires_explicit_target_action_and_values()
    {
        Assert.Throws<InvalidOperationException>(() => Sql.UpsertInto("users").Values(new { id = 1 }).DoNothing().Build(SqliteQueryCompiler.Instance));
        Assert.Throws<InvalidOperationException>(() => Sql.UpsertInto("users").Values(new { id = 1 }).OnConflict("id").Build(SqliteQueryCompiler.Instance));
        Assert.Throws<InvalidOperationException>(() => Sql.UpsertInto("users").OnConflict("id").DoNothing().Build(SqliteQueryCompiler.Instance));
    }

    [Fact]
    public void Rejects_missing_values_and_updates_to_conflict_columns()
    {
        Assert.Throws<InvalidOperationException>(() => Valid().OnConflict("absent").Build(SqliteQueryCompiler.Instance));
        Assert.Throws<InvalidOperationException>(() => Valid().UpdateColumns("absent").Build(SqliteQueryCompiler.Instance));
        Assert.Throws<InvalidOperationException>(() => Valid().UpdateColumns("id").Build(SqliteQueryCompiler.Instance));
    }

    [Theory]
    [InlineData("u.id")]
    [InlineData("id AS other")]
    [InlineData("*")]
    public void Rejects_qualified_aliased_and_wildcard_columns(string column)
    {
        Assert.Throws<ArgumentException>(() => Valid().OnConflict(column));
        Assert.Throws<ArgumentException>(() => Valid().UpdateColumns(column));
    }

    [Fact]
    public void Rejects_empty_and_duplicate_columns_and_table_aliases()
    {
        Assert.Throws<ArgumentException>(() => Valid().OnConflict());
        Assert.Throws<ArgumentException>(() => Valid().UpdateColumns());
        Assert.Throws<ArgumentException>(() => Valid().OnConflict("id", "ID"));
        Assert.Throws<ArgumentException>(() => Valid().UpdateColumns("name", "name"));
        Assert.Throws<ArgumentException>(() => Sql.UpsertInto(Sql.Table("users").As("u")));
    }

    [Fact]
    public void Action_can_be_replaced_and_build_is_repeatable()
    {
        var builder = Valid().DoNothing();
        Assert.EndsWith("DO NOTHING", builder.Build(SqliteQueryCompiler.Instance).Text);
        builder.UpdateColumns("name");
        var first = builder.Build(SqliteQueryCompiler.Instance);
        Assert.Equal(first.Text, builder.Build(SqliteQueryCompiler.Instance).Text);
        Assert.EndsWith("DO UPDATE SET \"name\" = excluded.\"name\"", first.Text);
    }

    [Fact]
    public async Task Typed_upsert_inserts_updates_selected_columns_and_ignores_conflicts()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var session = DbSession.Borrow(connection, SqliteQueryCompiler.Instance);
        await session.ExecuteAsync("CREATE TABLE accounts (tenant_id INTEGER NOT NULL, email TEXT NOT NULL, display_name TEXT, note TEXT, PRIMARY KEY(tenant_id,email))");
        var account = new Account { Tenant = 1, Email = "a@b", Name = "first", Note = "keep" };
        var insert = session.UpsertInto<Account>().Values(account)
            .OnConflict(a => a.Tenant, a => a.Email).UpdateColumns(a => a.Name);
        Assert.Equal(1, await insert.ExecuteAsync());
        account.Name = "second";
        account.Note = "replace";
        Assert.Equal(1, await session.UpsertInto<Account>().Values(account)
            .OnConflict(a => a.Tenant, a => a.Email).UpdateColumns(a => a.Name).ExecuteAsync());
        Assert.Equal("second", await session.ScalarAsync<string>("SELECT display_name FROM accounts"));
        Assert.Equal("keep", await session.ScalarAsync<string>("SELECT note FROM accounts"));
        Assert.Equal(0, await session.UpsertInto<Account>().Values(account)
            .OnConflict(a => a.Tenant, a => a.Email).DoNothing().ExecuteAsync());
        Assert.Equal(1L, await session.ScalarAsync<long>("SELECT COUNT(*) FROM accounts"));
        await using var transaction = await session.BeginTransactionAsync();
        account.Name = "rolled back";
        await transaction.UpsertInto<Account>().Values(account)
            .OnConflict(a => a.Tenant, a => a.Email).UpdateColumns(a => a.Name).ExecuteAsync();
        await transaction.RollbackAsync();
        Assert.Equal("second", await session.ScalarAsync<string>("SELECT display_name FROM accounts"));
    }

    [Fact]
    public async Task Do_nothing_does_not_hide_other_constraint_failures()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var session = DbSession.Borrow(connection, SqliteQueryCompiler.Instance);
        await session.ExecuteAsync("CREATE TABLE users (id INTEGER PRIMARY KEY, name TEXT UNIQUE NOT NULL)");
        await session.ExecuteAsync(Valid().DoNothing());
        Assert.Equal(0, await session.ExecuteAsync(Valid().DoNothing()));
        await Assert.ThrowsAsync<SqliteException>(() => session.ExecuteAsync(
            Sql.UpsertInto("users").Values(new { id = 2, name = "first" }).OnConflict("id").DoNothing()));
    }

    [Fact]
    public async Task Typed_api_protects_generated_and_key_properties()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await using var session = DbSession.Borrow(connection, SqliteQueryCompiler.Instance);
        Assert.Throws<InvalidOperationException>(() => session.UpsertInto<Account>().UpdateColumns(a => a.Tenant));
        Assert.Throws<InvalidOperationException>(() => session.UpsertInto<Generated>().Value(a => a.Id, 1));
        Assert.Throws<InvalidOperationException>(() => session.UpsertInto<Generated>().UpdateColumns(a => a.Computed));
    }

    private static UpsertQueryBuilder Valid() => Sql.UpsertInto("users")
        .Values(new { id = 1, name = "first" }).OnConflict("id").UpdateColumns("name");

    [Fact]
    public void Sql_server_merge_is_parameterized_locked_and_has_no_delete_action()
    {
        var update = Valid().Build(SqlServerQueryCompiler.Instance);
        Assert.StartsWith("MERGE INTO [users] WITH (HOLDLOCK)", update.Text);
        Assert.Contains("USING (VALUES (@p1, @p2)) AS [source] ([id], [name])", update.Text);
        Assert.Contains("WHEN MATCHED THEN UPDATE SET [target].[name] = [source].[name]", update.Text);
        Assert.EndsWith("WHEN NOT MATCHED THEN INSERT ([id], [name]) VALUES ([source].[id], [source].[name]);", update.Text);
        Assert.Equal("first", update.Parameters["p2"]);
        Assert.DoesNotContain("DELETE", update.Text);
        var ignored = Valid().DoNothing().Build(SqlServerQueryCompiler.Instance);
        Assert.DoesNotContain("WHEN MATCHED", ignored.Text);
        Assert.Throws<InvalidOperationException>(() => Valid().UpdateColumns("id").Build(SqlServerQueryCompiler.Instance));
    }

    [Fact]
    public async Task Typed_values_can_be_replaced_and_individual_values_execute()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var session = DbSession.Borrow(connection, SqliteQueryCompiler.Instance);
        await session.ExecuteAsync("CREATE TABLE accounts (tenant_id INTEGER, email TEXT, display_name TEXT, note TEXT, PRIMARY KEY(tenant_id,email))");
        var builder = session.UpsertInto<Account>()
            .Values(new Account { Tenant = 1, Email = "old", Name = "old" })
            .Values(new Account { Tenant = 2, Email = "new", Name = "new" })
            .OnConflict(a => a.Tenant, a => a.Email).DoNothing();
        Assert.Equal(1, await builder.ExecuteAsync());
        Assert.Equal("new", await session.ScalarAsync<string>("SELECT email FROM accounts"));
        Assert.Equal(1, await session.UpsertInto<Account>()
            .Value(a => a.Tenant, 2).Value(a => a.Email, "new").Value(a => a.Name, "changed")
            .OnConflict(a => a.Tenant, a => a.Email).UpdateColumns(a => a.Name).ExecuteAsync());
        Assert.Equal("changed", await session.ScalarAsync<string>("SELECT display_name FROM accounts"));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => builder.ExecuteAsync(cancellationToken: cancellation.Token));
        Assert.Equal(1L, await session.ScalarAsync<long>("SELECT COUNT(*) FROM accounts"));
    }

    [Fact]
    public void Rejects_duplicate_inserted_columns()
    {
        var values = new KeyValuePair<string, object?>[] { new("id", 1), new("id", 2) };
        Assert.Throws<InvalidOperationException>(() => Sql.UpsertInto("users")
            .Values(values).OnConflict("id").DoNothing().Build(SqliteQueryCompiler.Instance));
    }

    [Table("accounts")]
    private sealed class Account
    {
        [Key, Column("tenant_id")] public int Tenant { get; set; }
        [Key, Column("email")] public string Email { get; set; } = "";
        [Column("display_name")] public string Name { get; set; } = "";
        [Column("note")] public string Note { get; set; } = "";
    }

    private sealed class Generated
    {
        [Key, DatabaseGenerated(DatabaseGeneratedOption.Identity)] public int Id { get; set; }
        [DatabaseGenerated(DatabaseGeneratedOption.Computed)] public int Computed { get; set; }
    }
}
