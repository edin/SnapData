using SnapData.Migrations;

namespace SqliteMigrations.Migrations;

public sealed class CreateUsers : Migration
{
    public override string Id => "202608210001_create_users";

    public override void Up(MigrationPlan migration)
    {
        using var table = migration.CreateTable("users");
        table.Identity();
        table.String("name", 150);
        table.Boolean("active").Default(true);
        table.Timestamps();
    }

    public override void Down(MigrationPlan migration) =>
        migration.DropTable("users");
}
