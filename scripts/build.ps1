#requires -Version 7.2
param(
  [ValidateSet('Service', 'TestSite', 'Tests', 'All')][string]$Target = 'All',
  [ValidateSet('Debug', 'Release')][string]$Configuration = 'Debug',
  [switch]$Offline
)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
. "$PSScriptRoot/process.ps1"
if ($Target -in @('Tests', 'All') -and $Configuration -ne 'Debug') {
  throw 'The integration harness expects a Debug web build. Use -Configuration Debug for Tests or All.'
}
$buildEnvironment = @{
  MSBUILDDISABLENODEREUSE = '1'
  DOTNET_CLI_DO_NOT_USE_MSBUILD_SERVER = '1'
  DOTNET_CLI_TELEMETRY_OPTOUT = '1'
}
function Invoke-DotNet([string[]]$CommandArguments) {
  Invoke-StarterProcess -FilePath dotnet -CommandArguments $CommandArguments -WorkingDirectory $repoRoot -Environment $buildEnvironment
}

# Keep all build outputs inside this repository.
$artifacts = Join-Path $repoRoot 'artifacts'
$common = @('--disable-build-servers', '-m:1', '-nr:false', '-v:minimal')
if ($Offline) {
  $packageCache = if ($env:NUGET_PACKAGES) { $env:NUGET_PACKAGES } else { Join-Path ([Environment]::GetFolderPath('UserProfile')) '.nuget/packages' }
  $common += @('--source', $packageCache, '-p:NuGetAudit=false')
}
if ($Target -in @('Tests', 'All')) {
  Invoke-DotNet (@('build', 'Faith.UI.sln', '-c', $Configuration, '--artifacts-path', $artifacts) + $common)
} elseif ($Target -eq 'Service') {
  Invoke-DotNet (@('build', 'src/Faith.UI.Service/Faith.UI.Service.csproj', '-c', $Configuration, '--artifacts-path', $artifacts) + $common)
} else {
  Invoke-DotNet (@('build', 'src/Faith.UI.TestSite/Faith.UI.TestSite.csproj', '-c', $Configuration, '--artifacts-path', $artifacts) + $common)
}
if ($Target -in @('Tests', 'All')) {
  Invoke-DotNet @((Join-Path $artifacts 'bin/Faith.UI.Tests/debug/Faith.UI.Tests.dll'), $repoRoot)
}
