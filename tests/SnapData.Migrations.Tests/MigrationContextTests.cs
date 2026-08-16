using SnapData.Migrations;
using SnapData.Schema;

namespace SnapData.Migrations.Tests;

public sealed class MigrationContextTests
{
    [Fact]
    public async Task Synchronous_migration_runs_through_async_lifecycle()
    {
        var plan = new MigrationPlan();
        var context = new MigrationContext(plan, new RecordingInspector());

        await new SyncMigration().UpAsync(context);

        Assert.IsType<CreateTableOperation>(Assert.Single(plan.Operations));
    }

    [Fact]
    public async Task Async_migration_can_conditionally_build_a_plan()
    {
        var inspector = new RecordingInspector { TableExists = false };
        var plan = new MigrationPlan();
        var context = new MigrationContext(plan, inspector);

        await new ConditionalMigration().UpAsync(context);

        Assert.IsType<CreateTableOperation>(Assert.Single(plan.Operations));
    }

    [Fact]
    public async Task Schema_facade_parses_names_and_propagates_cancellation()
    {
        using var cancellation = new CancellationTokenSource();
        var inspector = new RecordingInspector();
        var context = new MigrationContext(
            new MigrationPlan(),
            inspector,
            cancellation.Token);

        await context.Schema.ColumnExistsAsync("app.users", "email");

        Assert.Equal("app", inspector.LastTable!.Schema);
        Assert.Equal("users", inspector.LastTable.Name);
        Assert.Equal("email", inspector.LastColumn);
        Assert.Equal(cancellation.Token, inspector.LastCancellationToken);
    }

    [Theory]
    [InlineData("")]
    [InlineData("app.")]
    [InlineData(".users")]
    [InlineData("catalog.app.users")]
    public async Task Schema_facade_rejects_invalid_table_paths(string value)
    {
        var context = new MigrationContext(
            new MigrationPlan(),
            new RecordingInspector());

        await Assert.ThrowsAsync<ArgumentException>(() =>
            context.Schema.TableExistsAsync(value));
    }

    private sealed class SyncMigration : Migration
    {
        public override void Up(MigrationPlan migration)
        {
            using var table = migration.CreateTable("users");
            table.Identity();
        }
    }

    private sealed class ConditionalMigration : Migration
    {
        public override async ValueTask UpAsync(MigrationContext context)
        {
            if (!await context.Schema.TableExistsAsync("users"))
            {
                using var table = context.Plan.CreateTable("users");
                table.Identity();
            }
        }
    }

    private sealed class RecordingInspector : ISchemaInspector
    {
        public bool TableExists { get; init; }
        public SchemaObjectName? LastTable { get; private set; }
        public string? LastColumn { get; private set; }
        public CancellationToken LastCancellationToken { get; private set; }

        public Task<bool> TableExistsAsync(
            SchemaObjectName table,
            CancellationToken cancellationToken = default)
        {
            Record(table, cancellationToken);
            return Task.FromResult(TableExists);
        }

        public Task<bool> ColumnExistsAsync(
            SchemaObjectName table,
            string column,
            CancellationToken cancellationToken = default)
        {
            Record(table, cancellationToken);
            LastColumn = column;
            return Task.FromResult(false);
        }

        public Task<TableSchema?> GetTableAsync(
            SchemaObjectName table,
            SchemaReadOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            Record(table, cancellationToken);
            return Task.FromResult<TableSchema?>(null);
        }

        public Task<DatabaseSchema> ReadAsync(
            SchemaReadOptions? options = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new DatabaseSchema("test"));

        public Task<IReadOnlyList<SchemaObjectInfo>> GetObjectsAsync(
            string? schema = null,
            bool includeSystemObjects = false,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<SchemaObjectInfo>>([]);

        private void Record(
            SchemaObjectName table,
            CancellationToken cancellationToken)
        {
            LastTable = table;
            LastCancellationToken = cancellationToken;
        }
    }
}
