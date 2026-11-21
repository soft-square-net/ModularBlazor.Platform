namespace soft.square.net.ModularBlazor.Abstractions.Localization;

/// <summary>A language the app offers: its culture name ("ar"), its own name ("العربية") and its writing direction.</summary>
public sealed record LocaleInfo(string Culture, string NativeName, bool IsRightToLeft)
{
    /// <summary>"rtl" or "ltr", for the HTML dir attribute.</summary>
    public string Direction => IsRightToLeft ? "rtl" : "ltr";
}

/// <summary>
/// The user's language. The host registers it; layouts show a switcher, pages and modules read texts with
/// <c>IStringLocalizer&lt;T&gt;</c> (translations embedded in each module as i18n/{culture}.json, see the README).
/// <para>
/// The culture is the app's default (Localization:DefaultCulture, Arabic) until the user picks another one, which is
/// remembered in the browser. Switching sets <c>CultureInfo</c> (texts, dates, numbers), <c>&lt;html lang dir&gt;</c>,
/// MudTail's locale and the <c>Accept-Language</c> of every API call, then raises <see cref="Changed"/>; the host
/// renders the whole app again, so components only need to re-read their texts.
/// </para>
/// </summary>
public interface ILocaleService
{
    /// <summary>The current culture name, e.g. "ar".</summary>
    string Culture { get; }

    /// <summary>True when the current language is written right to left.</summary>
    bool IsRightToLeft { get; }

    /// <summary>The languages the app offers, the default first.</summary>
    IReadOnlyList<LocaleInfo> Available { get; }

    /// <summary>Switches the language (ignored when it is not offered) and remembers it in this browser.</summary>
    Task SetCultureAsync(string culture);

    /// <summary>Raised after a switch.</summary>
    event Action? Changed;
}
