namespace SnapData.Tests;

public sealed class MutationBuilderTests
{
    [Fact]
    public void Compiles_single_and_multi_row_inserts()
    {
        var single = Sql.InsertInto("users")
            .Values(new { name = "Edin", active = true })
            .Returning("id")
            .Build();
        var multiple = Sql.InsertInto("users")
            .Rows(
                new { name = "Edin", active = true },
                new { name = "John", active = false })
            .Build();

        Assert.Equal(
            "INSERT INTO \"users\" (\"name\", \"active\") VALUES (@p1, @p2) RETURNING \"id\"",
            single.Text);
        Assert.Equal(
            "INSERT INTO \"users\" (\"name\", \"active\") VALUES (@p1, @p2), (@p3, @p4)",
            multiple.Text);
        Assert.Equal("John", multiple.Parameters["p3"]);
    }

    [Fact]
    public void Rejects_multi_row_inserts_with_different_columns()
    {
        var builder = Sql.InsertInto("users")
            .Rows(new { name = "Edin" }, new { email = "edin@example.com" });

        var exception = Assert.Throws<InvalidOperationException>(() => builder.Build());
        Assert.Contains("same columns", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Compiles_update_with_arithmetic_expression()
    {
        var query = Sql.Update("accounts")
            .Set("balance", Exp.Col("balance") - 25m)
            .Set("updated_at", Exp.RawValue("CURRENT_TIMESTAMP"))
            .Where(Exp.Col("id") == 42)
            .Returning("id", "balance")
            .Build();

        Assert.Equal(
            "UPDATE \"accounts\" SET \"balance\" = (\"balance\" - @p1), \"updated_at\" = CURRENT_TIMESTAMP WHERE \"id\" = @p2 RETURNING \"id\", \"balance\"",
            query.Text);
        Assert.Equal(25m, query.Parameters["p1"]);
        Assert.Equal(42, query.Parameters["p2"]);
    }

    [Fact]
    public void Update_and_delete_require_where_or_all_rows()
    {
        Assert.Throws<InvalidOperationException>(() =>
            Sql.Update("users").Set("active", false).Build());
        Assert.Throws<InvalidOperationException>(() =>
            Sql.DeleteFrom("users").Build());

        Assert.Equal(
            "UPDATE \"users\" SET \"active\" = @p1",
            Sql.Update("users").Set("active", false).AllRows().Build().Text);
        Assert.Equal(
            "DELETE FROM \"users\"",
            Sql.DeleteFrom("users").AllRows().Build().Text);
    }

    [Fact]
    public void Where_and_all_rows_are_mutually_exclusive()
    {
        Assert.Throws<InvalidOperationException>(() =>
            Sql.DeleteFrom("users").Where(Exp.Col("active") == false).AllRows());
        Assert.Throws<InvalidOperationException>(() =>
            Sql.DeleteFrom("users").AllRows().Where(Exp.Col("active") == false));
    }
}
