# Build and test locally

Open **PowerShell in the repository root**: the folder containing `Win_1337_Patch.sln`. Follow the quick start below to build the app, run its automated tests, and open the GUI.

## Quick start

### 1. Install the .NET 10 SDK

You need **Windows**, **PowerShell 7**, and exact **.NET SDK 10.0.401** (pinned by `global.json`). Download that Windows SDK version from the [.NET 10 download page](https://dotnet.microsoft.com/en-us/download/dotnet/10.0). For a typical 64-bit Windows PC, choose the **x64 SDK**.

Choose **SDK**, not just Runtime: the SDK is required to compile the project. You do not need Visual Studio or the old .NET Framework 4.8 Developer Pack for these command-line steps. Internet access is needed for the initial package restore.

After installation, open a new PowerShell window and check:

```powershell
dotnet --list-sdks
```

The list must include `10.0.401`; another SDK version does not satisfy this repository's exact pin.

### 2. Open the project folder

In File Explorer, open the folder containing `Win_1337_Patch.sln`, right-click an empty area, and choose **Open in Terminal**. Use a PowerShell tab. Alternatively, run this command with your own checkout path:

```powershell
Set-Location "W:\GitHub\Win_1337_Apply_Patch"
```

Confirm you are in the right folder:

```powershell
Test-Path .\Win_1337_Patch.sln
```

Expected result: `True`. You do not need an administrator terminal to build or run the automated tests.

### 3. Build the app

```powershell
dotnet restore .\Win_1337_Patch.sln --locked-mode
dotnet build .\Win_1337_Patch.sln --configuration Release --no-restore -p:ReleaseBuild=true
```

This verifies dependency locks and compiles both the app and tests. `-p:ReleaseBuild=true` enables the built-in .NET analyzers, deterministic source paths, and warnings-as-errors, so this single command is also the repository's lint and type-check gate. Wait for **Build succeeded** (0 warnings, 0 errors) before continuing. For every CI/release check in one command, use `pwsh -NoProfile -File scripts/Verify-Build.ps1`.

The executable is created at:

```text
Win_1337_Patch\bin\Release\net10.0-windows\Win_1337_Patch.exe
```

Keep the other files in this folder beside the executable; do not move just the `.exe` elsewhere. A local build is framework-dependent and needs the .NET 10 Desktop Runtime, normally provided by the Windows SDK installation.

### 4. Run the automated tests

```powershell
dotnet test .\Win_1337_Patch.Tests\Win_1337_Patch.Tests.csproj --configuration Release --no-build
```

`--no-build` reuses the Release build from step 3. The current suite contains **79 tests**, all passing in local verification.

The tests use disposable temporary files and read-only configuration checks. They do not patch system files, change ownership, write RunOnce registry entries, save your GUI preferences, or display the app window.

### 5. Open the GUI

Run this only when you are ready to launch the app:

```powershell
Start-Process -FilePath ".\Win_1337_Patch\bin\Release\net10.0-windows\Win_1337_Patch.exe"
```

Normal startup uses `asInvoker` and does not request administrator privileges. Protected patch operations request a one-shot child only after explicit consent. You can also open the executable in File Explorer.

**For a first visual check, do not select a target or click Patch.** Check that the window, icon, labels, and controls display correctly, then close it. Launching the GUI is a manual check, separate from the automated tests.

## Safely testing an actual patch

The app rewrites the selected binary in place and performs PE certificate and checksum work. Only test with a **disposable copy of a Windows `.exe` or `.dll` you are authorized to modify**, plus a `.1337` patch matching that exact binary. Do not use a system DLL, the running patcher itself, or your only copy of a file.

- Keep an untouched original outside the test folder and enable **Create Backup**.
- Leave **Change Ownership** unchecked for an ordinary writable test copy; it defaults off and requires current-operation consent before any ownership fallback.
- Set **Fix File Offset** according to the patch. It is enabled by default and subtracts `0xC00` from each offset; disable it for patches using raw file offsets.
- Confirm the selected target is your disposable copy, especially if a known NVIDIA patch name causes automatic target selection.
- Do not use reboot scheduling for a basic smoke test; it writes a RunOnce entry.

See [README.md](README.md#usage) for GUI/CLI usage and the [patch format](README.md#1337-file-format). You do not need a real patch file to build, run the automated tests, or inspect the GUI.

## After changing code

Rebuild before reopening the app. This command also builds before running tests, so there is no risk of testing stale assemblies with `--no-build`:

```powershell
dotnet test .\Win_1337_Patch.Tests\Win_1337_Patch.Tests.csproj --configuration Release
```

To run just the patch-engine tests:

```powershell
dotnet test .\Win_1337_Patch.Tests\Win_1337_Patch.Tests.csproj --configuration Release --filter "FullyQualifiedName~PatchEngineTests"
```

If you prefer Debug builds, replace `Release` with `Debug` in the build/test commands and launch from `bin\Debug\net10.0-windows` instead.

## Quality gates (formatting, linting, type check, tests)

Enabling the .NET analyzers repository-wide, their analysis level, and code-style enforcement live in `Directory.Build.props`; the style rules and the reasons for the few narrow analyzer opt-outs live in the root `.editorconfig`. `pwsh -NoProfile -File scripts/Verify-Build.ps1` runs all of the gates below in order and is the single local/CI entry point, so use it when you want everything at once.

### Format

Apply the repository style to the whole solution:

```powershell
dotnet format .\Win_1337_Patch.sln
```

### Check formatting (non-mutating)

```powershell
dotnet format .\Win_1337_Patch.sln --verify-no-changes --no-restore
```

This exits with a non-zero code when tracked source is not correctly formatted; drop `--no-restore` if you have not restored yet. CI runs this exact gate.

### Lint / static analysis and type check

```powershell
dotnet build .\Win_1337_Patch.sln --configuration Release --no-restore -p:ReleaseBuild=true
```

The C# compiler is the type checker, so this clean compile of the full solution (app and tests) is the type-check gate. `-p:ReleaseBuild=true` adds `TreatWarningsAsErrors=true`, so analyzer and compiler warnings fail the build; it does not launch the GUI or publish anything.

### Tests

```powershell
dotnet test .\Win_1337_Patch.Tests\Win_1337_Patch.Tests.csproj --configuration Release --no-build --no-restore
```

`--no-build` reuses the Release build from the gate above; omit it to build first. This is the same MSTest suite documented in step 4 and in [README.md](README.md#testing).

### Full verification

```powershell
pwsh -NoProfile -File scripts/Verify-Build.ps1
```

Runs the exact SDK check, locked restore, repository settings/release/workflow fixtures, the formatting verify step, the analyzer/type-check Release build, and the test suite, stopping at the first failure. GitHub Actions runs this same command, so local and hosted results do not drift.

## Publish a folder to test on another PC

Publish the **application project**, not the whole solution. These examples target 64-bit Windows; use `win-x86` and matching output names for 32-bit Windows.

**Framework-dependent:** smaller output; the other PC must have the **.NET Desktop Runtime 10.0 (x64)** installed.

```powershell
dotnet publish .\Win_1337_Patch\Win_1337_Patch.csproj --configuration Release --runtime win-x64 --self-contained false --output .\Win_1337_Patch\bin\publish\win-x64-framework-dependent
```

**Self-contained:** larger output; bundles .NET so the other PC does not need a separate runtime installation.

```powershell
dotnet publish .\Win_1337_Patch\Win_1337_Patch.csproj --configuration Release --runtime win-x64 --self-contained true --output .\Win_1337_Patch\bin\publish\win-x64-self-contained
```

Copy the **entire output folder** to the other PC and open its `Win_1337_Patch.exe`. Both modes require a supported Windows version; administrator approval is only for explicitly authorized protected operations. These raw publish commands are developer outputs. For official two-mode x64 ZIPs, SHA-256, manual tagged drafts, pinned runtime and rebuild commands, use [README.md](README.md#manual-tagged-draft-releases). Republish self-contained builds for runtime security fixes.

To test local two-root rebuild equality without launching the app:

```powershell
pwsh -NoProfile -File scripts/Test-ReproducibleRelease.ps1
```

This compares current-source snapshots, not a newly published official tag or hosted run. CI/release workflows are prepared, but no hosted dispatch has been verified during implementation. The binaries remain unsigned.

## Troubleshooting

| Problem | Action |
|---|---|
| `dotnet` is not recognized | Install the .NET 10 SDK, then reopen PowerShell. |
| SDK unavailable / `NETSDK1045` | Run `dotnet --list-sdks` and install exact SDK 10.0.401. |
| Project or solution file not found | Return to the folder containing `Win_1337_Patch.sln`. |
| Package restore fails | Check internet/NuGet access; retry `dotnet restore .\Win_1337_Patch.sln --locked-mode`. Do not remove locks to bypass drift. |
| `--no-build` cannot find the test assembly | Run the Release build in step 3 first, or omit `--no-build`. |
| Build cannot overwrite a file | Close the running app before rebuilding. |
| App asks for a missing framework | Install the .NET 10 **Desktop** Runtime for the executable's architecture, or use a self-contained publish. |
| A protected patch reports access denied | Authorize the GUI's one-shot elevation prompt or explicit CLI `-elevate`; do not make all startup elevated. |

Launch the native `.exe`, not `dotnet Win_1337_Patch.dll` or `dotnet run`, for manual testing: the executable carries the application's elevation manifest. Do not commit `bin/`, `obj/`, published output, target binaries, private patches, backups, or user-specific settings.
