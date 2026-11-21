using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using FreeTurn.Core;
using static FreeTurnX.Strings;

namespace FreeTurnX.Services;

public enum UpdatePhase { Idle, Checking, UpToDate, Available, Downloading, Installing, Failed }

/// <summary>Автообновление из GitHub Releases: установщик своей архитектуры, проверка SHA-256,
/// тихая установка с запросом UAC и перезапуск.</summary>
public sealed class Updater : INotifyPropertyChanged
{
    static readonly HttpClient Http = CreateClient();

    public event PropertyChangedEventHandler? PropertyChanged;
    UpdatePhase phase;
    public UpdatePhase Phase { get => phase; private set { phase = value; Changed(); Changed(nameof(StatusText)); } }
    public AvailableUpdate? Available { get; private set; }
    string error = "";
    public DownloadProgress Download { get; private set; }

    public static string CurrentVersion => typeof(Updater).Assembly.GetName().Version is { } v ? $"{v.Major}.{v.Minor}.{v.Build}" : "0.0.0";

    /// <summary>Архитектура этого процесса: x64-сборка под эмуляцией на ARM ставит x64 и дальше.</summary>
    public static string Arch => RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "arm64" : "x64";

    public string StatusText => Phase switch
    {
        UpdatePhase.Checking => L("Проверка…"),
        UpdatePhase.UpToDate => L("Установлена последняя версия"),
        UpdatePhase.Available => F("Доступна версия %@", Available?.Version ?? ""),
        UpdatePhase.Downloading => Download.Total is > 0
            ? $"{L("Загрузка…")} {Download.Received * 100 / Download.Total}% · {Download.Received / 1048576.0:F1} / {Download.Total / 1048576.0:F1} MB"
            : $"{L("Загрузка…")} {Download.Received / 1048576.0:F1} MB",
        UpdatePhase.Installing => L("Установка…"),
        UpdatePhase.Failed => error,
        _ => "",
    };

    static HttpClient CreateClient()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd("FreeTurnX-Updater");
        c.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return c;
    }

    public async Task CheckAsync()
    {
        if (PackageChannel.IsPackaged) { PackageChannel.OpenUpdates(); return; }
        if (Phase is UpdatePhase.Checking or UpdatePhase.Downloading or UpdatePhase.Installing) return;
        Phase = UpdatePhase.Checking;
        try
        {
            using var r = await Http.GetAsync(UpdateCheck.LatestUrl);
            // 404 - в репозитории ещё нет релизов.
            if ((int)r.StatusCode == 404) { Phase = UpdatePhase.UpToDate; return; }
            r.EnsureSuccessStatusCode();
            Available = UpdateCheck.Available(await r.Content.ReadAsStringAsync(), CurrentVersion, Arch);
            Phase = Available is null ? UpdatePhase.UpToDate : UpdatePhase.Available;
        }
        catch (Exception)
        {
            Fail(L("Не удалось проверить обновления."));
        }
    }

    /// <summary>Скачивает и запускает установщик; beforeQuit - отключить VPN перед выходом.</summary>
    public async Task InstallAsync(AvailableUpdate update, Func<Task> beforeQuit, Action quit, CancellationToken cancellationToken = default)
    {
        if (PackageChannel.IsPackaged) { PackageChannel.OpenUpdates(); return; }
        Download = default;
        Phase = UpdatePhase.Downloading;
        string? setup = null;
        UpdateHandoff? handoff = null;
        try
        {
            setup = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + "-" + Path.GetFileName(update.SetupUrl.AbsolutePath));
            var progress = new Progress<DownloadProgress>(value => { Download = value; Changed(nameof(Download)); Changed(nameof(StatusText)); });
            await UpdateDownload.DownloadAsync(Http, update.SetupUrl, setup, progress, cancellationToken);
            // Без контрольной суммы не ставим: установщик без подписи Authenticode иначе ничем не проверить.
            if (update.ChecksumsUrl is null) throw new InvalidOperationException(L("Контрольная сумма обновления не совпала — установка отменена."));
            var sums = await Http.GetStringAsync(update.ChecksumsUrl, cancellationToken);
            if (UpdateCheck.Checksum(Path.GetFileName(update.SetupUrl.AbsolutePath), sums) is not { } expected || UpdateCheck.Sha256(setup) != expected)
                throw new InvalidOperationException(L("Контрольная сумма обновления не совпала — установка отменена."));

            Phase = UpdatePhase.Installing;
            handoff = await UpdateHandoff.PrepareAsync(setup, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            await beforeQuit();
            cancellationToken.ThrowIfCancellationRequested();
            quit();
        }
        catch (OperationCanceledException)
        {
            handoff?.Abort();
            if (setup is not null) TryDelete(setup);
            Phase = UpdatePhase.Idle;
        }
        catch (Exception e)
        {
            handoff?.Abort();
            if (setup is not null) TryDelete(setup);
            Fail(e is TimeoutException ? L("Загрузка остановилась: сервер не отвечает. Повторите попытку.") : L("Не удалось установить обновление: ") + e.Message);
        }
    }

    static void TryDelete(string path)
    {
        try { File.Delete(path); } catch (IOException) { }
    }

    void Fail(string message)
    {
        error = message;
        Phase = UpdatePhase.Failed;
    }

    void Changed([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
