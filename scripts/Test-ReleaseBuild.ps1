param(
    [string] $RepositoryPath = (Split-Path $PSScriptRoot -Parent)
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$failures = 0
$tests = 0
$temporaryDirectory = Join-Path ([IO.Path]::GetTempPath()) ('patch-release-build-tests-' + [Guid]::NewGuid())
$null = [IO.Directory]::CreateDirectory($temporaryDirectory)

function Assert-True([bool] $Condition, [string] $Message) {
    if (-not $Condition) { throw $Message }
}

function Test-Case([string] $Name, [scriptblock] $Body) {
    $script:tests++
    try {
        & $Body
        Write-Host "PASS $Name"
    }
    catch {
        $script:failures++
        Write-Host "FAIL ${Name}: $_"
    }
}

function Get-BuildProperties([string] $Mode = '', [switch] $Release) {
    $project = Join-Path $RepositoryPath 'Win_1337_Patch/Win_1337_Patch.csproj'
    $names = 'RuntimeIdentifier,SelfContained,RuntimeFrameworkVersion,TargetLatestRuntimePatch,NuGetLockFilePath,RestorePackagesWithLockFile,ContinuousIntegrationBuild,Deterministic,PathMap,TreatWarningsAsErrors,PublishTrimmed,PublishAot,PublishSingleFile,PublishReadyToRun'
    $arguments = @('msbuild', $project, '-nologo', "-getProperty:$names")
    if ($Release) { $arguments += '-p:ReleaseBuild=true' }
    if ($Mode) { $arguments += "-p:ReleaseMode=$Mode" }
    $output = & dotnet @arguments 2>&1
    Assert-True ($LASTEXITCODE -eq 0) ($output -join "`n")
    return (($output -join "`n") | ConvertFrom-Json).Properties
}

try {
    Test-Case 'SDK selection is exact and does not roll forward or select previews' {
        $path = Join-Path $RepositoryPath 'global.json'
        Assert-True ([IO.File]::Exists($path)) 'Pinned global.json is missing.'
        $sdk = ([IO.File]::ReadAllText($path) | ConvertFrom-Json).sdk
        Assert-True ($sdk.version -ceq '10.0.401') 'Wrong SDK version.'
        Assert-True ($sdk.rollForward -ceq 'disable') 'SDK can silently roll forward.'
        Assert-True ($sdk.allowPrerelease -eq $false) 'Preview SDK selection is allowed.'
        Push-Location $RepositoryPath
        try {
            $selected = & dotnet --version
            Assert-True ($LASTEXITCODE -eq 0 -and ($selected -join '').Trim() -ceq '10.0.401') 'Actual selected SDK is not 10.0.401.'
        }
        finally { Pop-Location }
    }
    Test-Case 'Ordinary builds retain no RID and use dependency locks' {
        $properties = Get-BuildProperties
        Assert-True ([string]::IsNullOrEmpty($properties.RuntimeIdentifier)) 'Ordinary build unexpectedly selects a RID.'
        Assert-True ($properties.SelfContained -ine 'true') 'Ordinary build unexpectedly bundles runtime.'
        Assert-True ($properties.RestorePackagesWithLockFile -ieq 'true') 'Ordinary restore does not use a dependency lock.'
    }
    Test-Case 'Release builds normalize source paths and fail on warnings' {
        $properties = Get-BuildProperties -Release
        Assert-True ($properties.ContinuousIntegrationBuild -ieq 'true') 'CI compiler behavior is not enabled.'
        Assert-True ($properties.Deterministic -ieq 'true') 'Compilation is not deterministic.'
        Assert-True ($properties.TreatWarningsAsErrors -ieq 'true') 'Release warnings do not fail the build.'
        Assert-True ($properties.PathMap.EndsWith('=/_/', [StringComparison]::Ordinal)) 'Source paths are not mapped to the stable root.'
        Assert-True ($properties.PathMap.StartsWith([IO.Path]::GetFullPath($RepositoryPath), [StringComparison]::OrdinalIgnoreCase)) 'Source path map does not cover this checkout.'
    }
    foreach ($mode in @('framework-dependent', 'self-contained')) {
        Test-Case "Release $mode restore and publish select the pinned runtime graph" {
            $properties = Get-BuildProperties -Mode $mode -Release
            Assert-True ($properties.RuntimeIdentifier -ceq 'win-x64') 'Wrong release RID.'
            $expectedSelfContained = if ($mode -eq 'self-contained') { 'true' } else { 'false' }
            Assert-True ($properties.SelfContained -ieq $expectedSelfContained) 'Wrong runtime mode.'
            Assert-True ($properties.RuntimeFrameworkVersion -ceq '10.0.12') 'Runtime version can silently change.'
            Assert-True ($properties.TargetLatestRuntimePatch -ieq 'false') 'Restore can select the latest runtime implicitly.'
            Assert-True ($properties.NuGetLockFilePath.EndsWith("packages.win-x64-$mode.lock.json", [StringComparison]::Ordinal)) 'Restore modes share the wrong lock graph.'
            foreach ($property in @('PublishTrimmed', 'PublishAot', 'PublishSingleFile', 'PublishReadyToRun')) {
                Assert-True ($properties.$property -ieq 'false') "Forbidden release option $property is not disabled."
            }
        }.GetNewClosure()
    }
    Test-Case 'Self-contained runtime and native apphost packs are actually pinned' {
        $project = Join-Path $RepositoryPath 'Win_1337_Patch/Win_1337_Patch.csproj'
        $output = & dotnet msbuild $project -nologo -p:ReleaseBuild=true -p:ReleaseMode=self-contained -t:ProcessFrameworkReferences -getItem:RuntimePack,AppHostPack 2>&1
        Assert-True ($LASTEXITCODE -eq 0) ($output -join "`n")
        $items = (($output -join "`n") | ConvertFrom-Json).Items
        foreach ($name in @('Microsoft.NETCore.App.Runtime.win-x64', 'Microsoft.WindowsDesktop.App.Runtime.win-x64')) {
            $pack = @($items.RuntimePack | Where-Object Identity -CEQ $name)
            Assert-True ($pack.Count -eq 1 -and $pack[0].NuGetPackageVersion -ceq '10.0.12') "Wrong runtime pack: $name."
        }
        Assert-True ($items.AppHostPack.Count -eq 1) 'Native apphost pack is missing.'
        Assert-True ($items.AppHostPack[0].Path -match '[\\/]10\.0\.12[\\/]') 'Native apphost can vary independently of the runtime.'
        Assert-True ([IO.File]::Exists($items.AppHostPack[0].Path)) 'Pinned apphost pack was not restored.'
    }
    Test-Case 'Locked restore rejects dependency drift without rewriting its lock' {
        $project = Join-Path $temporaryDirectory 'Drift.csproj'
        $lock = Join-Path $temporaryDirectory 'packages.lock.json'
        [IO.File]::WriteAllText($project, '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework><RestorePackagesWithLockFile>true</RestorePackagesWithLockFile></PropertyGroup></Project>')
        $output = & dotnet restore $project --use-lock-file 2>&1
        Assert-True ($LASTEXITCODE -eq 0) ($output -join "`n")
        $before = [Convert]::ToBase64String([IO.File]::ReadAllBytes($lock))
        [IO.File]::WriteAllText($project, '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework><RestorePackagesWithLockFile>true</RestorePackagesWithLockFile></PropertyGroup><ItemGroup><PackageReference Include="MSTest.TestFramework" Version="4.4.1" /></ItemGroup></Project>')
        $output = & dotnet restore $project --locked-mode 2>&1
        Assert-True ($LASTEXITCODE -ne 0) 'Locked restore accepted an added dependency.'
        Assert-True (($output -join "`n").Contains('NU1004')) 'Drift failed for a reason other than lock mismatch.'
        Assert-True ($before -ceq [Convert]::ToBase64String([IO.File]::ReadAllBytes($lock))) 'Failed locked restore modified its lock.'
    }
    foreach ($scenario in @('success', 'verification-failure', 'publish-failure', 'wrong-sdk', 'missing-lock', 'missing-generator', 'existing-output', 'hosted-seam', 'wrong-head')) {
        Test-Case "Release orchestration: $scenario" {
            $buildScript = Join-Path $PSScriptRoot 'Build-Release.ps1'
            Assert-True ([IO.File]::Exists($buildScript)) 'Build-Release command is missing.'
            $root = Join-Path $temporaryDirectory $scenario
            $null = [IO.Directory]::CreateDirectory((Join-Path $root 'Win_1337_Patch'))
            $null = [IO.Directory]::CreateDirectory((Join-Path $root 'Win_1337_Patch.Tests'))
            $null = [IO.Directory]::CreateDirectory((Join-Path $root 'scripts'))
            foreach ($file in @('global.json', 'Directory.Build.props', 'LICENSE', 'Win_1337_Patch.sln', 'Win_1337_Patch/Win_1337_Patch.csproj', 'Win_1337_Patch/packages.lock.json', 'Win_1337_Patch/packages.win-x64-framework-dependent.lock.json', 'Win_1337_Patch/packages.win-x64-self-contained.lock.json', 'Win_1337_Patch.Tests/packages.lock.json', 'scripts/Generate-Settings.ps1', 'scripts/Verify-Build.ps1')) {
                [IO.File]::WriteAllText((Join-Path $root $file), 'fixture')
            }
            $outputDirectory = Join-Path $root 'result'
            if ($scenario -eq 'missing-lock') { [IO.File]::Delete((Join-Path $root 'Win_1337_Patch/packages.win-x64-self-contained.lock.json')) }
            if ($scenario -eq 'missing-generator') { [IO.File]::Delete((Join-Path $root 'scripts/Generate-Settings.ps1')) }
            if ($scenario -eq 'existing-output') { $null = [IO.Directory]::CreateDirectory($outputDirectory) }
            $calls = [Collections.Generic.List[object]]::new()
            $verification = {
                param($Path)
                $calls.Add(@{ Command = 'verify'; Arguments = @($Path) })
                if ($scenario -eq 'verification-failure') { throw 'fixture verification failed' }
            }.GetNewClosure()
            $native = {
                param([string] $Command, [string[]] $Arguments, [string] $WorkingDirectory)
                $calls.Add(@{ Command = $Command; Arguments = $Arguments })
                if ($Arguments[0] -eq '--version') {
                    $version = if ($scenario -eq 'wrong-sdk') { '10.0.203' } else { '10.0.401' }
                    return @{ ExitCode = 0; Output = $version }
                }
                if ($Command -eq 'git' -and $Arguments[0] -eq 'rev-parse') { return @{ ExitCode = 0; Output = ('b' * 40) } }
                if ($Arguments[0] -eq 'publish') {
                    if ($scenario -eq 'publish-failure') { return @{ ExitCode = 1; Output = 'fixture publish failed' } }
                    $destination = $Arguments[[Array]::IndexOf($Arguments, '--output') + 1]
                    $null = [IO.Directory]::CreateDirectory($destination)
                    foreach ($name in @('Win_1337_Patch.exe', 'Win_1337_Patch.dll', 'Win_1337_Patch.pdb', 'Win_1337_Patch.deps.json', 'Win_1337_Patch.runtimeconfig.json', 'Win_1337_Patch.dll.config')) {
                        [IO.File]::WriteAllText((Join-Path $destination $name), 'fixture')
                    }
                    if ($Arguments -contains '-p:ReleaseMode=self-contained') {
                        foreach ($name in @('coreclr.dll', 'System.Private.CoreLib.dll', 'System.Windows.Forms.dll')) { [IO.File]::WriteAllText((Join-Path $destination $name), 'fixture') }
                    }
                }
                return @{ ExitCode = 0; Output = '' }
            }.GetNewClosure()
            $oldHosted = $env:GITHUB_ACTIONS
            try {
                $env:GITHUB_ACTIONS = if ($scenario -eq 'hosted-seam') { 'true' } else { '' }
                $errorText = ''
                try {
                    & $buildScript -RepositoryPath $root -Tag v2.4 -SourceCommit ('a' * 40) -CommitTimeUtc '2026-10-01T06:16:59Z' -OutputDirectory $outputDirectory -LocalSnapshot:($scenario -ne 'wrong-head') -NativeRunner $native -VerificationRunner $verification
                }
                catch { $errorText = $_.ToString() }
                if ($scenario -eq 'success') {
                    Assert-True ($errorText -ceq '') $errorText
                    Assert-True ($calls[1].Command -ceq 'verify') 'Publish ran before verification.'
                    $publishes = @($calls | Where-Object { $_.Arguments[0] -eq 'publish' })
                    $restores = @($calls | Where-Object { $_.Arguments[0] -eq 'restore' })
                    Assert-True ($publishes.Count -eq 2 -and $restores.Count -eq 2) 'Both mode graphs were not restored and published.'
                    foreach ($call in $restores) { Assert-True ($call.Arguments -contains '--locked-mode') 'Release performed an unlocked restore.' }
                    foreach ($call in $publishes) { Assert-True ($call.Arguments -contains '--no-restore') 'Publish silently re-restored.' }
                    $provenance = [IO.File]::ReadAllText((Join-Path $outputDirectory 'provenance.json')) | ConvertFrom-Json
                    Assert-True ($provenance.sourceStatus -ceq 'local-snapshot' -and $provenance.sourceCommit -ceq ('a' * 40)) 'Snapshot was mislabeled as an official tagged build.'
                    Assert-True ($null -eq $provenance.runUrl) 'A hosted run was invented.'
                    Assert-True ($provenance.artifacts.Count -eq 2) 'Missing artifact provenance.'
                    Import-Module (Join-Path $PSScriptRoot 'ReleaseArtifacts.psm1') -Force
                    Test-ReleaseHashes $outputDirectory (Get-ReleaseAssetNames v2.4)
                }
                else {
                    Assert-True ($errorText -cne '') "Failure scenario $scenario was accepted."
                    $publishCalls = @($calls | Where-Object { $_.Arguments[0] -eq 'publish' })
                    $expectedCount = if ($scenario -eq 'publish-failure') { 1 } else { 0 }
                    Assert-True ($publishCalls.Count -eq $expectedCount) 'Failure did not stop further publishing.'
                    Assert-True (-not [IO.File]::Exists((Join-Path $outputDirectory 'SHA256SUMS'))) 'Failure left successful release checksums.'
                }
            }
            finally { $env:GITHUB_ACTIONS = $oldHosted }
        }.GetNewClosure()
    }
}
finally {
    [IO.Directory]::Delete($temporaryDirectory, $true)
}

if ($failures -gt 0) { throw "$failures release build tests failed." }
Write-Host "Release build tests: $tests/$tests passed."
