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
        Assert.Equal([1L, 2L], history.Select(item => item.AppliedOrder));
        Assert.All(history, item => Assert.Equal(64, item.Fingerprint!.Length));
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
    public async Task Sqlite_uses_required_locking_by_default()
    {
        await using var test = await TestDatabase.CreateAsync();
        var runner = new MigrationRunner(
            test.Database,
            Array.Empty<Migration>(),
            SqliteMigrationDialect.Instance);

        await runner.MigrateAsync();

        await using var session = await test.Database.OpenSessionAsync();
        Assert.Equal(2L, await session.ScalarAsync<long>(
            "SELECT COUNT(*) FROM sqlite_schema WHERE name IN " +
            "('__snapdata_migrations', '__snapdata_migrations_lock')"));
        Assert.Equal(0L, await session.ScalarAsync<long>(
            "SELECT COUNT(*) FROM \"__snapdata_migrations_lock\""));
    }

    [Fact]
    public async Task Sqlite_lock_times_out_a_concurrent_runner_and_is_released()
    {
        await using var test = await TestDatabase.CreateAsync();
        var blocking = new BlockingMigration();
        var first = new MigrationRunner(
            test.Database,
            new Migration[] { blocking },
            SqliteMigrationDialect.Instance);
        var firstRun = first.MigrateAsync();
        await blocking.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));

        var second = new MigrationRunner(
            test.Database,
            new Migration[] { blocking },
            SqliteMigrationDialect.Instance,
            new MigrationRunnerOptions { LockTimeout = TimeSpan.FromMilliseconds(150) });
        var exception = await Assert.ThrowsAsync<MigrationLockTimeoutException>(() =>
            second.MigrateAsync());
        Assert.Equal(TimeSpan.FromMilliseconds(150), exception.Timeout);

        blocking.Release.TrySetResult();
        await firstRun;

        await second.MigrateAsync();
    }

    [Fact]
    public async Task Sqlite_lock_recovers_an_expired_lease()
    {
        await using var test = await TestDatabase.CreateAsync();
        var runner = new MigrationRunner(
            test.Database,
            Array.Empty<Migration>(),
            SqliteMigrationDialect.Instance);
        await runner.MigrateAsync();
        await using (var session = await test.Database.OpenSessionAsync())
        {
            await session.ExecuteAsync(
                "INSERT INTO \"__snapdata_migrations_lock\" " +
                "(\"resource\", \"owner_id\", \"expires_at\") " +
                "VALUES (@resource, 'abandoned', '2000-01-01T00:00:00.0000000Z')",
                new { resource = "SnapData.Migrations:__snapdata_migrations" });
        }

        await runner.MigrateAsync();

        await using var verification = await test.Database.OpenSessionAsync();
        Assert.Equal(0L, await verification.ScalarAsync<long>(
            "SELECT COUNT(*) FROM \"__snapdata_migrations_lock\""));
    }

    [Fact]
    public async Task Status_reports_applied_pending_and_schema_dependent_migrations()
    {
        await using var test = await TestDatabase.CreateAsync();
        var create = new CreateUsers();
        var conditional = new AddEmailIfMissing();
        var runner = Runner(test.Database, create, conditional);

        var pending = await runner.GetStatusAsync();
        Assert.All(pending, item => Assert.Equal(MigrationStatusState.Pending, item.State));

        await runner.MigrateAsync();
        var applied = await runner.GetStatusAsync();

        Assert.Equal(MigrationStatusState.Applied, applied[0].State);
        Assert.Equal(MigrationStatusState.Unverifiable, applied[1].State);
    }

    [Fact]
    public async Task Changed_generated_sql_is_detected_before_execution()
    {
        await using var test = await TestDatabase.CreateAsync();
        await Runner(test.Database, new CreateUsers()).MigrateAsync();
        var changed = Runner(test.Database, new ChangedCreateUsers());

        var status = Assert.Single(await changed.GetStatusAsync());
        Assert.Equal(MigrationStatusState.Changed, status.State);
        Assert.NotEqual(status.StoredFingerprint, status.CurrentFingerprint);

        var exception = await Assert.ThrowsAsync<MigrationHistoryValidationException>(() =>
            changed.MigrateAsync());
        Assert.Equal(MigrationStatusState.Changed,
            Assert.Single(exception.InvalidEntries).State);
    }

    [Fact]
    public async Task Missing_and_out_of_order_history_is_reported()
    {
        await using var test = await TestDatabase.CreateAsync();
        await Runner(test.Database, new CreateUsers(), new AddEmailIfMissing()).MigrateAsync();
        var incompleteBundle = Runner(test.Database, new AddEmailIfMissing());

        var status = await incompleteBundle.GetStatusAsync();

        Assert.Contains(status, item =>
            item.MigrationId == "002-email" && item.State == MigrationStatusState.OutOfOrder);
        Assert.Contains(status, item =>
            item.MigrationId == "001-users" && item.State == MigrationStatusState.Missing);
    }

    [Fact]
    public async Task MigrateTo_applies_through_the_requested_migration()
    {
        await using var test = await TestDatabase.CreateAsync();
        var runner = Runner(test.Database, new CreateUsers(), new AddEmailIfMissing());

        await runner.MigrateToAsync("001-USERS");

        Assert.Equal(["001-users"],
            (await runner.GetHistoryAsync()).Select(item => item.MigrationId));
        var partialStatus = await runner.GetStatusAsync();
        Assert.Equal(MigrationStatusState.Applied, partialStatus[0].State);
        Assert.Equal(MigrationStatusState.Pending, partialStatus[1].State);

        await runner.MigrateAsync();

        Assert.Equal(["001-users", "002-email"],
            (await runner.GetHistoryAsync()).Select(item => item.MigrationId));
    }

    [Fact]
    public async Task MigrateTo_rejects_an_unknown_migration_id()
    {
        await using var test = await TestDatabase.CreateAsync();
        var runner = Runner(test.Database, new CreateUsers());

        var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
            runner.MigrateToAsync("missing"));

        Assert.Contains("not registered", exception.Message);
        Assert.Empty(await runner.GetHistoryAsync());
    }

    [Fact]
    public async Task MigrateTo_does_not_implicitly_move_an_applied_database_backward()
    {
        await using var test = await TestDatabase.CreateAsync();
        var runner = Runner(test.Database, new CreateUsers(), new AddEmailIfMissing());
        await runner.MigrateAsync();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            runner.MigrateToAsync("001-users"));

        Assert.Contains("already beyond", exception.Message);
        Assert.Equal(["001-users", "002-email"],
            (await runner.GetHistoryAsync()).Select(item => item.MigrationId));
    }

    [Fact]
    public async Task Rollback_is_disabled_by_default()
    {
        await using var test = await TestDatabase.CreateAsync();
        var migration = new CreateUsers();
        var applyRunner = Runner(test.Database, migration);
        await applyRunner.MigrateAsync();
        var protectedRunner = new MigrationRunner(
            test.Database,
            new Migration[] { migration },
            SqliteMigrationDialect.Instance,
            new MigrationRunnerOptions { Locking = MigrationLocking.Disabled });

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            protectedRunner.RollbackAsync());

        Assert.Contains("disabled", exception.Message);
        Assert.Equal("001-users",
            Assert.Single(await protectedRunner.GetHistoryAsync()).MigrationId);
    }

    [Fact]
    public async Task Conditional_add_column_executes_or_skips_and_keeps_one_fingerprint()
    {
        await using var missingTest = await TestDatabase.CreateAsync();
        await using (var session = await missingTest.Database.OpenSessionAsync())
        {
            await session.ExecuteAsync("CREATE TABLE users (id INTEGER PRIMARY KEY)");
        }
        var missingRunner = Runner(
            missingTest.Database, new AddEmailConditionally());
        await missingRunner.MigrateAsync();

        await using var existingTest = await TestDatabase.CreateAsync();
        await using (var session = await existingTest.Database.OpenSessionAsync())
        {
            await session.ExecuteAsync(
                "CREATE TABLE users (id INTEGER PRIMARY KEY, email TEXT)");
        }
        var existingRunner = Runner(
            existingTest.Database, new AddEmailConditionally());
        await existingRunner.MigrateAsync();

        await using (var missingVerification =
            await missingTest.Database.OpenSessionAsync())
        {
            Assert.Equal(1L, await missingVerification.ScalarAsync<long>(
                "SELECT COUNT(*) FROM pragma_table_info('users') WHERE name = 'email'"));
        }
        await using (var existingVerification =
            await existingTest.Database.OpenSessionAsync())
        {
            Assert.Equal(1L, await existingVerification.ScalarAsync<long>(
                "SELECT COUNT(*) FROM pragma_table_info('users') WHERE name = 'email'"));
        }
        Assert.Equal(
            Assert.Single(await missingRunner.GetHistoryAsync()).Fingerprint,
            Assert.Single(await existingRunner.GetHistoryAsync()).Fingerprint);
    }

    [Fact]
    public async Task Conditional_schema_view_evolves_and_raw_sql_invalidates_it()
    {
        await using var test = await TestDatabase.CreateAsync();
        await using (var session = await test.Database.OpenSessionAsync())
        {
            await session.ExecuteAsync("CREATE TABLE users (id INTEGER PRIMARY KEY)");
        }
        var runner = Runner(test.Database, new ExerciseConditionalSchemaView());

        await runner.MigrateAsync();

        await using var verification = await test.Database.OpenSessionAsync();
        Assert.Equal(1L, await verification.ScalarAsync<long>(
            "SELECT COUNT(*) FROM pragma_table_info('users') WHERE name = 'raw_email'"));
        Assert.Equal(0L, await verification.ScalarAsync<long>(
            "SELECT COUNT(*) FROM pragma_table_info('users') WHERE name = 'transient'"));
    }

    [Fact]
    public async Task Conditional_indexes_execute_or_skip_and_evolve_the_schema_view()
    {
        await using var missingTest = await TestDatabase.CreateAsync();
        await using (var session = await missingTest.Database.OpenSessionAsync())
        {
            await session.ExecuteAsync(
                "CREATE TABLE users (id INTEGER PRIMARY KEY, email TEXT)");
        }

        await using var existingTest = await TestDatabase.CreateAsync();
        await using (var session = await existingTest.Database.OpenSessionAsync())
        {
            await session.ExecuteAsync(
                "CREATE TABLE users (id INTEGER PRIMARY KEY, email TEXT)");
            await session.ExecuteAsync(
                "CREATE INDEX IX_users_email ON users (email)");
        }

        var missingRunner = Runner(
            missingTest.Database, new ExerciseConditionalIndexes());
        var existingRunner = Runner(
            existingTest.Database, new ExerciseConditionalIndexes());
        await missingRunner.MigrateAsync();
        await existingRunner.MigrateAsync();

        await using (var missingVerification =
            await missingTest.Database.OpenSessionAsync())
        {
            Assert.Equal(0L, await missingVerification.ScalarAsync<long>(
                "SELECT COUNT(*) FROM pragma_index_list('users') " +
                "WHERE name = 'IX_users_email'"));
        }
        await using (var existingVerification =
            await existingTest.Database.OpenSessionAsync())
        {
            Assert.Equal(0L, await existingVerification.ScalarAsync<long>(
                "SELECT COUNT(*) FROM pragma_index_list('users') " +
                "WHERE name = 'IX_users_email'"));
        }
        Assert.Equal(
            Assert.Single(await missingRunner.GetHistoryAsync()).Fingerprint,
            Assert.Single(await existingRunner.GetHistoryAsync()).Fingerprint);
    }

    [Fact]
    public async Task Drop_table_if_exists_executes_or_skips_with_one_fingerprint()
    {
        await using var missingTest = await TestDatabase.CreateAsync();
        await using var existingTest = await TestDatabase.CreateAsync();
        await using (var session = await existingTest.Database.OpenSessionAsync())
        {
            await session.ExecuteAsync("CREATE TABLE users (id INTEGER PRIMARY KEY)");
        }

        var missingRunner = Runner(
            missingTest.Database, new DropUsersIfExists());
        var existingRunner = Runner(
            existingTest.Database, new DropUsersIfExists());
        await missingRunner.MigrateAsync();
        await existingRunner.MigrateAsync();

        await using (var verification = await existingTest.Database.OpenSessionAsync())
        {
            Assert.Equal(0L, await verification.ScalarAsync<long>(
                "SELECT COUNT(*) FROM sqlite_master " +
                "WHERE type = 'table' AND name = 'users'"));
        }
        Assert.Equal(
            Assert.Single(await missingRunner.GetHistoryAsync()).Fingerprint,
            Assert.Single(await existingRunner.GetHistoryAsync()).Fingerprint);
    }

    [Fact]
    public async Task Migration_plan_exposes_the_active_provider()
    {
        await using var test = await TestDatabase.CreateAsync();
        var runner = Runner(test.Database, new ProviderAwareMigration());

        await runner.MigrateAsync();

        await using var verification = await test.Database.OpenSessionAsync();
        Assert.Equal(1L, await verification.ScalarAsync<long>(
            "SELECT COUNT(*) FROM sqlite_master " +
            "WHERE type = 'table' AND name = 'for_sqlite_users'"));
        Assert.Equal(0L, await verification.ScalarAsync<long>(
            "SELECT COUNT(*) FROM sqlite_master " +
            "WHERE type = 'table' AND name = 'other_users'"));
    }

    private static MigrationRunner Runner(SnapDatabase database, params Migration[] migrations) =>
        new(
            database,
            migrations,
            SqliteMigrationDialect.Instance,
            new MigrationRunnerOptions
            {
                Locking = MigrationLocking.Disabled,
                RollbackPolicy = MigrationRollbackPolicy.Enabled
            });

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

    private sealed class ChangedCreateUsers : Migration
    {
        public override string Id => "001-users";

        public override void Up(MigrationPlan migration)
        {
            using var table = migration.CreateTable("users");
            table.Identity();
            table.String("name");
            table.String("changed_column");
        }

        public override void Down(MigrationPlan migration) => migration.DropTable("users");
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

    private sealed class AddEmailConditionally : Migration
    {
        public override string Id => "001-conditional-email";

        public override void Up(MigrationPlan migration)
        {
            using var table = migration.AlterTable("users");
            table.IfNotExists().String("email").Nullable();
        }
    }

    private sealed class ExerciseConditionalSchemaView : Migration
    {
        public override string Id => "001-conditional-view";

        public override void Up(MigrationPlan migration)
        {
            migration.ExecuteSql(
                "ALTER TABLE \"users\" ADD COLUMN \"raw_email\" TEXT");
            using var table = migration.AlterTable("users");
            table.IfNotExists().String("raw_email").Nullable();
            table.IfNotExists().String("transient").Nullable();
            table.IfExists().DropColumn("transient");
        }
    }

    private sealed class ExerciseConditionalIndexes : Migration
    {
        public override string Id => "001-conditional-indexes";

        public override void Up(MigrationPlan migration)
        {
            using var table = migration.AlterTable("users");
            table.IfNotExists().CreateIndex("IX_users_email", "email");
            table.IfNotExists().CreateIndex("IX_users_email", "email");
            table.IfExists().DropIndex("IX_users_email");
            table.IfExists().DropIndex("IX_users_email");
        }
    }

    private sealed class DropUsersIfExists : Migration
    {
        public override string Id => "001-drop-users-if-exists";

        public override void Up(MigrationPlan migration)
        {
            migration.DropTableIfExists("users");
            migration.DropTableIfExists("users");
        }
    }

    private sealed class ProviderAwareMigration : Migration
    {
        public override string Id => "001-provider-aware";

        public override void Up(MigrationPlan migration)
        {
            var tableName = migration.IsProvider(Provider.Sqlite)
                ? "for_sqlite_users"
                : "other_users";
            using var table = migration.CreateTable(tableName);
            table.Identity();
        }
    }

    private sealed class BlockingMigration : Migration
    {
        public override string Id => "001-blocking";

        public TaskCompletionSource Started { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Release { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public override async ValueTask UpAsync(MigrationContext context)
        {
            Started.TrySetResult();
            await Release.Task.WaitAsync(context.CancellationToken);
            using var table = context.Plan.CreateTable("blocking_completed");
            table.Identity();
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
