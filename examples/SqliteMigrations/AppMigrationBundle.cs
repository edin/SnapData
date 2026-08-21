using SnapData.Migrations;
using SqliteMigrations.Migrations;

namespace SqliteMigrations;

public sealed class AppMigrationBundle : MigrationBundle
{
    protected override void Configure(MigrationCollection migrations)
    {
        migrations
            .Add<CreateUsers>()
            .Add<AddUserEmail>();
    }
}
