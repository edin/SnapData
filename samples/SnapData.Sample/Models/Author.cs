using SnapData;

[Table("authors")]
public sealed class Author
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public long Id { get; set; }

    public required string Name { get; set; }

    [Relation(nameof(Id), nameof(Book.AuthorId))]
    public List<Book> Books { get; set; } = [];
}
