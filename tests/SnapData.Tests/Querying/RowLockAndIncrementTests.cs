using Microsoft.Data.Sqlite;

namespace SnapData.Tests;

public sealed class RowLockAndIncrementTests
{
    [Fact]
    public void Lock_clause_follows_paging_and_values_remain_parameterized()
    {
        foreach (var compiler in new IQueryCompiler[] { MySqlQueryCompiler.Instance, PostgresQueryCompiler.Instance })
        {
            var query = Sql.From("accounts a").Select("a.account_id")
                .Where(Exp.Col("a.account_id") == 7).OrderBy("a.account_id").Limit(2).Offset(1).ForUpdate();
            var sql = query.Build(compiler);
            Assert.True(compiler.Capabilities.ForUpdate);
            Assert.EndsWith("LIMIT 2 OFFSET 1 FOR UPDATE", sql.Text);
            Assert.Equal(7, sql.Parameters["p1"]);
            Assert.EndsWith("LIMIT 1 OFFSET 1 FOR UPDATE", query.Clone(1).Build(compiler).Text);
            Assert.EndsWith("FOR UPDATE", query.CloneWithoutPaging().Build(compiler).Text);
            Assert.EndsWith("LIMIT 2 OFFSET 1 FOR UPDATE", query.Build(compiler).Text);
            Assert.DoesNotContain("FOR UPDATE", query.ForUpdate(false).Build(compiler).Text);
        }
    }

    [Fact]
    public void Unsupported_providers_and_non_row_queries_reject_locks()
    {
        foreach (var compiler in new IQueryCompiler[] { SqlDialect.Ansi, SqliteQueryCompiler.Instance,
                     SqlServerQueryCompiler.Instance, FirebirdQueryCompiler.Instance })
        {
            Assert.False(compiler.Capabilities.ForUpdate);
            Assert.Throws<NotSupportedException>(() => Sql.From("accounts").ForUpdate().Build(compiler));
        }
        Assert.Throws<NotSupportedException>(() => Sql.From("accounts").Distinct().ForUpdate().Build(MySqlQueryCompiler.Instance));
        Assert.Throws<NotSupportedException>(() => Sql.From("accounts").Select(Sql.Count()).ForUpdate().Build(MySqlQueryCompiler.Instance));
        Assert.Throws<NotSupportedException>(() => Sql.From("accounts").GroupBy("account_id").ForUpdate().Build(PostgresQueryCompiler.Instance));
        Assert.Throws<NotSupportedException>(() => Sql.From("accounts").Having(Sql.Count() > 1).ForUpdate().Build(MySqlQueryCompiler.Instance));
    }

    [Fact]
    public async Task Typed_source_and_projected_builders_expose_locks_and_reject_count_terminals()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await using var session = DbSession.Borrow(connection, MySqlQueryCompiler.Instance);
        var account = session.Entity<Account>("a");
        var query = session.From(account).Where(a => a.Id == 7).ForUpdate();
        Assert.EndsWith("FOR UPDATE", query.Build().Text);
        var projection = query.Select<IdRow>(account.Col(a => a.Id, nameof(IdRow.Id)));
        Assert.EndsWith("FOR UPDATE", projection.Build().Text);
        Assert.DoesNotContain("FOR UPDATE", projection.ForUpdate(false).Build().Text);
        Assert.EndsWith("FOR UPDATE", projection.ForUpdate().Build().Text);
        Assert.EndsWith("FOR UPDATE", session.From("accounts").ForUpdate().Select<IdRow>("account_id AS Id").Build().Text);
        await Assert.ThrowsAsync<NotSupportedException>(() => projection.CountAsync());
        await Assert.ThrowsAsync<NotSupportedException>(() => projection.AnyAsync());
        await Assert.ThrowsAsync<NotSupportedException>(() => projection.PageAsync(1, 10));
        var typed = session.From<Account>().ForUpdate();
        await Assert.ThrowsAsync<NotSupportedException>(() => typed.CountAsync());
        await Assert.ThrowsAsync<NotSupportedException>(() => typed.AnyAsync());
        await Assert.ThrowsAsync<NotSupportedException>(() => typed.PageAsync(1, 10));
    }

    [Fact]
    public async Task Increment_uses_mapped_columns_and_parameters_and_retains_mutation_guards()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var session = DbSession.Borrow(connection, SqliteQueryCompiler.Instance);
        await session.ExecuteAsync("CREATE TABLE accounts (account_id INTEGER PRIMARY KEY, row_version INTEGER, balance NUMERIC, computed INTEGER); INSERT INTO accounts VALUES (7, 1, 10, 0)");
        var update = session.Update<Account>().Increment(a => a.Version).Increment(a => a.Balance, 2.5m).Where(a => a.Id == 7);
        var compiled = update.Build();
        Assert.Contains("\"row_version\" = (\"row_version\" + @p1)", compiled.Text);
        Assert.Equal(1L, compiled.Parameters["p1"]);
        Assert.Equal(2.5m, compiled.Parameters["p2"]);
        Assert.Equal(1, await update.ExecuteAsync());
        var account = await session.From<Account>().SingleAsync();
        Assert.Equal(2, account.Version);
        Assert.Equal(12.5m, account.Balance);
        Assert.Throws<InvalidOperationException>(() => session.Update<Account>().Increment(a => a.Id));
        Assert.Throws<InvalidOperationException>(() => session.Update<Account>().Increment(a => a.Computed));
        Assert.Throws<InvalidOperationException>(() => session.Update<Account>().Increment(a => a.Version).Build());
        Assert.NotNull(session.Update<Account>().Increment(a => a.Version).AllRows().Build());
        Assert.Equal(0, await session.Update<Account>().Increment(a => a.Version).Where(a => a.Id == 8).ExecuteAsync());
        Assert.Equal(2, (await session.From<Account>().SingleAsync()).Version);
    }

    [Table("accounts")]
    private sealed class Account
    {
        [Key, Column("account_id")] public int Id { get; set; }
        [Column("row_version")] public long Version { get; set; }
        public decimal Balance { get; set; }
        [DatabaseGenerated(DatabaseGeneratedOption.Computed)] public int Computed { get; set; }
    }
    private sealed record IdRow(int Id);
}
