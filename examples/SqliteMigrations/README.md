# SQLite migration example

This project is a small, executable SnapData migration bundle. It creates a
`users` table and then adds an indexed email column. The SQLite database is
created locally as `example.db` and is ignored by Git.

From this directory, run:

```powershell
snapdata list
snapdata status
snapdata script
snapdata migrate
snapdata status
snapdata rollback
```

Use `snapdata migrate --yes` to skip the confirmation prompt. Delete
`example.db` when you want to start the example again from an empty database.
Use `snapdata rollback --steps 2` to revert both example migrations.
