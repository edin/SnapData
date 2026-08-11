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

    [SqlServerFact]
    public async Task Typed_stored_procedure_parameters_and_result_set()
    {
        await using var harness = await CreateHarnessAsync();
        await using var session = await harness.Database.OpenSessionAsync();
        await session.InsertAsync(new ContractUser { Name = "Edin", Active = true });
        await session.InsertAsync(new ContractUser { Name = "Guest", Active = false });
        var procedure = new SearchUsers
        {
            Search = "Ed",
            State = 4
        };

        var result = await session.Query(procedure);

        var user = Assert.Single(result.Items);
        Assert.Equal("Edin", user.Name);
        Assert.True(user.Active);
        Assert.Equal(1, procedure.TotalCount);
        Assert.Equal(5, procedure.State);
        Assert.Equal(7, procedure.ReturnCode);
    }

    [SqlServerFact]
    public async Task Selectable_stored_procedure_returns_typed_rows()
    {
        await using var harness = await CreateHarnessAsync();
        await using (var command = harness.Connection.CreateCommand())
        {
            command.CommandText =
                """
                CREATE OR ALTER PROCEDURE SNAP_ECHO @INPUT_VALUE INT
                AS
                BEGIN
                    SET NOCOUNT ON;
                    SELECT @INPUT_VALUE + 1 AS OUTPUT_VALUE;
                END
                """;
            await command.ExecuteNonQueryAsync();
        }

        try
        {
            await using var session = await harness.Database.OpenSessionAsync();
            var result = await session.Query(new EchoProcedure { Value = 41 });
            Assert.Equal(42, Assert.Single(result.Items).Value);
        }
        finally
        {
            await using var command = harness.Connection.CreateCommand();
            command.CommandText = "DROP PROCEDURE SNAP_ECHO";
            await command.ExecuteNonQueryAsync();
        }
    }

    protected override async Task<ProviderHarness> CreateHarnessAsync()
    {
        var connectionString = Environment.GetEnvironmentVariable(ConnectionVariable);
        var anchor = new SqlConnection(connectionString!);
        await anchor.OpenAsync();
        await using (var command = anchor.CreateCommand())
        {
            command.CommandText =
                """
                DROP PROCEDURE IF EXISTS dbo.snapdata_search_users;
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

                EXEC(N'
                CREATE PROCEDURE dbo.snapdata_search_users
                    @search NVARCHAR(200),
                    @state INT OUTPUT,
                    @total_count INT OUTPUT
                AS
                BEGIN
                    SET NOCOUNT ON;
                    SELECT id AS Id, name AS Name, active AS Active
                    FROM contract_users
                    WHERE name LIKE @search + N''%'';
                    SET @total_count = @@ROWCOUNT;
                    SET @state = @state + 1;
                    RETURN 7;
                END');
                """;
            await command.ExecuteNonQueryAsync();
        }

        var database = new SnapDatabase(
            SqlClientFactory.Instance,
            connectionString!,
            SqlServerQueryCompiler.Instance);
        return new SqlServerHarness(database, anchor);
    }

    [StoredProcedure("SNAP_ECHO")]
    private sealed class EchoProcedure : IStoredProc<Result<EchoRow>>
    {
        [Input("INPUT_VALUE")]
        public int Value { get; init; }
    }

    private sealed record EchoRow([property: Column("OUTPUT_VALUE")] int Value);

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
                    DROP PROCEDURE IF EXISTS dbo.snapdata_search_users;
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

    [StoredProcedure("dbo.snapdata_search_users")]
    private sealed class SearchUsers : IStoredProc<Result<SearchUser>>
    {
        [Input("search", Size = 200)]
        public string Search { get; init; } = string.Empty;

        [InputOutput("state")]
        public int State { get; set; }

        [Output("total_count")]
        public int TotalCount { get; set; }

        [ReturnValue]
        public int ReturnCode { get; set; }
    }

    private sealed record SearchUser(long Id, string Name, bool Active);
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
