using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.Data.Sqlite;
using SnapData;
using SnapData.Migrations;
using SnapData.Schema;

var compiler = new ConsumerCompiler();
var database = new SnapDatabase(SqliteFactory.Instance, "Data Source=:memory:", compiler);
await using var session = await database.OpenSessionAsync();
await session.ExecuteAsync("CREATE TABLE people (id INTEGER PRIMARY KEY, name TEXT UNIQUE, visits INTEGER DEFAULT 0)");
var generated = new GeneratedOnly();
await session.InsertAsync(generated);
Check(generated.Id > 0 && compiler.GeneratedCalls == 1, "legacy generated compiler hook");
var defaultId = await session.InsertInto<GeneratedOnly>().DefaultValues().Returning<long>(p => p.Id).SingleAsync();
Check(defaultId > generated.Id, "typed default returning");
var people = new[] { new Person { Name = "one" }, new Person { Name = "two" }, new Person { Name = "three" } };
var batch = await session.InsertManyAsync(people, new InsertManyOptions { BatchSize = 2 });
Check(batch == new InsertManyResult(3, 2, 3) && people.All(p => p.Id == 0), "mapped batches");
var id = await session.UpsertInto<Person>().Values(new Person { Name = "one", Visits = 2 })
    .OnConflict(p => p.Name).UpdateColumns(p => p.Visits).Returning<long>(p => p.Id).SingleAsync();
var person = session.Entity<Person>();
var visits = await session.Update<Person>().SetExpression(p => p.Visits, person.Col(p => p.Visits) + 3)
    .Where(p => p.Id == id).Returning<int>(p => p.Visits).SingleAsync();
Check(visits == 5, "typed arithmetic returning");
Check(await session.UpsertInto<Person>().Values(new Person { Name = "one" }).OnConflict(p => p.Name)
    .DoNothing().Returning<long?>(p => p.Id).SingleOrDefaultAsync() is null, "do-nothing returning");
await using (var transaction = await session.BeginTransactionAsync())
{
    var built = transaction.UpsertInto<Person>().Values(new Person { Name = "temporary" })
        .OnConflict(p => p.Name).DoNothing().Build();
    Check(built.Text.Contains("ON CONFLICT"), "transaction-bound Build");
    await transaction.InsertManyAsync(new[] { new Person { Name = "rollback" } });
    await transaction.RollbackAsync();
}
Check(!await session.From<Person>().Where(p => p.Name == "rollback").AnyAsync(), "caller rollback");
var deleted = await session.DeleteFrom<Person>().Where(p => p.Id == id).Returning<string>(p => p.Name).SingleAsync();
Check(deleted == "one", "delete returning");
Check((await new SqliteSchemaInspector(session).GetObjectsAsync()).Any(o => o.Name.Name == "people"), "schema package");
var migration = new SqlMigration("001", "SELECT 1", "SELECT 2");
Check(migration.UpStatements.Single() == "SELECT 1", "migrations package");
IDbExecutor custom = new ConsumerExecutor(session);
Check(custom.Capabilities == QueryCapabilities.None, "custom executor default capabilities");
ExpectUnsupported(() => custom.Compile(Sql.Select("id").From("people")));
ExpectUnsupported(() => custom.UpsertInto<Person>());
ExpectUnsupported(() => custom.InsertManyAsync(Array.Empty<Person>()));
Check(await custom.From<Person>().CountAsync() == 4, "legacy executor forwarding");
Console.WriteLine($"Package API smoke passed on {System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}.");

static void Check(bool condition, string label)
{
    if (!condition) throw new InvalidOperationException("Smoke failed: " + label);
}
static void ExpectUnsupported(Action action)
{
    try { action(); }
    catch (NotSupportedException) { return; }
    throw new InvalidOperationException("Expected explicit unsupported custom executor operation.");
}

sealed class ConsumerCompiler : IQueryCompiler
{
    public int GeneratedCalls { get; private set; }
    public QueryCapabilities Capabilities => SqliteQueryCompiler.Instance.Capabilities;
    public SqlQuery Compile(ISqlQueryBuilder query) => SqliteQueryCompiler.Instance.Compile(query);
    public GeneratedInsertPlan CompileGeneratedInsert(InsertQueryBuilder insert, IReadOnlyList<ColumnReference> columns)
    {
        GeneratedCalls++;
        return SqliteQueryCompiler.Instance.CompileGeneratedInsert(insert, columns);
    }
}

[Table("people")]
sealed class Person
{
    [Key, Column("id"), DatabaseGenerated(DatabaseGeneratedOption.Identity)] public long Id { get; set; }
    [Column("name")] public string Name { get; set; } = "";
    [Column("visits")] public int Visits { get; set; }
}
[Table("people")]
sealed class GeneratedOnly
{
    [Key, Column("id"), DatabaseGenerated(DatabaseGeneratedOption.Identity)] public long Id { get; set; }
}
