using FirebirdSql.Data.FirebirdClient;
using Microsoft.Data.SqlClient;
using MySqlConnector;
using Npgsql;
using SnapData.Migrations;
using SnapData.Schema;

namespace SnapData.IntegrationTests;

public sealed class MigrationCompilerIntegrationTests
{
    [SqlServerFact]
    public Task SqlServer_ddl_executes() => VerifyAsync(
        "SNAPDATA_SQLSERVER_CONNECTION",
        SqlClientFactory.Instance,
        SqlServerQueryCompiler.Instance,
        new SqlServerMigrationCompiler());

    [PostgresFact]
    public Task Postgres_ddl_executes() => VerifyAsync(
        "SNAPDATA_POSTGRES_CONNECTION",
        NpgsqlFactory.Instance,
        PostgresQueryCompiler.Instance,
        new PostgresMigrationCompiler());

    [MySqlFact]
    public Task MySql_ddl_executes() => VerifyAsync(
        "SNAPDATA_MYSQL_CONNECTION",
        MySqlConnectorFactory.Instance,
        MySqlQueryCompiler.Instance,
        new MySqlMigrationCompiler());

    [FirebirdFact]
    public Task Firebird_ddl_executes() => VerifyAsync(
        "SNAPDATA_FIREBIRD_CONNECTION",
        FirebirdClientFactory.Instance,
        FirebirdQueryCompiler.Instance,
        new FirebirdMigrationCompiler());

    [SqlServerFact]
    public Task SqlServer_runner_executes() => VerifyRunnerAsync(
        "SNAPDATA_SQLSERVER_CONNECTION", SqlClientFactory.Instance,
        SqlServerQueryCompiler.Instance, SqlServerMigrationDialect.Instance);

    [PostgresFact]
    public Task Postgres_runner_executes() => VerifyRunnerAsync(
        "SNAPDATA_POSTGRES_CONNECTION", NpgsqlFactory.Instance,
        PostgresQueryCompiler.Instance, PostgresMigrationDialect.Instance);

    [MySqlFact]
    public Task MySql_runner_executes() => VerifyRunnerAsync(
        "SNAPDATA_MYSQL_CONNECTION", MySqlConnectorFactory.Instance,
        MySqlQueryCompiler.Instance, MySqlMigrationDialect.Instance);

    [FirebirdFact]
    public Task Firebird_runner_executes() => VerifyRunnerAsync(
        "SNAPDATA_FIREBIRD_CONNECTION", FirebirdClientFactory.Instance,
        FirebirdQueryCompiler.Instance, FirebirdMigrationDialect.Instance);

    private static async Task VerifyAsync(
        string connectionVariable,
        System.Data.Common.DbProviderFactory factory,
        IQueryCompiler queryCompiler,
        IMigrationCompiler migrationCompiler)
    {
        var tableName = $"mig_{Guid.NewGuid():N}"[..16];
        var plan = new MigrationPlan();
        using (var table = plan.CreateTable(tableName))
        {
            table.Identity();
            table.String("name", 80);
            table.Boolean("active").Default(true);
            table.Index($"ix_{tableName}_name", "name");
        }

        var database = new SnapDatabase(
            factory,
            Environment.GetEnvironmentVariable(connectionVariable)!,
            queryCompiler);
        await using var session = await database.OpenSessionAsync();
        var tableCreated = false;
        try
        {
            var script = migrationCompiler.Compile("smoke", MigrationDirection.Up, plan);
            for (var index = 0; index < script.Statements.Count; index++)
            {
                await session.ExecuteAsync(script.Statements[index].Sql);
                tableCreated |= index == 0;
            }
        }
        finally
        {
            if (tableCreated)
            {
                var drop = new MigrationPlan();
                drop.DropTable(tableName);
                var script = migrationCompiler.Compile("smoke", MigrationDirection.Down, drop);
                foreach (var statement in script.Statements)
                {
                    await session.ExecuteAsync(statement.Sql);
                }
            }
        }
    }

    private static async Task VerifyRunnerAsync(
        string connectionVariable,
        System.Data.Common.DbProviderFactory factory,
        IQueryCompiler queryCompiler,
        IMigrationDialect dialect)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var tableName = $"run_{suffix}";
        var historyName = $"hist_{suffix}";
        var database = new SnapDatabase(
            factory,
            Environment.GetEnvironmentVariable(connectionVariable)!,
            queryCompiler);
        var migration = new SmokeMigration(tableName, $"migration_{suffix}");
        var runner = new MigrationRunner(
            database,
            new[] { migration },
            dialect,
            new MigrationRunnerOptions
            {
                HistoryTable = historyName,
                Locking = dialect.MigrationLock is null
                    ? MigrationLocking.Disabled
                    : MigrationLocking.Required
            });
        try
        {
            await runner.MigrateAsync();
            Assert.Equal(migration.Id, Assert.Single(await runner.GetHistoryAsync()).MigrationId);
            await runner.RollbackAsync();
            Assert.Empty(await runner.GetHistoryAsync());
        }
        finally
        {
            bool historyExists;
            await using (var inspection = await database.OpenSessionAsync())
            {
                historyExists = await dialect.CreateSchemaInspector(inspection)
                    .TableExistsAsync(new SchemaObjectName(historyName));
            }
            if (historyExists)
            {
                await using var cleanup = await database.OpenSessionAsync();
                await cleanup.ExecuteAsync($"DROP TABLE {dialect.QuoteTable(historyName)}");
            }
        }
    }

    private sealed class SmokeMigration(string tableName, string id) : Migration
    {
        public override string Id => id;

        public override void Up(MigrationPlan migration)
        {
            using var table = migration.CreateTable(tableName);
            table.Identity();
            table.String("name", 80);
        }

        public override void Down(MigrationPlan migration) => migration.DropTable(tableName);
    }
}
