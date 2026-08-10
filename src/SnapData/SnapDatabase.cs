using System.Data.Common;

namespace SnapData;

public sealed class SnapDatabase
{
    private readonly IDatabaseAdapter _adapter;
    private readonly IEntityMappingProvider _mappingProvider;

    public SnapDatabase(
        IDatabaseAdapter adapter,
        IEntityMappingProvider? mappingProvider = null)
    {
        ArgumentNullException.ThrowIfNull(adapter);
        _adapter = adapter;
        _mappingProvider = mappingProvider ?? EntityMappingProvider.Default;
    }

    public SnapDatabase(
        DbProviderFactory factory,
        string connectionString,
        IQueryCompiler? queryCompiler = null,
        IEntityMappingProvider? mappingProvider = null)
        : this(new DatabaseAdapter(
            factory,
            connectionString,
            queryCompiler ?? SqlDialect.Ansi), mappingProvider)
    {
    }

    public DbSession BorrowSession(DbConnection connection) =>
        DbSession.Borrow(connection, _adapter.QueryCompiler, _mappingProvider);

    public async ValueTask<DbSession> OpenSessionAsync(
        CancellationToken cancellationToken = default)
    {
        var connection = _adapter.CreateConnection();

        try
        {
            await connection.OpenAsync(cancellationToken);
            return DbSession.Own(connection, _adapter.QueryCompiler, _mappingProvider);
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }
}
