using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
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

    public static string CurrentVersion => typeof(Updater).Assembly.GetName().Version is { } v ? $"{v.Major}.{v.Minor}.{v.Build}" : "0.0.0";

    /// <summary>Архитектура этого процесса: x64-сборка под эмуляцией на ARM ставит x64 и дальше.</summary>
    public static string Arch => RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "arm64" : "x64";

    public string StatusText => Phase switch
    {
        UpdatePhase.Checking => L("Проверка…"),
        UpdatePhase.UpToDate => L("Установлена последняя версия"),
        UpdatePhase.Available => F("Доступна версия %@", Available?.Version ?? ""),
        UpdatePhase.Downloading => L("Загрузка…"),
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
    public async Task InstallAsync(AvailableUpdate update, Func<Task> beforeQuit, Action quit)
    {
        Phase = UpdatePhase.Downloading;
        try
        {
            var setup = await DownloadAsync(update.SetupUrl);
            // Без контрольной суммы не ставим: установщик без подписи Authenticode иначе ничем не проверить.
            if (update.ChecksumsUrl is null) throw new InvalidOperationException(L("Контрольная сумма обновления не совпала — установка отменена."));
            var sums = await Http.GetStringAsync(update.ChecksumsUrl);
            if (UpdateCheck.Checksum(Path.GetFileName(update.SetupUrl.AbsolutePath), sums) is not { } expected || UpdateCheck.Sha256(setup) != expected)
                throw new InvalidOperationException(L("Контрольная сумма обновления не совпала — установка отменена."));

            Phase = UpdatePhase.Installing;
            // Установщику нужны права администратора, а приложение должно выйти, чтобы его заменили.
            // Скрытый PowerShell от пользователя ждёт установщик и открывает новую версию уже без прав.
            var script = $"$p = Start-Process -FilePath '{Escape(setup)}' -ArgumentList '/S' -Verb RunAs -Wait -PassThru; " +
                         $"if ($p.ExitCode -eq 0) {{ Start-Process -FilePath '{Escape(Environment.ProcessPath!)}' }}";
            Process.Start(new ProcessStartInfo("powershell.exe")
            {
                ArgumentList = { "-NoProfile", "-WindowStyle", "Hidden", "-Command", script },
                UseShellExecute = false,
                CreateNoWindow = true,
            });
            await beforeQuit();
            quit();
        }
        catch (Exception e)
        {
            Fail(e is InvalidOperationException ? e.Message : L("Не удалось установить обновление: ") + e.Message);
        }
    }

    static string Escape(string path) => path.Replace("'", "''");

    static async Task<string> DownloadAsync(Uri url)
    {
        var target = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + "-" + Path.GetFileName(url.AbsolutePath));
        using var r = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
        if (!r.IsSuccessStatusCode) throw new InvalidOperationException(L("Не удалось скачать обновление."));
        await using var file = File.Create(target);
        await r.Content.CopyToAsync(file);
        return target;
    }

    void Fail(string message)
    {
        error = message;
        Phase = UpdatePhase.Failed;
    }

    void Changed([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
