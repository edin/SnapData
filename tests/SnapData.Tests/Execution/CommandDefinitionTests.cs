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
        var command = Command.StoredProcedure("app.search_users", parameters);

        Assert.Equal("app.search_users", command.CommandText);
        Assert.Equal(CommandType.StoredProcedure, command.CommandType);
        Assert.Same(parameters, command.Parameters);
        Assert.Equal(DbType.Int32, parameters.GetParameter("total_count").DbType);
    }

    [Fact]
    public void Stored_procedure_command_does_not_require_parameters()
    {
        var command = Command.StoredProcedure("app.refresh_users");

        Assert.Equal("app.refresh_users", command.CommandText);
        Assert.Equal(CommandType.StoredProcedure, command.CommandType);
        Assert.Empty(command.Parameters);
    }

    [Fact]
    public void Command_exposes_fluent_inputs_and_typed_output_references()
    {
        var command = Command
            .StoredProcedure("app.search_users")
            .Input("search", "Ed");
        var total = command.Output<int>("total_count");
        var returnValue = command.ReturnValue<int>();

        command.Parameters.GetParameter("total_count").SetValue(12);
        command.Parameters.GetParameter("return_value").SetValue(0);

        Assert.Equal("Ed", command.Parameters["search"]);
        Assert.Equal(12, total.Value);
        Assert.Equal(0, returnValue.Value);
        Assert.Same(command.Parameters.GetParameter("total_count"), total.Parameter);
    }

    [Fact]
    public void Text_command_uses_text_command_type()
    {
        var command = Command.Text("SELECT @value").Input("value", 42);

        Assert.Equal(CommandType.Text, command.CommandType);
        Assert.Equal(42, command.Parameters["value"]);
    }

    [Fact]
    public void Existing_parameter_values_can_be_updated_without_losing_metadata()
    {
        var command = Command
            .Text("SELECT @value")
            .Input("value", 1, DbType.Int32, size: 4);

        command.SetParameter("value", 2);
        var value = command.Parameter<int>("value");
        value.Value = 3;

        Assert.Equal(3, command.Parameters.Get<int>("value"));
        Assert.Equal(DbType.Int32, value.Parameter.DbType);
        Assert.Equal(4, value.Parameter.Size);
    }

    [Fact]
    public void Typed_parameter_reference_requires_an_existing_parameter()
    {
        var command = Command.Text("SELECT 1");

        Assert.Throws<KeyNotFoundException>(() => command.Parameter<int>("missing"));
        Assert.Throws<KeyNotFoundException>(() => command.SetParameter("missing", 1));
    }
}
