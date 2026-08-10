using System.Data.Common;
using System.Data;

namespace SnapData.IntegrationTests;

public abstract class ProviderContractTests
{
    protected abstract Task<ProviderHarness> CreateHarnessAsync();

    public async Task Entity_crud_and_generated_identity_round_trip()
    {
        await using var harness = await CreateHarnessAsync();
        await using var session = await harness.Database.OpenSessionAsync();
        var user = new ContractUser { Name = "Edin", Active = true };

        Assert.Equal(1, await session.InsertAsync(user));
        if (user.Id == 0)
        {
            user.Id = (await session.From<ContractUser>()
                .Where(candidate => candidate.Name == user.Name)
                .SingleAsync()).Id;
        }

        Assert.True(user.Id > 0);

        user.Name = "Updated";
        Assert.Equal(1, await session.UpdateAsync(user));
        var stored = await session.From<ContractUser>()
            .Where(candidate => candidate.Id == user.Id)
            .SingleAsync();
        Assert.Equal("Updated", stored.Name);

        Assert.Equal(1, await session.DeleteAsync(user));
        Assert.False(await session.From<ContractUser>().AnyAsync());
    }

    public async Task Borrowed_session_does_not_dispose_the_connection()
    {
        await using var harness = await CreateHarnessAsync();
        await using (var session = harness.Database.BorrowSession(harness.Connection))
        {
            Assert.Equal(0, await session.From<ContractUser>().CountAsync());
        }

        Assert.Equal(ConnectionState.Open, harness.Connection.State);
    }

    public async Task Transaction_commit_and_rollback_are_observed()
    {
        await using var harness = await CreateHarnessAsync();
        await using var session = await harness.Database.OpenSessionAsync();

        await using (var transaction = await session.BeginTransactionAsync())
        {
            await transaction.InsertAsync(new ContractUser { Name = "Rolled back", Active = true });
            await transaction.RollbackAsync();
        }

        Assert.Equal(0, await session.From<ContractUser>().CountAsync());

        await using (var transaction = await session.BeginTransactionAsync())
        {
            await transaction.InsertAsync(new ContractUser { Name = "Committed", Active = true });
            await transaction.CommitAsync();
        }

        Assert.Equal(1, await session.From<ContractUser>().CountAsync());
    }

    public async Task Projection_join_grouping_subquery_and_paging_execute()
    {
        await using var harness = await CreateHarnessAsync();
        await using var session = await harness.Database.OpenSessionAsync();
        var edin = new ContractUser { Name = "Edin", Active = true };
        var guest = new ContractUser { Name = "Guest", Active = true };
        var idle = new ContractUser { Name = "Idle", Active = false };
        await InsertUserAsync(session, edin);
        await InsertUserAsync(session, guest);
        await InsertUserAsync(session, idle);
        await session.ExecuteAsync(
            "INSERT INTO contract_orders (user_id, total) VALUES (@userId, @total)",
            new { userId = edin.Id, total = 20m });
        await session.ExecuteAsync(
            "INSERT INTO contract_orders (user_id, total) VALUES (@userId, @total)",
            new { userId = edin.Id, total = 30m });
        await session.ExecuteAsync(
            "INSERT INTO contract_orders (user_id, total) VALUES (@userId, @total)",
            new { userId = guest.Id, total = 5m });

        var qualifyingUsers = Sql.From("contract_orders o")
            .Select("o.user_id")
            .GroupBy("o.user_id")
            .Having(Sql.Sum("o.total") > 10);
        var page = await session
            .From<UserOrderSummary>("contract_users u")
            .Join("contract_orders o ON o.user_id = u.id")
            .Select(
                Sql.Col("u.id").As("UserId"),
                Sql.Col("u.name").As("Name"),
                Sql.Sum("o.total").As("Total"))
            .Where(Exp.Col("u.id").In(qualifyingUsers))
            .GroupBy("u.id", "u.name")
            .OrderBy("u.id")
            .PageAsync(1, 10);

        var row = Assert.Single(page.Items);
        Assert.Equal(new UserOrderSummary(edin.Id, "Edin", 50), row);
        Assert.Equal(1, page.TotalCount);
    }

    public async Task Common_provider_types_round_trip()
    {
        await using var harness = await CreateHarnessAsync();
        await using var session = await harness.Database.OpenSessionAsync();
        var id = Guid.NewGuid();
        var instant = new DateTime(2026, 8, 10, 12, 30, 0, DateTimeKind.Utc);
        var payload = new byte[] { 1, 2, 3, 4 };
        await session.ExecuteAsync(
            """
            INSERT INTO contract_values (id, instant, amount, payload, optional_text)
            VALUES (@id, @instant, @amount, @payload, @optionalText)
            """,
            new { id, instant, amount = 12.50m, payload, optionalText = (string?)null });

        var value = await session.From<ContractValue>().SingleAsync();

        Assert.Equal(id, value.Id);
        Assert.Equal(instant, value.Instant);
        Assert.Equal(12.50m, value.Amount);
        Assert.Equal(payload, value.Payload);
        Assert.Null(value.OptionalText);
    }

    public abstract class ProviderHarness : IAsyncDisposable
    {
        protected ProviderHarness(SnapDatabase database)
        {
            Database = database;
        }

        public SnapDatabase Database { get; }

        public abstract DbConnection Connection { get; }

        public abstract ValueTask DisposeAsync();
    }

    [Table("contract_users")]
    protected sealed class ContractUser
    {
        [Key]
        [Generated(GeneratedKind.Identity)]
        [Column("id")]
        public long Id { get; set; }

        [Column("name")]
        public required string Name { get; set; }

        [Column("active")]
        public bool Active { get; set; }
    }

    [Table("contract_values")]
    protected sealed class ContractValue
    {
        [Key]
        [Column("id")]
        public Guid Id { get; init; }

        [Column("instant")]
        public DateTime Instant { get; init; }

        [Column("amount")]
        public decimal Amount { get; init; }

        [Column("payload")]
        public required byte[] Payload { get; init; }

        [Column("optional_text")]
        public string? OptionalText { get; init; }
    }

    protected sealed record UserOrderSummary(long UserId, string Name, decimal Total);

    private static async Task InsertUserAsync(
        IDbExecutor executor,
        ContractUser user)
    {
        await executor.InsertAsync(user);
        if (user.Id == 0)
        {
            user.Id = (await executor.From<ContractUser>()
                .Where(candidate => candidate.Name == user.Name)
                .SingleAsync()).Id;
        }
    }
}
