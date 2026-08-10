using System.Data.Common;

namespace SnapData;

public interface IDatabaseAdapter
{
    IQueryCompiler QueryCompiler { get; }

    DbConnection CreateConnection();
}

public sealed class DatabaseAdapter : IDatabaseAdapter
{
    private readonly DbProviderFactory _factory;
    private readonly string _connectionString;

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
    }

    public IQueryCompiler QueryCompiler { get; }

    public DbConnection CreateConnection()
    {
        var connection = _factory.CreateConnection()
            ?? throw new InvalidOperationException(
                $"{_factory.GetType().Name} did not create a connection.");
        connection.ConnectionString = _connectionString;
        return connection;
    }
}
