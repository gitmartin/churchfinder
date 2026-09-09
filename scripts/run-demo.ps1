#requires -Version 7.2
param([int]$RunSeconds = 0)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
$webAssembly = Join-Path $repoRoot 'artifacts/bin/Faith.UI.Service/debug/Faith.UI.Service.dll'
if (-not (Test-Path -LiteralPath $webAssembly)) { & "$PSScriptRoot/build.ps1" -Target Service }
. "$PSScriptRoot/process.ps1"
if (-not $IsWindows) { throw 'This local demo launcher currently requires Windows. See README for separate launch commands.' }
$stopFile = Join-Path $repoRoot 'artifacts/demo.stop'
if (Test-Path -LiteralPath $stopFile) { Remove-Item -LiteralPath $stopFile -Force }
$job = [StarterProcessJob]::new()
$processes = [System.Collections.Generic.List[System.Diagnostics.Process]]::new()
try {
  $serverStart = [System.Diagnostics.ProcessStartInfo]::new((Get-Command dotnet).Source)
  $serverStart.WorkingDirectory = Join-Path $repoRoot 'src/Faith.UI.Service'
  $serverStart.UseShellExecute = $false
  $serverStart.CreateNoWindow = $true
  $serverStart.ArgumentList.Add($webAssembly)
  $serverStart.Environment['ASPNETCORE_ENVIRONMENT'] = 'Development'
  $serverStart.Environment['ASPNETCORE_URLS'] = 'https://localhost:7241;http://localhost:5241'
  $serverStart.Environment['DOTNET_CLI_DO_NOT_USE_MSBUILD_SERVER'] = '1'
  $processes.Add($job.Start($serverStart))
  $hostStart = [System.Diagnostics.ProcessStartInfo]::new((Get-Command node).Source)
  $hostStart.WorkingDirectory = $repoRoot
  $hostStart.UseShellExecute = $false
  $hostStart.CreateNoWindow = $true
  $hostStart.ArgumentList.Add((Join-Path $repoRoot 'samples/embed-host/serve.mjs'))
  $processes.Add($job.Start($hostStart))
  $processes | Select-Object Id,ProcessName,StartTime | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $repoRoot 'artifacts/demo-processes.json')
  Write-Host 'Faith.UI: https://localhost:7241 | Test page: http://localhost:5242'
  Write-Host 'Stop with Ctrl+C or create artifacts/demo.stop. Both servers are owned by this launcher.'
  $elapsed = [System.Diagnostics.Stopwatch]::StartNew()
  while (-not (Test-Path -LiteralPath $stopFile)) {
    foreach ($process in $processes) {
      if ($process.HasExited) { throw "Demo process $($process.Id) exited with code $($process.ExitCode)." }
    }
    if ($RunSeconds -gt 0 -and $elapsed.Elapsed.TotalSeconds -ge $RunSeconds) { break }
    Start-Sleep -Milliseconds 250
  }
} finally {
  try {
    $job.Stop()
    $settle = [System.Diagnostics.Stopwatch]::StartNew()
    while ($job.ActiveProcesses -ne 0 -and $settle.Elapsed.TotalSeconds -lt 10) { Start-Sleep -Milliseconds 50 }
    if ($job.ActiveProcesses -ne 0) { throw 'Demo descendants did not stop within 10 seconds.' }
    Start-Sleep -Milliseconds 500
    if ($job.ActiveProcesses -ne 0) { throw 'Demo descendants remained after cleanup.' }
    Write-Host 'Demo servers stopped; no task-owned descendants remain.'
  } finally {
    $job.Dispose()
    foreach ($process in $processes) { $process.Dispose() }
  }
}
