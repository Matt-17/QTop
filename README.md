# QTop

QTop is a Windows desktop process manager focused on quick triage:
find a process, understand what it is, and kill it safely.

## What it does

- Shows all processes as an expandable parent/child tree with icons, PID, category, CPU %, CPU time, memory, and instance count.
- Categorizes processes (Apps, Background, Services, Windows/System) and filters them via pill buttons with live match counts and total CPU %.
- Search by process name, PID, executable path, or service name (F3 / Ctrl+F focuses the search box, Esc clears it).
- Detail panel with command line, user/session, integrity level, version metadata, and hosted services.
- Kill with graceful-close-first for apps, force kill with process tree, PID-reuse protection, and an optional confirmation setting (Del / Ctrl+Del).

## Administrator rights

The installer is machine-wide, and the app requires administrator rights when launched so that
process details and termination work across all sessions. The desktop app declares
`requireAdministrator`, so Windows shows the UAC elevation prompt.

## Installation

```powershell
winget install Code-iX.QTop
```

Manual fallback: download `QTop-X.Y.Z-x64.msi` from the latest GitHub Release and install it.

## Build and test

```powershell
dotnet restore QTop.slnx
dotnet build QTop.slnx -c Release
dotnet test QTop.slnx
```

## Releasing

- Push a tag like `v1.2.3`.
- The release workflow builds/tests, publishes self-contained `win-x64`, builds `QTop-1.2.3-x64.msi`, creates a GitHub Release, and runs `wingetcreate update --submit --no-open` when `WINGET_TOKEN` is configured.
- For automatic winget PRs, `WINGET_TOKEN` must be a classic GitHub PAT with `public_repo` scope.
- The first winget submission must be done manually with `wingetcreate new`; automated updates take over once the package is merged into `microsoft/winget-pkgs`.
- Keep the installer `UpgradeCode` stable across releases.
- The winget identifier is `Code-iX.QTop`; the installer is self-contained and should not declare a .NET runtime dependency.
