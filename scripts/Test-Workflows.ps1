param([string] $RepositoryPath = (Split-Path $PSScriptRoot -Parent))
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# Policy checks complement syntax validation and the fake-API behavioral tests;
# they are not a substitute for a hosted Actions run.
$path = Join-Path $RepositoryPath '.github/workflows/ci.yml'
if (-not [IO.File]::Exists($path)) { throw 'Read-only Windows CI workflow is missing.' }
$ci = [IO.File]::ReadAllText($path)
foreach ($forbidden in @('contents:\s*write', 'id-token:\s*write', 'attestations:\s*write', 'pull_request_target', 'workflow_run', 'workflow_dispatch', 'release:', 'tags:')) {
    if ($ci -match $forbidden) { throw "Ordinary CI violates read-only/no-release policy: $forbidden" }
}
foreach ($required in @('(?m)^  push:', '(?m)^  pull_request:', "branches: \['\*\*'\]", 'contents:\s*read', 'runs-on:\s*windows-2025', 'timeout-minutes:\s*\d+', 'persist-credentials:\s*false', 'global-json-file:\s*global.json', 'scripts/Verify-Build.ps1')) {
    if ($ci -notmatch $required) { throw "Ordinary CI is missing verification contract: $required" }
}
foreach ($match in [regex]::Matches($ci, 'uses:\s*([^\s]+)')) {
    if ($match.Groups[1].Value -cnotmatch '\Aactions/[a-z-]+@[a-f0-9]{40}\z') { throw 'Every CI action must use a full official action commit SHA.' }
}
$releasePath = Join-Path $RepositoryPath '.github/workflows/release.yml'
if ([IO.File]::Exists($releasePath)) {
    $release = [IO.File]::ReadAllText($releasePath)
    foreach ($forbidden in @('(?m)^  (push|pull_request|pull_request_target|workflow_run|release):', 'id-token:\s*write', 'attestations:\s*write', '--clobber')) {
        if ($release -match $forbidden) { throw "Manual release violates trigger/permission policy: $forbidden" }
    }
    foreach ($required in @('(?m)^  workflow_dispatch:', 'cancel-in-progress:\s*false', 'needs:\s*resolve', 'needs:\s*\[resolve, build\]', 'if: github.repository == ''ramhaidar/Win_1337_Apply_Patch''', 'ref: \$\{\{ needs.resolve.outputs.source_sha \}\}', 'persist-credentials:\s*false', 'if-no-files-found:\s*error', 'overwrite:\s*false')) {
        if ($release -notmatch $required) { throw "Manual release is missing source/promotion contract: $required" }
    }
    if ([regex]::Matches($release, 'contents:\s*write').Count -ne 1) { throw 'Only draft promotion may have contents:write.' }
    $draft = $release.Substring($release.IndexOf("`n  draft:", [StringComparison]::Ordinal))
    foreach ($forbidden in @('actions/checkout@', '(?m)^\s+(run|shell):', 'scripts/', 'child_process', 'eval\(', 'exec\(')) {
        if ($draft -match $forbidden) { throw "Write-token job executes source instead of inline asset validation: $forbidden" }
    }
    foreach ($match in [regex]::Matches($release, 'uses:\s*([^\s]+)')) {
        if ($match.Groups[1].Value -cnotmatch '\Aactions/[a-z-]+@[a-f0-9]{40}\z') { throw 'Every release action must use a full official commit SHA.' }
    }
}
$actionlint = Get-Command actionlint -ErrorAction SilentlyContinue
if ($actionlint) {
    $paths = @($path)
    if ([IO.File]::Exists($releasePath)) { $paths += $releasePath }
    & $actionlint.Source @paths
    if ($LASTEXITCODE -ne 0) { throw 'Workflow syntax validation failed.' }
}
else { Write-Host 'actionlint unavailable; policy checks only, hosted syntax/run acceptance pending.' }
Write-Host 'Workflow policy tests passed.'
