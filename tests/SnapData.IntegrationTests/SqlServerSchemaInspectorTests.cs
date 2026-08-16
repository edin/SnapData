using Microsoft.Data.SqlClient;
using SnapData.Schema;

namespace SnapData.IntegrationTests;

public sealed class SqlServerSchemaInspectorTests
{
    [SqlServerFact]
    public async Task Discovers_objects_and_reads_column_metadata()
    {
        var connectionString = Environment.GetEnvironmentVariable(
            "SNAPDATA_SQLSERVER_CONNECTION")!;
        var schemaName = $"snap_schema_{Guid.NewGuid():N}";
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        try
        {
            await ExecuteAsync(
                connection,
                $"CREATE SCHEMA [{schemaName}] AUTHORIZATION [dbo]");
            await ExecuteAsync(
                connection,
                $"CREATE TYPE [{schemaName}].[short_name] FROM NVARCHAR(40) NULL");
            await ExecuteAsync(
                connection,
                $"""
                CREATE TABLE [{schemaName}].[users] (
                    [id] BIGINT IDENTITY(1,1) NOT NULL
                        CONSTRAINT [PK_{schemaName}_users] PRIMARY KEY,
                    [name] NVARCHAR(100) NOT NULL,
                    [amount] DECIMAL(18,2) NULL,
                    [payload] VARBINARY(MAX) NULL,
                    [active] BIT NOT NULL CONSTRAINT [DF_{schemaName}_active] DEFAULT (1),
                    [normalized_name] AS UPPER([name]) PERSISTED,
                    [name_length] AS LEN([name])
                );
                CREATE TABLE [{schemaName}].[accounts] (
                    [tenant_id] INT NOT NULL,
                    [id] BIGINT NOT NULL,
                    CONSTRAINT [PK_{schemaName}_accounts]
                        PRIMARY KEY ([tenant_id], [id]),
                    CONSTRAINT [UQ_{schemaName}_accounts]
                        UNIQUE ([id], [tenant_id])
                );
                CREATE TABLE [{schemaName}].[account_links] (
                    [tenant_id] INT NULL,
                    [account_id] BIGINT NULL,
                    CONSTRAINT [FK_{schemaName}_account_links_accounts]
                        FOREIGN KEY ([tenant_id], [account_id])
                        REFERENCES [{schemaName}].[accounts] ([tenant_id], [id])
                        ON UPDATE CASCADE ON DELETE SET NULL
                );
                CREATE UNIQUE INDEX [IX_{schemaName}_users_name]
                    ON [{schemaName}].[users] ([name] DESC)
                    INCLUDE ([active])
                    WHERE [amount] IS NOT NULL;
                CREATE TABLE [{schemaName}].[type_samples] (
                    [id] UNIQUEIDENTIFIER NOT NULL,
                    [created] DATETIME2(3) NOT NULL,
                    [offset_value] DATETIMEOFFSET(4) NULL,
                    [time_value] TIME(2) NULL,
                    [unicode_text] NVARCHAR(MAX) NULL,
                    [ansi_text] VARCHAR(20) NULL,
                    [fixed_payload] VARBINARY(16) NULL,
                    [double_value] FLOAT NULL,
                    [single_value] REAL NULL,
                    [money_value] MONEY NULL,
                    [alias_value] [{schemaName}].[short_name] NULL,
                    [version] ROWVERSION NOT NULL
                );
                """);
            await ExecuteAsync(
                connection,
                $"""
                CREATE VIEW [{schemaName}].[active_users]
                AS SELECT [id], [name] FROM [{schemaName}].[users] WHERE [active] = 1;
                """);

            var database = new SnapDatabase(
                SqlClientFactory.Instance,
                connectionString,
                SqlServerQueryCompiler.Instance);
            var inspector = new SqlServerSchemaInspector(database);
            var tableName = new SchemaObjectName("users", schemaName);

            Assert.True(await inspector.TableExistsAsync(tableName));
            Assert.True(await inspector.ColumnExistsAsync(tableName, "name"));
            Assert.False(await inspector.ColumnExistsAsync(tableName, "missing"));

            var objects = await inspector.GetObjectsAsync(schemaName);
            Assert.Contains(objects, item =>
                item.Kind == SchemaObjectKind.Table && item.Name.Name == "users");
            Assert.Contains(objects, item =>
                item.Kind == SchemaObjectKind.View && item.Name.Name == "active_users");

            var table = await inspector.GetTableAsync(tableName);
            Assert.NotNull(table);
            Assert.Equal(schemaName, table.Name.Schema);
            Assert.Equal(7, table.Columns.Count);

            var columns = table.Columns.ToDictionary(column => column.Name);
            Assert.Equal("bigint", columns["id"].StoreType);
            Assert.Equal(System.Data.DbType.Int64, columns["id"].DbType);
            Assert.Equal(typeof(long), columns["id"].ClrType);
            Assert.Equal(SchemaGeneratedKind.Identity, columns["id"].GeneratedKind);
            Assert.True(columns["id"].IsAutoIncrement);
            Assert.False(columns["id"].IsNullable);
            Assert.Equal("nvarchar(100)", columns["name"].StoreType);
            Assert.Equal("decimal(18,2)", columns["amount"].StoreType);
            Assert.Equal("varbinary(max)", columns["payload"].StoreType);
            Assert.Equal(SchemaGeneratedKind.ComputedStored, columns["normalized_name"].GeneratedKind);
            Assert.Equal(SchemaGeneratedKind.ComputedVirtual, columns["name_length"].GeneratedKind);
            Assert.NotNull(columns["active"].DefaultExpression);
            Assert.Equal($"PK_{schemaName}_users", table.PrimaryKey!.Name);
            Assert.Equal(["id"], table.PrimaryKey.Columns);

            var primaryIndex = Assert.Single(
                table.Indexes,
                index => index.Origin == SchemaIndexOrigin.PrimaryKey);
            Assert.True(primaryIndex.IsUnique);
            Assert.Equal("id", Assert.Single(primaryIndex.Columns).Name);

            var filteredIndex = Assert.Single(
                table.Indexes,
                index => index.Origin == SchemaIndexOrigin.Created);
            Assert.True(filteredIndex.IsUnique);
            Assert.NotNull(filteredIndex.FilterExpression);
            Assert.Equal(2, filteredIndex.Columns.Count);
            Assert.Equal("name", filteredIndex.Columns[0].Name);
            Assert.True(filteredIndex.Columns[0].Descending);
            Assert.False(filteredIndex.Columns[0].IsIncluded);
            Assert.Equal("active", filteredIndex.Columns[1].Name);
            Assert.True(filteredIndex.Columns[1].IsIncluded);

            var accounts = await inspector.GetTableAsync(
                new SchemaObjectName("accounts", schemaName));
            Assert.Equal(["tenant_id", "id"], accounts!.PrimaryKey!.Columns);
            Assert.Contains(
                accounts.Indexes,
                index => index.Origin == SchemaIndexOrigin.UniqueConstraint);

            var links = await inspector.GetTableAsync(
                new SchemaObjectName("account_links", schemaName));
            var foreignKey = Assert.Single(links!.ForeignKeys);
            Assert.Equal(["tenant_id", "account_id"], foreignKey.Columns);
            Assert.Equal(new SchemaObjectName("accounts", schemaName), foreignKey.ReferencedTable);
            Assert.Equal(["tenant_id", "id"], foreignKey.ReferencedColumns);
            Assert.Equal(ReferentialAction.Cascade, foreignKey.OnUpdate);
            Assert.Equal(ReferentialAction.SetNull, foreignKey.OnDelete);

            var minimal = await inspector.GetTableAsync(
                tableName,
                new SchemaReadOptions
                {
                    IncludeColumns = false,
                    IncludePrimaryKeys = false,
                    IncludeForeignKeys = false,
                    IncludeIndexes = false,
                    IncludeViews = false,
                    IncludeDefinitionSql = false
                });
            Assert.Empty(minimal!.Columns);
            Assert.Null(minimal.PrimaryKey);
            Assert.Empty(minimal.ForeignKeys);

            Assert.Null(await inspector.GetTableAsync(
                new SchemaObjectName("missing", schemaName)));

            var types = (await inspector.GetTableAsync(
                new SchemaObjectName("type_samples", schemaName)))!;
            var typeColumns = types.Columns.ToDictionary(column => column.Name);
            Assert.Equal("uniqueidentifier", typeColumns["id"].StoreType);
            Assert.Equal(typeof(Guid), typeColumns["id"].ClrType);
            Assert.Equal("datetime2(3)", typeColumns["created"].StoreType);
            Assert.Equal("datetimeoffset(4)", typeColumns["offset_value"].StoreType);
            Assert.Equal("time(2)", typeColumns["time_value"].StoreType);
            Assert.Equal("nvarchar(max)", typeColumns["unicode_text"].StoreType);
            Assert.Equal("varchar(20)", typeColumns["ansi_text"].StoreType);
            Assert.Equal("varbinary(16)", typeColumns["fixed_payload"].StoreType);
            Assert.Equal(typeof(double), typeColumns["double_value"].ClrType);
            Assert.Equal(typeof(float), typeColumns["single_value"].ClrType);
            Assert.Equal(typeof(decimal), typeColumns["money_value"].ClrType);
            Assert.Equal("short_name", typeColumns["alias_value"].StoreType);
            Assert.Equal(System.Data.DbType.String, typeColumns["alias_value"].DbType);
            Assert.Equal(typeof(string), typeColumns["alias_value"].ClrType);
            Assert.Equal(SchemaGeneratedKind.RowVersion, typeColumns["version"].GeneratedKind);
            Assert.False(typeColumns["version"].IsAutoIncrement);
            Assert.Null(types.DefinitionSql);

            var schema = await inspector.ReadAsync();
            Assert.Contains(schema.Tables, item => item.Name == tableName);
            var view = Assert.Single(schema.Views, item =>
                item.Name == new SchemaObjectName("active_users", schemaName));
            Assert.Equal(2, view.Columns.Count);
            Assert.Contains("CREATE VIEW", view.DefinitionSql, StringComparison.OrdinalIgnoreCase);

            var withoutViews = await inspector.ReadAsync(
                new SchemaReadOptions { IncludeViews = false });
            Assert.Empty(withoutViews.Views);

            var withoutDefinitions = await inspector.ReadAsync(
                new SchemaReadOptions { IncludeDefinitionSql = false });
            Assert.Contains(
                withoutDefinitions.Views,
                item => item.Name == new SchemaObjectName("active_users", schemaName)
                    && item.DefinitionSql is null);
        }
        finally
        {
            await ExecuteAsync(
                connection,
                $"""
                DROP VIEW IF EXISTS [{schemaName}].[active_users];
                DROP TABLE IF EXISTS [{schemaName}].[account_links];
                DROP TABLE IF EXISTS [{schemaName}].[accounts];
                DROP TABLE IF EXISTS [{schemaName}].[type_samples];
                DROP TABLE IF EXISTS [{schemaName}].[users];
                IF TYPE_ID(N'[{schemaName}].[short_name]') IS NOT NULL
                    EXEC(N'DROP TYPE [{schemaName}].[short_name]');
                IF SCHEMA_ID(N'{schemaName}') IS NOT NULL
                    EXEC(N'DROP SCHEMA [{schemaName}]');
                """);
        }
    }

    private static async Task ExecuteAsync(SqlConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }
}
