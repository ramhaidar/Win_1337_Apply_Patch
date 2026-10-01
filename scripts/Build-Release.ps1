param(
    [string] $RepositoryPath = (Split-Path $PSScriptRoot -Parent),
    [Parameter(Mandatory)][string] $Tag,
    [Parameter(Mandatory)][string] $SourceCommit,
    [Parameter(Mandatory)][datetimeoffset] $CommitTimeUtc,
    [Parameter(Mandatory)][string] $OutputDirectory,
    [switch] $LocalSnapshot,
    [scriptblock] $NativeRunner,
    [scriptblock] $VerificationRunner
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'ReleaseArtifacts.psm1') -Force
$RepositoryPath = [IO.Path]::GetFullPath($RepositoryPath)
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
$Tag = Assert-ReleaseTag $Tag
if ($SourceCommit -cnotmatch '\A[a-f0-9]{40}\z') { throw 'SourceCommit must be a full lowercase commit SHA.' }
$hosted = $env:GITHUB_ACTIONS -ceq 'true'
if ($hosted -and ($LocalSnapshot -or $NativeRunner -or $VerificationRunner)) { throw 'Hosted release builds cannot bypass source validation or verification.' }
if ([IO.Directory]::Exists($OutputDirectory) -or [IO.File]::Exists($OutputDirectory)) { throw 'OutputDirectory must not already exist.' }
foreach ($file in @('global.json', 'Directory.Build.props', 'LICENSE', 'Win_1337_Patch.sln', 'Win_1337_Patch/Win_1337_Patch.csproj', 'Win_1337_Patch/packages.lock.json', 'Win_1337_Patch/packages.win-x64-framework-dependent.lock.json', 'Win_1337_Patch/packages.win-x64-self-contained.lock.json', 'Win_1337_Patch.Tests/packages.lock.json', 'scripts/Generate-Settings.ps1', 'scripts/Verify-Build.ps1')) {
    if (-not [IO.File]::Exists((Join-Path $RepositoryPath $file))) {
        throw "Missing release configuration: $file. Select a tag containing this pipeline and its committed lock files; historical tags cannot use an unlocked fallback."
    }
}
if (-not $NativeRunner) {
    $NativeRunner = {
        param([string] $Command, [string[]] $Arguments, [string] $WorkingDirectory)
        Push-Location $WorkingDirectory
        try {
            $output = & $Command @Arguments 2>&1
            return @{ ExitCode = $LASTEXITCODE; Output = ($output -join "`n") }
        }
        finally { Pop-Location }
    }
}
function Invoke-Native([string] $Command, [string[]] $Arguments) {
    $result = & $NativeRunner $Command $Arguments $RepositoryPath
    if ($result.ExitCode -ne 0) { throw "Command failed ($($result.ExitCode)): $Command $($Arguments -join ' ')`n$($result.Output)" }
    if ($result.Output) { Write-Host $result.Output }
    return [string]$result.Output
}
$sdk = Invoke-Native dotnet @('--version')
if ($sdk.Trim() -cne '10.0.401') { throw 'Release builds require exactly SDK 10.0.401.' }
if (-not $LocalSnapshot) {
    $head = Invoke-Native git @('rev-parse', 'HEAD')
    if ($head.Trim() -cne $SourceCommit) { throw 'Checkout does not match the resolved source commit.' }
    $status = Invoke-Native git @('status', '--porcelain', '--untracked-files=all')
    if ($status.Trim()) { throw 'Official builds require an unchanged selected-tag checkout.' }
    $resolved = Resolve-ReleaseTag -RepositoryPath $RepositoryPath -Tag $Tag
    if ($resolved.Commit -cne $SourceCommit -or $resolved.CommitTimeUtc -ne $CommitTimeUtc.ToUniversalTime()) { throw 'Source tag identity or commit time does not match the checkout.' }
}
if ($VerificationRunner) { & $VerificationRunner $RepositoryPath }
else {
    & (Join-Path $RepositoryPath 'scripts/Verify-Build.ps1') -RepositoryPath $RepositoryPath
}

$scratch = Join-Path ([IO.Path]::GetTempPath()) ('patch-release-' + [Guid]::NewGuid())
$null = [IO.Directory]::CreateDirectory($scratch)
$null = [IO.Directory]::CreateDirectory($OutputDirectory)
$completed = $false
try {
    $modes = @('framework-dependent', 'self-contained')
    $names = @(Get-ReleaseAssetNames $Tag)
    for ($index = 0; $index -lt $modes.Count; $index++) {
        $mode = $modes[$index]
        $publish = Join-Path $scratch $mode
        $properties = @('-p:ReleaseBuild=true', "-p:ReleaseMode=$mode")
        $null = Invoke-Native dotnet (@('restore', 'Win_1337_Patch/Win_1337_Patch.csproj', '--locked-mode') + $properties)
        $null = Invoke-Native dotnet (@('publish', 'Win_1337_Patch/Win_1337_Patch.csproj', '--configuration', 'Release', '--no-restore', '--output', $publish) + $properties)
        $required = @('Win_1337_Patch.exe', 'Win_1337_Patch.dll', 'Win_1337_Patch.pdb', 'Win_1337_Patch.deps.json', 'Win_1337_Patch.runtimeconfig.json', 'Win_1337_Patch.dll.config')
        if ($mode -eq 'self-contained') { $required += @('coreclr.dll', 'System.Private.CoreLib.dll', 'System.Windows.Forms.dll') }
        foreach ($file in $required) {
            if (-not [IO.File]::Exists((Join-Path $publish $file))) { throw "Incomplete $mode publish: $file" }
        }
        $info = "Win_1337_Patch`nOriginal author: DeltaFoX (DeFconX)`nLicense: GPLv3 (see LICENSE)`nSource tag: $Tag`nSource commit: $SourceCommit`nSDK: 10.0.401`nRuntime: 10.0.12`nAssembly version: 2.4.0.0`nMode: win-x64 $mode`n"
        New-ReleaseZip $publish (Join-Path $OutputDirectory $names[$index]) $CommitTimeUtc (Join-Path $RepositoryPath 'LICENSE') $info
    }
    Write-ReleaseHashes $OutputDirectory $names
    $artifacts = @($names | ForEach-Object { [ordered]@{ name = $_; sha256 = (Get-FileHash -LiteralPath (Join-Path $OutputDirectory $_) -Algorithm SHA256).Hash.ToLowerInvariant() } })
    $provenance = [ordered]@{
        schemaVersion = 1
        repository = if ($hosted) { $env:GITHUB_REPOSITORY } else { 'ramhaidar/Win_1337_Apply_Patch' }
        sourceTag = $Tag
        sourceCommit = $SourceCommit
        commitTimeUtc = $CommitTimeUtc.ToUniversalTime().ToString('o', [Globalization.CultureInfo]::InvariantCulture)
        sourceStatus = if ($LocalSnapshot) { 'local-snapshot' } else { 'tag-checkout' }
        workflowRef = if ($hosted) { $env:GITHUB_WORKFLOW_REF } else { $null }
        workflowSha = if ($hosted) { $env:GITHUB_WORKFLOW_SHA } else { $null }
        runUrl = if ($hosted) { "$($env:GITHUB_SERVER_URL)/$($env:GITHUB_REPOSITORY)/actions/runs/$($env:GITHUB_RUN_ID)" } else { $null }
        sdkVersion = '10.0.401'
        runtimeVersion = '10.0.12'
        runnerImage = if ($hosted) { "$($env:ImageOS) $($env:ImageVersion)" } else { 'local Windows' }
        assemblyVersion = '2.4.0.0'
        publishModes = $modes
        artifacts = $artifacts
    }
    [IO.File]::WriteAllText((Join-Path $OutputDirectory 'provenance.json'), (($provenance | ConvertTo-Json -Depth 6).Replace("`r`n", "`n") + "`n"), [Text.UTF8Encoding]::new($false))
    Test-ReleaseHashes $OutputDirectory $names
    $completed = $true
    Write-Host "Release artifacts verified: $OutputDirectory ($($provenance.sourceStatus))"
}
finally {
    [IO.Directory]::Delete($scratch, $true)
    if (-not $completed) {
        # This invocation exclusively created the output; failed partial files are not releasable.
        foreach ($file in @('SHA256SUMS', 'provenance.json')) {
            $path = Join-Path $OutputDirectory $file
            if ([IO.File]::Exists($path)) { [IO.File]::Delete($path) }
        }
    }
}
