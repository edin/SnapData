using System.Data;
using Microsoft.Data.Sqlite;

namespace SnapData.Tests;

public sealed class CommandDefinitionTests
{
    [Fact]
    public void Parameter_set_preserves_dictionary_api_and_rich_metadata()
    {
        var output = new CommandParameter(
            "total",
            direction: ParameterDirection.Output,
            dbType: DbType.Decimal,
            size: 12,
            precision: 10,
            scale: 2);
        var parameters = new ParameterSet()
            .Input("@name", "Edin", DbType.String, 100)
            .Add(output)
            .ReturnValue<int>();

        Assert.Equal(3, parameters.Count);
        Assert.Equal("Edin", parameters["name"]);
        Assert.Equal("Edin", parameters["@NAME"]);
        Assert.True(parameters.ContainsKey("return_value"));
        Assert.Equal(ParameterDirection.Output, output.Direction);
        Assert.Equal(DbType.Decimal, output.DbType);
        Assert.Equal(12, output.Size);
        Assert.Equal((byte)10, output.Precision);
        Assert.Equal((byte)2, output.Scale);
    }

    [Fact]
    public async Task Command_definition_executes_through_existing_session_pipeline()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var session = DbSession.Borrow(connection, SqliteQueryCompiler.Instance);
        var command = new CommandDefinition(
            "SELECT @value",
            new ParameterSet().Input("value", 42, DbType.Int32));

        var result = await session.ScalarAsync<int>(command);

        Assert.Equal(42, result);
        Assert.Equal(CommandType.Text, command.CommandType);
    }

    [Fact]
    public void Command_definition_can_describe_stored_procedure()
    {
        var parameters = new ParameterSet()
            .Input("search", "Ed")
            .Output<int>("total_count");
        var command = new CommandDefinition(
            "app.search_users",
            parameters,
            CommandType.StoredProcedure);

        Assert.Equal("app.search_users", command.CommandText);
        Assert.Equal(CommandType.StoredProcedure, command.CommandType);
        Assert.Equal(DbType.Int32, parameters.GetParameter("total_count").DbType);
    }
}
