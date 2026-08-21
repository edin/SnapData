[Table("books")]
public sealed class Book
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public long Id { get; set; }

    [Column("author_id")]
    public long AuthorId { get; set; }

    public required string Title { get; set; }

    public bool Published { get; set; }
}
