Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Assert-ReleaseTag {
    param([AllowEmptyString()][string] $Tag)
    if ($Tag -cnotmatch '\A[A-Za-z0-9][A-Za-z0-9._-]{0,63}\z' -or $Tag.Contains('..') -or
        $Tag.EndsWith('.', [StringComparison]::Ordinal) -or $Tag.EndsWith('.lock', [StringComparison]::OrdinalIgnoreCase) -or
        $Tag -cmatch '\A(?:[a-fA-F0-9]{40}|[a-fA-F0-9]{64})\z') {
        throw 'Use an existing simple tag (1-64 ASCII letters, digits, dots, underscores or hyphens), not a branch, ref, path or commit SHA.'
    }
    return $Tag
}

function Resolve-ReleaseTag {
    param([string] $RepositoryPath, [string] $Tag, [scriptblock] $GitRunner)
    $Tag = Assert-ReleaseTag $Tag
    if (-not $GitRunner) {
        $GitRunner = {
            param([string] $Root, [string[]] $Arguments)
            $output = & git -C $Root @Arguments 2>&1
            if ($LASTEXITCODE -ne 0) { throw "Tag lookup failed: $($output -join '`n')" }
            return ($output -join "`n").Trim()
        }
    }
    $ref = "refs/tags/$Tag"
    $object = & $GitRunner $RepositoryPath @('show-ref', '--verify', '--hash', $ref)
    if ($object -cnotmatch '\A[a-f0-9]{40}\z') { throw 'Tag reference does not identify a valid Git object.' }
    $commit = & $GitRunner $RepositoryPath @('rev-parse', '--verify', "$ref^{commit}")
    if ($commit -cnotmatch '\A[a-f0-9]{40}\z') { throw 'Tag does not resolve to a commit SHA.' }
    $time = & $GitRunner $RepositoryPath @('show', '-s', '--format=%cI', $commit)
    $parsed = [datetimeoffset]::Parse($time, [Globalization.CultureInfo]::InvariantCulture)
    return [pscustomobject]@{ Tag = $Tag; Commit = $commit; CommitTimeUtc = $parsed.ToUniversalTime() }
}

function Get-ReleaseAssetNames {
    param([string] $Tag)
    $Tag = Assert-ReleaseTag $Tag
    return @("Win_1337_Patch-$Tag-win-x64-framework-dependent.zip", "Win_1337_Patch-$Tag-win-x64-self-contained.zip")
}

function Get-PublishEntries {
    param([string] $Root)
    $rootInfo = [IO.DirectoryInfo]::new([IO.Path]::GetFullPath($Root))
    if (-not $rootInfo.Exists -or ($rootInfo.Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw 'Publish root is missing or a reparse point.' }
    $entries = [Collections.Generic.Dictionary[string, string]]::new([StringComparer]::Ordinal)
    $caseNames = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    $pending = [Collections.Generic.Stack[IO.DirectoryInfo]]::new()
    $pending.Push($rootInfo)
    while ($pending.Count -gt 0) {
        foreach ($item in $pending.Pop().EnumerateFileSystemInfos()) {
            if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw "Cannot package reparse point: $($item.Name)" }
            $relative = [IO.Path]::GetRelativePath($rootInfo.FullName, $item.FullName).Replace('\', '/')
            if ($relative.Contains('..') -or $relative.Contains(':') -or $relative.StartsWith('/')) { throw "Unsafe publish entry: $relative" }
            if ($item -is [IO.DirectoryInfo]) {
                # The framework runtime includes culture satellite folders, not arbitrary source trees.
                if ($relative -cnotmatch '\A[a-z]{2,3}(?:-[A-Za-z0-9]{2,8})*\z') { throw "Unexpected publish directory: $relative" }
                $pending.Push($item)
                continue
            }
            $allowed = if ($relative.Contains('/')) {
                $relative -cmatch '\A[a-z]{2,3}(?:-[A-Za-z0-9]{2,8})*/[A-Za-z0-9._-]+\.resources\.dll\z'
            }
            else {
                $relative -cmatch '\A[A-Za-z0-9._-]+\.(?:exe|dll|pdb|json)\z' -or $relative -ceq 'Win_1337_Patch.dll.config' -or $relative -ceq 'Win_1337_Patch.exe.config'
            }
            if (-not $allowed -or $relative.StartsWith('testhost.', [StringComparison]::OrdinalIgnoreCase)) { throw "Unexpected publish file: $relative" }
            if (-not $caseNames.Add($relative)) { throw "Case-colliding publish entry: $relative" }
            $entries.Add($relative, $item.FullName)
        }
    }
    if ($entries.Count -eq 0) { throw 'Publish directory is empty.' }
    return ,$entries
}

function New-ReleaseZip {
    param([string] $PublishDirectory, [string] $Destination, [datetimeoffset] $CommitTimeUtc, [string] $LicensePath, [string] $BuildInfo)
    $entries = Get-PublishEntries $PublishDirectory
    if (-not [IO.File]::Exists($LicensePath)) { throw 'Original LICENSE is missing.' }
    if ([IO.File]::Exists($Destination) -or [IO.Directory]::Exists($Destination)) { throw 'Archive destination already exists; refusing to replace it.' }
    $time = $CommitTimeUtc.ToUniversalTime()
    $minimum = [datetimeoffset]'1980-01-01T00:00:00Z'
    $maximum = [datetimeoffset]'2107-12-31T23:59:58Z'
    if ($time -lt $minimum) { $time = $minimum }
    if ($time -gt $maximum) { $time = $maximum }
    $time = [datetimeoffset]::new($time.Year, $time.Month, $time.Day, $time.Hour, $time.Minute, ($time.Second - ($time.Second % 2)), [timespan]::Zero)
    $names = [string[]]@($entries.Keys) + @('LICENSE', 'BUILD-INFO.txt')
    [Array]::Sort($names, [StringComparer]::Ordinal)
    $stream = $null
    $archive = $null
    $created = $false
    try {
        $stream = [IO.File]::Open($Destination, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
        $created = $true
        $archive = [IO.Compression.ZipArchive]::new($stream, [IO.Compression.ZipArchiveMode]::Create, $true)
        foreach ($name in $names) {
            $entry = $archive.CreateEntry($name, [IO.Compression.CompressionLevel]::Optimal)
            $entry.LastWriteTime = $time
            $entry.ExternalAttributes = 0
            $output = $entry.Open()
            try {
                if ($name -ceq 'BUILD-INFO.txt') {
                    $bytes = [Text.UTF8Encoding]::new($false).GetBytes($BuildInfo.Replace("`r`n", "`n"))
                    $output.Write($bytes, 0, $bytes.Length)
                }
                else {
                    $path = if ($name -ceq 'LICENSE') { $LicensePath } else { $entries[$name] }
                    $inputStream = [IO.File]::OpenRead($path)
                    try { $inputStream.CopyTo($output) } finally { $inputStream.Dispose() }
                }
            }
            finally { $output.Dispose() }
        }
        $archive.Dispose()
        $archive = $null
        $stream.Dispose()
        $stream = $null
    }
    catch {
        if ($archive) { $archive.Dispose(); $archive = $null }
        if ($stream) { $stream.Dispose(); $stream = $null }
        if ($created) { [IO.File]::Delete($Destination) }
        throw
    }
    finally {
        if ($archive) { $archive.Dispose() }
        if ($stream) { $stream.Dispose() }
    }
}

function Assert-AssetNames {
    param([string[]] $AssetNames)
    if ($AssetNames.Count -ne 2) { throw 'Exactly two ZIP asset names are required.' }
    $seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($name in $AssetNames) {
        if ($name -cnotmatch '\AWin_1337_Patch-[A-Za-z0-9][A-Za-z0-9._-]{0,63}-win-x64-(?:framework-dependent|self-contained)\.zip\z' -or -not $seen.Add($name)) {
            throw 'Invalid or duplicate release asset name.'
        }
    }
}

function Write-ReleaseHashes {
    param([string] $Directory, [string[]] $AssetNames)
    Assert-AssetNames $AssetNames
    $sorted = [string[]]$AssetNames.Clone()
    [Array]::Sort($sorted, [StringComparer]::Ordinal)
    $lines = foreach ($name in $sorted) { "$((Get-FileHash -LiteralPath (Join-Path $Directory $name) -Algorithm SHA256).Hash.ToLowerInvariant())  $name" }
    [IO.File]::WriteAllText((Join-Path $Directory 'SHA256SUMS'), (($lines -join "`n") + "`n"), [Text.UTF8Encoding]::new($false))
}

function Test-ReleaseHashes {
    param([string] $Directory, [string[]] $AssetNames)
    Assert-AssetNames $AssetNames
    $expected = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($name in $AssetNames + @('SHA256SUMS', 'provenance.json')) { $null = $expected.Add($name) }
    $root = [IO.DirectoryInfo]::new([IO.Path]::GetFullPath($Directory))
    if (-not $root.Exists -or ($root.Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw 'Artifact directory is missing or a reparse point.' }
    foreach ($item in $root.EnumerateFileSystemInfos()) {
        if ($item -isnot [IO.FileInfo] -or ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -or -not $expected.Contains($item.Name)) { throw "Unexpected artifact entry: $($item.Name)" }
    }
    foreach ($name in $expected) { if (-not [IO.File]::Exists((Join-Path $Directory $name))) { throw "Missing artifact: $name" } }
    $text = [IO.File]::ReadAllText((Join-Path $Directory 'SHA256SUMS'), [Text.UTF8Encoding]::new($false, $true))
    $lines = $text.Split("`n")
    if ($lines.Count -ne 3 -or $lines[2] -cne '') { throw 'Checksum manifest must contain exactly two LF-terminated entries.' }
    $sorted = [string[]]$AssetNames.Clone()
    [Array]::Sort($sorted, [StringComparer]::Ordinal)
    for ($index = 0; $index -lt 2; $index++) {
        $name = $sorted[$index]
        $hash = (Get-FileHash -LiteralPath (Join-Path $Directory $name) -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($lines[$index] -cne "$hash  $name") { throw "Checksum manifest mismatch: $name" }
    }
}

Export-ModuleMember -Function Assert-ReleaseTag, Resolve-ReleaseTag, Get-ReleaseAssetNames, New-ReleaseZip, Write-ReleaseHashes, Test-ReleaseHashes
