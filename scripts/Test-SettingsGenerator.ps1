param(
    [string] $GeneratorPath = (Join-Path $PSScriptRoot 'Generate-Settings.ps1')
)

$ErrorActionPreference = 'Stop'
$temporaryDirectory = Join-Path ([IO.Path]::GetTempPath()) ('patch-settings-tests-' + [Guid]::NewGuid())
$null = [IO.Directory]::CreateDirectory($temporaryDirectory)
$source = Join-Path $temporaryDirectory 'Settings.settings'
$output = Join-Path $temporaryDirectory 'Settings.Designer.cs'
$pwsh = (Get-Process -Id $PID).Path
$failures = 0

function Assert-True([bool] $Condition, [string] $Message) {
    if (-not $Condition) { throw $Message }
}

function Write-Fixture([string] $Ownership = 'False', [string] $Type = 'System.Boolean', [string] $Name = 'changeOwnership') {
    $xml = @"
<SettingsFile xmlns="http://schemas.microsoft.com/VisualStudio/2004/01/settings" CurrentProfile="(Default)" GeneratedClassNamespace="Win_1337_Patch.Properties" GeneratedClassName="Settings">
  <Profiles />
  <Settings>
    <Setting Name="fixoffset" Type="System.Boolean" Scope="User"><Value Profile="(Default)">True</Value></Setting>
    <Setting Name="backup" Type="System.Boolean" Scope="User"><Value Profile="(Default)">True</Value></Setting>
    <Setting Name="$Name" Type="$Type" Scope="User"><Value Profile="(Default)">$Ownership</Value></Setting>
  </Settings>
</SettingsFile>
"@
    [IO.File]::WriteAllText($source, $xml)
}

function Invoke-Generator([switch] $Check) {
    $arguments = @('-NoProfile', '-File', $GeneratorPath, '-SourcePath', $source, '-OutputPath', $output)
    if ($Check) { $arguments += '-Check' }
    $diagnostics = & $pwsh @arguments 2>&1
    return @{ ExitCode = $LASTEXITCODE; Diagnostics = ($diagnostics -join "`n") }
}

function Test-Case([string] $Name, [scriptblock] $Body) {
    try {
        & $Body
        Write-Host "PASS $Name"
    }
    catch {
        $script:failures++
        Write-Host "FAIL ${Name}: $_"
    }
}

try {
    Test-Case 'Generates all Boolean user settings with source defaults' {
        Write-Fixture
        $result = Invoke-Generator
        Assert-True ($result.ExitCode -eq 0) $result.Diagnostics
        $text = [IO.File]::ReadAllText($output)
        foreach ($name in @('fixoffset', 'backup', 'changeOwnership')) {
            $value = if ($name -eq 'changeOwnership') { 'False' } else { 'True' }
            $pattern = '\[global::System.Configuration.DefaultSettingValueAttribute\("' + $value + '"\)\]\s+public bool @?' + $name + '\s*\{'
            Assert-True ($text -match $pattern) "Wrong generated default for $name."
            Assert-True ($text.Contains('this["' + $name + '"]')) "Missing settings access for $name."
        }
        Assert-True ($text.Contains('ApplicationSettingsBase.Synchronized(new Settings())')) 'Missing synchronized default instance.'
        Assert-True ($text.Contains('namespace Win_1337_Patch.Properties')) 'Wrong generated namespace.'
        Assert-True (-not $text.Contains('Microsoft.VisualStudio.Editors')) 'Generator must not impersonate Visual Studio.'
    }
    Test-Case 'Regeneration follows a changed source default' {
        Write-Fixture -Ownership True
        $result = Invoke-Generator
        Assert-True ($result.ExitCode -eq 0) $result.Diagnostics
        Assert-True ([IO.File]::ReadAllText($output) -match 'DefaultSettingValueAttribute\("True"\)\]\s+public bool @?changeOwnership') 'Source change was ignored.'
    }
    Test-Case 'Repeated generation is byte-identical and does not rewrite' {
        Write-Fixture
        $result = Invoke-Generator
        Assert-True ($result.ExitCode -eq 0) $result.Diagnostics
        $bytes = [Convert]::ToBase64String([IO.File]::ReadAllBytes($output))
        [IO.File]::SetLastWriteTimeUtc($output, [DateTime]'2001-01-01T00:00:00Z')
        $before = [IO.File]::GetLastWriteTimeUtc($output)
        $result = Invoke-Generator
        Assert-True ($result.ExitCode -eq 0) $result.Diagnostics
        Assert-True ($bytes -ceq [Convert]::ToBase64String([IO.File]::ReadAllBytes($output))) 'Output changed between identical inputs.'
        Assert-True ([IO.File]::GetLastWriteTimeUtc($output) -eq $before) "Unchanged output was rewritten: $before -> $([IO.File]::GetLastWriteTimeUtc($output)); $($result.Diagnostics)"
    }
    Test-Case 'Check accepts current output without writing' {
        $before = [IO.File]::GetLastWriteTimeUtc($output)
        $result = Invoke-Generator -Check
        Assert-True ($result.ExitCode -eq 0) $result.Diagnostics
        Assert-True ([IO.File]::GetLastWriteTimeUtc($output) -eq $before) 'Check rewrote output.'
    }
    Test-Case 'Check rejects stale output without changing it' {
        Write-Fixture -Ownership True
        $bytes = [Convert]::ToBase64String([IO.File]::ReadAllBytes($output))
        $result = Invoke-Generator -Check
        Assert-True ($result.ExitCode -ne 0) 'Stale output was accepted.'
        Assert-True ($result.Diagnostics.Contains('stale')) 'Failure was not a stale-output diagnostic.'
        Assert-True (-not $result.Diagnostics.Contains('missing')) 'Stale output was mistaken for missing output.'
        Assert-True ($bytes -ceq [Convert]::ToBase64String([IO.File]::ReadAllBytes($output))) 'Stale-output check changed output.'
    }
    Test-Case 'Unsupported type is rejected before overwriting output' {
        Write-Fixture -Type System.String
        $bytes = [Convert]::ToBase64String([IO.File]::ReadAllBytes($output))
        $result = Invoke-Generator
        Assert-True ($result.ExitCode -ne 0) 'Unsupported type was accepted.'
        Assert-True ($result.Diagnostics.Contains('Boolean')) 'Failure was not a type-validation diagnostic.'
        Assert-True ($bytes -ceq [Convert]::ToBase64String([IO.File]::ReadAllBytes($output))) 'Invalid input changed output.'
    }
    Test-Case 'Invalid defaults and identifiers fail closed' {
        foreach ($fixture in @(@{ Ownership = 'not-a-bool' }, @{ Name = 'bad-name' }, @{ Name = 'backup' })) {
            Write-Fixture @fixture
            $bytes = [Convert]::ToBase64String([IO.File]::ReadAllBytes($output))
            $result = Invoke-Generator
            Assert-True ($result.ExitCode -ne 0) 'Invalid setting was accepted.'
            Assert-True ($bytes -ceq [Convert]::ToBase64String([IO.File]::ReadAllBytes($output))) 'Invalid setting changed output.'
        }
    }
    Test-Case 'Check rejects missing output without creating it' {
        Write-Fixture
        [IO.File]::Delete($output)
        $result = Invoke-Generator -Check
        Assert-True ($result.ExitCode -ne 0) 'Missing output was accepted.'
        Assert-True ($result.Diagnostics.Contains('missing')) 'Failure was not a missing-output diagnostic.'
        Assert-True (-not $result.Diagnostics.Contains('stale')) 'Missing output was mistaken for stale output.'
        Assert-True (-not [IO.File]::Exists($output)) 'Check created missing output.'
    }
}
finally {
    [IO.Directory]::Delete($temporaryDirectory, $true)
}

if ($failures -gt 0) { throw "$failures settings generator tests failed." }
Write-Host 'Settings generator tests: 8/8 passed.'
