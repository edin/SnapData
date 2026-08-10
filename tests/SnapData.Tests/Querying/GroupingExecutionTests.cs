using Microsoft.Data.Sqlite;

namespace SnapData.Tests;

public sealed class GroupingExecutionTests
{
    [Fact]
    public async Task Grouped_projection_executes_and_maps_result_shape()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText =
                """
                CREATE TABLE orders (id INTEGER PRIMARY KEY, customer_id INTEGER NOT NULL, total REAL NOT NULL);
                INSERT INTO orders VALUES (1, 10, 20), (2, 10, 30), (3, 20, 5);
                """;
            await command.ExecuteNonQueryAsync();
        }

        await using var session = DbSession.Borrow(connection, SqliteQueryCompiler.Instance);
        var count = Sql.Count("o.id");

        var rows = await session
            .From<CustomerSummary>("orders o")
            .Select(
                Sql.Col("o.customer_id").As("CustomerId"),
                count.As("OrderCount"),
                Sql.Sum("o.total").As("Total"))
            .GroupBy("o.customer_id")
            .Having(count > 1)
            .ToListAsync();

        var row = Assert.Single(rows);
        Assert.Equal(10, row.CustomerId);
        Assert.Equal(2, row.OrderCount);
        Assert.Equal(50, row.Total);
    }

    private sealed record CustomerSummary(long CustomerId, long OrderCount, double Total);
}
