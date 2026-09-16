# Packed API consumer smoke tests

This is intentionally not in the solution and uses package references only.
After packing all four projects to a local directory, run from the repo root:

```powershell
./eng/verify-api.ps1 -Version 0.2.0 -PackageDirectory artifacts/packages
./eng/verify-packages.ps1 -Version 0.2.0 -PackageDirectory artifacts/packages -ToolDirectory artifacts/tool-smoke-new
```

Use the version actually packed; these commands do not publish or change project
versions. `verify-api` restores into a unique temporary cache, maps SnapData
packages exclusively to the supplied feed, then builds/runs the consumer on
.NET 8 and .NET 10. The cache is retained for inspection and its path is printed.
The checks include typed mutations/returning, batching, transaction rollback,
default/generated inserts, schema/migrations references, old generated compiler
hooks, and a custom executor implementing only the pre-0.4 required members.

`verify-packages` exercises the installed CLI in generated C# and SQL workspaces.
Choose a fresh tool directory; it will not delete an existing directory. Its
temporary migration workspaces are removed after the check, while the tool
installation remains available. Set `NUGET_PACKAGES` to an isolated directory
when testing a working-tree package with an already-published version number.
Neither smoke test replaces live-provider contracts or explicit release approval.
