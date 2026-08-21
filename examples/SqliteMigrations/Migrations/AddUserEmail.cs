using SnapData.Migrations;

namespace SqliteMigrations.Migrations;

public sealed class AddUserEmail : Migration
{
    public override string Id => "202608210002_add_user_email";

    public override void Up(MigrationPlan migration)
    {
        using var table = migration.AlterTable("users");
        table.String("email", 250).Nullable();
        table.CreateUniqueIndex("UX_users_email", "email");
    }

    public override void Down(MigrationPlan migration)
    {
        using var table = migration.AlterTable("users");
        table.DropIndex("UX_users_email");
        table.DropColumn("email");
    }
}
