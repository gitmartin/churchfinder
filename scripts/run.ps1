#requires -Version 7.2
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
$webAssembly = Join-Path $repoRoot 'artifacts/bin/Faith.UI.Service/debug/Faith.UI.Service.dll'
if (-not (Test-Path -LiteralPath $webAssembly)) { & "$PSScriptRoot/build.ps1" -Target Service }
. "$PSScriptRoot/process.ps1"
$serverEnvironment = @{
  ASPNETCORE_ENVIRONMENT = 'Development'
  ASPNETCORE_URLS = 'https://localhost:7241;http://localhost:5241'
}
Write-Host 'Control: https://localhost:7241/embed/lorem | SQLite: src/Faith.UI.Service/App_Data/faith-ui.db'
Invoke-StarterProcess -FilePath dotnet -CommandArguments @($webAssembly) -WorkingDirectory (Join-Path $repoRoot 'src/Faith.UI.Service') -Environment $serverEnvironment
