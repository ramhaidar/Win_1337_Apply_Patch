# Win_1337_Apply_Patch v2.2

A professional Windows tool to apply .1337 patch files directly into .exe or .dll files. Features both an intuitive GUI and a powerful command-line interface for automation.

![Screenshot](/docs/screenshots/image.png)

## Features

### Core Functionality
- **Apply .1337 patches** to Windows executables (.exe) and libraries (.dll)
- **Binary byte-level patching** with validation and safety checks
- **Automatic PE checksum recalculation** after patching
- **Certificate removal** from PE files for modified binaries

### User Interface
- **Modern GUI** with drag-and-drop support
- **Settings persistence** - remembers your last used files and preferences
- **Case-preserving file dialogs** - properly handles filename case sensitivity
- **Visual feedback** with detailed tooltips and status messages

### Automation & CLI
- **Full command-line interface** for unattended/scripted patching
- **Scheduled patching** - schedule patches to run on next system boot
- **Exit codes** for integration with batch scripts and CI/CD pipelines

### Safety Features
- **Automatic backups** - timestamped .BAK files before patching
- **File ownership management** - uses `takeown` and `icacls` for protected files
- **Byte validation** - verifies expected bytes before patching
- **File offset adjustment** - optional 0xC00 offset fix for specific use cases

## Installation

### Requirements
- Windows 7 or later
- .NET Framework 4.5 or higher
- Administrator privileges (for taking ownership of protected files)

### Download
Download the latest release from the [Releases](https://github.com/ramhaidar/Win_1337_Apply_Patch/releases) page.

### Build from Source
```bash
# Clone the repository
git clone https://github.com/ramhaidar/Win_1337_Apply_Patch.git

# Open the solution in Visual Studio
cd Win_1337_Apply_Patch
start Win_1337_Patch.sln

# Build the solution (Ctrl+Shift+B)
```

## Usage

### GUI Mode

1. Launch `Win_1337_Apply_Patch.exe`
2. Select a `.1337` patch file (or drag and drop it onto the window)
3. Select the target `.exe` or `.dll` file to patch
4. Configure options:
   - **Fix File Offset**: Apply 0xC00 offset adjustment
   - **Create Backup**: Save a timestamped backup before patching
   - **Change Ownership**: Take ownership of protected system files
5. Click **Patch** to apply

### Command Line Interface

```batch
Win_1337_Apply_Patch.exe -patch <1337-file> <target-file> [options]
```

#### Options
| Option | Description |
|--------|-------------|
| `-fileoffset`, `--fileoffset` | Apply 0xC00 file offset adjustment |
| `-backup`, `--backup` | Create timestamped backup before patching |
| `-takeownership` | Take ownership of protected files (requires admin) |
| `-schedule`, `--schedule` | Schedule patch to run on next boot |
| `-help`, `--help`, `/?` | Show help text |

#### Examples

**Basic patching:**
```batch
Win_1337_Apply_Patch.exe -patch patch.1337 target.exe
```

**Patch with backup and offset fix:**
```batch
Win_1337_Apply_Patch.exe -patch patch.1337 target.exe -backup -fileoffset
```

**Patch protected system file:**
```batch
Win_1337_Apply_Patch.exe -patch patch.1337 C:\Windows\System32\target.dll -takeownership -backup
```

**Schedule patch for next boot:**
```batch
Win_1337_Apply_Patch.exe -patch patch.1337 target.exe -schedule -backup
```

### Exit Codes
| Code | Meaning |
|------|---------|
| 0 | Success |
| 1 | Error (check console output for details) |

## .1337 File Format

The .1337 patch file format is a simple text-based format:

```
>target.exe
<offset>:<expected_byte>-><replacement_byte>
```

**Example:**
```
>notepad.exe
1A3F:90->EB
1A40:00->90
```

- Line 1: `>` followed by the expected target filename
- Subsequent lines: `offset:expected->replacement` in hexadecimal

## Architecture

### Project Structure
```
Win_1337_Apply_Patch/
├── Win_1337_Patch/              # Main application
│   ├── PatchEngine.cs           # Core patching logic
│   ├── ScheduledPatchManager.cs # Boot-time scheduling
│   ├── 1337.cs                  # GUI form
│   └── Program.cs               # Entry point & CLI
├── Win_1337_Patch.Tests/        # Unit tests
│   ├── PatchEngineTests.cs
│   └── ConsolePatchParserTests.cs
└── Win_1337_Patch.sln           # Solution file
```

### Key Components

#### PatchEngine
The core patching engine (`PatchEngine.cs`) provides:
- Validation of patch files and target binaries
- Byte-level patching with safety checks
- Backup creation
- PE checksum normalization
- File ownership management

#### ScheduledPatchManager
Handles scheduling patches to run on next boot via Windows RunOnce registry entries.

#### ConsolePatchParser
Parses command-line arguments with support for various option formats.

## Testing

The project includes comprehensive unit tests:

```bash
# Run tests in Visual Studio
Test > Run All Tests

# Or use vstest.console
vstest.console.exe Win_1337_Patch.Tests.dll
```

### Test Coverage
- Patch application with backups
- Invalid header detection
- Target filename validation
- Byte mismatch detection
- CLI argument parsing

## Contributing

Contributions are welcome! Please:

1. Fork the repository
2. Create a feature branch (`git checkout -b feature/amazing-feature`)
3. Commit your changes (`git commit -m 'Add amazing feature'`)
4. Push to the branch (`git push origin feature/amazing-feature`)
5. Open a Pull Request

### Development Guidelines
- Follow existing code style
- Add unit tests for new features
- Update documentation for API changes
- Ensure all tests pass before submitting

## Changelog

### v2.2.0 (Latest)
- **Added**: Command-line interface for automation
- **Added**: Scheduled patching for next boot
- **Added**: Unit test project with comprehensive coverage
- **Added**: File ownership management for protected files
- **Added**: Automatic backup creation with timestamps
- **Refactored**: Core patching logic moved to `PatchEngine` class
- **Fixed**: Case sensitivity issues in file dialogs
- **Improved**: Error handling and user feedback

### v2.1.0
- **Added**: File offset fix option (0xC00 adjustment)
- **Added**: Settings persistence
- **Improved**: UI with drag-and-drop support

### v2.0.0
- **Added**: PE checksum recalculation
- **Added**: Certificate removal
- **Initial**: GUI implementation

## License

This project is open source. See the repository for license details.

## Credits

- **Maintainer**: [@ramhaidar](https://github.com/ramhaidar)
- **Fork Repository**: https://github.com/ramhaidar/Win_1337_Apply_Patch
- **Original Author**: DeltaFoX (DeFconX)
- **Copyright**: © 2026
- **OriginalRepository**: https://github.com/Deltafox79/Win_1337_Apply_Patch

## Support

For issues, feature requests, or questions:
- Open an issue on [GitHub](https://github.com/ramhaidar/Win_1337_Apply_Patch/issues)
- Check existing issues before creating new ones

---

**Warning**: Patching executable files can cause them to malfunction. Always create backups before patching and use at your own risk.
