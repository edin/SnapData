using Microsoft.Data.Sqlite;
using SnapData;

const string connectionString = "Data Source=SnapDataSample;Mode=Memory;Cache=Shared";

await using var anchor = new SqliteConnection(connectionString);
await anchor.OpenAsync();

var database = new SnapDatabase(
    SqliteFactory.Instance,
    connectionString,
    SqliteQueryCompiler.Instance,
    commandObserver: new ConsoleCommandObserver());

await using (var setup = database.Borrow(anchor))
{
    await setup.ExecuteAsync(
        """
        CREATE TABLE authors (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            name TEXT NOT NULL
        );

        CREATE TABLE books (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            author_id INTEGER NOT NULL,
            title TEXT NOT NULL,
            published INTEGER NOT NULL,
            FOREIGN KEY (author_id) REFERENCES authors (id)
        );
        """);
}

await using var session = await database.OpenSessionAsync();
await using (var transaction = await session.BeginTransactionAsync())
{
    var author = new Author { Name = "Ursula K. Le Guin" };
    await transaction.InsertAsync(author);

    await transaction.InsertAsync(new Book
    {
        AuthorId = author.Id,
        Title = "A Wizard of Earthsea",
        Published = true
    });
    await transaction.InsertAsync(new Book
    {
        AuthorId = author.Id,
        Title = "The Tombs of Atuan",
        Published = true
    });

    await transaction.CommitAsync();
}

var authorsWithBooks = await session
    .From<Author>()
    .Include(author => author.Books)
    .ToListAsync();

foreach (var author in authorsWithBooks)
{
    Console.WriteLine($"{author.Name}: {string.Join(", ", author.Books.Select(book => book.Title))}");
}

var books = session.Entity<Book>("b");
var authors = session.Entity<Author>("a");

var projectedBooks = await session
    .From(books)
    .Join(authors, books.Col(book => book.AuthorId) == authors.Col(author => author.Id))
    .Where(books.Col(book => book.Published) == true)
    .Select<BookWithAuthor>(
        books.Col(book => book.Id),
        books.Col(book => book.Title),
        authors.Col(author => author.Name, "AuthorName"))
    .OrderBy(books.Col(book => book.Title))
    .ToListAsync();

Console.WriteLine();
foreach (var book in projectedBooks)
{
    Console.WriteLine($"{book.Title} — {book.AuthorName}");
}

[Table("authors")]
public sealed class Author
{
    [Key]
    [Generated(GeneratedKind.Identity)]
    public long Id { get; set; }

    public required string Name { get; set; }

    [Relation(nameof(Id), nameof(Book.AuthorId))]
    public List<Book> Books { get; set; } = [];
}

[Table("books")]
public sealed class Book
{
    [Key]
    [Generated(GeneratedKind.Identity)]
    public long Id { get; set; }

    [Column("author_id")]
    public long AuthorId { get; set; }

    public required string Title { get; set; }

    public bool Published { get; set; }
}

public sealed class BookWithAuthor
{
    public long Id { get; set; }

    public required string Title { get; set; }

    public required string AuthorName { get; set; }
}

public sealed class ConsoleCommandObserver : CommandObserver
{
    public override void Executed(CommandExecutedContext context) =>
        Console.WriteLine(
            $"[{context.Duration.TotalMilliseconds,7:F2} ms] {context.Command.CommandText.Replace(Environment.NewLine, " ")}");
}
