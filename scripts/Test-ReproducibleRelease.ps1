param(
    [string] $RepositoryPath = (Split-Path $PSScriptRoot -Parent),
    [string] $Tag = 'local-rebuild',
    [switch] $FixturesOnly
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$scratch = Join-Path ([IO.Path]::GetTempPath()) ('patch-rebuild-tests-' + [Guid]::NewGuid())
$null = [IO.Directory]::CreateDirectory($scratch)

function Get-TreeHashes([string] $Root) {
    $hashes = [Collections.Generic.Dictionary[string, string]]::new([StringComparer]::Ordinal)
    foreach ($file in [IO.Directory]::EnumerateFiles($Root, '*', [IO.SearchOption]::AllDirectories)) {
        $relative = [IO.Path]::GetRelativePath($Root, $file).Replace('\', '/')
        if ($relative -ceq 'provenance.json') { continue }
        $hashes.Add($relative, (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash)
    }
    return ,$hashes
}

function Compare-ReleaseTrees([string] $First, [string] $Second) {
    $left = Get-TreeHashes $First
    $right = Get-TreeHashes $Second
    $names = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($name in @($left.Keys) + @($right.Keys)) { $null = $names.Add($name) }
    $different = @($names | Where-Object { -not $left.ContainsKey($_) -or -not $right.ContainsKey($_) -or $left[$_] -cne $right[$_] })
    if ($different.Count -gt 0) { throw "Rebuild mismatch: $($different -join ', ')" }
    Write-Host "Byte-identical trees: $($left.Count) files."
}

try {
    $first = Join-Path $scratch 'first-fixture'
    $second = Join-Path $scratch 'second-fixture'
    $null = [IO.Directory]::CreateDirectory($first)
    $null = [IO.Directory]::CreateDirectory($second)
    $files = @('Win_1337_Patch.exe', 'Win_1337_Patch.dll', 'Win_1337_Patch.pdb', 'Win_1337_Patch.runtimeconfig.json', 'coreclr.dll', 'release.zip')
    foreach ($file in $files) {
        [IO.File]::WriteAllText((Join-Path $first $file), 'same fixture')
        [IO.File]::WriteAllText((Join-Path $second $file), 'same fixture')
    }
    Compare-ReleaseTrees $first $second
    foreach ($file in $files) {
        [IO.File]::WriteAllText((Join-Path $second $file), 'changed fixture')
        $message = ''
        try { Compare-ReleaseTrees $first $second } catch { $message = $_.ToString() }
        if (-not $message.Contains($file)) { throw "Comparison did not identify changed file: $file" }
        [IO.File]::WriteAllText((Join-Path $second $file), 'same fixture')
    }
    [IO.File]::WriteAllText((Join-Path $first 'provenance.json'), '{"runUrl":"first"}')
    [IO.File]::WriteAllText((Join-Path $second 'provenance.json'), '{"runUrl":"second"}')
    Compare-ReleaseTrees $first $second
    [IO.File]::Delete((Join-Path $second $files[0]))
    $message = ''
    try { Compare-ReleaseTrees $first $second } catch { $message = $_.ToString() }
    if (-not $message.Contains($files[0])) { throw 'Missing output file was not reported.' }
    Write-Host 'Rebuild comparison fixtures: identical trees, six differing file types, external provenance and missing-file checks passed.'
    if ($FixturesOnly) { return }
    $RepositoryPath = [IO.Path]::GetFullPath($RepositoryPath)
    Import-Module (Join-Path $PSScriptRoot 'ReleaseArtifacts.psm1') -Force
    $Tag = Assert-ReleaseTag $Tag
    $sourceCommit = (& git -C $RepositoryPath rev-parse HEAD).Trim()
    if ($LASTEXITCODE -ne 0) { throw 'Unable to read source commit.' }
    $commitTime = (& git -C $RepositoryPath show -s --format=%cI HEAD).Trim()
    if ($LASTEXITCODE -ne 0) { throw 'Unable to read source timestamp.' }
    $paths = & git -C $RepositoryPath -c core.quotepath=false ls-files --cached --others --exclude-standard
    if ($LASTEXITCODE -ne 0) { throw 'Unable to enumerate source snapshot.' }
    $paths = @($paths | Sort-Object -Unique)
    $roots = @((Join-Path $scratch 'source-a'), (Join-Path $scratch 'different-source-root-b'))
    # Copy both snapshots before either build, so concurrent source changes cannot
    # make the two experiments use different input bytes.
    foreach ($relative in $paths) {
        if ($relative -match '(^|/)(bin|obj|TestResults|artifacts|\.git|\.pi|\.superpowers)(/|$)') { continue }
        if ($relative -match '(^|/)\.\.?(/|$)' -or [IO.Path]::IsPathRooted($relative)) { throw "Unsafe snapshot path: $relative" }
        $source = Join-Path $RepositoryPath $relative
        if (-not [IO.File]::Exists($source)) { throw "Source snapshot includes a missing file: $relative" }
        $item = [IO.FileInfo]::new($source)
        $parent = $item.Directory
        while ($parent -and $parent.FullName.Length -ge $RepositoryPath.Length) {
            if ($parent.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw "Reparse source directory: $relative" }
            $parent = $parent.Parent
        }
        if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw "Reparse source file: $relative" }
        $bytes = [IO.File]::ReadAllBytes($source)
        foreach ($root in $roots) {
            $destination = Join-Path $root $relative
            $null = [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($destination))
            [IO.File]::WriteAllBytes($destination, $bytes)
        }
    }
    foreach ($root in $roots) {
        $outputDirectory = Join-Path $root 'artifacts/release'
        & (Join-Path $root 'scripts/Build-Release.ps1') -RepositoryPath $root -Tag $Tag -SourceCommit $sourceCommit -CommitTimeUtc $commitTime -OutputDirectory $outputDirectory -LocalSnapshot
        if ($LASTEXITCODE -ne 0) { throw 'Snapshot build failed.' }
        foreach ($name in Get-ReleaseAssetNames $Tag) {
            [IO.Compression.ZipFile]::ExtractToDirectory((Join-Path $outputDirectory $name), (Join-Path $root "artifacts/extracted/$name"))
        }
    }
    Compare-ReleaseTrees (Join-Path $roots[0] 'artifacts/extracted') (Join-Path $roots[1] 'artifacts/extracted')
    Compare-ReleaseTrees (Join-Path $roots[0] 'artifacts/release') (Join-Path $roots[1] 'artifacts/release')
    foreach ($name in Get-ReleaseAssetNames $Tag) {
        Write-Host "$name SHA256=$((Get-FileHash -LiteralPath (Join-Path $roots[0] "artifacts/release/$name") -Algorithm SHA256).Hash.ToLowerInvariant())"
    }
    Write-Host 'Local two-root snapshot comparison passed; no official tag or hosted reproducibility claim.'
}
finally { [IO.Directory]::Delete($scratch, $true) }
