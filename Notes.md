# Development Notes

## .NET 10 modernization

The `sp-1.6.0` branch is the modernized continuation of the existing
SuperPuTTY WinForms application. It is an in-place migration, not a UI rewrite.

- `SuperPutty` is an SDK-style `net10.0-windows` WinForms project.
- Release output is self-contained for Windows x64 and does not require a
  separately installed .NET Desktop Runtime.
- Assembly and installer versions are `1.6.0`.
- Single-instance command forwarding uses a current-user named pipe instead of
  .NET Remoting.
- File transfer cancellation terminates the active `pscp` process and uses
  cooperative reader shutdown instead of `Thread.Abort`.
- HTTP access uses a shared `HttpClient` rather than `WebRequest`.
- The test project uses Microsoft.NET.Test.Sdk, NUnit 4, and the NUnit adapter.
  The retired NUnit 2 GUI runner and checked-in runner binaries were removed.
- The installer uses SDK-style WiX 6 authoring and packages the complete
  self-contained output.
- CI installs the .NET 10 SDK, builds the solution, runs non-network tests, and
  publishes the MSI.

WiX is intentionally pinned to 6.0.2. WiX 7 requires explicit acceptance of
its Open Source Maintenance Fee EULA; the project does not impose that
acceptance on contributors or build agents.

## Build and test

```powershell
dotnet restore .\SuperPutty.sln
dotnet build .\SuperPutty.sln -c Release --no-restore
dotnet test .\SuperPuttyUnitTests\SuperPuttyUnitTests.csproj `
  -c Release --no-build --filter "TestCategory!~Net"
```

Expected outputs:

- `bin\Release\SuperPutty.exe`
- `SuperPuttyInstaller\bin\Release\SuperPuttySetup.msi`

The network/SCP fixtures require values in
`SuperPuttyUnitTests\app.config`, an installed `pscp.exe`, and a disposable SSH
test service. They are not run in the default CI job.

## CSV session import

The CSV importer supports comments, quoted fields, case-insensitive headers,
protocol and port defaults, nested folders, optional session properties,
whole-file validation, rollback on persistence failure, and atomic replacement
of `Sessions.XML`. `Sessions.example.csv` provides an import template.
