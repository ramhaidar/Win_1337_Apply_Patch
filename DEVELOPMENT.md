# Build and test locally

Open **PowerShell in the repository root**: the folder containing `Win_1337_Patch.sln`. Follow the quick start below to build the app, run its automated tests, and open the GUI.

## Quick start

### 1. Install the .NET 10 SDK

You need **Windows** and the **.NET 10 SDK**. Download the Windows installer from the [.NET 10 download page](https://dotnet.microsoft.com/en-us/download/dotnet/10.0). For a typical 64-bit Windows PC, choose the **x64 SDK**.

Choose **SDK**, not just Runtime: the SDK is required to compile the project. You do not need Visual Studio or the old .NET Framework 4.8 Developer Pack for these command-line steps. Internet access is needed for the initial package restore.

After installation, open a new PowerShell window and check:

```powershell
dotnet --list-sdks
```

The list must include a `10.0.xxx` SDK. If you already have one, skip installation.

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
dotnet build .\Win_1337_Patch.sln --configuration Release
```

This restores packages automatically and compiles both the app and its tests. Wait for **Build succeeded** before continuing.

The executable is created at:

```text
Win_1337_Patch\bin\Release\net10.0-windows\Win_1337_Patch.exe
```

Keep the other files in this folder beside the executable; do not move just the `.exe` elsewhere. A local build is framework-dependent and needs the .NET 10 Desktop Runtime, normally provided by the Windows SDK installation.

### 4. Run the automated tests

```powershell
dotnet test .\Win_1337_Patch.Tests\Win_1337_Patch.Tests.csproj --configuration Release --no-build
```

`--no-build` reuses the Release build from step 3. At the time this guide was written, the suite contained **15 tests**, all passing.

The tests use disposable temporary files and read-only configuration checks. They do not patch system files, change ownership, write RunOnce registry entries, save your GUI preferences, or display the app window.

### 5. Open the GUI

Run this only when you are ready to launch the app:

```powershell
Start-Process -FilePath ".\Win_1337_Patch\bin\Release\net10.0-windows\Win_1337_Patch.exe" -Verb RunAs
```

Approve the Windows **User Account Control** prompt. The application always requests administrator privileges, including when you only want to inspect the GUI. You can also open the executable in File Explorer.

**For a first visual check, do not select a target or click Patch.** Check that the window, icon, labels, and controls display correctly, then close it. Launching the GUI is a manual check, separate from the automated tests.

## Safely testing an actual patch

The app rewrites the selected binary in place and performs PE certificate and checksum work. Only test with a **disposable copy of a Windows `.exe` or `.dll` you are authorized to modify**, plus a `.1337` patch matching that exact binary. Do not use a system DLL, the running patcher itself, or your only copy of a file.

- Keep an untouched original outside the test folder and enable **Create Backup**.
- Disable **Change Ownership** for an ordinary writable test copy; this option is enabled by default and invokes privileged ownership/permission commands.
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

Copy the **entire output folder** to the other PC and open its `Win_1337_Patch.exe`. Both modes still require a supported Windows version and administrator approval. Republish self-contained builds to include updated .NET runtime security fixes. See [README.md](README.md#deployment) for details.

## Troubleshooting

| Problem | Action |
|---|---|
| `dotnet` is not recognized | Install the .NET 10 SDK, then reopen PowerShell. |
| SDK cannot target .NET 10 / `NETSDK1045` | Run `dotnet --list-sdks` and install a .NET 10 SDK. |
| Project or solution file not found | Return to the folder containing `Win_1337_Patch.sln`. |
| Package restore fails | Check internet/NuGet access; retry `dotnet restore .\Win_1337_Patch.sln`. |
| `--no-build` cannot find the test assembly | Run the Release build in step 3 first, or omit `--no-build`. |
| Build cannot overwrite a file | Close the running app before rebuilding. |
| App asks for a missing framework | Install the .NET 10 **Desktop** Runtime for the executable's architecture, or use a self-contained publish. |
| GUI launch reports that elevation is required | Use the `Start-Process ... -Verb RunAs` command in step 5 and approve UAC. |

Launch the native `.exe`, not `dotnet Win_1337_Patch.dll` or `dotnet run`, for manual testing: the executable carries the application's elevation manifest. Do not commit `bin/`, `obj/`, published output, target binaries, private patches, backups, or user-specific settings.
