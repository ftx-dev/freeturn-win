namespace FreeTurn.Core;

public sealed record CaptchaRequest(string Url, bool Manual);

public static class CaptchaUrl
{
    /// <summary>Только локальная страница капчи ядра (порт 8765 на loopback) допускается к показу.</summary>
    public static bool IsLocal(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var u) && u.Scheme == "http" && u.UserInfo.Length == 0 && u.Port == 8765
        && (u.Host == "127.0.0.1" || u.Host == "localhost");
}

public enum CaptchaActionKind { None, ShowWindow, OpenBrowser, LoadHidden, Dismiss }

public sealed record CaptchaAction(CaptchaActionKind Kind, string Url = "")
{
    public static readonly CaptchaAction None = new(CaptchaActionKind.None);
    public static readonly CaptchaAction Dismiss = new(CaptchaActionKind.Dismiss);
}

/// <summary>Решает, что делать с каждым событием капчи от ядра. Без интерфейса - чтобы это можно было проверить.</summary>
public sealed class CaptchaPresenter
{
    string? shownUrl;
    string? manualUrl;

    /// <summary>Ручная капча ждёт пользователя (в том числе после того, как он закрыл окно).</summary>
    public bool NeedsAttention => manualUrl is not null;

    public CaptchaAction ActionFor(CaptchaRequest? captcha, string target)
    {
        if (captcha is null || captcha.Url.Length == 0)
        {
            if (shownUrl is null) return CaptchaAction.None;
            shownUrl = null;
            manualUrl = null;
            return CaptchaAction.Dismiss;
        }
        if (!CaptchaUrl.IsLocal(captcha.Url) || captcha.Url == shownUrl) return CaptchaAction.None;
        shownUrl = captcha.Url;
        manualUrl = captcha.Manual ? captcha.Url : null;
        return captcha.Manual ? Present(captcha.Url, target) : new CaptchaAction(CaptchaActionKind.LoadHidden, captcha.Url);
    }

    /// <summary>Окно закрыли руками: тот же адрес сам не всплывёт (он уже показан), но открыть его снова можно.</summary>
    public CaptchaAction ReopenAction(string target) => manualUrl is { } url ? Present(url, target) : CaptchaAction.None;

    static CaptchaAction Present(string url, string target) =>
        new(target == "browser" ? CaptchaActionKind.OpenBrowser : CaptchaActionKind.ShowWindow, url);
}
