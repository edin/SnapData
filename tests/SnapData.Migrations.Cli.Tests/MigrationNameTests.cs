using SnapData.Migrations.Cli.Scaffolding;

namespace SnapData.Migrations.Cli.Tests;

public sealed class MigrationNameTests
{
    [Theory]
    [InlineData("Create users", "CreateUsers", "create_users")]
    [InlineData("create-users", "CreateUsers", "create_users")]
    [InlineData("create_users", "CreateUsers", "create_users")]
    [InlineData("CreateUsers", "CreateUsers", "create_users")]
    [InlineData("AddURL2Column", "AddUrl2Column", "add_url_2_column")]
    public void Normalizes_human_and_code_style_names(
        string value,
        string className,
        string slug)
    {
        var result = MigrationName.Parse(value);

        Assert.Equal(className, result.ClassName);
        Assert.Equal(slug, result.Slug);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("123 users")]
    [InlineData("create.users")]
    [InlineData("créate users")]
    public void Rejects_names_that_cannot_produce_safe_csharp_identifiers(string value)
    {
        Assert.Throws<ArgumentException>(() => MigrationName.Parse(value));
    }

    [Fact]
    public void Rejects_names_that_are_too_long_for_portable_file_paths()
    {
        var value = new string('a', 129);

        var exception = Assert.Throws<ArgumentException>(() => MigrationName.Parse(value));

        Assert.Contains("128", exception.Message);
    }
}
