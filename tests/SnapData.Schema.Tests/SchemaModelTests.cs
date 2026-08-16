using System.Data;

namespace SnapData.Schema.Tests;

public sealed class SchemaModelTests
{
    [Fact]
    public void Qualified_object_name_preserves_schema_and_name()
    {
        var name = new SchemaObjectName("users", "app");

        Assert.Equal("app", name.Schema);
        Assert.Equal("users", name.Name);
        Assert.Equal("app.users", name.ToString());
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void Object_name_rejects_empty_names(string value) =>
        Assert.ThrowsAny<ArgumentException>(() => new SchemaObjectName(value));

    [Fact]
    public void Schema_models_snapshot_supplied_collections()
    {
        var columns = new List<ColumnSchema>
        {
            new("id", 0, "INTEGER", DbType.Int64, typeof(long), false)
        };
        var table = new TableSchema(new SchemaObjectName("users"), columns);
        var tables = new List<TableSchema> { table };
        var database = new DatabaseSchema("main", tables);

        columns.Clear();
        tables.Clear();

        Assert.Single(table.Columns);
        Assert.Single(database.Tables);
    }

    [Fact]
    public void Default_read_options_request_complete_table_details()
    {
        var options = SchemaReadOptions.Default;

        Assert.True(options.IncludeColumns);
        Assert.True(options.IncludePrimaryKeys);
        Assert.True(options.IncludeForeignKeys);
        Assert.True(options.IncludeIndexes);
        Assert.True(options.IncludeViews);
        Assert.True(options.IncludeDefinitionSql);
    }

    [Fact]
    public void Composite_foreign_key_snapshots_and_preserves_column_pairs()
    {
        var columns = new List<string> { "tenant_id", "user_id" };
        var referenced = new List<string> { "tenant_id", "id" };
        var foreignKey = new ForeignKeySchema(
            "fk_orders_users",
            columns,
            new SchemaObjectName("users"),
            referenced,
            ReferentialAction.Cascade,
            ReferentialAction.Restrict);

        columns.Clear();
        referenced.Clear();

        Assert.Equal(["tenant_id", "user_id"], foreignKey.Columns);
        Assert.Equal(["tenant_id", "id"], foreignKey.ReferencedColumns);
        Assert.Equal(ReferentialAction.Cascade, foreignKey.OnUpdate);
        Assert.Equal(ReferentialAction.Restrict, foreignKey.OnDelete);
    }

    [Fact]
    public void Foreign_key_requires_matching_nonempty_column_lists()
    {
        var table = new SchemaObjectName("users");

        Assert.Throws<ArgumentException>(() =>
            new ForeignKeySchema(null, [], table, []));
        Assert.Throws<ArgumentException>(() =>
            new ForeignKeySchema(null, ["tenant_id", "user_id"], table, ["id"]));
    }

    [Fact]
    public void Primary_key_and_index_snapshot_their_columns()
    {
        var keyColumns = new List<string> { "tenant_id", "id" };
        var indexColumns = new List<IndexColumnSchema>
        {
            new("tenant_id", 0),
            new("name", 1, descending: true)
        };

        var primaryKey = new PrimaryKeySchema("pk_users", keyColumns);
        var index = new IndexSchema("ix_users_name", indexColumns, isUnique: true);
        keyColumns.Clear();
        indexColumns.Clear();

        Assert.Equal(["tenant_id", "id"], primaryKey.Columns);
        Assert.Equal(2, index.Columns.Count);
        Assert.True(index.Columns[1].Descending);
        Assert.True(index.IsUnique);
    }

    [Fact]
    public void Keys_and_indexes_reject_invalid_columns()
    {
        Assert.Throws<ArgumentException>(() => new PrimaryKeySchema(null, []));
        Assert.Throws<ArgumentException>(() => new PrimaryKeySchema(null, [""]));
        Assert.Throws<ArgumentException>(() => new IndexSchema("ix_users", []));
        Assert.Throws<ArgumentException>(() => new IndexSchema(
            "ix_users",
            [new IndexColumnSchema("id", 0), new IndexColumnSchema("name", 0)]));
        Assert.Throws<ArgumentOutOfRangeException>(() => new IndexColumnSchema("id", -1));
        Assert.Throws<ArgumentException>(() => new IndexColumnSchema(null, 0));
        Assert.Throws<ArgumentException>(() => new IndexColumnSchema("id", 0, expression: "lower(id)"));
    }

    [Fact]
    public void Expression_index_entry_has_expression_instead_of_column_name()
    {
        var column = new IndexColumnSchema(null, 0, expression: "lower(name)");

        Assert.Null(column.Name);
        Assert.Equal("lower(name)", column.Expression);
    }

    [Fact]
    public void Index_entry_can_represent_an_included_column()
    {
        var column = new IndexColumnSchema("active", 1, isIncluded: true);

        Assert.True(column.IsIncluded);
        Assert.False(column.Descending);
    }

    [Fact]
    public void Index_metadata_preserves_method_visibility_and_prefix_length()
    {
        var column = new IndexColumnSchema("name", 0, prefixLength: 12);
        var index = new IndexSchema(
            "ix_users_name",
            [column],
            isVisible: false,
            method: "BTREE");

        Assert.Equal(12, column.PrefixLength);
        Assert.False(index.IsVisible);
        Assert.Equal("BTREE", index.Method);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new IndexColumnSchema("name", 0, prefixLength: 0));
    }
}
