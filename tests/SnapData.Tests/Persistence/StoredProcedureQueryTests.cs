using System.Collections;
using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;

namespace SnapData.Tests;

public sealed class StoredProcedureQueryTests
{
    [Fact]
    public async Task Query_infers_result_type_and_uses_existing_row_mapper()
    {
        await using var connection = new ProcedureConnection(CreateOrders());
        await using var session = DbSession.Borrow(connection);
        var request = new GetOrders { A = 1, B = 2 };

        Task<Result<OrderDto>> pending = session.Query(request);
        var result = await pending;

        Assert.Equal(2, result.Items.Count);
        Assert.Equal(new OrderDto { Id = 10, Description = "First" }, result.Items[0]);
        Assert.Equal(CommandType.StoredProcedure, connection.LastCommand!.CommandType);
        Assert.Equal("dbo.GetOrders", connection.LastCommand.CommandText);
        Assert.Equal(1, connection.LastCommand.Parameters["@A"].Value);
        Assert.Equal(2, connection.LastCommand.Parameters["@B"].Value);
    }

    [Fact]
    public async Task Transaction_session_exposes_same_typed_procedure_query_api()
    {
        await using var connection = new ProcedureConnection(CreateOrders());
        await using var session = DbSession.Borrow(connection);
        await using var transaction = await session.BeginTransactionAsync();

        Result<OrderDto> result = await transaction.Query(
            new GetOrders { A = 3, B = 4 });

        Assert.Equal(2, result.Items.Count);
        Assert.NotNull(connection.LastCommand!.Transaction);
    }

    [Fact]
    public async Task Output_property_is_bound_as_output_and_updated_after_execution()
    {
        await using var connection = new ProcedureConnection(CreateOrders());
        await using var session = DbSession.Borrow(connection);
        var request = new SearchOrders { Search = "Ed" };

        _ = await session.Query(request);

        Assert.Equal(42, request.TotalCount);
        Assert.Equal("Ed", connection.LastCommand!.Parameters["@Search"].Value);
        Assert.Equal(
            ParameterDirection.Output,
            connection.LastCommand.Parameters["@total_count"].Direction);
    }

    [Fact]
    public async Task Typed_request_supports_all_parameter_directions_and_metadata()
    {
        await using var connection = new ProcedureConnection(CreateOrders());
        await using var session = DbSession.Borrow(connection);
        var request = new CompleteSearchOrders
        {
            Search = "Ed",
            State = 4
        };

        _ = await session.Query(request);

        Assert.Equal(5, request.State);
        Assert.Equal(42, request.TotalCount);
        Assert.Equal(7, request.ReturnCode);
        Assert.Equal("Ed", connection.LastCommand!.Parameters["@search_text"].Value);
        Assert.Equal(50, connection.LastCommand.Parameters["@search_text"].Size);
        Assert.Equal(ParameterDirection.InputOutput, connection.LastCommand.Parameters["@state"].Direction);
        Assert.Equal(ParameterDirection.ReturnValue, connection.LastCommand.Parameters["@return_value"].Direction);
    }

    [Fact]
    public async Task Request_requires_stored_procedure_attribute()
    {
        await using var connection = new ProcedureConnection(CreateOrders());
        await using var session = DbSession.Borrow(connection);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            session.Query(new MissingAttribute()));

        Assert.Contains(nameof(StoredProcedureAttribute), exception.Message);
    }

    [Fact]
    public async Task Custom_holder_maps_multiple_result_sets_by_attribute_index()
    {
        await using var connection = new ProcedureConnection(CreateOrders(), CreateCustomers());
        await using var session = DbSession.Borrow(connection);

        OrderData result = await session.Query(new GetOrderData { CustomerId = 7 });

        Assert.Equal([10, 20], result.Orders.Select(order => order.Id));
        Assert.Equal([7, 8], result.Customers.Select(customer => customer.Id));
    }

    [Fact]
    public async Task Custom_holder_without_attributes_uses_property_declaration_order()
    {
        await using var connection = new ProcedureConnection(CreateOrders(), CreateCustomers());
        await using var session = DbSession.Borrow(connection);

        ConventionOrderData result = await session.Query(new GetConventionOrderData());

        Assert.Equal("First", result.Orders[0].Description);
        Assert.Equal("Edin", result.Customers[0].Name);
    }

    private static DataTable CreateOrders()
    {
        var table = new DataTable();
        table.Columns.Add("Id", typeof(int));
        table.Columns.Add("Description", typeof(string));
        table.Rows.Add(10, "First");
        table.Rows.Add(20, "Second");
        return table;
    }

    private static DataTable CreateCustomers()
    {
        var table = new DataTable();
        table.Columns.Add("Id", typeof(int));
        table.Columns.Add("Name", typeof(string));
        table.Rows.Add(7, "Edin");
        table.Rows.Add(8, "Sara");
        return table;
    }

    [StoredProcedure("dbo.GetOrders")]
    private sealed class GetOrders : IStoredProc<Result<OrderDto>>
    {
        public int A { get; init; }

        public int B { get; init; }
    }

    private sealed class MissingAttribute : IStoredProc<Result<OrderDto>>;

    [StoredProcedure("dbo.SearchOrders")]
    private sealed class SearchOrders : IStoredProc<Result<OrderDto>>
    {
        public string Search { get; init; } = string.Empty;

        [Output("total_count")]
        public int TotalCount { get; set; }
    }

    [StoredProcedure("dbo.CompleteSearchOrders")]
    private sealed class CompleteSearchOrders : IStoredProc<Result<OrderDto>>
    {
        [Input("search_text", Size = 50)]
        public string Search { get; init; } = string.Empty;

        [InputOutput("state")]
        public int State { get; set; }

        [Output("total_count")]
        public int TotalCount { get; set; }

        [ReturnValue]
        public int ReturnCode { get; set; }
    }

    [StoredProcedure("dbo.GetOrderData")]
    private sealed class GetOrderData : IStoredProc<OrderData>
    {
        public int CustomerId { get; init; }
    }

    [StoredProcedure("dbo.GetConventionOrderData")]
    private sealed class GetConventionOrderData : IStoredProc<ConventionOrderData>;

    private sealed class OrderData
    {
        [ResultSet(1)]
        public List<CustomerDto> Customers { get; init; } = [];

        [ResultSet(0)]
        public List<OrderDto> Orders { get; init; } = [];
    }

    private sealed class ConventionOrderData
    {
        public List<OrderDto> Orders { get; init; } = [];

        public List<CustomerDto> Customers { get; init; } = [];
    }

    private sealed class CustomerDto
    {
        public int Id { get; init; }

        public string Name { get; init; } = string.Empty;
    }

    private sealed class OrderDto
    {
        public int Id { get; init; }

        public string Description { get; init; } = string.Empty;

        public override bool Equals(object? obj) =>
            obj is OrderDto other && Id == other.Id && Description == other.Description;

        public override int GetHashCode() => HashCode.Combine(Id, Description);
    }

    private sealed class ProcedureConnection(params DataTable[] results) : DbConnection
    {
        private ConnectionState _state = ConnectionState.Open;

        internal ProcedureCommand? LastCommand { get; private set; }

        [AllowNull]
        public override string ConnectionString { get; set; } = string.Empty;

        public override string Database => "StoredProcedureTests";

        public override string DataSource => "InMemory";

        public override string ServerVersion => "1.0";

        public override ConnectionState State => _state;

        public override void ChangeDatabase(string databaseName)
        {
        }

        public override void Close() => _state = ConnectionState.Closed;

        public override void Open() => _state = ConnectionState.Open;

        protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel) =>
            new ProcedureTransaction(this, isolationLevel);

        protected override DbCommand CreateDbCommand()
        {
            LastCommand = new ProcedureCommand(this, results);
            return LastCommand;
        }
    }

    private sealed class ProcedureTransaction(
        DbConnection connection,
        IsolationLevel isolationLevel) : DbTransaction
    {
        public override IsolationLevel IsolationLevel => isolationLevel;

        protected override DbConnection DbConnection => connection;

        public override void Commit()
        {
        }

        public override void Rollback()
        {
        }
    }

    private sealed class ProcedureCommand(
        DbConnection connection,
        DataTable[] results) : DbCommand
    {
        private readonly ProcedureParameterCollection _parameters = new();

        [AllowNull]
        public override string CommandText { get; set; } = string.Empty;

        public override int CommandTimeout { get; set; }

        public override CommandType CommandType { get; set; }

        public override bool DesignTimeVisible { get; set; }

        public override UpdateRowSource UpdatedRowSource { get; set; }

        [AllowNull]
        protected override DbConnection DbConnection { get; set; } = connection;

        protected override DbParameterCollection DbParameterCollection => _parameters;

        protected override DbTransaction? DbTransaction { get; set; }

        public override void Cancel()
        {
        }

        public override int ExecuteNonQuery() => results.Sum(table => table.Rows.Count);

        public override object? ExecuteScalar() => null;

        public override void Prepare()
        {
        }

        protected override DbParameter CreateDbParameter() => new ProcedureParameter();

        protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior)
        {
            foreach (DbParameter parameter in _parameters)
            {
                if (parameter.Direction == ParameterDirection.Output)
                {
                    parameter.Value = 42;
                }
                else if (parameter.Direction == ParameterDirection.InputOutput)
                {
                    parameter.Value = Convert.ToInt32(parameter.Value) + 1;
                }
                else if (parameter.Direction == ParameterDirection.ReturnValue)
                {
                    parameter.Value = 7;
                }
            }

            return new DataTableReader(results);
        }
    }

    private sealed class ProcedureParameter : DbParameter
    {
        public override DbType DbType { get; set; }

        public override ParameterDirection Direction { get; set; }

        public override bool IsNullable { get; set; }

        [AllowNull]
        public override string ParameterName { get; set; } = string.Empty;

        public override int Size { get; set; }

        [AllowNull]
        public override string SourceColumn { get; set; } = string.Empty;

        public override bool SourceColumnNullMapping { get; set; }

        public override object? Value { get; set; }

        public override void ResetDbType()
        {
        }
    }

    private sealed class ProcedureParameterCollection : DbParameterCollection
    {
        private readonly List<DbParameter> _parameters = [];

        public override int Count => _parameters.Count;

        public override object SyncRoot => ((ICollection)_parameters).SyncRoot;

        public override int Add(object value)
        {
            _parameters.Add((DbParameter)value);
            return _parameters.Count - 1;
        }

        public override void AddRange(Array values)
        {
            foreach (var value in values)
            {
                Add(value!);
            }
        }

        public override void Clear() => _parameters.Clear();

        public override bool Contains(object value) => _parameters.Contains((DbParameter)value);

        public override bool Contains(string value) => IndexOf(value) >= 0;

        public override void CopyTo(Array array, int index) =>
            ((ICollection)_parameters).CopyTo(array, index);

        public override IEnumerator GetEnumerator() => _parameters.GetEnumerator();

        public override int IndexOf(object value) => _parameters.IndexOf((DbParameter)value);

        public override int IndexOf(string parameterName) =>
            _parameters.FindIndex(parameter => string.Equals(
                parameter.ParameterName,
                parameterName,
                StringComparison.OrdinalIgnoreCase));

        public override void Insert(int index, object value) =>
            _parameters.Insert(index, (DbParameter)value);

        public override void Remove(object value) => _parameters.Remove((DbParameter)value);

        public override void RemoveAt(int index) => _parameters.RemoveAt(index);

        public override void RemoveAt(string parameterName) => RemoveAt(IndexOf(parameterName));

        protected override DbParameter GetParameter(int index) => _parameters[index];

        protected override DbParameter GetParameter(string parameterName) =>
            _parameters[IndexOf(parameterName)];

        protected override void SetParameter(int index, DbParameter value) =>
            _parameters[index] = value;

        protected override void SetParameter(string parameterName, DbParameter value)
        {
            var index = IndexOf(parameterName);
            if (index < 0)
            {
                _parameters.Add(value);
            }
            else
            {
                _parameters[index] = value;
            }
        }
    }
}
