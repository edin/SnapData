using System.Data.Common;

namespace SnapData;

public interface IDatabaseAdapter
{
    IQueryCompiler QueryCompiler { get; }

    bool CanUse(DbConnection connection);

    DbConnection CreateConnection();
}

public sealed class DatabaseAdapter : IDatabaseAdapter
{
    private readonly DbProviderFactory _factory;
    private readonly string _connectionString;
    private readonly Lazy<Type> _connectionType;

    public DatabaseAdapter(
        DbProviderFactory factory,
        string connectionString,
        IQueryCompiler queryCompiler)
    {
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(connectionString);
        ArgumentNullException.ThrowIfNull(queryCompiler);
        _factory = factory;
        _connectionString = connectionString;
        QueryCompiler = queryCompiler;
        _connectionType = new Lazy<Type>(() =>
        {
            using var connection = CreateConnection();
            return connection.GetType();
        });
    }

    public IQueryCompiler QueryCompiler { get; }

    public bool CanUse(DbConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);
        return _connectionType.Value.IsInstanceOfType(connection);
    }

    public DbConnection CreateConnection()
    {
        var connection = _factory.CreateConnection()
            ?? throw new InvalidOperationException(
                $"{_factory.GetType().Name} did not create a connection.");
        connection.ConnectionString = _connectionString;
        return connection;
    }
}
