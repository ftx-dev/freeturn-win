using System.Diagnostics;
using System.Text;
using FreeTurnX.Services;
using Xunit;

namespace FreeTurn.Core.Tests;

public class UpdateHandoffTests
{
    static Process Run(string script) => Process.Start(new ProcessStartInfo("powershell.exe")
    {
        ArgumentList = { "-NoProfile", "-WindowStyle", "Hidden", "-EncodedCommand", Convert.ToBase64String(Encoding.Unicode.GetBytes(script)) },
        UseShellExecute = false, CreateNoWindow = true,
    })!;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WaitsForOldClientThenInstallsAndRelaunchesUnlessCancelled(bool cancel)
    {
        if (!OperatingSystem.IsWindows()) return;
        var folder = Path.Combine(Path.GetTempPath(), "FreeTurn-handoff-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var setup = Path.Combine(folder, "setup.cmd");
        var installed = Path.Combine(folder, "installed");
        var reopened = Path.Combine(folder, "reopened");
        var app = Path.Combine(folder, "app.vbs");
        await File.WriteAllTextAsync(setup, $"@echo off\r\necho installed>\"{installed}\"\r\nexit /b 0\r\n", Encoding.ASCII);
        await File.WriteAllTextAsync(app, $"Set f = CreateObject(\"Scripting.FileSystemObject\").CreateTextFile(\"{reopened}\", True)\r\nf.WriteLine \"reopened\"\r\nf.Close\r\n", Encoding.ASCII);
        using var parent = Run("Start-Sleep -Seconds 60");
        using var supervisor = Run(UpdateHandoff.BuildSupervisor(setup, folder, UpdateCheck.Sha256(setup), app, parent.Id, elevate: false));
        try
        {
            var status = Path.Combine(folder, "status");
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(35));
            while (!File.Exists(status)) await Task.Delay(100, timeout.Token);
            Assert.Equal("ready", await File.ReadAllTextAsync(status, timeout.Token));
            Assert.False(File.Exists(installed));
            Assert.False(File.Exists(reopened));
            if (cancel) await File.WriteAllTextAsync(Path.Combine(folder, "abort"), "1", timeout.Token);
            parent.Kill();
            await parent.WaitForExitAsync(timeout.Token);
            await supervisor.WaitForExitAsync(timeout.Token);
            if (!cancel) while (!File.Exists(reopened)) await Task.Delay(100, timeout.Token);
            Assert.Equal(!cancel, File.Exists(installed));
            Assert.Equal(!cancel, File.Exists(reopened));
        }
        finally
        {
            if (!parent.HasExited) parent.Kill();
            if (!supervisor.HasExited) supervisor.Kill(entireProcessTree: true);
            // Only the unique directory created by this test is removed.
            try { Directory.Delete(folder, recursive: true); } catch (IOException) { }
        }
    }
}
