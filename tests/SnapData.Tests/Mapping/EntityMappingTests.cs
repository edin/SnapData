using System.Reflection;

namespace SnapData.Tests;

public sealed class EntityMappingTests
{
    [Fact]
    public void Discovers_attributes_types_and_persistence_behavior()
    {
        var mapping = new EntityMappingProvider().GetMapping<MappedUser>();

        Assert.Equal("users", mapping.TableName);
        Assert.Equal("app", mapping.Schema);
        Assert.Equal("app.users", mapping.QualifiedTableName);
        Assert.Equal(3, mapping.Properties.Count);

        var id = mapping.FindProperty(nameof(MappedUser.Id));
        Assert.NotNull(id);
        Assert.Equal("user_id", id.ColumnName);
        Assert.True(id.IsKey);
        Assert.Equal(DatabaseGeneratedOption.Identity, id.Generated);
        Assert.False(id.IsNullable);
        Assert.False(id.IsInsertable);
        Assert.False(id.IsUpdatable);

        var name = mapping.FindColumn("display_name");
        Assert.NotNull(name);
        Assert.Equal(typeof(string), name.PropertyType);
        Assert.False(name.IsNullable);
        Assert.True(name.IsInsertable);
        Assert.True(name.IsUpdatable);

        var optional = mapping.FindProperty(nameof(MappedUser.Nickname));
        Assert.NotNull(optional);
        Assert.True(optional.IsNullable);
        Assert.DoesNotContain(mapping.Properties,
            property => property.PropertyName == nameof(MappedUser.DisplayLabel));
    }

    [Fact]
    public void Applies_key_and_name_conventions_and_caches_mappings()
    {
        var provider = new EntityMappingProvider(new MappingOptions
        {
            TableName = type => type.Name.ToLowerInvariant(),
            ColumnName = property => $"col_{property.Name.ToLowerInvariant()}"
        });

        var first = provider.GetMapping<ConventionEntity>();
        var second = provider.GetMapping(typeof(ConventionEntity));

        Assert.Same(first, second);
        Assert.Equal("conventionentity", first.TableName);
        Assert.Equal("col_id", first.SingleKey?.ColumnName);
        Assert.Equal("col_name", first.FindProperty("Name")?.ColumnName);
    }

    [Fact]
    public void Supports_explicit_composite_keys()
    {
        var mapping = new EntityMappingProvider().GetMapping<CompositeEntity>();

        Assert.Equal(2, mapping.Keys.Count);
        Assert.Null(mapping.SingleKey);
    }

    [Fact]
    public void Supports_schema_qualified_table_attribute()
    {
        var mapping = new EntityMappingProvider().GetMapping<InlineSchemaEntity>();

        Assert.Equal("app", mapping.Schema);
        Assert.Equal("users", mapping.TableName);
        Assert.Equal("app.users", mapping.QualifiedTableName);
    }

    [Fact]
    public void Rejects_ambiguous_or_malformed_qualified_table_names()
    {
        var provider = new EntityMappingProvider();

        var ambiguous = Assert.Throws<InvalidOperationException>(
            () => provider.GetMapping<AmbiguousSchemaEntity>());
        var malformed = Assert.Throws<InvalidOperationException>(
            () => provider.GetMapping<MalformedSchemaEntity>());

        Assert.Contains("both inline", ambiguous.Message);
        Assert.Contains("schema.table", malformed.Message);
    }

    [Fact]
    public void Rejects_duplicate_columns_and_invalid_attribute_combinations()
    {
        var provider = new EntityMappingProvider();

        var duplicate = Assert.Throws<InvalidOperationException>(
            () => provider.GetMapping<DuplicateColumns>());
        var ignoredKey = Assert.Throws<InvalidOperationException>(
            () => provider.GetMapping<IgnoredKey>());
        var multipleIdentities = Assert.Throws<InvalidOperationException>(
            () => provider.GetMapping<MultipleIdentities>());

        Assert.Contains("multiple properties", duplicate.Message);
        Assert.Contains("both Key and NotMapped", ignoredKey.Message);
        Assert.Contains("multiple identity", multipleIdentities.Message);
    }

    [Table("users", Schema = "app")]
    private sealed class MappedUser
    {
        [Key]
        [Column("user_id")]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public long Id { get; init; }

        [Column("display_name")]
        public required string Name { get; init; }

        public string? Nickname { get; init; }

        [NotMapped]
        public string DisplayLabel => Name;
    }

    private sealed class ConventionEntity
    {
        public long Id { get; init; }
        public required string Name { get; init; }
    }

    private sealed class CompositeEntity
    {
        [Key]
        public long TenantId { get; init; }

        [Key]
        public long UserId { get; init; }
    }

    [Table("app.users")]
    private sealed class InlineSchemaEntity
    {
        public long Id { get; init; }
    }

    [Table("app.users", Schema = "other")]
    private sealed class AmbiguousSchemaEntity
    {
        public long Id { get; init; }
    }

    [Table("catalog.app.users")]
    private sealed class MalformedSchemaEntity
    {
        public long Id { get; init; }
    }

    private sealed class DuplicateColumns
    {
        [Column("value")]
        public string? First { get; init; }

        [Column("VALUE")]
        public string? Second { get; init; }
    }

    private sealed class IgnoredKey
    {
        [Key]
        [NotMapped]
        public long Id { get; init; }
    }

    private sealed class MultipleIdentities
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public long First { get; init; }

        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public long Second { get; init; }
    }
}
