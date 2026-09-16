using Microsoft.Data.Sqlite;

namespace SnapData.Tests;

public sealed class TypedAssignmentTests
{
    [Fact]
    public async Task Expression_assignment_uses_mapped_columns_and_parameters()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var session = DbSession.Borrow(connection, SqliteQueryCompiler.Instance);
        await session.ExecuteAsync("CREATE TABLE accounts (id INTEGER PRIMARY KEY, current_balance NUMERIC, calculated NUMERIC); INSERT INTO accounts VALUES(1, 10, 0)");
        var account = session.Entity<Account>();
        var update = session.Update<Account>()
            .SetExpression(a => a.Balance, account.Col(a => a.Balance) + 5m)
            .Where(a => a.Id == 1);
        var compiled = update.Build();
        Assert.Contains("\"current_balance\" = (\"accounts\".\"current_balance\" + @p1)", compiled.Text);
        Assert.Equal(5m, compiled.Parameters["p1"]);
        Assert.Equal(1, await update.ExecuteAsync());
        Assert.Equal(15m, await session.ScalarAsync<decimal>("SELECT current_balance FROM accounts"));
    }

    [Fact]
    public async Task Expression_assignment_keeps_key_generated_and_whole_table_guards()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await using var session = DbSession.Borrow(connection, SqliteQueryCompiler.Instance);
        Assert.Throws<InvalidOperationException>(() => session.Update<Account>().SetExpression(a => a.Id, Exp.Col("id") + 1));
        Assert.Throws<InvalidOperationException>(() => session.Update<Account>().SetExpression(a => a.Calculated, Exp.Col("calculated") + 1));
        Assert.Throws<ArgumentNullException>(() => session.Update<Account>().SetExpression(a => a.Balance, null!));
        Assert.Throws<InvalidOperationException>(() => session.Update<Account>().SetExpression(a => a.Balance, Exp.Col("current_balance") + 1).Build());
        Assert.NotNull(session.Update<Account>().SetExpression(a => a.Balance, Exp.Col("current_balance") + 1).AllRows().Build());
    }

    [Table("accounts")]
    private sealed class Account
    {
        [Key, Column("id")] public int Id { get; set; }
        [Column("current_balance")] public decimal Balance { get; set; }
        [Column("calculated"), DatabaseGenerated(DatabaseGeneratedOption.Computed)] public decimal Calculated { get; set; }
    }
}
