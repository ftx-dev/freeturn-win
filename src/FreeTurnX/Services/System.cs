using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Net.Http;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace FreeTurnX.Services;

public static class Paths
{
    public static string InstallDir => AppContext.BaseDirectory;
    public static string CoreExe => Path.Combine(InstallDir, "freeturn-core.exe");
    public static string DaemonExe => Path.Combine(InstallDir, "freeturnd.exe");
}

/// <summary>Служба туннеля ставится установщиком; отсюда - переустановка и удаление по запросу UAC.</summary>
public static class DaemonInstaller
{
    public static Task InstallAsync() => RunElevatedAsync("install");
    public static Task UninstallAsync() => RunElevatedAsync("uninstall");

    static async Task RunElevatedAsync(string verb)
    {
        var info = new ProcessStartInfo(Paths.DaemonExe, verb) { UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden };
        Process? p;
        try { p = Process.Start(info); }
        catch (Win32Exception e) when (e.NativeErrorCode == 1223) { throw new OperationCanceledException(); } // пользователь отказал в UAC
        if (p is null) throw new InvalidOperationException("freeturnd не запустился");
        await p.WaitForExitAsync();
        if (p.ExitCode != 0) throw new InvalidOperationException($"freeturnd {verb}: код {p.ExitCode}");
        // Службе нужно время, чтобы открыть канал.
        await Task.Delay(1500);
    }
}

/// <summary>Запуск при входе в систему.</summary>
public static class LoginItem
{
    const string Key = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string Name = "FreeTurnX";

    public static bool IsEnabled
    {
        get
        {
            using var k = Registry.CurrentUser.OpenSubKey(Key);
            return k?.GetValue(Name) is string;
        }
    }

    public static void Set(bool on)
    {
        using var k = Registry.CurrentUser.CreateSubKey(Key);
        if (on) k.SetValue(Name, $"\"{Environment.ProcessPath}\" --minimized");
        else k.DeleteValue(Name, throwOnMissingValue: false);
    }
}

/// <summary>Сколько машина проспала: после долгого сна аллокации реле протухают.</summary>
public sealed class SleepWatcher : IDisposable
{
    DateTime? sleptAt;
    readonly Action<TimeSpan> onWake;
    readonly SynchronizationContext ui;

    public SleepWatcher(SynchronizationContext ui, Action<TimeSpan> onWake)
    {
        this.ui = ui;
        this.onWake = onWake;
        SystemEvents.PowerModeChanged += OnPower;
    }

    void OnPower(object sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Suspend) sleptAt = DateTime.Now;
        else if (e.Mode == PowerModes.Resume && sleptAt is { } at)
        {
            sleptAt = null;
            ui.Post(_ => onWake(DateTime.Now - at), null);
        }
    }

    public void Dispose() => SystemEvents.PowerModeChanged -= OnPower;
}

/// <summary>Один экземпляр на пользователя; второй запуск (например, клик по ссылке freeturn://)
/// передаёт аргумент первому через канал и выходит.</summary>
public sealed class SingleInstance : IDisposable
{
    // Independent data directories must not forward launches to the installed client.
    static readonly string Id = "FreeTurnX-" + (WindowsIdentity.GetCurrent().User?.Value ?? Environment.UserName)
        + (Environment.GetEnvironmentVariable("FREETURN_DATA_DIR") is { Length: > 0 } directory
            ? "-" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(Path.GetFullPath(directory).TrimEnd('\\').ToUpperInvariant())))[..16]
            : "");
    readonly Mutex mutex;
    public bool IsFirst { get; }
    CancellationTokenSource? cts;

    public SingleInstance()
    {
        mutex = new Mutex(true, @"Local\" + Id, out var created);
        IsFirst = created;
    }

    public void Listen(Action<string> onMessage, SynchronizationContext ui)
    {
        cts = new CancellationTokenSource();
        var token = cts.Token;
        _ = Task.Run(async () =>
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    // Канал только для текущего пользователя: CurrentUserOnly.
                    using var server = new NamedPipeServerStream(Id, PipeDirection.In, 1, PipeTransmissionMode.Byte,
                        PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                    await server.WaitForConnectionAsync(token);
                    using var reader = new StreamReader(server);
                    var message = await reader.ReadToEndAsync(token);
                    ui.Post(_ => onMessage(message), null);
                }
                catch (OperationCanceledException) { return; }
                catch (IOException) { }
            }
        });
    }

    public static bool Send(string message)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", Id, PipeDirection.Out, PipeOptions.CurrentUserOnly);
            client.Connect(2000);
            using var writer = new StreamWriter(client);
            writer.Write(message);
            return true;
        }
        catch (Exception e) when (e is TimeoutException or IOException or UnauthorizedAccessException) { return false; }
    }

    public void Dispose()
    {
        cts?.Cancel();
        if (IsFirst) mutex.ReleaseMutex();
        mutex.Dispose();
    }
}

public static class PingMeter
{
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(3) };

    /// <summary>Задержка HTTP-запроса через туннель, мс; null - нет ответа.</summary>
    public static async Task<int?> MeasureAsync()
    {
        var sw = Stopwatch.StartNew();
        try
        {
            using var r = await Http.SendAsync(new HttpRequestMessage(HttpMethod.Head, "https://www.gstatic.com/generate_204"));
            return (int)r.StatusCode == 204 ? (int)sw.ElapsedMilliseconds : null;
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException) { return null; }
    }
}
