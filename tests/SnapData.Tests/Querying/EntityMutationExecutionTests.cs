using Microsoft.Data.Sqlite;

namespace SnapData.Tests;

public sealed class EntityMutationExecutionTests
{
    [Fact]
    public async Task Typed_mutations_use_mapped_table_columns_and_predicates()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText =
                "CREATE TABLE users (id INTEGER PRIMARY KEY, name TEXT NOT NULL, active INTEGER NOT NULL);";
            await command.ExecuteNonQueryAsync();
        }

        await using var session = DbSession.Borrow(connection, SqliteQueryCompiler.Instance);

        var inserted = await session
            .InsertInto<User>()
            .Value(user => user.Id, 10L)
            .Value(user => user.Name, "Edin")
            .Value(user => user.Active, false)
            .ExecuteAsync();
        var updated = await session
            .Update<User>()
            .Set(user => user.Name, "SnapData")
            .Set(user => user.Active, true)
            .Where(user => user.Id == 10)
            .ExecuteAsync();
        var deleted = await session
            .DeleteFrom<User>()
            .Where(user => user.Id == 10)
            .ExecuteAsync();

        Assert.Equal(1, inserted);
        Assert.Equal(1, updated);
        Assert.Equal(1, deleted);
    }

    [Table("users")]
    private sealed class User
    {
        [Key]
        public long Id { get; set; }

        public string Name { get; set; } = string.Empty;

        public bool Active { get; set; }
    }
}
