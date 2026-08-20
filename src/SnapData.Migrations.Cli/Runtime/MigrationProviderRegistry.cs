using System.Data.Common;
using FirebirdSql.Data.FirebirdClient;
using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using MySqlConnector;
using Npgsql;
using SnapData.Migrations;

namespace SnapData.Migrations.Cli.Runtime;

internal sealed record MigrationProviderRegistration(
    string Name,
    DbProviderFactory Factory,
    IQueryCompiler QueryCompiler,
    IMigrationDialect MigrationDialect);

internal static class MigrationProviderRegistry
{
    public static MigrationProviderRegistration Resolve(string provider)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(provider);
        return provider.Trim().ToLowerInvariant() switch
        {
            "sqlite" => new(
                Provider.Sqlite,
                SqliteFactory.Instance,
                SqliteQueryCompiler.Instance,
                SqliteMigrationDialect.Instance),
            "sqlserver" or "mssql" => new(
                Provider.SqlServer,
                SqlClientFactory.Instance,
                SqlServerQueryCompiler.Instance,
                SqlServerMigrationDialect.Instance),
            "postgres" or "postgresql" => new(
                Provider.Postgres,
                NpgsqlFactory.Instance,
                PostgresQueryCompiler.Instance,
                PostgresMigrationDialect.Instance),
            "mysql" => new(
                Provider.MySql,
                MySqlConnectorFactory.Instance,
                MySqlQueryCompiler.Instance,
                MySqlMigrationDialect.Instance),
            "firebird" => new(
                Provider.Firebird,
                FirebirdClientFactory.Instance,
                FirebirdQueryCompiler.Instance,
                FirebirdMigrationDialect.Instance),
            _ => throw new InvalidOperationException(
                $"Migration provider '{provider}' is not supported. " +
                "Supported providers: Sqlite, SqlServer, Postgres, MySql, Firebird.")
        };
    }

    public static MigrationRunner CreateRunner(MigrationSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var configuration = source.Configuration;
        if (string.IsNullOrWhiteSpace(configuration.Provider))
        {
            throw new InvalidOperationException(
                $"Migration Provider is not configured in '{configuration.ConfigFile}'.");
        }
        if (string.IsNullOrWhiteSpace(configuration.Connection))
        {
            throw new InvalidOperationException(
                $"Migration Connection is not configured in '{configuration.ConfigFile}'.");
        }

        var provider = Resolve(configuration.Provider);
        var database = new SnapDatabase(
            provider.Factory,
            configuration.Connection,
            provider.QueryCompiler);
        return new MigrationRunner(
            database,
            source.Catalog.Migrations,
            provider.MigrationDialect,
            new MigrationRunnerOptions
            {
                HistoryTable = configuration.HistoryTable,
                LockTimeout = configuration.Timeout
            });
    }
}
