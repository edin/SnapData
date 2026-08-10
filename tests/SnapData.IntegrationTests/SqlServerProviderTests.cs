using Microsoft.Data.SqlClient;

namespace SnapData.IntegrationTests;

public sealed class SqlServerProviderTests : ProviderContractTests
{
    private const string ConnectionVariable = "SNAPDATA_SQLSERVER_CONNECTION";

    [SqlServerFact]
    public Task Entity_crud() => Entity_crud_and_generated_identity_round_trip();

    [SqlServerFact]
    public Task Borrowed_session_ownership() =>
        Borrowed_session_does_not_dispose_the_connection();

    [SqlServerFact]
    public Task Transactions() => Transaction_commit_and_rollback_are_observed();

    [SqlServerFact]
    public Task Advanced_queries() =>
        Projection_join_grouping_subquery_and_paging_execute();

    [SqlServerFact]
    public Task Common_types() => Common_provider_types_round_trip();

    protected override async Task<ProviderHarness> CreateHarnessAsync()
    {
        var connectionString = Environment.GetEnvironmentVariable(ConnectionVariable);
        var anchor = new SqlConnection(connectionString!);
        await anchor.OpenAsync();
        await using (var command = anchor.CreateCommand())
        {
            command.CommandText =
                """
                DROP TABLE IF EXISTS contract_orders;
                DROP TABLE IF EXISTS contract_values;
                DROP TABLE IF EXISTS contract_users;

                CREATE TABLE contract_users (
                    id BIGINT IDENTITY(1,1) PRIMARY KEY,
                    name NVARCHAR(200) NOT NULL,
                    active BIT NOT NULL
                );
                CREATE TABLE contract_orders (
                    id BIGINT IDENTITY(1,1) PRIMARY KEY,
                    user_id BIGINT NOT NULL,
                    total DECIMAL(18,2) NOT NULL
                );
                CREATE TABLE contract_values (
                    id UNIQUEIDENTIFIER PRIMARY KEY,
                    instant DATETIME2 NOT NULL,
                    amount DECIMAL(18,2) NOT NULL,
                    payload VARBINARY(MAX) NOT NULL,
                    optional_text NVARCHAR(200) NULL
                );
                """;
            await command.ExecuteNonQueryAsync();
        }

        var database = new SnapDatabase(
            SqlClientFactory.Instance,
            connectionString!,
            SqlServerQueryCompiler.Instance);
        return new SqlServerHarness(database, anchor);
    }

    private sealed class SqlServerHarness(
        SnapDatabase database,
        SqlConnection anchor) : ProviderHarness(database)
    {
        public override SqlConnection Connection => anchor;

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

internal sealed class SqlServerFactAttribute : FactAttribute
{
    public SqlServerFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(
            Environment.GetEnvironmentVariable("SNAPDATA_SQLSERVER_CONNECTION")))
        {
            Skip = "Set SNAPDATA_SQLSERVER_CONNECTION to run SQL Server integration tests.";
        }
    }
}
