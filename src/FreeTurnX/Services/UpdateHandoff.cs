using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace FreeTurnX.Services;

/// <summary>The unelevated supervisor survives the client and restarts the installed copy.</summary>
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public sealed class UpdateHandoff
{
    readonly string folder = Path.Combine(Path.GetTempPath(), "FreeTurnX-update-" + Guid.NewGuid().ToString("N"));
    public void Abort() => File.WriteAllText(Path.Combine(folder, "abort"), "1");
    static string Quote(string value) => "'" + value.Replace("'", "''") + "'";
    static string Encode(string value) => Convert.ToBase64String(Encoding.Unicode.GetBytes(value));

    internal static string BuildSupervisor(string setup, string folder, string expected, string installedPath, int parentId, bool elevate = true)
    {
        var status = Quote(Path.Combine(folder, "status"));
        var abort = Quote(Path.Combine(folder, "abort"));
        var installer = Quote(setup);
        var installed = Quote(installedPath);
        var elevated = $$"""
            $ErrorActionPreference = 'Stop'
            try {
                if ((Get-FileHash -LiteralPath {{installer}} -Algorithm SHA256).Hash.ToLowerInvariant() -ne '{{expected}}') { throw 'Installer checksum mismatch' }
                $client = Get-Process -Id {{parentId}} -ErrorAction SilentlyContinue
                [IO.File]::WriteAllText({{status}}, 'ready')
                if ($client -and !$client.WaitForExit(120000)) { throw 'Client did not exit; installation cancelled' }
                if (Test-Path -LiteralPath {{abort}}) { exit 1223 }
                $setupProcess = Start-Process -FilePath {{installer}} -ArgumentList '/S' -WindowStyle Hidden -Wait -PassThru
                exit $setupProcess.ExitCode
            } catch { [IO.File]::WriteAllText({{status}}, $_.Exception.Message); exit 1 }
            """;
        var supervisor = $$"""
            $ErrorActionPreference = 'Stop'
            try {
                $worker = Start-Process -FilePath "$PSHOME\powershell.exe" -ArgumentList '-NoProfile','-WindowStyle','Hidden','-EncodedCommand','{{Encode(elevated)}}' {{(elevate ? "-Verb RunAs" : "")}} -WindowStyle Hidden -Wait -PassThru
                if (Test-Path -LiteralPath {{abort}}) { exit }
                if ($worker.ExitCode -ne 0) { throw "Installer exited with code $($worker.ExitCode)" }
                $app = {{installed}}
                Start-Process -FilePath $app
                Remove-Item -LiteralPath {{installer}} -ErrorAction SilentlyContinue
            } catch {
                $cancelled = $_.Exception.NativeErrorCode -eq 1223 -or $_.Exception.InnerException.NativeErrorCode -eq 1223
                $message = if ($cancelled) { 'cancelled' } else { $_.Exception.Message }
                [IO.File]::WriteAllText({{status}}, $message)
                if (!(Get-Process -Id {{parentId}} -ErrorAction SilentlyContinue) -and !$cancelled) {
                    Add-Type -AssemblyName PresentationFramework
                    [System.Windows.MessageBox]::Show($message, 'FreeTurnX update') | Out-Null
                }
            }
            """;
        return supervisor;
    }

    public static async Task<UpdateHandoff> PrepareAsync(string setup, CancellationToken cancellationToken)
    {
        var handoff = new UpdateHandoff();
        Directory.CreateDirectory(handoff.folder);
        using var registry = Microsoft.Win32.RegistryKey.OpenBaseKey(Microsoft.Win32.RegistryHive.LocalMachine, Microsoft.Win32.RegistryView.Registry64);
        using var key = registry.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\FreeTurnX");
        var directory = key?.GetValue("InstallLocation") as string;
        if (string.IsNullOrWhiteSpace(directory)) directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "FreeTurnX");
        var supervisor = BuildSupervisor(setup, handoff.folder, FreeTurn.Core.UpdateCheck.Sha256(setup), Path.Combine(directory, "FreeTurnX.exe"), Environment.ProcessId);
        try
        {
            using var process = Process.Start(new ProcessStartInfo("powershell.exe")
            {
                ArgumentList = { "-NoProfile", "-WindowStyle", "Hidden", "-EncodedCommand", Encode(supervisor) },
                UseShellExecute = false, CreateNoWindow = true,
            }) ?? throw new IOException("Could not start update supervisor");
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(TimeSpan.FromMinutes(5));
            while (true)
            {
                await Task.Delay(200, deadline.Token);
                var path = Path.Combine(handoff.folder, "status");
                if (File.Exists(path))
                {
                    string state;
                    try { state = await File.ReadAllTextAsync(path, deadline.Token); }
                    catch (IOException) { continue; }
                    if (state == "ready") return handoff;
                    if (state == "cancelled") throw new OperationCanceledException();
                    if (state.Length > 0) throw new InvalidOperationException(state);
                }
                if (process.HasExited) throw new IOException("Update supervisor exited before installation was ready");
            }
        }
        catch { handoff.Abort(); throw; }
    }
}
