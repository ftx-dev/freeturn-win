using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using FreeTurn.Core;

namespace FreeTurnX.Services;

/// <summary>Клиент службы freeturnd (именованный канал, строки JSON).
/// Пока соединение открыто, служба держит движок; закрытие соединения снимает туннель.</summary>
public sealed class DaemonClient(SynchronizationContext ui, string pipeName = DaemonProtocol.PipeName) : IEngineControl
{
    public event Action<EngineEvent>? EventReceived;

    NamedPipeClientStream? pipe;
    StreamWriter? writer;
    readonly object gate = new();
    readonly Queue<TaskCompletionSource<JsonObject>> waiting = new();
    /// <summary>Ответ без запроса: служба сама говорит «busy» при подключении.</summary>
    JsonObject? unsolicited;
    bool open;

    public async Task<DaemonStatus> StatusAsync()
    {
        var reply = await RequestAsync(new JsonObject { ["cmd"] = "status" });
        return new DaemonStatus((bool?)reply["running"] ?? false, (string?)reply["version"] ?? "");
    }

    public Task StartAsync(string config) => RequestAsync(new JsonObject { ["cmd"] = "start", ["config"] = config });

    public async Task StopAsync()
    {
        if (!open) return;
        try { await RequestAsync(new JsonObject { ["cmd"] = "stop" }); } catch (DaemonException) { }
    }

    public void Close()
    {
        lock (gate) open = false;
        try { pipe?.Dispose(); } catch (IOException) { }
        Finish();
    }

    async Task<JsonObject> RequestAsync(JsonObject body)
    {
        await EnsureConnectedAsync();
        var tcs = new TaskCompletionSource<JsonObject>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (gate)
        {
            if (unsolicited is { } early) { unsolicited = null; tcs.SetResult(early); }
            else if (!open) throw new DaemonException(DaemonErrorKind.Failed, "служба закрыла соединение");
            else waiting.Enqueue(tcs);
        }
        if (!tcs.Task.IsCompleted)
        {
            // Ответ («busy») может уже лежать в канале - его дочитает поток чтения.
            try { await writer!.WriteLineAsync(body.ToJsonString()); } catch (IOException) { }
        }
        JsonObject reply;
        try { reply = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(35)); }
        catch (TimeoutException)
        {
            Close();
            throw new DaemonException(DaemonErrorKind.Failed, "Таймаут ответа службы туннеля");
        }
        if ((bool?)reply["ok"] == true) return reply;
        var err = (string?)reply["err"] ?? "";
        throw new DaemonException(err == "busy" ? DaemonErrorKind.Busy : DaemonErrorKind.Failed, err);
    }

    async Task EnsureConnectedAsync()
    {
        if (open) return;
        var p = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        try
        {
            await p.ConnectAsync(1500);
        }
        catch (Exception e) when (e is TimeoutException or IOException or UnauthorizedAccessException)
        {
            p.Dispose();
            // Канала нет - служба не установлена или не запущена.
            throw new DaemonException(DaemonErrorKind.NotInstalled, e.Message);
        }
        pipe = p;
        writer = new StreamWriter(p, new UTF8Encoding(false)) { AutoFlush = true, NewLine = "\n" };
        lock (gate) open = true;
        var reader = new StreamReader(p, Encoding.UTF8);
        _ = Task.Run(async () =>
        {
            try
            {
                while (await reader.ReadLineAsync() is { } line) Receive(line);
            }
            catch (Exception e) when (e is IOException or ObjectDisposedException) { }
            Finish();
        });
    }

    void Receive(string line)
    {
        JsonObject? o;
        try { o = JsonNode.Parse(line) as JsonObject; }
        catch (System.Text.Json.JsonException) { return; }
        if (o is null) return;
        if (o["ev"] is { } ev)
        {
            EngineEvent? e = (string?)ev switch
            {
                "log" => new EngineEvent.Log((string?)o["line"] ?? ""),
                "exit" => new EngineEvent.Exited((int?)o["code"] ?? 0, (string?)o["tail"] ?? ""),
                _ => null,
            };
            if (e is not null) ui.Post(_ => EventReceived?.Invoke(e), null);
            return;
        }
        TaskCompletionSource<JsonObject>? tcs = null;
        lock (gate)
        {
            if (waiting.Count == 0) unsolicited = o;
            else tcs = waiting.Dequeue();
        }
        tcs?.SetResult(o);
    }

    /// <summary>Соединение кончилось: все, кто ждал ответа, получают ошибку.</summary>
    void Finish()
    {
        TaskCompletionSource<JsonObject>[] pending;
        lock (gate)
        {
            open = false;
            pending = [.. waiting];
            waiting.Clear();
        }
        foreach (var t in pending) t.TrySetException(new DaemonException(DaemonErrorKind.Failed, "служба закрыла соединение"));
    }
}
