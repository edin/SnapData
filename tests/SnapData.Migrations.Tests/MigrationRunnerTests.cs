using Microsoft.Data.Sqlite;
using SnapData.Migrations;

namespace SnapData.Migrations.Tests;

public sealed class MigrationRunnerTests
{
    [Fact]
    public async Task Preview_uses_async_planning_without_mutating_the_database()
    {
        await using var test = await TestDatabase.CreateAsync();
        var database = test.Database;
        var migration = new CreateUsers();
        var runner = Runner(database, migration);

        var script = await runner.PreviewAsync(migration);

        Assert.Equal(MigrationDirection.Up, script.Direction);
        Assert.Equal(2, script.Statements.Count);
        await using var session = await database.OpenSessionAsync();
        Assert.Equal(0L, await session.ScalarAsync<long>(
            "SELECT COUNT(*) FROM sqlite_schema WHERE name = 'users'"));
    }

    [Fact]
    public async Task Migrate_is_idempotent_and_records_history()
    {
        await using var test = await TestDatabase.CreateAsync();
        var database = test.Database;
        var runner = Runner(database, new CreateUsers(), new AddEmailIfMissing());

        await runner.MigrateAsync();
        await runner.MigrateAsync();

        var history = await runner.GetHistoryAsync();
        Assert.Equal(["001-users", "002-email"],
            history.Select(item => item.MigrationId));
        await using var session = await database.OpenSessionAsync();
        Assert.Equal(1L, await session.ScalarAsync<long>(
            "SELECT COUNT(*) FROM pragma_table_info('users') WHERE name = 'email'"));
    }

    [Fact]
    public async Task Rollback_runs_down_plans_in_reverse_order()
    {
        await using var test = await TestDatabase.CreateAsync();
        var database = test.Database;
        var runner = Runner(database, new CreateUsers(), new AddEmailIfMissing());
        await runner.MigrateAsync();

        await runner.RollbackAsync();

        Assert.Equal(["001-users"],
            (await runner.GetHistoryAsync()).Select(item => item.MigrationId));
        await using (var session = await database.OpenSessionAsync())
        {
            Assert.Equal(0L, await session.ScalarAsync<long>(
                "SELECT COUNT(*) FROM pragma_table_info('users') WHERE name = 'email'"));
        }

        await runner.RollbackAsync();
        await using var verification = await database.OpenSessionAsync();
        Assert.Equal(0L, await verification.ScalarAsync<long>(
            "SELECT COUNT(*) FROM sqlite_schema WHERE name = 'users'"));
    }

    [Fact]
    public async Task Empty_conditional_plan_is_recorded_as_applied()
    {
        await using var test = await TestDatabase.CreateAsync();
        var database = test.Database;
        await using (var session = await database.OpenSessionAsync())
        {
            await session.ExecuteAsync(
                "CREATE TABLE users (id INTEGER PRIMARY KEY, email TEXT)");
        }
        var runner = Runner(database, new AddEmailIfMissing());

        await runner.MigrateAsync();

        Assert.Equal("002-email", Assert.Single(await runner.GetHistoryAsync()).MigrationId);
    }

    [Fact]
    public async Task Failed_migration_rolls_back_schema_and_history()
    {
        await using var test = await TestDatabase.CreateAsync();
        var database = test.Database;
        var runner = Runner(database, new FailingMigration());

        await Assert.ThrowsAnyAsync<Exception>(() => runner.MigrateAsync());

        Assert.Empty(await runner.GetHistoryAsync());
        await using var session = await database.OpenSessionAsync();
        Assert.Equal(0L, await session.ScalarAsync<long>(
            "SELECT COUNT(*) FROM sqlite_schema WHERE name = 'doomed'"));
    }

    [Fact]
    public async Task Custom_lock_receives_context_and_is_released_after_failure()
    {
        await using var test = await TestDatabase.CreateAsync();
        var migrationLock = new RecordingLock();
        var runner = new MigrationRunner(
            test.Database,
            new Migration[] { new FailingMigration() },
            SqliteMigrationDialect.Instance,
            new MigrationRunnerOptions
            {
                MigrationLock = migrationLock,
                LockResource = "application:migrations",
                LockTimeout = TimeSpan.FromSeconds(7)
            });

        await Assert.ThrowsAnyAsync<Exception>(() => runner.MigrateAsync());

        Assert.True(migrationLock.Acquired);
        Assert.True(migrationLock.Released);
        Assert.Equal("application:migrations", migrationLock.Context!.Resource);
        Assert.Equal(TimeSpan.FromSeconds(7), migrationLock.Context.Timeout);
    }

    [Fact]
    public async Task Required_lock_fails_before_creating_history_when_unavailable()
    {
        await using var test = await TestDatabase.CreateAsync();
        var runner = new MigrationRunner(
            test.Database,
            Array.Empty<Migration>(),
            SqliteMigrationDialect.Instance);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            runner.MigrateAsync());

        Assert.Contains("does not provide a migration lock", exception.Message);
        await using var session = await test.Database.OpenSessionAsync();
        Assert.Equal(0L, await session.ScalarAsync<long>(
            "SELECT COUNT(*) FROM sqlite_schema WHERE name = '__snapdata_migrations'"));
    }

    private static MigrationRunner Runner(SnapDatabase database, params Migration[] migrations) =>
        new(
            database,
            migrations,
            SqliteMigrationDialect.Instance,
            new MigrationRunnerOptions { Locking = MigrationLocking.Disabled });

    private sealed class TestDatabase : IAsyncDisposable
    {
        private readonly SqliteConnection anchor;

        private TestDatabase(SqliteConnection anchor, SnapDatabase database)
        {
            this.anchor = anchor;
            Database = database;
        }

        public SnapDatabase Database { get; }

        public static async Task<TestDatabase> CreateAsync()
        {
            var connectionString =
                $"Data Source=snapdata-migrations-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
            var anchor = new SqliteConnection(connectionString);
            await anchor.OpenAsync();
            return new TestDatabase(
                anchor,
                new SnapDatabase(
                    SqliteFactory.Instance,
                    connectionString,
                    SqliteQueryCompiler.Instance));
        }

        public ValueTask DisposeAsync() => anchor.DisposeAsync();
    }

    private sealed class CreateUsers : Migration
    {
        public override string Id => "001-users";

        public override void Up(MigrationPlan migration)
        {
            using var table = migration.CreateTable("users");
            table.Identity();
            table.String("name");
            table.Index("IX_users_name", "name");
        }

        public override void Down(MigrationPlan migration) => migration.DropTable("users");
    }

    private sealed class AddEmailIfMissing : Migration
    {
        public override string Id => "002-email";

        public override async ValueTask UpAsync(MigrationContext context)
        {
            if (!await context.Schema.ColumnExistsAsync("users", "email"))
            {
                context.Plan.ExecuteSql("ALTER TABLE \"users\" ADD COLUMN \"email\" TEXT");
            }
        }

        public override void Down(MigrationPlan migration) =>
            migration.DropColumn("users", "email");
    }

    private sealed class FailingMigration : Migration
    {
        public override string Id => "001-failing";

        public override void Up(MigrationPlan migration)
        {
            using (var table = migration.CreateTable("doomed"))
            {
                table.Int64("id");
            }
            migration.ExecuteSql("THIS IS NOT SQL");
        }
    }

    private sealed class RecordingLock : IMigrationLock
    {
        public bool Acquired { get; private set; }
        public bool Released { get; private set; }
        public MigrationLockContext? Context { get; private set; }

        public ValueTask<IAsyncDisposable> AcquireAsync(MigrationLockContext context)
        {
            Context = context;
            Acquired = true;
            return ValueTask.FromResult<IAsyncDisposable>(new ReleaseHandle(this));
        }

        private sealed class ReleaseHandle(RecordingLock owner) : IAsyncDisposable
        {
            public ValueTask DisposeAsync()
            {
                owner.Released = true;
                return ValueTask.CompletedTask;
            }
        }
    }
}
