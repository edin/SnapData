namespace SnapData.Tests;

public sealed class QueryBuilderTests
{
    [Fact]
    public void Compiles_operator_expressions_to_parameterized_sql()
    {
        var since = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var predicate =
            (Exp.Col("active") == true)
            & (Exp.Col("created_at") >= since);

        var query = Sql
            .Select("id", "name")
            .From("users")
            .Where(predicate)
            .OrderBy("name")
            .Limit(20)
            .Build();

        Assert.Equal(
            """
            SELECT "id", "name" FROM "users" WHERE ("active" = @p1 AND "created_at" >= @p2) ORDER BY "name" ASC LIMIT 20
            """,
            query.Text);
        Assert.Equal(true, query.Parameters["p1"]);
        Assert.Equal(since, query.Parameters["p2"]);
    }

    [Fact]
    public void Null_and_empty_in_have_safe_semantics()
    {
        var nullQuery = Sql.Select().From("users")
            .Where(Exp.Col("deleted_at").IsNull())
            .Build();
        var emptyInQuery = Sql.Select().From("users")
            .Where(Exp.Col("id").In())
            .Build();

        Assert.Equal("""SELECT * FROM "users" WHERE "deleted_at" IS NULL""", nullQuery.Text);
        Assert.Equal("""SELECT * FROM "users" WHERE 1 = 0""", emptyInQuery.Text);
    }

    [Fact]
    public void Compact_references_are_normalized_to_structured_ast_nodes()
    {
        var query = Sql
            .Select("u.id", "u.name AS display_name", "u.*")
            .From("app.users AS u")
            .Where(Exp.Col("u.active") == true)
            .OrderBy("u.name")
            .Build();

        Assert.Equal(
            "SELECT \"u\".\"id\", \"u\".\"name\" AS \"display_name\", \"u\".* FROM \"app\".\"users\" AS \"u\" WHERE \"u\".\"active\" = @p1 ORDER BY \"u\".\"name\" ASC",
            query.Text);
    }

    [Fact]
    public void Structured_references_have_fluent_alias_and_column_support()
    {
        var users = Sql.Table("users", schema: "app").As("u");
        var query = Sql
            .Select(users.Col("id"), users.Col("name").As("display_name"))
            .From(users)
            .Build(MySqlQueryCompiler.Instance);

        Assert.Equal(
            "SELECT `u`.`id`, `u`.`name` AS `display_name` FROM `app`.`users` AS `u`",
            query.Text);
        Assert.Equal("users", users.Name);
        Assert.Equal("app", users.Schema);
        Assert.Equal("u", users.Alias);

        var compactWithExplicitAlias = Sql.Table("app.users", alias: "usr");
        Assert.Equal("app", compactWithExplicitAlias.Schema);
        Assert.Equal("users", compactWithExplicitAlias.Name);
        Assert.Equal("usr", compactWithExplicitAlias.Alias);
    }

    [Theory]
    [InlineData("app.users extra tokens")]
    [InlineData("catalog.app.users")]
    [InlineData("users AS")]
    public void Invalid_compact_table_references_are_rejected(string reference)
    {
        Assert.Throws<ArgumentException>(() => Sql.Table(reference));
    }

    [Fact]
    public void Predicate_columns_reject_aliases()
    {
        Assert.Throws<ArgumentException>(() => Exp.Col("u.id AS user_id"));
    }

    [Fact]
    public void Compiles_structured_inner_and_left_joins()
    {
        var users = Sql.Table("app.users").As("u");
        var roles = Sql.Table("app.roles").As("r");
        var audits = Sql.Table("app.audits").As("a");

        var query = Sql
            .Select(
                users.Col("id"),
                users.Col("name"),
                roles.Col("name").As("role_name"))
            .From(users)
            .Join(roles, users.Col("role_id") == roles.Col("id"))
            .LeftJoin(audits, users.Col("id") == audits.Col("user_id"))
            .Where(users.Col("active") == true)
            .Build();

        Assert.Equal(
            "SELECT \"u\".\"id\", \"u\".\"name\", \"r\".\"name\" AS \"role_name\" FROM \"app\".\"users\" AS \"u\" INNER JOIN \"app\".\"roles\" AS \"r\" ON \"u\".\"role_id\" = \"r\".\"id\" LEFT JOIN \"app\".\"audits\" AS \"a\" ON \"u\".\"id\" = \"a\".\"user_id\" WHERE \"u\".\"active\" = @p1",
            query.Text);
    }

    [Fact]
    public void Compiles_compact_right_full_and_cross_joins()
    {
        var query = Sql
            .Select("u.id", "p.code")
            .From("users u")
            .RightJoin("profiles AS p", Exp.Col("u.id") == Exp.Col("p.user_id"))
            .FullJoin("accounts a", Exp.Col("a.user_id") == Exp.Col("u.id"))
            .CrossJoin("permissions permission")
            .Build(MySqlQueryCompiler.Instance);

        Assert.Equal(
            "SELECT `u`.`id`, `p`.`code` FROM `users` AS `u` RIGHT JOIN `profiles` AS `p` ON `u`.`id` = `p`.`user_id` FULL JOIN `accounts` AS `a` ON `a`.`user_id` = `u`.`id` CROSS JOIN `permissions` AS `permission`",
            query.Text);
    }

    [Fact]
    public void Compiles_distinct_grouping_having_and_aggregate_projections()
    {
        var count = Sql.Count("o.id");
        var query = Sql
            .From("orders o")
            .Select(
                Sql.Col("o.customer_id").As("CustomerId"),
                count.As("OrderCount"),
                Sql.Sum("o.total").As("Total"),
                Sql.Avg("o.total").As("Average"),
                Sql.Min("o.total").As("Minimum"),
                Sql.Max("o.total").As("Maximum"))
            .Distinct()
            .Where("o.active = @active", new { active = true })
            .GroupBy("o.customer_id")
            .Having(count > 1)
            .Build();

        Assert.Equal(
            "SELECT DISTINCT \"o\".\"customer_id\" AS \"CustomerId\", COUNT(\"o\".\"id\") AS \"OrderCount\", SUM(\"o\".\"total\") AS \"Total\", AVG(\"o\".\"total\") AS \"Average\", MIN(\"o\".\"total\") AS \"Minimum\", MAX(\"o\".\"total\") AS \"Maximum\" FROM \"orders\" AS \"o\" WHERE \"o\".\"active\" = @p1 GROUP BY \"o\".\"customer_id\" HAVING COUNT(\"o\".\"id\") > @p2",
            query.Text);
        Assert.Equal(true, query.Parameters["p1"]);
        Assert.Equal(1, query.Parameters["p2"]);
    }

    [Fact]
    public void Compiles_count_distinct()
    {
        var query = Sql.From("orders")
            .Select(Sql.Count("customer_id").DistinctValues().As("Customers"))
            .Build();

        Assert.Equal(
            "SELECT COUNT(DISTINCT \"customer_id\") AS \"Customers\" FROM \"orders\"",
            query.Text);
    }

    [Fact]
    public void Compiles_string_helpers_and_negated_set_predicates()
    {
        var query = Sql.From("users")
            .Where(
                Exp.Col("name").StartsWith("Ed")
                & Exp.Col("email").EndsWith("@example.com")
                & Exp.Col("code").Contains("42")
                & Exp.Col("status").NotLike("disabled%")
                & Exp.Col("id").NotIn(1, 2, 3)
                & Exp.Col("score").NotBetween(10, 20))
            .Build();

        Assert.Contains("\"name\" LIKE @p1", query.Text);
        Assert.Contains("\"email\" LIKE @p2", query.Text);
        Assert.Contains("\"code\" LIKE @p3", query.Text);
        Assert.Contains("NOT (\"status\" LIKE @p4)", query.Text);
        Assert.Contains("NOT (\"id\" IN (@p5, @p6, @p7))", query.Text);
        Assert.Contains("NOT (\"score\" BETWEEN @p8 AND @p9)", query.Text);
        Assert.Equal("Ed%", query.Parameters["p1"]);
        Assert.Equal("%@example.com", query.Parameters["p2"]);
        Assert.Equal("%42%", query.Parameters["p3"]);
    }

    [Fact]
    public void Compiles_exists_and_in_subqueries_with_shared_parameters()
    {
        var orderUsers = Sql.From("orders o")
            .Select("o.user_id")
            .Where(Exp.Col("o.total") > 100);
        var audits = Sql.From("audits a")
            .Select("a.id")
            .Where(
                (Exp.Col("a.user_id") == Exp.Col("u.id"))
                & (Exp.Col("a.kind") == "login"));

        var query = Sql.From("users u")
            .Where(
                Exp.Col("u.id").In(orderUsers)
                & Exp.Exists(audits))
            .Build();

        Assert.Equal(
            "SELECT * FROM \"users\" AS \"u\" WHERE (\"u\".\"id\" IN (SELECT \"o\".\"user_id\" FROM \"orders\" AS \"o\" WHERE \"o\".\"total\" > @p1) AND EXISTS (SELECT \"a\".\"id\" FROM \"audits\" AS \"a\" WHERE (\"a\".\"user_id\" = \"u\".\"id\" AND \"a\".\"kind\" = @p2)))",
            query.Text);
        Assert.Equal(100, query.Parameters["p1"]);
        Assert.Equal("login", query.Parameters["p2"]);
    }

    [Fact]
    public void In_subquery_requires_one_selected_expression()
    {
        var subquery = Sql.From("orders").Select("user_id", "total");
        var query = Sql.From("users").Where(Exp.Col("id").In(subquery));

        var exception = Assert.Throws<InvalidOperationException>(() => query.Build());

        Assert.Contains("exactly one", exception.Message);
    }
}
