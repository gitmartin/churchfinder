#requires -Version 7.2

# Share process isolation between builds and the development host. Closing the
# Windows job also catches descendants when PowerShell itself is interrupted.
if ($IsWindows -and -not ('StarterProcessJob' -as [type])) {
  Add-Type @'
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
public sealed class StarterProcessJob : IDisposable {
  [StructLayout(LayoutKind.Sequential)] struct Basic {
    public long ProcessTime, JobTime;
    public uint Flags;
    public UIntPtr MinWorkingSet, MaxWorkingSet;
    public uint ActiveProcesses;
    public UIntPtr Affinity;
    public uint Priority, Scheduling;
  }
  [StructLayout(LayoutKind.Sequential)] struct Io {
    public ulong ReadOps, WriteOps, OtherOps, ReadBytes, WriteBytes, OtherBytes;
  }
  [StructLayout(LayoutKind.Sequential)] struct Extended {
    public Basic Basic;
    public Io Io;
    public UIntPtr ProcessMemory, JobMemory, PeakProcessMemory, PeakJobMemory;
  }
  [StructLayout(LayoutKind.Sequential)] struct Accounting {
    public long UserTime, KernelTime, PeriodUserTime, PeriodKernelTime;
    public uint PageFaults, TotalProcesses, ActiveProcesses, TerminatedProcesses;
  }
  [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] struct StartupInfo {
    public uint Size;
    public string Reserved, Desktop, Title;
    public uint X, Y, XSize, YSize, XChars, YChars, Fill, Flags;
    public ushort ShowWindow, ReservedSize;
    public IntPtr ReservedData, Input, Output, Error;
  }
  [StructLayout(LayoutKind.Sequential)] struct StartupInfoEx {
    public StartupInfo Startup;
    public IntPtr Attributes;
  }
  [StructLayout(LayoutKind.Sequential)] struct ProcessInformation {
    public IntPtr Process, Thread;
    public uint ProcessId, ThreadId;
  }
  [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern IntPtr CreateJobObject(IntPtr attributes, string name);
  [DllImport("kernel32.dll", SetLastError = true)] static extern bool SetInformationJobObject(IntPtr job, int info, IntPtr data, uint length);
  [DllImport("kernel32.dll", SetLastError = true)] static extern bool QueryInformationJobObject(IntPtr job, int info, out Accounting data, uint length, IntPtr returnedLength);
  [DllImport("kernel32.dll", SetLastError = true)] static extern bool InitializeProcThreadAttributeList(IntPtr attributes, int count, int flags, ref IntPtr size);
  [DllImport("kernel32.dll", SetLastError = true)] static extern bool UpdateProcThreadAttribute(IntPtr attributes, uint flags, IntPtr attribute, IntPtr value, IntPtr size, IntPtr previous, IntPtr returnedSize);
  [DllImport("kernel32.dll")] static extern void DeleteProcThreadAttributeList(IntPtr attributes);
  [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern bool CreateProcess(string application, StringBuilder commandLine, IntPtr processSecurity, IntPtr threadSecurity, bool inheritHandles, uint flags, IntPtr environment, string directory, ref StartupInfoEx startup, out ProcessInformation information);
  [DllImport("kernel32.dll", SetLastError = true)] static extern uint ResumeThread(IntPtr thread);
  [DllImport("kernel32.dll", SetLastError = true)] static extern bool TerminateProcess(IntPtr process, uint exitCode);
  [DllImport("kernel32.dll")] static extern IntPtr GetStdHandle(int kind);
  [DllImport("kernel32.dll", SetLastError = true)] static extern bool TerminateJobObject(IntPtr job, uint exitCode);
  [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);
  IntPtr handle;
  public StarterProcessJob() {
    handle = CreateJobObject(IntPtr.Zero, null);
    if (handle == IntPtr.Zero) throw new Win32Exception();
    var settings = new Extended { Basic = new Basic { Flags = 0x2000 } };
    int size = Marshal.SizeOf<Extended>();
    var data = Marshal.AllocHGlobal(size);
    try {
      Marshal.StructureToPtr(settings, data, false);
      if (!SetInformationJobObject(handle, 9, data, (uint)size)) throw new Win32Exception();
    } catch { Dispose(); throw; }
    finally { Marshal.FreeHGlobal(data); }
  }
  static string Quote(string argument) {
    var result = new StringBuilder("\"");
    int slashes = 0;
    foreach (char character in argument) {
      if (character == '\\') { slashes++; continue; }
      if (character == '"') { result.Append('\\', slashes * 2 + 1).Append('"'); }
      else { result.Append('\\', slashes).Append(character); }
      slashes = 0;
    }
    return result.Append('\\', slashes * 2).Append('"').ToString();
  }
  public Process Start(ProcessStartInfo start) {
    var commandLine = new StringBuilder(Quote(start.FileName));
    foreach (string argument in start.ArgumentList) commandLine.Append(' ').Append(Quote(argument));
    var variables = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    foreach (var variable in start.Environment) {
      if (variable.Value != null) variables[variable.Key] = variable.Value;
    }
    var environmentText = new StringBuilder();
    foreach (var variable in variables) environmentText.Append(variable.Key).Append('=').Append(variable.Value).Append('\0');
    environmentText.Append('\0');
    IntPtr attributesSize = IntPtr.Zero;
    InitializeProcThreadAttributeList(IntPtr.Zero, 1, 0, ref attributesSize);
    if (attributesSize == IntPtr.Zero) throw new Win32Exception();
    IntPtr attributes = Marshal.AllocHGlobal(attributesSize);
    IntPtr jobValue = Marshal.AllocHGlobal(IntPtr.Size);
    IntPtr environment = Marshal.StringToHGlobalUni(environmentText.ToString());
    bool initialized = false;
    var information = new ProcessInformation();
    Process process = null;
    try {
      if (!InitializeProcThreadAttributeList(attributes, 1, 0, ref attributesSize)) throw new Win32Exception();
      initialized = true;
      Marshal.WriteIntPtr(jobValue, handle);
      // PROC_THREAD_ATTRIBUTE_JOB_LIST assigns the job atomically at creation.
      if (!UpdateProcThreadAttribute(attributes, 0, new IntPtr(0x2000D), jobValue, new IntPtr(IntPtr.Size), IntPtr.Zero, IntPtr.Zero)) throw new Win32Exception();
      var startup = new StartupInfoEx {
        Startup = new StartupInfo {
          Size = (uint)Marshal.SizeOf<StartupInfoEx>(), Flags = 0x100,
          Input = GetStdHandle(-10), Output = GetStdHandle(-11), Error = GetStdHandle(-12)
        },
        Attributes = attributes
      };
      // Suspend until the managed handle exists, so even an instant exit is safe.
      const uint flags = 0x08000000 | 0x00080000 | 0x00000400 | 0x00000004;
      if (!CreateProcess(null, commandLine, IntPtr.Zero, IntPtr.Zero, true, flags, environment, start.WorkingDirectory, ref startup, out information)) throw new Win32Exception();
      process = Process.GetProcessById((int)information.ProcessId);
      _ = process.Handle;
      if (ResumeThread(information.Thread) == uint.MaxValue) throw new Win32Exception();
      return process;
    } catch {
      if (information.Process != IntPtr.Zero) TerminateProcess(information.Process, 1);
      process?.Dispose();
      throw;
    } finally {
      if (information.Thread != IntPtr.Zero) CloseHandle(information.Thread);
      if (information.Process != IntPtr.Zero) CloseHandle(information.Process);
      if (initialized) DeleteProcThreadAttributeList(attributes);
      Marshal.FreeHGlobal(attributes);
      Marshal.FreeHGlobal(jobValue);
      Marshal.FreeHGlobal(environment);
    }
  }
  public uint ActiveProcesses {
    get {
      if (!QueryInformationJobObject(handle, 1, out var data, (uint)Marshal.SizeOf<Accounting>(), IntPtr.Zero)) throw new Win32Exception();
      return data.ActiveProcesses;
    }
  }
  public void Stop() {
    if (ActiveProcesses != 0 && !TerminateJobObject(handle, 1)) throw new Win32Exception();
  }
  public void Dispose() { if (handle != IntPtr.Zero) { CloseHandle(handle); handle = IntPtr.Zero; } }
}
'@
}

function Invoke-StarterProcess {
  param(
    [Parameter(Mandatory)][string]$FilePath,
    [Parameter(Mandatory)][string[]]$CommandArguments,
    [Parameter(Mandatory)][string]$WorkingDirectory,
    [hashtable]$Environment = @{}
  )
  $start = [System.Diagnostics.ProcessStartInfo]::new($FilePath)
  $start.WorkingDirectory = $WorkingDirectory
  $start.UseShellExecute = $false
  $start.CreateNoWindow = $true
  foreach ($item in $CommandArguments) { $start.ArgumentList.Add($item) }
  foreach ($key in $Environment.Keys) { $start.Environment[$key] = $Environment[$key] }
  $job = if ($IsWindows) { [StarterProcessJob]::new() } else { $null }
  $process = $null
  $started = $false
  try {
    $process = if ($job) { $job.Start($start) } else { [System.Diagnostics.Process]::Start($start) }
    $started = $null -ne $process
    if (-not $started) { throw "Could not start $FilePath." }
    while (-not $process.WaitForExit(1000)) { }
    if ($process.ExitCode -ne 0) { throw "$FilePath failed with exit code $($process.ExitCode)." }
  } finally {
    try {
      if ($job) {
        $job.Stop()
        $timeout = [System.Diagnostics.Stopwatch]::StartNew()
        while ($job.ActiveProcesses -ne 0 -and $timeout.Elapsed.TotalSeconds -lt 10) { Start-Sleep -Milliseconds 50 }
        if ($job.ActiveProcesses -ne 0) { throw 'Task-owned process descendants did not stop within 10 seconds.' }
        Start-Sleep -Milliseconds 500
        if ($job.ActiveProcesses -ne 0) { throw 'Task-owned process descendants remained active after cleanup.' }
      } elseif ($started) {
        if (-not $process.HasExited) { $process.Kill($true) }
        if (-not $process.WaitForExit(10000)) { throw "Task process $($process.Id) did not stop within 10 seconds." }
        if (-not $process.HasExited) { throw "Task process $($process.Id) did not stop." }
        Start-Sleep -Milliseconds 500
        if (-not $process.HasExited) { throw "Task process $($process.Id) remained active after cleanup." }
      }
    } finally {
      if ($job) { $job.Dispose() }
      if ($process) { $process.Dispose() }
    }
  }
}
