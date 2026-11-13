using System.Globalization;

namespace ChestCounter;

/// <summary>
/// User interface language (French, English, Moroccan Darija written in Latin letters, the way players type it:
/// 3 = ع, 7 = ح, 9 = ق, or Spanish), chosen on first launch and saved in config.json. Texts are written inline as
/// <c>L.T("français", "English", "darija", "español")</c>. Names stored in data.json (chest kinds, treasure items) are
/// kept in French for compatibility and translated when displayed (<see cref="Name"/>).
/// </summary>
public static class L
{
    /// <summary>Languages offered, in menu order ("ary": ISO code of Moroccan Arabic).</summary>
    public static readonly string[] Codes = { "fr", "en", "ary", "es" };

    /// <summary>"fr", "en", "ary" or "es".</summary>
    public static string Code { get; private set; } = "fr";

    public static bool En => Code == "en";
    public static bool Dr => Code == "ary";
    public static bool Es => Code == "es";

    /// <summary>Numbers and dates: Darija uses the Moroccan French conventions ("2 800", "9,02 %", "5 déc.").</summary>
    public static CultureInfo Culture { get; private set; } = CultureInfo.GetCultureInfo("fr-FR");

    public static void Set(string? code)
    {
        Code = Normalize(code) ?? "fr";
        Culture = CultureInfo.GetCultureInfo(T("fr-FR", "en-GB", "fr-MA", "es-ES"));
    }

    /// <summary>"fr", "en", "ary", "es" or null when the answer is not recognized.</summary>
    public static string? Normalize(string? code) => code?.Trim().ToLowerInvariant() switch
    {
        "fr" or "fra" or "fre" or "french" or "français" or "francais" => "fr",
        "en" or "eng" or "english" or "anglais" => "en",
        "ary" or "darija" or "dr" or "ma" or "ar" or "arabic" or "arabe" or "maghribiya" => "ary",
        "es" or "spa" or "spanish" or "español" or "espanol" or "espagnol" => "es",
        _ => null,
    };

    /// <summary>Name of a language in that language (menus).</summary>
    public static string NativeName(string code) => code switch
    {
        "en" => "English",
        "ary" => "Darija (b l7rouf latin)",
        "es" => "Español",
        _ => "Français",
    };

    /// <summary>Language of Windows when it is one of ours (Arabic: Darija), English otherwise.</summary>
    public static string SystemDefault => Normalize(CultureInfo.CurrentUICulture.TwoLetterISOLanguageName) ?? "en";

    public static string T(string fr, string en, string dr, string es) => Code switch { "en" => en, "ary" => dr, "es" => es, _ => fr };

    public static string N(int n) => n.ToString("N0", Culture);

    /// <summary>"3 coffres" / "3 chests" / "3 snadeq" / "3 cofres" (French and English: + "s"; Darija: its own
    /// plurals; Spanish: + "s" or "es").</summary>
    public static string Count(int n, string fr, string en, string dr, string es) =>
        Count(n, fr, fr + "s", en, en + "s", dr, DarijaPlural(dr), es, SpanishPlural(es));

    /// <summary>French: singular for 0 and 1. English and Spanish: singular for 1 only. Darija: plural from 2 to 10
    /// (and 0), singular for 1 and from 11 on, as it is said ("3 snadeq", "50 sndouq").</summary>
    public static string Count(int n, string frOne, string frMany, string enOne, string enMany, string drOne, string drMany,
        string esOne, string esMany) => Code switch
    {
        "en" => $"{N(n)} {(n == 1 ? enOne : enMany)}",
        "ary" => $"{N(n)} {(n == 1 || n > 10 ? drOne : drMany)}",
        "es" => $"{N(n)} {(n == 1 ? esOne : esMany)}",
        _ => $"{N(n)} {(n > 1 ? frMany : frOne)}",
    };

    /// <summary>Darija plurals of the words counted by <see cref="Count(int, string, string, string, string)"/>.</summary>
    private static string DarijaPlural(string one) => one switch
    {
        "sndouq" => "snadeq",
        "partiya" => "partiyat",
        "kenz" => "knouz",
        "mkesser" => "mkessrin",
        "jeton" => "jetonat",
        _ => one,
    };

    /// <summary>Spanish plural of a single word: "cofre" → "cofres", "jugador" → "jugadores", "repetición" →
    /// "repeticiones", "vez" → "veces".</summary>
    private static string SpanishPlural(string one)
    {
        if (one.Length == 0 || one.Contains(' '))
        {
            return one;
        }
        if (one.EndsWith("ión", StringComparison.Ordinal))
        {
            return one[..^3] + "iones";
        }
        if (one.EndsWith('z'))
        {
            return one[..^1] + "ces";
        }
        return "aeiouáéó".Contains(one[^1]) ? one + "s" : one + "es";
    }

    /// <summary>A probability as a percentage: "9.02%" / "9,02 %". Small values keep 3 significant digits ("0.0123%").</summary>
    public static string Pct(double p)
    {
        var v = Math.Clamp(p, 0, 1) * 100;
        if (v <= 0 || double.IsNaN(v))
        {
            v = 0; // never "-0.00%" (a negative zero from the log-space maths)
        }
        string s;
        if (v > 99.99 && v < 100)
        {
            s = "> " + 99.99.ToString("F2", Culture);
        }
        else
        {
            var digits = v >= 0.1 || v == 0 ? 2 : Math.Min(8, 2 - (int)Math.Floor(Math.Log10(v)));
            s = v.ToString("F" + digits, Culture);
        }
        return En ? s + "%" : s + " %";
    }

    /// <summary>"1 in 11" / "1 sur 11" / "1 f 11" / "1 entre 11" (a dash when the probability is zero).</summary>
    public static string OneIn(double p)
    {
        if (p <= 0 || double.IsNaN(p))
        {
            return "—";
        }
        var n = 1 / p;
        var text = n < 10 ? n.ToString("0.0", Culture) : Math.Round(n).ToString("N0", Culture);
        return T($"1 sur {text}", $"1 in {text}", $"1 f {text}", $"1 entre {text}");
    }

    /// <summary>A number with no decimals, or one below 10 ("2.4", "1,234").</summary>
    public static string Num(double n) => Math.Abs(n) < 10 && n != Math.Round(n) ? n.ToString("0.0", Culture) : Math.Round(n).ToString("N0", Culture);

    /// <summary>"12e" / "12th" / "lwel" then "#12" / "12.º".</summary>
    public static string Ordinal(int n)
    {
        if (Dr)
        {
            return n == 1 ? "lwel" : $"#{n}";
        }
        if (Es)
        {
            return $"{n}.º";
        }
        if (!En)
        {
            return n == 1 ? "1er" : $"{n}e";
        }
        var suffix = (n % 100) is 11 or 12 or 13 ? "th" : (n % 10) switch { 1 => "st", 2 => "nd", 3 => "rd", _ => "th" };
        return $"{n}{suffix}";
    }

    /// <summary>Short date + time of a match ("05/11 22:54" / "5 Nov 22:54").</summary>
    public static string DateTimeShort(DateTime d) => d.ToString(En ? "d MMM HH:mm" : "dd/MM HH:mm", Culture);

    // French names stored in data.json -> English / Darija / Spanish display names.
    private static readonly Dictionary<string, (string En, string Dr, string Es)> Names = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Coffre"] = ("Chest", "Sndouq", "Cofre"),
        ["Coffre rare"] = ("Rare chest", "Sndouq rare", "Cofre raro"),
        ["Coffre enterré"] = ("Buried chest", "Sndouq mdfoun", "Cofre enterrado"),
        ["Coffre d'esprit (rare)"] = ("Sprite chest (rare)", "Sndouq dyal l'esprit (rare)", "Cofre de espíritu (raro)"),
        ["Coffre des affiches d'esprit"] = ("Sprite poster chest", "Sndouq dyal les affiches", "Cofre de carteles de espíritu"),
        ["Autre coffre"] = ("Other chest", "Sndouq akhor", "Otro cofre"),
        ["Canon à butin"] = ("Loot cannon", "Canon dyal l'butin", "Cañón de botín"),
        ["Caisse de munitions"] = ("Ammo box", "Caisse dyal l'munitions", "Caja de munición"),
        ["Caisse de nourriture"] = ("Food box", "Caisse dyal l'makla", "Caja de comida"),
        ["Autre conteneur"] = ("Other container", "Chi 7aja khra", "Otro contenedor"),
        ["Trésor"] = ("Treasure", "Kenz", "Tesoro"),
        ["Trésor (type inconnu)"] = ("Treasure gizmo", "Gadget dyal l'kenz", "Gizmo del tesoro"),
        ["Gizmo de trésor"] = ("Treasure gizmo", "Gadget dyal l'kenz", "Gizmo del tesoro"),
        ["Feu d'artifice doré"] = ("Golden firework", "Feu d'artifice dhabi", "Fuego artificial dorado"),
        ["Extracteur portable"] = ("Portable extractor", "Extracteur portable", "Extractor portátil"),
        ["Localisateur d'esprit"] = ("Sprite locator", "Localisateur dyal l'esprit", "Localizador de espíritus"),
        ["Localisateur chanceux"] = ("Lucky locator", "Localisateur dyal zher", "Localizador de la suerte"),
        ["Accélérateur d'extraction"] = ("Extraction accelerator", "Accélérateur d'extraction", "Acelerador de extracción"),
        ["inconnu"] = ("unknown", "ma3rofch", "desconocido"),
    };

    /// <summary>Display name of a chest kind or treasure item stored in French.</summary>
    public static string Name(string? stored)
    {
        if (stored != null)
        {
            stored = ChestKinds.Canonical(stored); // older name of a kind
        }
        if (string.IsNullOrEmpty(stored) || Code == "fr" || !Names.TryGetValue(stored, out var t))
        {
            return stored ?? "";
        }
        return Code switch { "en" => t.En, "es" => t.Es, _ => t.Dr };
    }
}
