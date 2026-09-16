using Microsoft.Data.Sqlite;

namespace SnapData.Tests;

public sealed class DefaultInsertTests
{
    [Fact]
    public void Compiles_defaults_in_provider_specific_position()
    {
        var insert = Sql.InsertInto("items").DefaultValues();
        Assert.Equal("INSERT INTO \"items\" DEFAULT VALUES", insert.Build(SqliteQueryCompiler.Instance).Text);
        Assert.Equal("INSERT INTO \"items\" DEFAULT VALUES", insert.Build(PostgresQueryCompiler.Instance).Text);
        Assert.Equal("INSERT INTO `items` () VALUES ()", insert.Build(MySqlQueryCompiler.Instance).Text);
        Assert.Equal("INSERT INTO \"ITEMS\" DEFAULT VALUES", insert.Build(FirebirdQueryCompiler.Instance).Text);
        insert.Returning("id");
        Assert.Equal("INSERT INTO [items] OUTPUT INSERTED.[id] DEFAULT VALUES", insert.Build(SqlServerQueryCompiler.Instance).Text);
        Assert.Equal("INSERT INTO \"items\" DEFAULT VALUES RETURNING \"id\"", insert.Build(PostgresQueryCompiler.Instance).Text);
        Assert.Throws<NotSupportedException>(() => insert.Build(MySqlQueryCompiler.Instance));
    }

    [Fact]
    public void Default_mode_cannot_silently_discard_supplied_values()
    {
        Assert.Throws<InvalidOperationException>(() => Sql.InsertInto("items").DefaultValues().Values(new { id = 1 }));
        Assert.Throws<InvalidOperationException>(() => Sql.InsertInto("items").DefaultValues().Value("id", 1));
        Assert.Throws<InvalidOperationException>(() => Sql.InsertInto("items").DefaultValues().Rows(new { id = 1 }));
        Assert.Throws<InvalidOperationException>(() => Sql.InsertInto("items").Values(new { id = 1 }).DefaultValues());
        Assert.Throws<InvalidOperationException>(() => Sql.InsertInto("items").Rows(new { id = 1 }).DefaultValues());
        Assert.Throws<InvalidOperationException>(() => Sql.InsertInto("items").Build());
    }

    [Fact]
    public async Task Generated_only_entities_use_defaults_and_rollback_does_not_restore_in_memory_values()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var session = DbSession.Borrow(connection, SqliteQueryCompiler.Instance);
        await session.ExecuteAsync("CREATE TABLE items (id INTEGER PRIMARY KEY, label TEXT DEFAULT 'server')");
        var item = new GeneratedOnly();
        await using (var transaction = await session.BeginTransactionAsync())
        {
            Assert.Equal(1, await transaction.InsertAsync(item));
            Assert.True(item.Id > 0);
            Assert.Equal("server", item.Label);
            var id = await transaction.InsertInto<GeneratedOnly>().Values(new GeneratedOnly()).Returning<long>(x => x.Id).SingleAsync();
            Assert.True(id > item.Id);
            await transaction.RollbackAsync();
        }
        Assert.True(item.Id > 0);
        Assert.Equal("server", item.Label);
        Assert.Equal(0, await session.From<GeneratedOnly>().CountAsync());
        await session.InsertInto<GeneratedOnly>().DefaultValues().ExecuteAsync();
        await session.ExecuteAsync(Sql.InsertInto("items").Value("label", null));
        Assert.Equal(1, await session.ScalarAsync<int>("SELECT COUNT(*) FROM items WHERE label IS NULL"));
        Assert.Equal(1, await session.ScalarAsync<int>("SELECT COUNT(*) FROM items WHERE label = 'server'"));
    }

    [Fact]
    public async Task Mysql_computed_hydration_is_rejected_before_opening_connection()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await using var session = DbSession.Borrow(connection, MySqlQueryCompiler.Instance);
        await Assert.ThrowsAsync<NotSupportedException>(() => session.InsertAsync(new ComputedOnly()));
        await Assert.ThrowsAsync<NotSupportedException>(() => session.InsertAsync(new GeneratedOnly()));
        Assert.Equal(System.Data.ConnectionState.Closed, connection.State);
    }

    [Fact]
    public async Task Typed_values_replace_previous_values_consistently()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await using var session = DbSession.Borrow(connection, SqliteQueryCompiler.Instance);
        var query = session.InsertInto<Writable>().Values(new Writable { Label = "old" })
            .Values(new Writable { Label = "new" }).Build();
        Assert.Equal("new", query.Parameters["p1"]);
        Assert.Single(query.Parameters);
    }

    [Table("items")]
    private sealed class GeneratedOnly
    {
        [Key, Column("id"), DatabaseGenerated(DatabaseGeneratedOption.Identity)] public long Id { get; set; }
        [Column("label"), DatabaseGenerated(DatabaseGeneratedOption.Computed)] public string? Label { get; set; }
    }
    private sealed class ComputedOnly
    {
        [DatabaseGenerated(DatabaseGeneratedOption.Computed)] public int Number { get; set; }
    }
    private sealed class Writable { public string Label { get; set; } = ""; }
}
