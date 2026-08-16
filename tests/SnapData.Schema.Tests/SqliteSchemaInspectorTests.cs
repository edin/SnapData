using Microsoft.Data.Sqlite;

namespace SnapData.Schema.Tests;

public sealed class SqliteSchemaInspectorTests
{
    [Fact]
    public async Task Executor_backed_inspector_uses_the_active_transaction()
    {
        var path = Path.Combine(Path.GetTempPath(), $"snapdata-schema-{Guid.NewGuid():N}.db");
        try
        {
            var database = Database($"Data Source={path};Pooling=False");
            await using (var session = await database.OpenSessionAsync())
            await using (var transaction = await session.BeginTransactionAsync())
            {
                await transaction.ExecuteAsync("CREATE TABLE pending_books (id INTEGER PRIMARY KEY)");

                var inspector = new SqliteSchemaInspector(transaction);

                Assert.True(await inspector.TableExistsAsync(new SchemaObjectName("pending_books")));
                await transaction.RollbackAsync();
            }

            var committedInspector = new SqliteSchemaInspector(database);
            Assert.False(await committedInspector.TableExistsAsync(new SchemaObjectName("pending_books")));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task Existence_checks_find_tables_and_columns_without_full_schema_reading()
    {
        var connectionString =
            $"Data Source=snapdata-schema-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText =
                """
                CREATE TABLE users (
                    id INTEGER PRIMARY KEY,
                    name TEXT NOT NULL,
                    computed_name TEXT GENERATED ALWAYS AS (upper(name)) VIRTUAL
                );
                """;
            await command.ExecuteNonQueryAsync();
        }

        var inspector = new SqliteSchemaInspector(Database(connectionString));

        Assert.True(await inspector.TableExistsAsync(new SchemaObjectName("users")));
        Assert.False(await inspector.TableExistsAsync(new SchemaObjectName("missing")));
        Assert.True(await inspector.ColumnExistsAsync(new SchemaObjectName("users"), "name"));
        Assert.True(await inspector.ColumnExistsAsync(new SchemaObjectName("users"), "computed_name"));
        Assert.False(await inspector.ColumnExistsAsync(new SchemaObjectName("users"), "missing"));
    }

    [Fact]
    public async Task Database_backed_inspector_opens_its_own_session()
    {
        var path = Path.Combine(Path.GetTempPath(), $"snapdata-schema-{Guid.NewGuid():N}.db");
        try
        {
            var database = Database($"Data Source={path};Pooling=False");
            await using (var session = await database.OpenSessionAsync())
            {
                await session.ExecuteAsync("CREATE TABLE books (id INTEGER PRIMARY KEY, title TEXT)");
            }

            var inspector = new SqliteSchemaInspector(database);

            Assert.True(await inspector.TableExistsAsync(new SchemaObjectName("books")));
            Assert.True(await inspector.ColumnExistsAsync(new SchemaObjectName("books"), "title"));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task Discovers_lightweight_objects_and_reads_detailed_schema()
    {
        var connectionString =
            $"Data Source=snapdata-schema-read-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
        await using var anchor = new SqliteConnection(connectionString);
        await anchor.OpenAsync();
        await using (var command = anchor.CreateCommand())
        {
            command.CommandText =
                """
                CREATE TABLE memberships (
                    tenant_id INTEGER NOT NULL,
                    user_id INTEGER NOT NULL,
                    role TEXT DEFAULT 'viewer',
                    label TEXT GENERATED ALWAYS AS (tenant_id || ':' || user_id) STORED,
                    PRIMARY KEY (tenant_id, user_id)
                );
                CREATE VIEW membership_names AS
                    SELECT tenant_id, user_id, label FROM memberships;
                """;
            await command.ExecuteNonQueryAsync();
        }

        var inspector = new SqliteSchemaInspector(Database(connectionString));

        var objects = await inspector.GetObjectsAsync();
        var tableInfo = Assert.Single(objects, item => item.Kind == SchemaObjectKind.Table);
        var viewInfo = Assert.Single(objects, item => item.Kind == SchemaObjectKind.View);
        Assert.Equal("memberships", tableInfo.Name.Name);
        Assert.Equal("membership_names", viewInfo.Name.Name);

        var table = await inspector.GetTableAsync(new SchemaObjectName("memberships"));
        Assert.NotNull(table);
        Assert.Equal(4, table.Columns.Count);
        Assert.Equal(["tenant_id", "user_id"], table.PrimaryKey!.Columns);
        Assert.Equal(SchemaGeneratedKind.ComputedStored, table.Columns[3].GeneratedKind);
        Assert.Equal("'viewer'", table.Columns[2].DefaultExpression);
        Assert.Contains("CREATE TABLE memberships", table.DefinitionSql);

        var database = await inspector.ReadAsync();
        Assert.Single(database.Tables);
        Assert.Single(database.Views);
        Assert.Equal(3, database.Views[0].Columns.Count);
        Assert.Contains("CREATE VIEW membership_names", database.Views[0].DefinitionSql);
    }

    [Fact]
    public async Task Detailed_read_honors_options_and_missing_table_returns_null()
    {
        var connectionString =
            $"Data Source=snapdata-schema-options-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
        await using var anchor = new SqliteConnection(connectionString);
        await anchor.OpenAsync();
        await using (var command = anchor.CreateCommand())
        {
            command.CommandText = "CREATE TABLE users (id INTEGER PRIMARY KEY, name TEXT)";
            await command.ExecuteNonQueryAsync();
        }

        var inspector = new SqliteSchemaInspector(Database(connectionString));
        var options = new SchemaReadOptions
        {
            IncludeColumns = false,
            IncludePrimaryKeys = false,
            IncludeDefinitionSql = false,
            IncludeViews = false
        };

        var table = await inspector.GetTableAsync(new SchemaObjectName("users"), options);
        var missing = await inspector.GetTableAsync(new SchemaObjectName("missing"), options);

        Assert.NotNull(table);
        Assert.Empty(table.Columns);
        Assert.Null(table.PrimaryKey);
        Assert.Null(table.DefinitionSql);
        Assert.Null(missing);
    }

    [Fact]
    public async Task Reads_single_and_composite_foreign_keys_with_actions()
    {
        var connectionString =
            $"Data Source=snapdata-schema-fk-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
        await using var anchor = new SqliteConnection(connectionString);
        await anchor.OpenAsync();
        await using (var command = anchor.CreateCommand())
        {
            command.CommandText =
                """
                CREATE TABLE tenants (
                    id INTEGER PRIMARY KEY
                );
                CREATE TABLE users (
                    tenant_id INTEGER NOT NULL,
                    id INTEGER NOT NULL,
                    PRIMARY KEY (tenant_id, id),
                    FOREIGN KEY (tenant_id) REFERENCES tenants (id)
                        ON UPDATE CASCADE ON DELETE RESTRICT
                );
                CREATE TABLE orders (
                    tenant_id INTEGER NOT NULL,
                    user_id INTEGER NOT NULL,
                    FOREIGN KEY (tenant_id, user_id) REFERENCES users (tenant_id, id)
                        ON UPDATE SET NULL ON DELETE CASCADE
                );
                """;
            await command.ExecuteNonQueryAsync();
        }

        var inspector = new SqliteSchemaInspector(Database(connectionString));

        var users = await inspector.GetTableAsync(new SchemaObjectName("users"));
        var userForeignKey = Assert.Single(users!.ForeignKeys);
        Assert.Equal(["tenant_id"], userForeignKey.Columns);
        Assert.Equal("tenants", userForeignKey.ReferencedTable.Name);
        Assert.Equal(["id"], userForeignKey.ReferencedColumns);
        Assert.Equal(ReferentialAction.Cascade, userForeignKey.OnUpdate);
        Assert.Equal(ReferentialAction.Restrict, userForeignKey.OnDelete);

        var orders = await inspector.GetTableAsync(new SchemaObjectName("orders"));
        var orderForeignKey = Assert.Single(orders!.ForeignKeys);
        Assert.Equal(["tenant_id", "user_id"], orderForeignKey.Columns);
        Assert.Equal(["tenant_id", "id"], orderForeignKey.ReferencedColumns);
        Assert.Equal(ReferentialAction.SetNull, orderForeignKey.OnUpdate);
        Assert.Equal(ReferentialAction.Cascade, orderForeignKey.OnDelete);
    }

    [Fact]
    public async Task Resolves_implicit_referenced_columns_from_parent_primary_key()
    {
        var connectionString =
            $"Data Source=snapdata-schema-implicit-fk-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
        await using var anchor = new SqliteConnection(connectionString);
        await anchor.OpenAsync();
        await using (var command = anchor.CreateCommand())
        {
            command.CommandText =
                """
                CREATE TABLE parents (
                    tenant_id INTEGER NOT NULL,
                    id INTEGER NOT NULL,
                    PRIMARY KEY (tenant_id, id)
                );
                CREATE TABLE children (
                    tenant_id INTEGER NOT NULL,
                    parent_id INTEGER NOT NULL,
                    FOREIGN KEY (tenant_id, parent_id) REFERENCES parents
                );
                """;
            await command.ExecuteNonQueryAsync();
        }

        var inspector = new SqliteSchemaInspector(Database(connectionString));
        var children = await inspector.GetTableAsync(new SchemaObjectName("children"));
        var foreignKey = Assert.Single(children!.ForeignKeys);

        Assert.Equal(["tenant_id", "parent_id"], foreignKey.Columns);
        Assert.Equal(["tenant_id", "id"], foreignKey.ReferencedColumns);
    }

    [Fact]
    public async Task Foreign_key_reading_honors_options()
    {
        var connectionString =
            $"Data Source=snapdata-schema-fk-options-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
        await using var anchor = new SqliteConnection(connectionString);
        await anchor.OpenAsync();
        await using (var command = anchor.CreateCommand())
        {
            command.CommandText =
                """
                CREATE TABLE parents (id INTEGER PRIMARY KEY);
                CREATE TABLE children (
                    parent_id INTEGER REFERENCES parents (id)
                );
                """;
            await command.ExecuteNonQueryAsync();
        }

        var inspector = new SqliteSchemaInspector(Database(connectionString));
        var table = await inspector.GetTableAsync(
            new SchemaObjectName("children"),
            new SchemaReadOptions { IncludeForeignKeys = false });

        Assert.Empty(table!.ForeignKeys);
    }

    [Fact]
    public async Task Reads_created_unique_composite_partial_and_expression_indexes()
    {
        var connectionString =
            $"Data Source=snapdata-schema-index-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
        await using var anchor = new SqliteConnection(connectionString);
        await anchor.OpenAsync();
        await using (var command = anchor.CreateCommand())
        {
            command.CommandText =
                """
                CREATE TABLE users (
                    id INTEGER PRIMARY KEY,
                    tenant_id INTEGER NOT NULL,
                    email TEXT NOT NULL,
                    name TEXT NOT NULL,
                    code TEXT UNIQUE
                );
                CREATE UNIQUE INDEX ix_users_tenant_email
                    ON users (tenant_id ASC, email DESC);
                CREATE INDEX ix_users_active_name
                    ON users (name) WHERE email IS NOT NULL;
                CREATE INDEX ix_users_normalized_name
                    ON users (lower(name), substr(email, 1, 3) DESC);
                """;
            await command.ExecuteNonQueryAsync();
        }

        var inspector = new SqliteSchemaInspector(Database(connectionString));
        var table = await inspector.GetTableAsync(new SchemaObjectName("users"));

        Assert.Equal(4, table!.Indexes.Count);

        var composite = Assert.Single(table.Indexes, index => index.Name == "ix_users_tenant_email");
        Assert.True(composite.IsUnique);
        Assert.Equal(SchemaIndexOrigin.Created, composite.Origin);
        Assert.Equal(["tenant_id", "email"], composite.Columns.Select(column => column.Name));
        Assert.False(composite.Columns[0].Descending);
        Assert.True(composite.Columns[1].Descending);
        Assert.Contains("CREATE UNIQUE INDEX", composite.DefinitionSql);

        var partial = Assert.Single(table.Indexes, index => index.Name == "ix_users_active_name");
        Assert.Equal("email IS NOT NULL", partial.FilterExpression);

        var expression = Assert.Single(table.Indexes, index => index.Name == "ix_users_normalized_name");
        Assert.Equal("lower(name)", expression.Columns[0].Expression);
        Assert.Equal("substr(email, 1, 3)", expression.Columns[1].Expression);
        Assert.True(expression.Columns[1].Descending);

        var automatic = Assert.Single(
            table.Indexes,
            index => index.Origin == SchemaIndexOrigin.UniqueConstraint);
        Assert.True(automatic.IsUnique);
        Assert.Equal("code", Assert.Single(automatic.Columns).Name);
        Assert.Null(automatic.DefinitionSql);
    }

    [Fact]
    public async Task Index_reading_honors_options()
    {
        var connectionString =
            $"Data Source=snapdata-schema-index-options-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
        await using var anchor = new SqliteConnection(connectionString);
        await anchor.OpenAsync();
        await using (var command = anchor.CreateCommand())
        {
            command.CommandText =
                """
                CREATE TABLE users (id INTEGER, email TEXT);
                CREATE INDEX ix_users_email ON users (email);
                """;
            await command.ExecuteNonQueryAsync();
        }

        var inspector = new SqliteSchemaInspector(Database(connectionString));
        var table = await inspector.GetTableAsync(
            new SchemaObjectName("users"),
            new SchemaReadOptions { IncludeIndexes = false });

        Assert.Empty(table!.Indexes);
    }

    [Fact]
    public async Task Maps_declared_types_using_sqlite_affinity_rules()
    {
        var connectionString =
            $"Data Source=snapdata-schema-types-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
        await using var anchor = new SqliteConnection(connectionString);
        await anchor.OpenAsync();
        await using (var command = anchor.CreateCommand())
        {
            command.CommandText =
                """
                CREATE TABLE values_table (
                    integer_value BIGINT,
                    text_value VARCHAR(200),
                    real_value DOUBLE PRECISION,
                    blob_value BLOB,
                    untyped_value,
                    numeric_value DECIMAL(18, 2),
                    point_value FLOATING POINT
                );
                """;
            await command.ExecuteNonQueryAsync();
        }

        var inspector = new SqliteSchemaInspector(Database(connectionString));
        var table = await inspector.GetTableAsync(new SchemaObjectName("values_table"));
        var columns = table!.Columns.ToDictionary(column => column.Name);

        AssertType(columns["integer_value"], SchemaTypeAffinity.Integer, System.Data.DbType.Int64, typeof(long));
        AssertType(columns["text_value"], SchemaTypeAffinity.Text, System.Data.DbType.String, typeof(string));
        AssertType(columns["real_value"], SchemaTypeAffinity.Real, System.Data.DbType.Double, typeof(double));
        AssertType(columns["blob_value"], SchemaTypeAffinity.Blob, System.Data.DbType.Binary, typeof(byte[]));
        AssertType(columns["untyped_value"], SchemaTypeAffinity.Blob, System.Data.DbType.Binary, typeof(byte[]));
        AssertType(columns["numeric_value"], SchemaTypeAffinity.Numeric, System.Data.DbType.Decimal, typeof(decimal));

        // SQLite's documented precedence makes "FLOATING POINT" INTEGER affinity.
        Assert.Equal(SchemaTypeAffinity.Integer, columns["point_value"].Affinity);
    }

    [Fact]
    public async Task Classifies_identity_autoincrement_and_generated_columns()
    {
        var connectionString =
            $"Data Source=snapdata-schema-generated-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
        await using var anchor = new SqliteConnection(connectionString);
        await anchor.OpenAsync();
        await using (var command = anchor.CreateCommand())
        {
            command.CommandText =
                """
                CREATE TABLE users (
                    id INTEGER PRIMARY KEY,
                    name TEXT NOT NULL,
                    normalized_name TEXT GENERATED ALWAYS AS (lower(name)) VIRTUAL,
                    display_name TEXT GENERATED ALWAYS AS (upper(name)) STORED
                );
                CREATE TABLE audit (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    message TEXT
                );
                CREATE TABLE typed_keys (
                    id INT PRIMARY KEY,
                    value TEXT
                );
                CREATE TABLE strict_keys (
                    id INTEGER PRIMARY KEY,
                    value TEXT
                ) WITHOUT ROWID;
                """;
            await command.ExecuteNonQueryAsync();
        }

        var inspector = new SqliteSchemaInspector(Database(connectionString));

        var users = (await inspector.GetTableAsync(new SchemaObjectName("users")))!;
        Assert.Equal(SchemaGeneratedKind.Identity, users.Columns[0].GeneratedKind);
        Assert.False(users.Columns[0].IsAutoIncrement);
        Assert.Equal(SchemaGeneratedKind.ComputedVirtual, users.Columns[2].GeneratedKind);
        Assert.Equal(SchemaGeneratedKind.ComputedStored, users.Columns[3].GeneratedKind);

        var audit = (await inspector.GetTableAsync(new SchemaObjectName("audit")))!;
        Assert.Equal(SchemaGeneratedKind.Identity, audit.Columns[0].GeneratedKind);
        Assert.True(audit.Columns[0].IsAutoIncrement);

        var typedKeys = (await inspector.GetTableAsync(new SchemaObjectName("typed_keys")))!;
        Assert.Equal(SchemaGeneratedKind.None, typedKeys.Columns[0].GeneratedKind);

        var strictKeys = (await inspector.GetTableAsync(new SchemaObjectName("strict_keys")))!;
        Assert.Equal(SchemaGeneratedKind.None, strictKeys.Columns[0].GeneratedKind);
    }

    [Fact]
    public async Task Classifies_identity_without_exposing_definition_sql()
    {
        var connectionString =
            $"Data Source=snapdata-schema-generated-options-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
        await using var anchor = new SqliteConnection(connectionString);
        await anchor.OpenAsync();
        await using (var command = anchor.CreateCommand())
        {
            command.CommandText = "CREATE TABLE users (id INTEGER PRIMARY KEY, name TEXT)";
            await command.ExecuteNonQueryAsync();
        }

        var inspector = new SqliteSchemaInspector(Database(connectionString));
        var table = await inspector.GetTableAsync(
            new SchemaObjectName("users"),
            new SchemaReadOptions { IncludeDefinitionSql = false });

        Assert.Null(table!.DefinitionSql);
        Assert.Equal(SchemaGeneratedKind.Identity, table.Columns[0].GeneratedKind);
    }

    [Fact]
    public async Task Classifies_virtual_table_hidden_columns()
    {
        var connectionString =
            $"Data Source=snapdata-schema-hidden-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
        await using var anchor = new SqliteConnection(connectionString);
        await anchor.OpenAsync();
        await using (var command = anchor.CreateCommand())
        {
            command.CommandText = "CREATE VIRTUAL TABLE documents USING fts5(content)";
            await command.ExecuteNonQueryAsync();
        }

        var inspector = new SqliteSchemaInspector(Database(connectionString));
        var table = await inspector.GetTableAsync(new SchemaObjectName("documents"));

        Assert.Contains(table!.Columns, column => column.GeneratedKind == SchemaGeneratedKind.Hidden);
    }

    [Fact]
    public async Task Inspection_uses_sqlite_identifier_casing_rules()
    {
        var connectionString =
            $"Data Source=snapdata-schema-casing-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
        await using var anchor = new SqliteConnection(connectionString);
        await anchor.OpenAsync();
        await using (var command = anchor.CreateCommand())
        {
            command.CommandText =
                "CREATE TABLE \"Order Items\" (\"Item ID\" INTEGER PRIMARY KEY, \"Unit Price\" NUMERIC DEFAULT (12.50))";
            await command.ExecuteNonQueryAsync();
        }

        var inspector = new SqliteSchemaInspector(Database(connectionString));
        var requestedName = new SchemaObjectName("order items");

        Assert.True(await inspector.TableExistsAsync(requestedName));
        Assert.True(await inspector.ColumnExistsAsync(requestedName, "item id"));

        var table = await inspector.GetTableAsync(requestedName);
        Assert.NotNull(table);
        Assert.Equal("Item ID", table.Columns[0].Name);
        Assert.Equal("12.50", table.Columns[1].DefaultExpression);
        Assert.Contains("CREATE TABLE \"Order Items\"", table.DefinitionSql);
    }

    [Fact]
    public async Task Primary_key_and_column_options_are_independent()
    {
        var connectionString =
            $"Data Source=snapdata-schema-independent-options-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
        await using var anchor = new SqliteConnection(connectionString);
        await anchor.OpenAsync();
        await using (var command = anchor.CreateCommand())
        {
            command.CommandText =
                "CREATE TABLE memberships (tenant_id INTEGER, user_id INTEGER, PRIMARY KEY (tenant_id, user_id))";
            await command.ExecuteNonQueryAsync();
        }

        var inspector = new SqliteSchemaInspector(Database(connectionString));
        var table = await inspector.GetTableAsync(
            new SchemaObjectName("memberships"),
            new SchemaReadOptions
            {
                IncludeColumns = false,
                IncludePrimaryKeys = true,
                IncludeForeignKeys = false,
                IncludeIndexes = false,
                IncludeViews = false,
                IncludeDefinitionSql = false
            });

        Assert.Empty(table!.Columns);
        Assert.Equal(["tenant_id", "user_id"], table.PrimaryKey!.Columns);
        Assert.Null(table.DefinitionSql);
    }

    [Fact]
    public async Task Ordinary_sqlite_primary_keys_preserve_reported_nullability()
    {
        var connectionString =
            $"Data Source=snapdata-schema-nullability-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
        await using var anchor = new SqliteConnection(connectionString);
        await anchor.OpenAsync();
        await using (var command = anchor.CreateCommand())
        {
            command.CommandText =
                "CREATE TABLE keys (code TEXT PRIMARY KEY, identity_value INTEGER UNIQUE)";
            await command.ExecuteNonQueryAsync();
        }

        var inspector = new SqliteSchemaInspector(Database(connectionString));
        var table = await inspector.GetTableAsync(new SchemaObjectName("keys"));

        Assert.True(table!.Columns[0].IsNullable);
        Assert.Equal(SchemaGeneratedKind.None, table.Columns[0].GeneratedKind);
    }

    [Fact]
    public async Task Empty_database_returns_an_empty_main_schema()
    {
        var connectionString =
            $"Data Source=snapdata-schema-empty-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
        await using var anchor = new SqliteConnection(connectionString);
        await anchor.OpenAsync();

        var inspector = new SqliteSchemaInspector(Database(connectionString));
        var schema = await inspector.ReadAsync();

        Assert.Equal("main", schema.Name);
        Assert.Empty(schema.Tables);
        Assert.Empty(schema.Views);
    }

    private static void AssertType(
        ColumnSchema column,
        SchemaTypeAffinity affinity,
        System.Data.DbType dbType,
        Type clrType)
    {
        Assert.Equal(affinity, column.Affinity);
        Assert.Equal(dbType, column.DbType);
        Assert.Equal(clrType, column.ClrType);
    }

    private static SnapDatabase Database(string connectionString) => new(
        SqliteFactory.Instance,
        connectionString,
        SqliteQueryCompiler.Instance);
}
