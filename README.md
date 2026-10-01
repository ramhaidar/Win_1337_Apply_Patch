# Win_1337_Apply_Patch

Windows desktop utility for applying text-based `.1337` byte patches to `.exe` and `.dll` files. It provides a Windows Forms GUI for interactive use and a command-line mode for scripted patching.

> **Warning:** Patching executable files changes them in place and can make them unusable. Work on a copy where possible, enable backups before patching, and verify that the patch file belongs to the exact target binary. The application requests administrator privileges and should be used only with files you are authorized to modify.

![Win_1337_Apply_Patch screenshot](docs/screenshots/image.png)

## Overview

A `.1337` file identifies an expected target filename and one or more byte replacements. Win_1337_Apply_Patch validates the target name and expected bytes before writing the modified binary. After a successful write it removes the PE certificate and recalculates the PE checksum; checksum normalization is skipped only through the internal `skipChecksum` path used by tests.

The assembly version is `2.3.0.0` (`Win_1337_Patch/Properties/AssemblyInfo.cs`), matching the latest tag `v2.3`.

The project is a fork associated with [@ramhaidar](https://github.com/ramhaidar/Win_1337_Apply_Patch) and retains attribution to the original author, DeltaFoX (DeFconX). Two remotes are configured: `origin` points at the `ramhaidar` fork and `upstream` at [Deltafox79/Win_1337_Apply_Patch](https://github.com/Deltafox79/Win_1337_Apply_Patch).

## Features

- Apply `.1337` patches to Windows `.exe` and `.dll` files.
- Validate the patch header, target filename, offsets, hexadecimal bytes, and expected byte values before modifying the target.
- Create timestamped `.BAK` backups before patching.
- Optionally apply the `0xC00` file-offset adjustment used by the GUI and CLI.
- Remove PE certificates and recalculate the PE checksum after a successful write.
- Use a Windows Forms GUI with file dialogs, drag-and-drop patch selection, tooltips, and persisted checkbox settings.
- Automatically suggest known NVIDIA targets for `nvencodeapi.1337` and `nvencodeapi64.1337` when the expected files exist.
- Patch from the command line with success/error exit codes.
- Optionally update ownership and permissions for protected files through `takeown` and `icacls`.
- Schedule a patch through the current user's Windows `RunOnce` registry entry for execution after the next reboot.

## Tech Stack

- C# 14 Windows Forms application (`OutputType` is `WinExe`).
- .NET 10 (`net10.0-windows`) for both projects; SDK-style MSBuild (`Microsoft.NET.Sdk`) with default file globbing: `Win_1337_Patch/Win_1337_Patch.csproj` and `Win_1337_Patch.Tests/Win_1337_Patch.Tests.csproj`.
- No direct application NuGet packages: the Windows Desktop framework (`UseWindowsForms`) supplies the `System.Configuration.ConfigurationManager` settings API, and the legacy backport packages (`System.Memory`, `System.Buffers`, `System.Numerics.Vectors`, `System.Runtime.CompilerServices.Unsafe`, `System.Resources.Extensions`) were removed during the migration.
- Test packages: `Microsoft.NET.Test.Sdk` 18.10.1, `MSTest.TestAdapter` 4.4.1, `MSTest.TestFramework` 4.4.1 (VSTest workflow via `dotnet test`).
- PE operations use `Imagehlp.dll` (`ImageRemoveCertificate`); protected-file ownership shells out to `takeown` and `icacls` through `cmd.exe`.
- The app requests `requireAdministrator` via `app.manifest` and uses `vampire.ico` as its application icon.

## Repository Structure

```text
Win_1337_Apply_Patch/
├── Win_1337_Patch.sln             # Application and test solution
├── Win_1337_Patch/
│   ├── 1337.cs                     # Windows Forms UI logic (Form1)
│   ├── Ellipsis.cs                 # Auto-Ellipsis path compaction helper (CodeProject-derived)
│   ├── PatchEngine.cs              # Validation, byte patching, backup, PE normalization
│   ├── PatchTargetResolver.cs      # Known automatic target resolution
│   ├── Program.cs                  # GUI entry point and CLI parser/runner (ConsolePatchParser)
│   ├── ScheduledPatchManager.cs    # Windows RunOnce scheduling and command construction
│   ├── mCheckSum.cs                # PE checksum calculation and fixing
│   ├── app.manifest                # Requests administrator execution
│   ├── app.config                  # Modern user-settings defaults (no Framework startup section)
│   └── Properties/                # Assembly metadata, resources, and user settings
├── Win_1337_Patch.Tests/
│   ├── PatchEngineTests.cs
│   ├── ConsolePatchParserTests.cs  # Also contains ScheduledPatchManagerTests
│   ├── RuntimeCompatibilityTests.cs # .NET 10 migration checks (settings, config, form)
│   ├── PatchTargetResolverTests.cs
│   └── Win_1337_Patch.Tests.csproj
├── docs/screenshots/image.png      # GUI screenshot used above
├── LICENSE                         # GNU GPLv3 text
└── README.md
```

## Prerequisites

### Running the application

- Windows.
- For a **framework-dependent** build or publish output: the [.NET Desktop Runtime 10.0](https://dotnet.microsoft.com/en-us/download/dotnet/10.0) for the matching executable architecture (x64 or x86).
- For a **self-contained** publish output: no separate runtime installation; the .NET runtime is bundled with the application.
- Administrator privileges. The application manifest requests `requireAdministrator`, including for normal GUI startup.
- A `.1337` patch file and the matching target `.exe` or `.dll`.

Windows version support follows the official [.NET support policy](https://dotnet.microsoft.com/en-us/platform/support/policy): .NET 10 supports currently in-support Windows versions, and it does not restore compatibility with Windows versions that .NET 10 does not list (the `windows7.0` platform annotation in the source matches the API baseline of the target framework, not a promise that Windows 7 is supported).

### Building and testing

- The **.NET SDK 10.0** (verified with 10.0.401), which includes MSBuild support for `net10.0-windows` SDK-style projects.
- Visual Studio with the .NET desktop development workload, or the SDK alone — no .NET Framework Developer Pack or targeting pack is required.
- Access to restore the test NuGet packages (`Microsoft.NET.Test.Sdk`, `MSTest.TestAdapter`, `MSTest.TestFramework`).
- A Windows environment for the Windows Forms and Windows API portions of the application.

## Setup

1. Clone the repository and enter its directory:

   ```powershell
   git clone https://github.com/ramhaidar/Win_1337_Apply_Patch.git
   Set-Location Win_1337_Apply_Patch
   ```

2. Restore dependencies:

   ```powershell
   dotnet restore .\Win_1337_Patch.sln
   ```

3. Build the solution in Release configuration:

   ```powershell
   dotnet build .\Win_1337_Patch.sln --configuration Release
   ```

   Alternatively, open `Win_1337_Patch.sln` in Visual Studio and build the `Release | Any CPU` solution configuration.

## Configuration

The application has no required environment variables or external service configuration.

The following user-scoped settings are defined in `Win_1337_Patch/Properties/Settings.settings` and default to `True`:

| Setting | Effect |
|---|---|
| `fixoffset` | Enables the `0xC00` file-offset adjustment by default. |
| `backup` | Enables timestamped target backups by default. |
| `changeOwnership` | Enables the ownership/permission option by default. |

GUI settings are persisted through the normal .NET user-settings mechanism (`Properties.Settings.Default.Save()`), with the same defaults mirrored in `Win_1337_Patch/app.config` using the modern `System.Configuration.ConfigurationManager` section identities. No secrets are stored in the repository. Do not commit target binaries, private patch files, backups, or generated build output.

> **Note (post-migration):** Settings keys, names, and defaults are unchanged, but the .NET 10 runtime can store `user.config` in a different location than the previous .NET Framework 4.8 build. Existing preferences from a Framework installation are **not** imported automatically; the first run on a .NET 10 build starts from the defaults above. There is no migration or import feature.

## Usage

### GUI

1. Launch the built `Win_1337_Patch.exe` as an administrator.
2. Select a `.1337` file, drag it onto the patch-file box, or double-click the patch-file box to open the dialog.
3. Select the target `.exe` or `.dll`. The application may auto-select a known NVIDIA target; otherwise use **Select EXE** to choose the file manually.
4. Review the options:
   - **Fix File Offset**: subtract `0xC00` from each patch offset before applying it.
   - **Create Backup**: create a timestamped `<target>.<timestamp>.BAK` copy before writing.
   - **Change Ownership**: run `takeown` and `icacls` for a protected target.
5. Click **Patch** and review the result message.

### Command line

The executable supports patch mode with this form:

```text
Win_1337_Patch.exe -patch <1337-file> <target-file> [options]
```

The executable is built as a Windows GUI application, so command-line mode allocates a console (`AllocConsole`) for output. The process returns exit code `0` on success and `1` on validation, scheduling, or patching failure.

#### Options

| Option | Description |
|---|---|
| `-patch`, `--patch` | Select command-line patch mode. |
| `-fileoffset`, `--fileoffset`, `-offset`, `--offset` | Apply the `0xC00` offset adjustment. |
| `-backup`, `--backup`, `-b` | Create a timestamped backup before patching. |
| `-takeownership`, `--takeownership`, `-take-ownership`, `--take-ownership` | Run `takeown` and `icacls` for the target. Requires administrator privileges. |
| `-schedule`, `--schedule`, `-runonce`, `--run-once`, `-run-on-reboot`, `--run-on-reboot` | Store the patch command in the current user's `RunOnce` registry key for the next boot. |
| `-h`, `--help`, `/?`, `/help` | Display usage information. |

> **Note:** The application's built-in usage text prints `-help` as an option, but `Program.IsHelpSwitch` actually matches only `-h`, `--help`, `/?`, and `/help`. Bare `-help` is not treated as a help request.

Examples:

```powershell
# Apply a patch
.\Win_1337_Patch.exe -patch .\patch.1337 .\target.dll

# Apply a patch and keep a backup
.\Win_1337_Patch.exe -patch .\patch.1337 .\target.dll -backup

# Apply a patch with the 0xC00 adjustment
.\Win_1337_Patch.exe -patch .\patch.1337 .\target.dll -fileoffset

# Patch a protected Windows file and keep a backup
.\Win_1337_Patch.exe -patch .\patch.1337 C:\Windows\System32\target.dll -takeownership -backup

# Schedule the patch for the next reboot
.\Win_1337_Patch.exe -patch .\patch.1337 .\target.dll -schedule -backup
```

The `-scheduledrun` / `--scheduled-run` switch is an internal marker added to commands created by the scheduler. It is not normally needed for manual invocation.

### Automatic target resolution

The GUI resolves these patch filenames, when the candidate target exists:

| Patch file | Suggested target |
|---|---|
| `nvencodeapi.1337` | `%WINDIR%\SysWOW64\nvEncodeAPI.dll` |
| `nvencodeapi64.1337` | `%WINDIR%\System32\nvEncodeAPI64.dll` |

Automatic resolution is limited to these names. Other patch files require manual target selection.

## `.1337` File Format

The patch engine expects a text file whose first line starts with `>` and whose remaining non-empty lines contain hexadecimal byte replacements:

```text
>target.exe
1A3F:90->EB
1A40:00->90
```

- The header is `>` followed by the expected target filename. The comparison uses the filename, not the full path, and is case-insensitive.
- Each patch entry is `offset:expected->replacement`.
- Offsets and bytes are hexadecimal; the offset is parsed as a 32-bit hex integer.
- The expected byte must match the target at the computed offset; otherwise the patch fails before the target is written.
- With **Fix File Offset** enabled, the engine subtracts `0xC00` from every declared offset.
- Blank lines after the header are ignored.

## Architecture

- **`PatchEngine`** is shared by the GUI and CLI. It resolves paths, validates files and patch entries, checks expected bytes, optionally grants ownership, optionally creates a backup, writes the modified bytes, removes the PE certificate, and recalculates the checksum.
- **`Program`** chooses CLI mode when `-patch`/`--patch` is present; otherwise it starts the Windows Forms application. The nested `ConsolePatchParser` handles CLI switches and positional paths.
- **`PatchTargetResolver`** contains the known NVIDIA filename-to-target mappings used by the GUI.
- **`ScheduledPatchManager`** validates paths and writes a generated command to `HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\RunOnce`, with an entry name of the form `Win_1337_Patch_<timestamp>_<guid>`. The command runs the application with `-scheduledrun` after the next boot/login cycle.
- **`mCheckSum`** performs PE checksum recalculation (`FixCheckSum`) after patching.
- **`Ellipsis`** compacts long paths for display in the form's text boxes.

`Win_1337_Patch/Properties/AssemblyInfo.cs` declares `InternalsVisibleTo("Win_1337_Patch.Tests")`, which is how the MSTest project exercises the internal `ConsolePatchParser`, `PatchScheduleDescriptor`, `ScheduledPatchManager`, and the internal `skipChecksum` constructor.

## Common Commands

Run these from the repository root in PowerShell:

| Task | Command | Status |
|---|---|---|
| Restore | `dotnet restore .\Win_1337_Patch.sln` | Verified working on .NET SDK 10.0.401 |
| Build Debug | `dotnet build .\Win_1337_Patch.sln --configuration Debug` | Supported with the .NET 10 SDK; no targeting pack needed |
| Build Release | `dotnet build .\Win_1337_Patch.sln --configuration Release` | Verified working; builds clean with 0 warnings/errors on .NET SDK 10.0.401 |
| Run all tests | `dotnet test .\Win_1337_Patch.Tests\Win_1337_Patch.Tests.csproj --configuration Release` | Verified working; 15 tests in 5 classes, all passing on Windows |
| Run focused tests | `dotnet test .\Win_1337_Patch.Tests\Win_1337_Patch.Tests.csproj --filter "FullyQualifiedName~PatchEngineTests"` | Supported by the MSTest test runner |
| Lint | — | No lint command/configuration was found in the repository |
| Format | — | No formatter command/configuration was found in the repository |
| Typecheck | — | No separate typecheck command was found; compilation is the available C# check |

Visual Studio users can run the full suite through **Test > Run All Tests**.

## Testing

Tests are in `Win_1337_Patch.Tests` and use MSTest (`Microsoft.NET.Test.Sdk` 18.10.1, `MSTest.TestAdapter` 4.4.1, and `MSTest.TestFramework` 4.4.1, run through the VSTest-based `dotnet test` workflow). Test classes use `[TestClass]` with `[TestMethod]` methods, plus `[STATestMethod]` for the tests that must run on an STA thread. Current coverage — 15 tests in 5 classes:

- `PatchEngineTests` - patch application, backup creation (including verifying backup contents), header validation, target-name validation, and expected-byte mismatch handling. These use `[TestInitialize]`/`[TestCleanup]` with per-test temporary directories and the internal `skipChecksum: true` path to avoid PE normalization on fake files.
- `ConsolePatchParserTests` - CLI schedule and scheduled-run flag parsing.
- `ScheduledPatchManagerTests` - scheduled command-line construction (defined inside `ConsolePatchParserTests.cs`).
- `PatchTargetResolverTests` - known and unknown automatic target resolution, and empty-path handling.
- `RuntimeCompatibilityTests` - .NET 10 migration checks: the three settings defaults remain `True`, the mapped `app.config` fixture deserializes through the modern `System.Configuration.ConfigurationManager` sections, and `Form1` constructs on an STA thread with its original icon, title, layout mode, and font without being shown.

Run the test project on Windows with the .NET 10 SDK and restored test packages. Tests use synthetic temporary files, an `InternalsVisibleTo` internal bypass, and a copy of the application `app.config` as a read-only fixture; they never write the real user settings, show a window, or touch real system files.

Coverage verifies settings metadata, settings/config construction, embedded resources, scheduler command construction, and patch-engine logic — not cross-launch settings persistence, real PE rewriting of system files, visual high-DPI rendering, or a real reboot-scheduled run.

## Development Notes

- The application targets `AnyCPU` and uses the `Debug` and `Release` configurations defined in the solution.
- The application manifest requests administrator execution and disables the assumption that the process can run as a normal unelevated desktop process.
- Patch files are validated against the current target bytes. Do not reuse a patch against a different binary revision without confirming its expected bytes.
- Backups are created only when the backup option is enabled. A backup failure prevents the patch from proceeding; if no backup was requested, the target is written without a backup copy.
- Ownership handling runs `takeown /F <file>` and `icacls <file> /grant Administrators:F` and should be treated as a privileged operation.
- A scheduled patch writes a `RunOnce` value under the current user's registry hive. Inspect or remove that entry through normal Windows registry administration if a scheduled operation must be cancelled.
- Tests must run on Windows: the application references Windows Forms, `Imagehlp.dll`, and the registry.
- There are no repository-defined migrations, seed steps, code-generation steps, CI workflows, or automated release scripts.

## Deployment

No deployment pipeline, installer project, container configuration, or release automation was found in this repository. A normal Release build produces `Win_1337_Patch/bin/Release/net10.0-windows/Win_1337_Patch.exe` (framework-dependent output under the modern SDK-style path).

### Publishing

The project supports directory-based `dotnet publish` in framework-dependent and self-contained modes. Verified examples (all completed with exit code 0 and no warnings during the migration verification):

```powershell
# Framework-dependent: requires the .NET Desktop Runtime 10.0 (x64) on the target machine
dotnet publish .\Win_1337_Patch\Win_1337_Patch.csproj --configuration Release --runtime win-x64 --self-contained false --output .\Win_1337_Patch\bin\publish\win-x64-framework-dependent

# Self-contained: bundles the .NET runtime; no separate runtime install needed, but the output is much larger
dotnet publish .\Win_1337_Patch\Win_1337_Patch.csproj --configuration Release --runtime win-x64 --self-contained true --output .\Win_1337_Patch\bin\publish\win-x64-self-contained
```

The 32-bit alternative (`--runtime win-x86`, with either `--self-contained` value) was verified the same way during the migration; use it only for 32-bit scenarios.

Publishing notes:

- Distribute the **complete published directory**, not just the `Win_1337_Patch.exe`. The `.dll`, `.deps.json`, `.runtimeconfig.json`, and `.dll.config` files next to it are required at runtime.
- A framework-dependent output requires the .NET Desktop Runtime 10.0 installed **for the matching architecture** (an x64 build needs the x64 runtime, x86 needs the x86 runtime).
- A self-contained output bundles the runtime and needs no .NET installation, but it is significantly larger and must be **republished** to pick up .NET runtime security updates.
- The published `.exe` is the native apphost; it carries the `requireAdministrator` manifest, the application icon, and version `2.3.0.0` resources.
- No trimming, Native AOT, single-file packaging, or ReadyToRun options are used or supported by this repository's configuration.

## Troubleshooting

### The patch is rejected as invalid

Confirm that the first line is a `>` header and that the filename after `>` matches the target filename. The comparison ignores case but does not ignore a different filename.

### The expected byte does not match

The patch is for a different binary revision, the target was already modified, or the offset mode is wrong. Restore a known-good backup and verify whether the patch requires **Fix File Offset** before trying again.

### A protected file cannot be changed

Run the application elevated, confirm that the target is not locked by another process, and use **Change Ownership** / `-takeownership` only when appropriate. That option invokes Windows `takeown` and `icacls`; it does not bypass every lock or security policy.

### A scheduled patch does not run

Scheduling uses the current user's `HKCU` `RunOnce` entry and runs only after the next Windows startup/login flow. Confirm that the source patch and target paths still exist and that the generated command can start the application with administrator privileges.

### The build reports an unknown target framework or missing SDK

The projects target `net10.0-windows` and require the **.NET SDK 10.0**. Verify the installed SDK with `dotnet --list-sdks` and install the latest .NET 10 SDK if none is listed. Older SDKs (for example .NET 8 or 9) cannot build a `net10.0` target. No .NET Framework Developer Pack or targeting pack is needed anymore.

### A framework-dependent build does not start on another machine

A framework-dependent build or publish requires the **.NET Desktop Runtime 10.0 for the matching architecture** (x64 vs x86) on the target machine. If the runtime is missing or the architecture does not match, the apphost fails to start. Either install the matching Desktop Runtime or use a self-contained publish, which bundles the runtime.

## Contributing

1. Create a focused branch for your change.
2. Keep changes limited to the requested behavior and preserve the SDK-style project structure, the shared `PatchEngine` design, and the .NET 10 target.
3. Add or update MSTest coverage for patch validation, CLI parsing, target resolution, scheduling, or runtime-compatibility changes.
4. Run the solution build and the relevant test project with the .NET 10 SDK before opening a pull request.
5. Do not include target binaries, private patch files, backups, `bin`/`obj`/publish output, or user-specific settings in commits.

No separate `CONTRIBUTING.md` was found. For issues and pull requests, use the repository's [GitHub project](https://github.com/ramhaidar/Win_1337_Apply_Patch).

## License

This project is licensed under the [GNU General Public License v3.0](LICENSE). The repository also contains attribution to the original author, DeltaFoX (DeFconX).
