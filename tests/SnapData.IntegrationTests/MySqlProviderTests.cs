using MySqlConnector;

namespace SnapData.IntegrationTests;

public sealed class MySqlProviderTests : ProviderContractTests
{
    private const string ConnectionVariable = "SNAPDATA_MYSQL_CONNECTION";

    [MySqlFact]
    public Task Entity_crud() => Entity_crud_and_generated_identity_round_trip();

    [MySqlFact]
    public Task Borrowed_session_ownership() =>
        Borrowed_session_does_not_dispose_the_connection();

    [MySqlFact]
    public Task Transactions() => Transaction_commit_and_rollback_are_observed();

    [MySqlFact]
    public Task Advanced_queries() =>
        Projection_join_grouping_subquery_and_paging_execute();

    [MySqlFact]
    public Task Common_types() => Common_provider_types_round_trip();

    [MySqlFact]
    public async Task Selectable_stored_procedure_returns_typed_rows()
    {
        await using var harness = await CreateHarnessAsync();
        await using (var drop = harness.Connection.CreateCommand())
        {
            drop.CommandText = "DROP PROCEDURE IF EXISTS SNAP_ECHO";
            await drop.ExecuteNonQueryAsync();
        }

        await using (var create = harness.Connection.CreateCommand())
        {
            create.CommandText =
                """
                CREATE PROCEDURE SNAP_ECHO(IN INPUT_VALUE INT)
                SELECT INPUT_VALUE + 1 AS OUTPUT_VALUE
                """;
            await create.ExecuteNonQueryAsync();
        }

        try
        {
            await using var session = await harness.Database.OpenSessionAsync();
            var result = await session.Query(new EchoProcedure { Value = 41 });
            Assert.Equal(42, Assert.Single(result.Items).Value);
        }
        finally
        {
            await using var drop = harness.Connection.CreateCommand();
            drop.CommandText = "DROP PROCEDURE SNAP_ECHO";
            await drop.ExecuteNonQueryAsync();
        }
    }

    protected override async Task<ProviderHarness> CreateHarnessAsync()
    {
        var connectionString = Environment.GetEnvironmentVariable(ConnectionVariable)!;
        var anchor = new MySqlConnection(connectionString);
        await anchor.OpenAsync();
        await using (var command = anchor.CreateCommand())
        {
            command.CommandText =
                """
                DROP TABLE IF EXISTS contract_orders;
                DROP TABLE IF EXISTS contract_values;
                DROP TABLE IF EXISTS contract_users;

                CREATE TABLE contract_users (
                    id BIGINT NOT NULL AUTO_INCREMENT PRIMARY KEY,
                    name VARCHAR(200) NOT NULL,
                    active BOOLEAN NOT NULL
                );
                CREATE TABLE contract_orders (
                    id BIGINT NOT NULL AUTO_INCREMENT PRIMARY KEY,
                    user_id BIGINT NOT NULL,
                    total DECIMAL(18,2) NOT NULL
                );
                CREATE TABLE contract_values (
                    id CHAR(36) PRIMARY KEY,
                    instant DATETIME(6) NOT NULL,
                    amount DECIMAL(18,2) NOT NULL,
                    payload LONGBLOB NOT NULL,
                    optional_text VARCHAR(200) NULL
                );
                """;
            await command.ExecuteNonQueryAsync();
        }

        var database = new SnapDatabase(
            MySqlConnectorFactory.Instance,
            connectionString,
            MySqlQueryCompiler.Instance);
        return new MySqlHarness(database, anchor);
    }

    [StoredProcedure("SNAP_ECHO")]
    private sealed class EchoProcedure : IStoredProc<Result<EchoRow>>
    {
        [Input("INPUT_VALUE")]
        public int Value { get; init; }
    }

    private sealed record EchoRow([property: Column("OUTPUT_VALUE")] int Value);

    private sealed class MySqlHarness(
        SnapDatabase database,
        MySqlConnection anchor) : ProviderHarness(database)
    {
        public override MySqlConnection Connection => anchor;

        public override async ValueTask DisposeAsync()
        {
            try
            {
                await using var command = anchor.CreateCommand();
                command.CommandText =
                    """
                    DROP TABLE IF EXISTS contract_orders;
                    DROP TABLE IF EXISTS contract_values;
                    DROP TABLE IF EXISTS contract_users;
                    """;
                await command.ExecuteNonQueryAsync();
            }
            finally
            {
                await anchor.DisposeAsync();
            }
        }
    }
}

internal sealed class MySqlFactAttribute : FactAttribute
{
    public MySqlFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(
            Environment.GetEnvironmentVariable("SNAPDATA_MYSQL_CONNECTION")))
        {
            Skip = "Set SNAPDATA_MYSQL_CONNECTION to run MySQL integration tests.";
        }
    }
}
