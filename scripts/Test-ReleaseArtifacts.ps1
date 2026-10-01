param([string] $ModulePath = (Join-Path $PSScriptRoot 'ReleaseArtifacts.psm1'))

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$temporaryDirectory = Join-Path ([IO.Path]::GetTempPath()) ('patch-artifact-tests-' + [Guid]::NewGuid())
$null = [IO.Directory]::CreateDirectory($temporaryDirectory)
$failures = 0
$tests = 0

function Assert-True([bool] $Condition, [string] $Message) {
    if (-not $Condition) { throw $Message }
}
function Assert-Throws([scriptblock] $Body, [string] $Message) {
    $threw = $false
    try { & $Body } catch { $threw = $true }
    Assert-True $threw $Message
}
function Test-Case([string] $Name, [scriptblock] $Body) {
    $script:tests++
    try {
        Assert-True ([IO.File]::Exists($ModulePath)) 'ReleaseArtifacts module is missing.'
        Import-Module $ModulePath -Force
        & $Body
        Write-Host "PASS $Name"
    }
    catch {
        $script:failures++
        Write-Host "FAIL ${Name}: $_"
    }
}
function Write-PublishFixture([string] $Path, [switch] $Reverse) {
    $null = [IO.Directory]::CreateDirectory($Path)
    $names = @('Win_1337_Patch.exe', 'Win_1337_Patch.dll', 'Win_1337_Patch.pdb', 'Win_1337_Patch.deps.json', 'Win_1337_Patch.runtimeconfig.json', 'Win_1337_Patch.dll.config', 'System.Windows.Forms.dll')
    if ($Reverse) { [Array]::Reverse($names) }
    foreach ($name in $names) { [IO.File]::WriteAllText((Join-Path $Path $name), "fixture $name", [Text.UTF8Encoding]::new($false)) }
    $null = [IO.Directory]::CreateDirectory((Join-Path $Path 'it'))
    [IO.File]::WriteAllText((Join-Path $Path 'it/System.Windows.Forms.resources.dll'), 'satellite')
}

try {
    Test-Case 'Tag grammar prevents unsafe ref and filename inputs' {
        Assert-True ((Assert-ReleaseTag 'v2.4') -ceq 'v2.4') 'Ordinary version tag was rejected.'
        Assert-True ((Assert-ReleaseTag '1.8') -ceq '1.8') 'Legacy numeric tag was rejected.'
        foreach ($value in @('', '-x', '../x', 'x..y', 'x.', 'x.lock', 'refs/tags/v2.4', 'v2.4;exit', "v2.4`n", ('a' * 65), ('a' * 40))) {
            Assert-Throws { Assert-ReleaseTag $value } "Unsafe tag accepted: $value"
        }
    }
    Test-Case 'Exact tag peeling uses argument arrays and returns commit time' {
        $script:gitCalls = [Collections.Generic.List[string]]::new()
        $runner = {
            param([string] $RepositoryPath, [string[]] $Arguments)
            $call = $Arguments -join '|'
            $script:gitCalls.Add($call)
            switch -Exact ($call) {
                'show-ref|--verify|--hash|refs/tags/v2.3' { return ('b' * 40) }
                'rev-parse|--verify|refs/tags/v2.3^{commit}' { return ('a' * 40) }
                ('show|-s|--format=%cI|' + ('a' * 40)) { return '2026-10-01T06:16:59+00:00' }
                default { throw "Unexpected git call $call" }
            }
        }
        $resolved = Resolve-ReleaseTag -RepositoryPath $temporaryDirectory -Tag v2.3 -GitRunner $runner
        Assert-True ($resolved.Tag -ceq 'v2.3' -and $resolved.Commit -ceq ('a' * 40)) 'Annotated tag did not peel to commit.'
        Assert-True ($resolved.CommitTimeUtc.ToUniversalTime().ToString('o') -ceq '2026-10-01T06:16:59.0000000+00:00') 'Wrong source timestamp.'
        Assert-True ($script:gitCalls.Count -eq 3) 'Unexpected tag lookup behavior.'
        Assert-Throws { Resolve-ReleaseTag -RepositoryPath $temporaryDirectory -Tag main -GitRunner { throw 'missing exact tag' } } 'Missing tag fell back to a branch.'
    }
    Test-Case 'Lightweight tags resolve without requiring an annotated object' {
        $runner = {
            param([string] $Root, [string[]] $Arguments)
            if ($Arguments[0] -eq 'show-ref' -or $Arguments[0] -eq 'rev-parse') { return ('c' * 40) }
            if ($Arguments[0] -eq 'show') { return '2026-10-01T13:16:59+07:00' }
            throw 'Unexpected git operation.'
        }
        $resolved = Resolve-ReleaseTag $temporaryDirectory 'v2.2' $runner
        Assert-True ($resolved.Commit -ceq ('c' * 40)) 'Lightweight tag resolution failed.'
        Assert-True ($resolved.CommitTimeUtc.Hour -eq 6) 'Source timestamp was not normalized to UTC.'
        Assert-Throws { Resolve-ReleaseTag $temporaryDirectory v2.2 { return 'not-a-sha' } } 'Malformed Git identity was accepted.'
    }
    Test-Case 'Release asset names bind both runtime modes to safe tag' {
        $names = @(Get-ReleaseAssetNames v2.4)
        Assert-True ($names.Count -eq 2) 'Wrong number of distributions.'
        Assert-True ($names[0] -ceq 'Win_1337_Patch-v2.4-win-x64-framework-dependent.zip') 'Wrong framework-dependent name.'
        Assert-True ($names[1] -ceq 'Win_1337_Patch-v2.4-win-x64-self-contained.zip') 'Wrong self-contained name.'
    }
    Test-Case 'ZIP bytes ignore filesystem order times source roots and culture' {
        $first = Join-Path $temporaryDirectory 'first'
        $second = Join-Path $temporaryDirectory 'other-root'
        Write-PublishFixture $first
        Write-PublishFixture $second -Reverse
        foreach ($file in [IO.Directory]::EnumerateFiles($first, '*', [IO.SearchOption]::AllDirectories)) { [IO.File]::SetLastWriteTimeUtc($file, [datetime]'2001-01-01T00:00:00Z') }
        $license = Join-Path $temporaryDirectory 'LICENSE'
        [IO.File]::WriteAllText($license, 'GPLv3 fixture')
        $zip1 = Join-Path $temporaryDirectory 'first.zip'
        $zip2 = Join-Path $temporaryDirectory 'second.zip'
        $culture = [Threading.Thread]::CurrentThread.CurrentCulture
        try {
            [Threading.Thread]::CurrentThread.CurrentCulture = [Globalization.CultureInfo]::GetCultureInfo('tr-TR')
            New-ReleaseZip $first $zip1 ([datetimeoffset]'2026-10-01T06:16:59Z') $license "source v2.4`nDeltaFoX/DeFconX"
            [Threading.Thread]::CurrentThread.CurrentCulture = [Globalization.CultureInfo]::GetCultureInfo('en-US')
            New-ReleaseZip $second $zip2 ([datetimeoffset]'2026-10-01T06:16:59Z') $license "source v2.4`nDeltaFoX/DeFconX"
        }
        finally { [Threading.Thread]::CurrentThread.CurrentCulture = $culture }
        Assert-True ((Get-FileHash $zip1).Hash -ceq (Get-FileHash $zip2).Hash) 'ZIP bytes depend on machine paths/order/times/culture.'
        $archive = [IO.Compression.ZipFile]::OpenRead($zip1)
        try {
            $actual = @($archive.Entries | ForEach-Object FullName)
            $expected = @('BUILD-INFO.txt', 'LICENSE', 'System.Windows.Forms.dll', 'Win_1337_Patch.deps.json', 'Win_1337_Patch.dll', 'Win_1337_Patch.dll.config', 'Win_1337_Patch.exe', 'Win_1337_Patch.pdb', 'Win_1337_Patch.runtimeconfig.json', 'it/System.Windows.Forms.resources.dll')
            Assert-True (($actual -join '|') -ceq ($expected -join '|')) 'Archive order/content is not canonical.'
            foreach ($entry in $archive.Entries) {
                Assert-True ($entry.LastWriteTime.DateTime -eq [datetime]'2026-10-01T06:16:58') 'Archive timestamp is not fixed at UTC two-second precision.'
                Assert-True ($entry.ExternalAttributes -eq 0) 'Archive contains machine-specific attributes.'
            }
        }
        finally { $archive.Dispose() }
        $extracted = Join-Path $temporaryDirectory 'extracted'
        [IO.Compression.ZipFile]::ExtractToDirectory($zip1, $extracted)
        Assert-True ((Get-FileHash (Join-Path $first 'Win_1337_Patch.exe')).Hash -ceq (Get-FileHash (Join-Path $extracted 'Win_1337_Patch.exe')).Hash) 'Native apphost changed inside ZIP.'
        Assert-Throws { New-ReleaseZip $first $zip1 ([datetimeoffset]'2026-10-01T00:00:00Z') $license 'info' } 'Existing archive was overwritten.'
    }
    Test-Case 'Forbidden files and reparse points fail before creating an archive' {
        $publish = Join-Path $temporaryDirectory 'invalid'
        Write-PublishFixture $publish
        $license = Join-Path $temporaryDirectory 'LICENSE'
        foreach ($name in @('private.1337', 'target.BAK', 'Program.cs', 'user.config', 'unexpected.txt')) {
            $path = Join-Path $publish $name
            [IO.File]::WriteAllText($path, 'private')
            $zip = Join-Path $temporaryDirectory ('invalid-' + [Guid]::NewGuid() + '.zip')
            Assert-Throws { New-ReleaseZip $publish $zip ([datetimeoffset]'2026-10-01T00:00:00Z') $license 'info' } "Forbidden file accepted: $name"
            Assert-True (-not [IO.File]::Exists($zip)) 'Invalid tree left a successful archive.'
            [IO.File]::Delete($path)
        }
        $outside = Join-Path $temporaryDirectory 'outside'
        $null = [IO.Directory]::CreateDirectory($outside)
        [IO.File]::WriteAllText((Join-Path $outside 'private.dll'), 'do not package')
        $junction = Join-Path $publish 'linked'
        $null = New-Item -ItemType Junction -Path $junction -Target $outside
        Assert-Throws { New-ReleaseZip $publish (Join-Path $temporaryDirectory 'link.zip') ([datetimeoffset]'2026-10-01T00:00:00Z') $license 'info' } 'Reparse point was traversed.'
        [IO.Directory]::Delete($junction)
        Assert-True ([IO.File]::Exists((Join-Path $outside 'private.dll'))) 'Reparse target was modified.'
    }
    Test-Case 'ZIP dates outside format range are normalized without local time conversion' {
        $publish = Join-Path $temporaryDirectory 'dates'
        Write-PublishFixture $publish
        foreach ($fixture in @(@{ Input = '1970-01-01T00:00:00Z'; Expected = '1980-01-01T00:00:00' }, @{ Input = '2200-01-01T00:00:00Z'; Expected = '2107-12-31T23:59:58' })) {
            $zip = Join-Path $temporaryDirectory ([Guid]::NewGuid().ToString() + '.zip')
            New-ReleaseZip $publish $zip ([datetimeoffset]$fixture.Input) (Join-Path $temporaryDirectory 'LICENSE') 'info'
            $archive = [IO.Compression.ZipFile]::OpenRead($zip)
            try { Assert-True ($archive.Entries[0].LastWriteTime.DateTime -eq [datetime]$fixture.Expected) 'ZIP time boundary normalization failed.' }
            finally { $archive.Dispose() }
        }
    }
    Test-Case 'SHA256SUMS has canonical independent SHA-256 values and verifies only expected assets' {
        $directory = Join-Path $temporaryDirectory 'hashes'
        $null = [IO.Directory]::CreateDirectory($directory)
        $names = @('Win_1337_Patch-v2.4-win-x64-framework-dependent.zip', 'Win_1337_Patch-v2.4-win-x64-self-contained.zip')
        [IO.File]::WriteAllText((Join-Path $directory $names[0]), 'abc', [Text.UTF8Encoding]::new($false))
        [IO.File]::WriteAllText((Join-Path $directory $names[1]), '', [Text.UTF8Encoding]::new($false))
        [IO.File]::WriteAllText((Join-Path $directory 'provenance.json'), '{}')
        Write-ReleaseHashes $directory $names
        $manifest = [IO.File]::ReadAllText((Join-Path $directory 'SHA256SUMS'))
        $expected = "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad  $($names[0])`ne3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855  $($names[1])`n"
        Assert-True ($manifest -ceq $expected) 'Hash manifest differs from independently known SHA-256 fixture values.'
        Test-ReleaseHashes $directory $names
        foreach ($invalid in @($expected + $expected, $expected.Replace($names[0], '../evil.zip'), $expected.Replace('ba7816', 'zz7816'), $expected.Split("`n")[0] + "`n")) {
            [IO.File]::WriteAllText((Join-Path $directory 'SHA256SUMS'), $invalid)
            Assert-Throws { Test-ReleaseHashes $directory $names } 'Malformed/duplicate/incomplete hash manifest was accepted.'
        }
        [IO.File]::WriteAllText((Join-Path $directory 'SHA256SUMS'), $expected)
        [IO.File]::WriteAllText((Join-Path $directory 'extra.ps1'), 'bad')
        Assert-Throws { Test-ReleaseHashes $directory $names } 'Unexpected promotion file was accepted.'
        [IO.File]::Delete((Join-Path $directory 'extra.ps1'))
        [IO.File]::WriteAllText((Join-Path $directory $names[0]), 'changed')
        Assert-Throws { Test-ReleaseHashes $directory $names } 'Modified ZIP hash was accepted.'
        [IO.File]::Delete((Join-Path $directory $names[1]))
        Assert-Throws { Test-ReleaseHashes $directory $names } 'Missing ZIP was accepted.'
        Assert-Throws { Write-ReleaseHashes $directory @($names[0], $names[0]) } 'Duplicate assets were accepted.'
    }
}
finally {
    [IO.Directory]::Delete($temporaryDirectory, $true)
}
if ($failures -gt 0) { throw "$failures/$tests release artifact tests failed." }
Write-Host "Release artifact tests: $tests/$tests passed."
