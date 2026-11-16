using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using FreeTurn.Core;

namespace FreeTurnX.Services;

/// <summary>Дочерний процесс freeturn-core.exe. Общение - строки JSON через stdin/stdout.</summary>
public sealed class CoreProcess(string binary, string stateDir, SynchronizationContext ui) : ICoreControl
{
    public event Action<CoreEvent>? EventReceived;
    Process? process;
    StreamWriter? input;

    public void Launch()
    {
        Directory.CreateDirectory(stateDir);
        var p = new Process
        {
            StartInfo = new ProcessStartInfo(binary)
            {
                ArgumentList = { "-state-dir", stateDir },
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
            },
            EnableRaisingEvents = true,
        };
        p.OutputDataReceived += (_, e) =>
        {
            if (e.Data is null || CoreEvent.Decode(e.Data) is not { } ev) return;
            // Очередь потока интерфейса сохраняет порядок событий.
            ui.Post(_ => EventReceived?.Invoke(ev), null);
        };
        p.ErrorDataReceived += (_, _) => { };
        p.Exited += (_, _) => ui.Post(_ => EventReceived?.Invoke(new CoreEvent.Exited()), null);
        p.Start();
        p.BeginOutputReadLine();
        p.BeginErrorReadLine();
        input = p.StandardInput;
        input.AutoFlush = true;
        process = p;
    }

    public void Send(CoreCommand command)
    {
        try { input?.WriteLine(command.Encoded()); }
        catch (IOException) { } // ядро уже вышло - об этом скажет событие Exited
        catch (ObjectDisposedException) { }
    }

    /// <summary>Закрытый stdin для ядра значит «приложение ушло»: оно останавливает сессию и выходит само.</summary>
    public void Terminate()
    {
        try { input?.Close(); } catch (IOException) { }
        input = null;
        var stuck = process;
        process = null;
        if (stuck is null) return;
        _ = System.Threading.Tasks.Task.Delay(6000).ContinueWith(_ =>
        {
            try { if (!stuck.HasExited) stuck.Kill(); } catch (InvalidOperationException) { }
        });
    }
}
