using Microsoft.Data.Sqlite;

namespace SnapData.Tests;

public sealed class CommandObserverTests
{
    [Fact]
    public async Task Database_observer_receives_commands_results_and_parameters()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var observer = new RecordingObserver();
        var database = new SnapDatabase(
            new DatabaseAdapter(
                SqliteFactory.Instance,
                "Data Source=:memory:",
                SqliteQueryCompiler.Instance),
            commandObserver: observer);
        await using var session = database.Borrow(connection);
        await session.ExecuteAsync("CREATE TABLE users (id INTEGER, name TEXT)");
        observer.Clear();

        var affected = await session.ExecuteAsync(
            "INSERT INTO users VALUES (@id, @name)",
            new { id = 1, name = "Edin" });
        var users = await session.QueryAsync<User>("SELECT id, name FROM users");

        Assert.Equal(1, affected);
        Assert.Single(users);
        Assert.Equal(2, observer.ExecutingContexts.Count);
        Assert.Equal("Edin", observer.ExecutingContexts[0].Command.GetParameter("name").Value);
        Assert.Equal(1, observer.ExecutedContexts[0].AffectedRows);
        Assert.Equal(CommandExecutionKind.NonQuery, observer.ExecutedContexts[0].Kind);
        Assert.Equal(CommandExecutionKind.Query, observer.ExecutedContexts[1].Kind);
        Assert.Equal(nameof(SqliteConnection), observer.ExecutedContexts[1].ProviderName);
        Assert.False(observer.ExecutedContexts[1].HasTransaction);
        Assert.Equal(1, observer.ExecutedContexts[1].ResultCount);
        Assert.All(observer.ExecutedContexts, context => Assert.True(context.Duration >= TimeSpan.Zero));
    }

    [Fact]
    public async Task Transaction_inherits_observer_and_reports_transaction_state()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var observer = new RecordingObserver();
        var database = new SnapDatabase(
            new DatabaseAdapter(
                SqliteFactory.Instance,
                "Data Source=:memory:",
                SqliteQueryCompiler.Instance),
            commandObserver: observer);
        await using var session = database.Borrow(connection);
        await using var transaction = await session.BeginTransactionAsync();

        _ = await transaction.ScalarAsync<int>("SELECT 1");

        Assert.True(Assert.Single(observer.ExecutingContexts).HasTransaction);
    }

    [Fact]
    public async Task Failed_observer_cannot_replace_original_command_exception()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var observer = new RecordingObserver { ThrowWhenFailed = true };
        await using var session = DbSession.Borrow(
            connection,
            SqliteQueryCompiler.Instance,
            commandObserver: observer);

        var exception = await Assert.ThrowsAsync<SqliteException>(() =>
            session.ExecuteAsync("INSERT INTO missing_table VALUES (1)"));

        Assert.Contains("missing_table", exception.Message);
        Assert.IsType<SqliteException>(Assert.Single(observer.FailedContexts).Exception);
    }

    [Fact]
    public async Task Retained_context_keeps_parameter_values_from_its_execution()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var observer = new RecordingObserver();
        var command = Command.Text("SELECT @value").Input("value", 1);
        await using var session = DbSession.Borrow(
            connection,
            SqliteQueryCompiler.Instance,
            commandObserver: observer);

        _ = await session.ScalarAsync<int>(command);
        command.SetParameter("value", 2);
        _ = await session.ScalarAsync<int>(command);

        Assert.Equal(1, observer.ExecutingContexts[0].Command.GetParameter("value").Value);
        Assert.Equal(2, observer.ExecutingContexts[1].Command.GetParameter("value").Value);
    }

    private sealed record User(long Id, string Name);

    private sealed class RecordingObserver : CommandObserver
    {
        internal List<CommandExecutingContext> ExecutingContexts { get; } = [];

        internal List<CommandExecutedContext> ExecutedContexts { get; } = [];

        internal List<CommandFailedContext> FailedContexts { get; } = [];

        internal bool ThrowWhenFailed { get; init; }

        public override void Executing(CommandExecutingContext context) =>
            ExecutingContexts.Add(context);

        public override void Executed(CommandExecutedContext context) =>
            ExecutedContexts.Add(context);

        public override void Failed(CommandFailedContext context)
        {
            FailedContexts.Add(context);
            if (ThrowWhenFailed)
            {
                throw new InvalidOperationException("Observer failure");
            }
        }

        internal void Clear()
        {
            ExecutingContexts.Clear();
            ExecutedContexts.Clear();
            FailedContexts.Clear();
        }
    }
}
