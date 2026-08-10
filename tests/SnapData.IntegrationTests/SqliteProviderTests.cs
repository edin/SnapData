using Microsoft.Data.Sqlite;

namespace SnapData.IntegrationTests;

public sealed class SqliteProviderTests : ProviderContractTests
{
    [Fact]
    public Task Entity_crud() => Entity_crud_and_generated_identity_round_trip();

    [Fact]
    public Task Borrowed_session_ownership() =>
        Borrowed_session_does_not_dispose_the_connection();

    [Fact]
    public Task Transactions() => Transaction_commit_and_rollback_are_observed();

    [Fact]
    public Task Advanced_queries() =>
        Projection_join_grouping_subquery_and_paging_execute();

    [Fact]
    public Task Common_types() => Common_provider_types_round_trip();

    protected override async Task<ProviderHarness> CreateHarnessAsync()
    {
        var connectionString =
            $"Data Source=snapdata-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
        var anchor = new SqliteConnection(connectionString);
        await anchor.OpenAsync();
        await using (var command = anchor.CreateCommand())
        {
            command.CommandText =
                """
                CREATE TABLE contract_users (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    name TEXT NOT NULL,
                    active INTEGER NOT NULL
                );
                CREATE TABLE contract_orders (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    user_id INTEGER NOT NULL,
                    total NUMERIC NOT NULL
                );
                CREATE TABLE contract_values (
                    id TEXT PRIMARY KEY,
                    instant TEXT NOT NULL,
                    amount NUMERIC NOT NULL,
                    payload BLOB NOT NULL,
                    optional_text TEXT NULL
                );
                """;
            await command.ExecuteNonQueryAsync();
        }

        var database = new SnapDatabase(
            SqliteFactory.Instance,
            connectionString,
            SqliteQueryCompiler.Instance);
        return new SqliteHarness(database, anchor);
    }

    private sealed class SqliteHarness(
        SnapDatabase database,
        SqliteConnection anchor) : ProviderHarness(database)
    {
        public override SqliteConnection Connection => anchor;

        public override async ValueTask DisposeAsync() => await anchor.DisposeAsync();
    }
}
