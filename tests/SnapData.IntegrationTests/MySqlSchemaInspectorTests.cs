using MySqlConnector;
using SnapData.Schema;

namespace SnapData.IntegrationTests;

public sealed class MySqlSchemaInspectorTests
{
    [MySqlFact]
    public async Task Discovers_objects_and_reads_column_metadata()
    {
        var connectionString = Environment.GetEnvironmentVariable(
            "SNAPDATA_MYSQL_CONNECTION")!;
        var prefix = $"snap_schema_{Guid.NewGuid():N}";
        var usersName = $"{prefix}_users";
        var viewName = $"{prefix}_active_users";
        var typesName = $"{prefix}_type_samples";
        var accountsName = $"{prefix}_accounts";
        var linksName = $"{prefix}_links";
        var restrictedLinksName = $"{prefix}_restricted_links";
        await using var connection = new MySqlConnection(connectionString);
        await connection.OpenAsync();

        try
        {
            await ExecuteAsync(
                connection,
                $"""
                CREATE TABLE `{usersName}` (
                    `id` BIGINT NOT NULL AUTO_INCREMENT,
                    `name` VARCHAR(100) NOT NULL,
                    `amount` DECIMAL(18,2) NULL,
                    `active` BOOLEAN NOT NULL DEFAULT TRUE,
                    `normalized_name` VARCHAR(100)
                        GENERATED ALWAYS AS (UPPER(`name`)) STORED,
                    `name_length` INT
                        GENERATED ALWAYS AS (CHAR_LENGTH(`name`)) VIRTUAL,
                    PRIMARY KEY (`id`)
                );
                CREATE VIEW `{viewName}` AS
                    SELECT `id`, `name` FROM `{usersName}` WHERE `active` = TRUE;
                CREATE TABLE `{typesName}` (
                    `unsigned_id` BIGINT UNSIGNED,
                    `unsigned_int` INT UNSIGNED,
                    `unsigned_small` SMALLINT UNSIGNED,
                    `unsigned_tiny` TINYINT UNSIGNED,
                    `small_value` SMALLINT,
                    `tiny_flag` TINYINT(1),
                    `bit_value` BIT(8),
                    `year_value` YEAR,
                    `event_date` DATE,
                    `created` DATETIME(6),
                    `updated` TIMESTAMP(3),
                    `duration` TIME(3),
                    `fixed_binary` BINARY(16),
                    `payload` LONGBLOB,
                    `status` ENUM('active', 'disabled'),
                    `flags` SET('a', 'b'),
                    `metadata` JSON,
                    `search_text` TEXT,
                    `location` POINT,
                    `double_value` DOUBLE,
                    `single_value` FLOAT
                );
                CREATE FULLTEXT INDEX `{prefix}_ix_search`
                    ON `{typesName}` (`search_text`);
                CREATE TABLE `{accountsName}` (
                    `tenant_id` INT NOT NULL,
                    `id` BIGINT NOT NULL,
                    PRIMARY KEY (`tenant_id`, `id`)
                );
                CREATE TABLE `{linksName}` (
                    `tenant_id` INT NULL,
                    `account_id` BIGINT NULL,
                    CONSTRAINT `{prefix}_fk_links`
                        FOREIGN KEY (`tenant_id`, `account_id`)
                        REFERENCES `{accountsName}` (`tenant_id`, `id`)
                        ON UPDATE CASCADE ON DELETE SET NULL
                );
                CREATE TABLE `{restrictedLinksName}` (
                    `tenant_id` INT NULL,
                    `account_id` BIGINT NULL,
                    CONSTRAINT `{prefix}_fk_restrict`
                        FOREIGN KEY (`tenant_id`, `account_id`)
                        REFERENCES `{accountsName}` (`tenant_id`, `id`)
                        ON UPDATE NO ACTION ON DELETE RESTRICT
                );
                CREATE UNIQUE INDEX `{prefix}_ix_name`
                    ON `{usersName}` (`name`(12) DESC);
                CREATE INDEX `{prefix}_ix_normalized`
                    ON `{usersName}` ((LOWER(`name`)));
                CREATE INDEX `{prefix}_ix_active`
                    ON `{usersName}` (`active`) INVISIBLE;
                """);

            var database = new SnapDatabase(
                MySqlConnectorFactory.Instance,
                connectionString,
                MySqlQueryCompiler.Instance);
            var inspector = new MySqlSchemaInspector(database);
            var tableName = new SchemaObjectName(usersName);

            Assert.True(await inspector.TableExistsAsync(tableName));
            Assert.True(await inspector.ColumnExistsAsync(tableName, "name"));
            Assert.False(await inspector.ColumnExistsAsync(tableName, "missing"));
            Assert.Null(await inspector.GetTableAsync(new SchemaObjectName($"{prefix}_missing")));

            var objects = await inspector.GetObjectsAsync();
            Assert.Contains(objects, item =>
                item.Kind == SchemaObjectKind.Table && item.Name.Name == usersName);
            Assert.Contains(objects, item =>
                item.Kind == SchemaObjectKind.View && item.Name.Name == viewName);

            var table = await inspector.GetTableAsync(tableName);
            Assert.NotNull(table);
            Assert.Equal(connection.Database, table.Name.Schema, ignoreCase: true);
            var columns = table.Columns.ToDictionary(column => column.Name);
            Assert.Equal("bigint", columns["id"].StoreType);
            Assert.Equal(System.Data.DbType.Int64, columns["id"].DbType);
            Assert.Equal(typeof(long), columns["id"].ClrType);
            Assert.Equal(SchemaGeneratedKind.Identity, columns["id"].GeneratedKind);
            Assert.True(columns["id"].IsAutoIncrement);
            Assert.Equal("varchar(100)", columns["name"].StoreType);
            Assert.Equal("decimal(18,2)", columns["amount"].StoreType);
            Assert.Equal(typeof(bool), columns["active"].ClrType);
            Assert.NotNull(columns["active"].DefaultExpression);
            Assert.Equal(
                SchemaGeneratedKind.ComputedStored,
                columns["normalized_name"].GeneratedKind);
            Assert.Equal(
                SchemaGeneratedKind.ComputedVirtual,
                columns["name_length"].GeneratedKind);
            Assert.Null(table.DefinitionSql);
            Assert.Equal("PRIMARY", table.PrimaryKey!.Name);
            Assert.Equal(["id"], table.PrimaryKey.Columns);

            var primaryIndex = Assert.Single(
                table.Indexes,
                index => index.Origin == SchemaIndexOrigin.PrimaryKey);
            Assert.True(primaryIndex.IsUnique);
            Assert.Equal("id", Assert.Single(primaryIndex.Columns).Name);

            var prefixIndex = Assert.Single(
                table.Indexes,
                index => index.Name == $"{prefix}_ix_name");
            Assert.True(prefixIndex.IsUnique);
            Assert.Equal(SchemaIndexOrigin.UniqueConstraint, prefixIndex.Origin);
            Assert.True(prefixIndex.IsVisible);
            Assert.Equal("BTREE", prefixIndex.Method, ignoreCase: true);
            var prefixColumn = Assert.Single(prefixIndex.Columns);
            Assert.Equal("name", prefixColumn.Name);
            Assert.Equal(12, prefixColumn.PrefixLength);
            Assert.True(prefixColumn.Descending);

            var expressionIndex = Assert.Single(
                table.Indexes,
                index => index.Name == $"{prefix}_ix_normalized");
            var expression = Assert.Single(expressionIndex.Columns);
            Assert.Null(expression.Name);
            Assert.Contains("lower", expression.Expression, StringComparison.OrdinalIgnoreCase);

            var invisibleIndex = Assert.Single(
                table.Indexes,
                index => index.Name == $"{prefix}_ix_active");
            Assert.False(invisibleIndex.IsVisible);
            Assert.Null(invisibleIndex.DefinitionSql);

            var accounts = await inspector.GetTableAsync(
                new SchemaObjectName(accountsName));
            Assert.Equal(["tenant_id", "id"], accounts!.PrimaryKey!.Columns);

            var links = await inspector.GetTableAsync(new SchemaObjectName(linksName));
            var foreignKey = Assert.Single(links!.ForeignKeys);
            Assert.Equal($"{prefix}_fk_links", foreignKey.Name);
            Assert.Equal(["tenant_id", "account_id"], foreignKey.Columns);
            Assert.Equal(accountsName, foreignKey.ReferencedTable.Name);
            Assert.Equal(
                connection.Database,
                foreignKey.ReferencedTable.Schema,
                ignoreCase: true);
            Assert.Equal(["tenant_id", "id"], foreignKey.ReferencedColumns);
            Assert.Equal(ReferentialAction.Cascade, foreignKey.OnUpdate);
            Assert.Equal(ReferentialAction.SetNull, foreignKey.OnDelete);

            var restricted = await inspector.GetTableAsync(
                new SchemaObjectName(restrictedLinksName));
            var restrictedForeignKey = Assert.Single(restricted!.ForeignKeys);
            Assert.Equal(ReferentialAction.NoAction, restrictedForeignKey.OnUpdate);
            Assert.Equal(ReferentialAction.Restrict, restrictedForeignKey.OnDelete);

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
            Assert.Empty(minimal.Indexes);

            var types = (await inspector.GetTableAsync(new SchemaObjectName(typesName)))!;
            var typeColumns = types.Columns.ToDictionary(column => column.Name);
            Assert.Equal(typeof(ulong), typeColumns["unsigned_id"].ClrType);
            Assert.Equal(System.Data.DbType.UInt64, typeColumns["unsigned_id"].DbType);
            Assert.Equal(typeof(uint), typeColumns["unsigned_int"].ClrType);
            Assert.Equal(typeof(ushort), typeColumns["unsigned_small"].ClrType);
            Assert.Equal(typeof(byte), typeColumns["unsigned_tiny"].ClrType);
            Assert.Equal(typeof(short), typeColumns["small_value"].ClrType);
            Assert.Equal(typeof(bool), typeColumns["tiny_flag"].ClrType);
            Assert.Equal(typeof(ulong), typeColumns["bit_value"].ClrType);
            Assert.Equal(typeof(int), typeColumns["year_value"].ClrType);
            Assert.Equal(typeof(DateOnly), typeColumns["event_date"].ClrType);
            Assert.Equal(typeof(DateTime), typeColumns["created"].ClrType);
            Assert.Equal(typeof(DateTime), typeColumns["updated"].ClrType);
            Assert.Equal(typeof(TimeSpan), typeColumns["duration"].ClrType);
            Assert.Equal(typeof(byte[]), typeColumns["fixed_binary"].ClrType);
            Assert.Equal(typeof(byte[]), typeColumns["payload"].ClrType);
            Assert.Equal(typeof(string), typeColumns["status"].ClrType);
            Assert.Equal("enum('active','disabled')", typeColumns["status"].StoreType);
            Assert.Equal(typeof(string), typeColumns["flags"].ClrType);
            Assert.Equal("set('a','b')", typeColumns["flags"].StoreType);
            Assert.Equal(typeof(string), typeColumns["metadata"].ClrType);
            Assert.Equal(typeof(object), typeColumns["location"].ClrType);
            Assert.Equal(typeof(double), typeColumns["double_value"].ClrType);
            Assert.Equal(typeof(float), typeColumns["single_value"].ClrType);

            var fullTextIndex = Assert.Single(
                types.Indexes,
                index => index.Name == $"{prefix}_ix_search");
            Assert.Equal("FULLTEXT", fullTextIndex.Method, ignoreCase: true);
            Assert.False(fullTextIndex.IsUnique);
            Assert.Null(fullTextIndex.DefinitionSql);

            var schema = await inspector.ReadAsync();
            Assert.Equal(connection.Database, schema.Name, ignoreCase: true);
            Assert.Contains(schema.Tables, item => item.Name.Name == usersName);
            var view = Assert.Single(schema.Views, item => item.Name.Name == viewName);
            Assert.Equal(2, view.Columns.Count);
            Assert.Contains("select", view.DefinitionSql, StringComparison.OrdinalIgnoreCase);

            var withoutViews = await inspector.ReadAsync(
                new SchemaReadOptions { IncludeViews = false });
            Assert.Empty(withoutViews.Views);

            var withoutDefinitions = await inspector.ReadAsync(
                new SchemaReadOptions { IncludeDefinitionSql = false });
            Assert.Contains(
                withoutDefinitions.Views,
                item => item.Name.Name == viewName && item.DefinitionSql is null);
        }
        finally
        {
            await ExecuteAsync(
                connection,
                $"""
                DROP VIEW IF EXISTS `{viewName}`;
                DROP TABLE IF EXISTS `{restrictedLinksName}`;
                DROP TABLE IF EXISTS `{linksName}`;
                DROP TABLE IF EXISTS `{accountsName}`;
                DROP TABLE IF EXISTS `{typesName}`;
                DROP TABLE IF EXISTS `{usersName}`;
                """);
        }
    }

    private static async Task ExecuteAsync(MySqlConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }
}
