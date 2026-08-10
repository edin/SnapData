using Microsoft.Data.Sqlite;

namespace SnapData.Tests;

public sealed class RelationIncludeTests
{
    [Fact]
    public async Task Include_loads_reference_relation_with_split_query()
    {
        await using var connection = await OpenConnectionAsync();
        await using var session = DbSession.Borrow(connection, SqliteQueryCompiler.Instance);

        var users = await session
            .From<User>()
            .OrderBy(user => user.Id)
            .Include(user => user.Address)
            .ToListAsync();

        Assert.Equal(3, users.Count);
        Assert.Equal("Sarajevo", users[0].Address!.City);
        Assert.Same(users[0].Address, users[1].Address);
        Assert.Null(users[2].Address);
    }

    [Fact]
    public async Task Include_uses_same_transaction_executor()
    {
        await using var connection = await OpenConnectionAsync();
        await using var session = DbSession.Borrow(connection, SqliteQueryCompiler.Instance);
        await using var transaction = await session.BeginTransactionAsync();
        await transaction.ExecuteAsync(
            "INSERT INTO addresses (id, city) VALUES (2, 'Mostar')");
        await transaction.ExecuteAsync(
            "INSERT INTO users (id, name, address_id) VALUES (4, 'Transaction', 2)");

        var users = await transaction
            .From<User>()
            .Where(user => user.Id == 4)
            .Include(user => user.Address)
            .ToListAsync();

        Assert.Equal("Mostar", Assert.Single(users).Address!.City);
    }

    [Fact]
    public void Relation_is_mapping_metadata_and_not_a_column()
    {
        var mapping = EntityMappingProvider.Default.GetMapping<User>();
        var relation = Assert.Single(
            mapping.Relations,
            relation => relation.NavigationName == nameof(User.Address));

        Assert.Null(mapping.FindProperty(nameof(User.Address)));
        Assert.Equal(nameof(User.AddressId), relation.LocalKey.PropertyName);
        Assert.Equal(nameof(Address.Id), relation.ForeignKeyPropertyName);
        Assert.Equal(typeof(Address), relation.RelatedType);
        Assert.Equal(RelationCardinality.Reference, relation.Cardinality);
    }

    [Fact]
    public void Include_rejects_unmapped_and_collection_navigations()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        using var session = DbSession.Borrow(connection);

        Assert.Throws<InvalidOperationException>(() =>
            session.From<User>().Include(user => user.Name));
        Assert.Throws<NotSupportedException>(() =>
            session.From<User>().Include(user => user.Orders));
    }

    private static async Task<SqliteConnection> OpenConnectionAsync()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            CREATE TABLE addresses (
                id INTEGER PRIMARY KEY,
                city TEXT NOT NULL
            );
            CREATE TABLE users (
                id INTEGER PRIMARY KEY,
                name TEXT NOT NULL,
                address_id INTEGER NULL
            );
            CREATE TABLE orders (
                id INTEGER PRIMARY KEY,
                user_id INTEGER NOT NULL
            );
            INSERT INTO addresses VALUES (1, 'Sarajevo');
            INSERT INTO users VALUES
                (1, 'Edin', 1),
                (2, 'Sara', 1),
                (3, 'No address', NULL);
            """;
        await command.ExecuteNonQueryAsync();
        return connection;
    }

    [Table("users")]
    private sealed class User
    {
        [Key]
        public long Id { get; init; }

        public string Name { get; init; } = string.Empty;

        [Column("address_id")]
        public long? AddressId { get; init; }

        [Relation(nameof(AddressId), nameof(Address.Id))]
        public Address? Address { get; set; }

        [Relation(nameof(Id), nameof(Order.UserId))]
        public List<Order> Orders { get; init; } = [];
    }

    [Table("addresses")]
    private sealed class Address
    {
        [Key]
        public long Id { get; init; }

        public string City { get; init; } = string.Empty;
    }

    [Table("orders")]
    private sealed class Order
    {
        [Key]
        public long Id { get; init; }

        [Column("user_id")]
        public long UserId { get; init; }
    }
}
