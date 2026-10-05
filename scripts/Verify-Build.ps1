param([string] $RepositoryPath = (Split-Path $PSScriptRoot -Parent))

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$RepositoryPath = [IO.Path]::GetFullPath($RepositoryPath)
$pwsh = (Get-Process -Id $PID).Path

function Invoke-Checked([string] $Command, [string[]] $Arguments) {
    & $Command @Arguments
    if ($LASTEXITCODE -ne 0) { throw "Verification command failed ($LASTEXITCODE): $Command $($Arguments -join ' ')" }
}

Push-Location $RepositoryPath
try {
    $sdk = & dotnet --version
    if ($LASTEXITCODE -ne 0 -or ($sdk -join '').Trim() -cne '10.0.401') { throw 'Install the exact SDK 10.0.401 required by global.json.' }
    Invoke-Checked dotnet @('restore', 'Win_1337_Patch.sln', '--locked-mode')
    Invoke-Checked $pwsh @('-NoProfile', '-File', 'scripts/Generate-Settings.ps1', '-Check')
    foreach ($script in @('Test-SettingsGenerator.ps1', 'Test-ReleaseArtifacts.ps1', 'Test-ReleaseBuild.ps1')) {
        $arguments = @('-NoProfile', '-File', "scripts/$script")
        if ($script -eq 'Test-ReleaseBuild.ps1') { $arguments += @('-RepositoryPath', $RepositoryPath) }
        Invoke-Checked $pwsh $arguments
    }
    if ([IO.File]::Exists((Join-Path $RepositoryPath 'scripts/Test-Workflows.ps1'))) {
        Invoke-Checked $pwsh @('-NoProfile', '-File', 'scripts/Test-Workflows.ps1')
    }
    if ([IO.File]::Exists((Join-Path $RepositoryPath 'scripts/Test-ReleaseWorkflow.mjs'))) {
        Invoke-Checked node @('--test', 'scripts/Test-ReleaseWorkflow.mjs')
    }
    if ([IO.File]::Exists((Join-Path $RepositoryPath 'scripts/Test-ReproducibleRelease.ps1'))) {
        Invoke-Checked $pwsh @('-NoProfile', '-File', 'scripts/Test-ReproducibleRelease.ps1', '-FixturesOnly')
    }

    # Quality gate: formatting. Non-mutating; fails when tracked source would be
    # reformatted. Runs after restore so it can reuse the locked graph.
    Write-Host 'Verifying formatting (dotnet format --verify-no-changes)...'
    Invoke-Checked dotnet @('format', 'Win_1337_Patch.sln', '--verify-no-changes', '--no-restore')

    # Quality gates: lint/static analysis and type-check. Both are produced by the
    # single Release compiler invocation below; ReleaseBuild=true turns compiler
    # and analyzer warnings into errors, so a clean build means the analyzer gate
    # and the type-check gate both pass without a redundant second build.
    Write-Host 'Building Release with analyzers enforced as errors (lint + type-check)...'
    Invoke-Checked dotnet @('build', 'Win_1337_Patch.sln', '--configuration', 'Release', '--no-restore', '-p:ReleaseBuild=true')

    # Quality gate: automated tests against the binaries just built.
    Write-Host 'Running automated tests...'
    Invoke-Checked dotnet @('test', 'Win_1337_Patch.Tests/Win_1337_Patch.Tests.csproj', '--configuration', 'Release', '--no-build', '--no-restore')
}
finally { Pop-Location }
Write-Host 'Build verification passed.'
