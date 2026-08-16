using Microsoft.Data.Sqlite;

namespace SnapData.Tests;

public sealed class EntityExecutionTests
{
    [Fact]
    public async Task Session_inserts_updates_and_deletes_mapped_entity()
    {
        await using var connection = await OpenConnectionAsync();
        await using var session = DbSession.Borrow(connection);
        var user = new User { Name = "Edin", Active = true };

        Assert.Equal(1, await session.InsertAsync(user));
        Assert.True(user.Id > 0);

        user.Name = "Updated";
        user.Active = false;
        Assert.Equal(1, await session.UpdateAsync(user));

        var stored = await session.QuerySingleOrDefaultAsync<StoredUser>(
            "SELECT id, name, active FROM users WHERE id = @Id",
            new { user.Id });
        Assert.NotNull(stored);
        Assert.Equal("Updated", stored.Name);
        Assert.False(stored.Active);

        Assert.Equal(1, await session.DeleteAsync(user));
        Assert.Equal(0, await session.ScalarAsync<int>("SELECT COUNT(*) FROM users"));
    }

    [Fact]
    public async Task Transaction_entity_operations_use_transaction_and_roll_back_on_disposal()
    {
        await using var connection = await OpenConnectionAsync();
        await using var session = DbSession.Borrow(connection);

        await using (var transaction = await session.BeginTransactionAsync())
        {
            await transaction.InsertAsync(new User { Name = "Rollback", Active = true });
            Assert.Equal(1, await transaction.ScalarAsync<int>("SELECT COUNT(*) FROM users"));
        }

        Assert.Equal(0, await session.ScalarAsync<int>("SELECT COUNT(*) FROM users"));
    }

    [Fact]
    public async Task Insert_assigns_generated_values_inside_transaction()
    {
        await using var connection = await OpenConnectionAsync();
        await using var session = DbSession.Borrow(connection);
        await using var transaction = await session.BeginTransactionAsync();
        var user = new User { Name = "Generated", Active = true };

        Assert.Equal(1, await transaction.InsertAsync(user));

        Assert.True(user.Id > 0);
        Assert.Equal(
            "Generated",
            await transaction.ScalarAsync<string>(
                "SELECT name FROM users WHERE id = @Id",
                new { user.Id }));
    }

    [Fact]
    public async Task Composite_keys_are_applied_during_execution()
    {
        await using var connection = await OpenConnectionAsync();
        await using var session = DbSession.Borrow(connection);
        var first = new Membership { TenantId = 1, UserId = 7, Role = "viewer" };
        var second = new Membership { TenantId = 2, UserId = 7, Role = "viewer" };
        await session.InsertAsync(first);
        await session.InsertAsync(second);

        first.Role = "admin";
        Assert.Equal(1, await session.UpdateAsync(first));
        Assert.Equal(1, await session.DeleteAsync(first));

        var remainingRole = await session.ScalarAsync<string>(
            "SELECT role FROM memberships WHERE tenant_id = 2 AND user_id = 7");
        Assert.Equal("viewer", remainingRole);
        Assert.Equal(1, await session.ScalarAsync<int>("SELECT COUNT(*) FROM memberships"));
    }

    [Fact]
    public async Task Session_entity_commands_use_configured_mapping_provider()
    {
        await using var connection = await OpenConnectionAsync();
        var mappings = new EntityMappingProvider(new MappingOptions
        {
            TableName = _ => "custom_entities",
            ColumnName = property => property.Name switch
            {
                nameof(CustomEntity.Id) => "entity_id",
                nameof(CustomEntity.Value) => "entity_value",
                _ => property.Name
            }
        });
        await using var session = DbSession.Borrow(
            connection,
            mappingProvider: mappings);

        var affected = await session.InsertAsync(new CustomEntity
        {
            Id = 10,
            Value = "custom"
        });

        Assert.Equal(1, affected);
        Assert.Equal(
            "custom",
            await session.ScalarAsync<string>(
                "SELECT entity_value FROM custom_entities WHERE entity_id = 10"));
    }

    [Fact]
    public async Task Invalid_entity_mapping_fails_before_command_execution()
    {
        await using var connection = await OpenConnectionAsync();
        await using var session = DbSession.Borrow(connection);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => session.UpdateAsync(new KeylessEntity { Value = "test" }));
        Assert.Equal(0, await session.ScalarAsync<int>("SELECT COUNT(*) FROM users"));
    }

    private static async Task<SqliteConnection> OpenConnectionAsync()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            CREATE TABLE users (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                name TEXT NOT NULL,
                active INTEGER NOT NULL
            );

            CREATE TABLE memberships (
                tenant_id INTEGER NOT NULL,
                user_id INTEGER NOT NULL,
                role TEXT NOT NULL,
                PRIMARY KEY (tenant_id, user_id)
            );

            CREATE TABLE custom_entities (
                entity_id INTEGER PRIMARY KEY,
                entity_value TEXT NOT NULL
            );
            """;
        await command.ExecuteNonQueryAsync();
        return connection;
    }

    [Table("users")]
    private sealed class User
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public long Id { get; set; }

        public required string Name { get; set; }

        public bool Active { get; set; }
    }

    [Table("memberships")]
    private sealed class Membership
    {
        [Key]
        [Column("tenant_id")]
        public long TenantId { get; init; }

        [Key]
        [Column("user_id")]
        public long UserId { get; init; }

        public required string Role { get; set; }
    }

    private sealed class CustomEntity
    {
        public long Id { get; init; }
        public required string Value { get; init; }
    }

    private sealed class KeylessEntity
    {
        public required string Value { get; init; }
    }

    private sealed record StoredUser(long Id, string Name, bool Active);
}
