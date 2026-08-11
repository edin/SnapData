using Microsoft.Data.Sqlite;
using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;

namespace SnapData.Tests;

public sealed class DatabaseAdapterTests
{
    [Fact]
    public void Borrowing_is_exposed_through_configured_database_instance()
    {
        Assert.NotNull(typeof(SnapDatabase).GetMethod(
            nameof(SnapDatabase.Borrow),
            [typeof(System.Data.Common.DbConnection)]));
        Assert.Null(typeof(DbSession).GetMethod(
            "Borrow",
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static));
    }

    [Fact]
    public void Borrow_rejects_connection_from_another_provider()
    {
        var database = new SnapDatabase(
            SqliteFactory.Instance,
            "Data Source=:memory:",
            SqliteQueryCompiler.Instance);

        var exception = Assert.Throws<ArgumentException>(() =>
            database.Borrow(new IncompatibleConnection()));

        Assert.Equal("connection", exception.ParamName);
        Assert.Contains(nameof(IncompatibleConnection), exception.Message);
    }

    [Fact]
    public async Task Opened_session_uses_its_adapters_query_compiler()
    {
        var compiler = new RecordingCompiler();
        var database = new SnapDatabase(new DatabaseAdapter(
            SqliteFactory.Instance,
            "Data Source=:memory:",
            compiler));

        await using var session = await database.OpenSessionAsync();
        await session.ExecuteAsync("CREATE TABLE values_table (value INTEGER NOT NULL)");
        var affected = await session.ExecuteAsync(
            Sql.InsertInto("values_table").Value("value", 1));

        Assert.Equal(1, affected);
        Assert.Single(compiler.CompiledBuilders);
        Assert.IsType<InsertQueryBuilder>(compiler.CompiledBuilders[0]);
    }

    [Fact]
    public async Task Borrowed_session_and_transaction_keep_the_adapter_compiler()
    {
        var compiler = new RecordingCompiler();
        var database = new SnapDatabase(new DatabaseAdapter(
            SqliteFactory.Instance,
            "Data Source=:memory:",
            compiler));
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var session = database.Borrow(connection);
        await session.ExecuteAsync("CREATE TABLE values_table (value INTEGER NOT NULL)");

        await using (var transaction = await session.BeginTransactionAsync())
        {
            await transaction.ExecuteAsync(
                Sql.InsertInto("values_table").Value("value", 1));
            await transaction.CommitAsync();
        }

        Assert.Single(compiler.CompiledBuilders);
        Assert.Equal(System.Data.ConnectionState.Open, connection.State);
    }

    [Fact]
    public void Provider_compilers_apply_provider_rules()
    {
        var mysql = Sql.Select("users.id")
            .From("users")
            .Where(Exp.Col("active") == true)
            .Limit(10)
            .Offset(20)
            .Build(MySqlQueryCompiler.Instance);
        var postgres = Sql.InsertInto("users")
            .Value("name", "Edin")
            .Returning("id")
            .Build(PostgresQueryCompiler.Instance);
        var sqlServerFirst = Sql.From("users u")
            .Select("u.id")
            .OrderBy("u.id")
            .Limit(1)
            .Build(SqlServerQueryCompiler.Instance);
        var sqlServerPage = Sql.From("users u")
            .Select("u.id")
            .OrderBy("u.id")
            .Limit(10)
            .Offset(20)
            .Build(SqlServerQueryCompiler.Instance);
        var sqlServerInsert = Sql.InsertInto("users")
            .Value("name", "Edin")
            .Returning("id")
            .Build(SqlServerQueryCompiler.Instance);
        var firebirdPage = Sql.From("users u")
            .Select("u.id")
            .OrderBy("u.id")
            .Limit(10)
            .Offset(20)
            .Build(FirebirdQueryCompiler.Instance);
        var firebirdGenerated = FirebirdQueryCompiler.Instance.CompileGeneratedInsert(
            Sql.InsertInto("users").Value("name", "Edin"),
            [new ColumnReference("id")]);
        var mysqlGenerated = MySqlQueryCompiler.Instance.CompileGeneratedInsert(
            Sql.InsertInto("users").Value("name", "Edin"),
            [new ColumnReference("id")]);

        Assert.Equal(
            "SELECT `users`.`id` FROM `users` WHERE `active` = @p1 LIMIT 10 OFFSET 20",
            mysql.Text);
        Assert.Equal(
            "INSERT INTO \"users\" (\"name\") VALUES (@p1) RETURNING \"id\"",
            postgres.Text);
        Assert.Equal(
            "SELECT TOP (1) [u].[id] FROM [users] AS [u] ORDER BY [u].[id] ASC",
            sqlServerFirst.Text);
        Assert.Equal(
            "SELECT [u].[id] FROM [users] AS [u] ORDER BY [u].[id] ASC OFFSET 20 ROWS FETCH NEXT 10 ROWS ONLY",
            sqlServerPage.Text);
        Assert.Equal(
            "INSERT INTO [users] ([name]) OUTPUT INSERTED.[id] VALUES (@p1)",
            sqlServerInsert.Text);
        Assert.Equal(
            "SELECT \"U\".\"ID\" FROM \"USERS\" AS \"U\" ORDER BY \"U\".\"ID\" ASC OFFSET 20 ROWS FETCH NEXT 10 ROWS ONLY",
            firebirdPage.Text);
        Assert.Equal(
            "INSERT INTO \"USERS\" (\"NAME\") VALUES (@p1) RETURNING \"ID\"",
            firebirdGenerated.Command.Text);
        Assert.Null(firebirdGenerated.FollowUpQuery);
        Assert.Equal("INSERT INTO `users` (`name`) VALUES (@p1)", mysqlGenerated.Command.Text);
        Assert.Equal("SELECT LAST_INSERT_ID()", mysqlGenerated.FollowUpQuery!.Text);
        Assert.Throws<NotSupportedException>(() =>
            Sql.InsertInto("users")
                .Value("name", "Edin")
                .Returning("id")
                .Build(MySqlQueryCompiler.Instance));
    }

    private sealed class RecordingCompiler : IQueryCompiler
    {
        internal List<ISqlQueryBuilder> CompiledBuilders { get; } = [];

        public SqlQuery Compile(ISqlQueryBuilder query)
        {
            CompiledBuilders.Add(query);
            return SqlDialect.Ansi.Compile(query);
        }
    }

    private sealed class IncompatibleConnection : DbConnection
    {
        [AllowNull]
        public override string ConnectionString { get; set; } = string.Empty;

        public override string Database => string.Empty;

        public override string DataSource => string.Empty;

        public override string ServerVersion => string.Empty;

        public override ConnectionState State => ConnectionState.Closed;

        public override void ChangeDatabase(string databaseName) =>
            throw new NotSupportedException();

        public override void Close()
        {
        }

        public override void Open() => throw new NotSupportedException();

        protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel) =>
            throw new NotSupportedException();

        protected override DbCommand CreateDbCommand() =>
            throw new NotSupportedException();
    }
}
