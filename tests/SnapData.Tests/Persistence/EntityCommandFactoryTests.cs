namespace SnapData.Tests;

public sealed class EntityCommandFactoryTests
{
    private readonly EntityCommandFactory _factory = new();

    [Fact]
    public void Insert_uses_mapped_table_and_insertable_properties()
    {
        var entity = new User
        {
            Id = 0,
            Name = "Edin",
            UpdatedAt = new DateTime(2026, 8, 9)
        };

        var query = _factory.Insert(entity).Build();

        Assert.Equal(
            "INSERT INTO \"app\".\"users\" (\"display_name\") VALUES (@p1)",
            query.Text);
        Assert.Equal("Edin", query.Parameters["p1"]);
    }

    [Fact]
    public void Update_uses_updatable_properties_and_all_composite_keys()
    {
        var entity = new Membership
        {
            TenantId = 7,
            UserId = 42,
            Role = "admin"
        };

        var query = _factory.Update(entity).Build();

        Assert.Equal(
            "UPDATE \"memberships\" SET \"role\" = @p1 WHERE (\"tenant_id\" = @p2 AND \"user_id\" = @p3)",
            query.Text);
        Assert.Equal("admin", query.Parameters["p1"]);
        Assert.Equal(7L, query.Parameters["p2"]);
        Assert.Equal(42L, query.Parameters["p3"]);
    }

    [Fact]
    public void Delete_uses_only_key_properties()
    {
        var query = _factory.Delete(new Membership
        {
            TenantId = 7,
            UserId = 42,
            Role = "ignored"
        }).Build();

        Assert.Equal(
            "DELETE FROM \"memberships\" WHERE (\"tenant_id\" = @p1 AND \"user_id\" = @p2)",
            query.Text);
        Assert.Equal(2, query.Parameters.Count);
    }

    [Fact]
    public void Update_and_delete_reject_entities_without_keys()
    {
        var entity = new KeylessEntity { Value = "test" };

        var update = Assert.Throws<InvalidOperationException>(() => _factory.Update(entity));
        var delete = Assert.Throws<InvalidOperationException>(() => _factory.Delete(entity));

        Assert.Contains("at least one key", update.Message);
        Assert.Contains("at least one key", delete.Message);
    }

    [Fact]
    public void Update_and_delete_reject_unassigned_identity_keys()
    {
        var entity = new User { Name = "Edin" };

        Assert.Throws<InvalidOperationException>(() => _factory.Update(entity));
        Assert.Throws<InvalidOperationException>(() => _factory.Delete(entity));
    }

    [Table("users", Schema = "app")]
    private sealed class User
    {
        [Key]
        [Column("user_id")]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public long Id { get; init; }

        [Column("display_name")]
        public required string Name { get; init; }

        [DatabaseGenerated(DatabaseGeneratedOption.Computed)]
        public DateTime UpdatedAt { get; init; }
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

        [Column("role")]
        public required string Role { get; init; }
    }

    private sealed class KeylessEntity
    {
        public required string Value { get; init; }
    }
}
