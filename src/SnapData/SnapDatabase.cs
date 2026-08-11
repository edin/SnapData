using System.Data.Common;

namespace SnapData;

public sealed class SnapDatabase
{
    private readonly IDatabaseAdapter _adapter;
    private readonly IEntityMappingProvider _mappingProvider;
    private readonly ICommandObserver? _commandObserver;

    public SnapDatabase(
        IDatabaseAdapter adapter,
        IEntityMappingProvider? mappingProvider = null,
        ICommandObserver? commandObserver = null)
    {
        ArgumentNullException.ThrowIfNull(adapter);
        _adapter = adapter;
        _mappingProvider = mappingProvider ?? EntityMappingProvider.Default;
        _commandObserver = commandObserver;
    }

    public SnapDatabase(
        DbProviderFactory factory,
        string connectionString,
        IQueryCompiler? queryCompiler = null,
        IEntityMappingProvider? mappingProvider = null,
        ICommandObserver? commandObserver = null)
        : this(new DatabaseAdapter(
            factory,
            connectionString,
            queryCompiler ?? SqlDialect.Ansi), mappingProvider, commandObserver)
    {
    }

    public DbSession Borrow(DbConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);
        if (!_adapter.CanUse(connection))
        {
            throw new ArgumentException(
                $"Connection type {connection.GetType().Name} is incompatible with the configured database adapter.",
                nameof(connection));
        }

        return DbSession.Borrow(
            connection,
            _adapter.QueryCompiler,
            _mappingProvider,
            _commandObserver);
    }

    public async ValueTask<DbSession> OpenSessionAsync(
        CancellationToken cancellationToken = default)
    {
        var connection = _adapter.CreateConnection();

        try
        {
            await connection.OpenAsync(cancellationToken);
            return DbSession.Own(
                connection,
                _adapter.QueryCompiler,
                _mappingProvider,
                _commandObserver);
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }
}
