using System.Reflection;
using System.Text.RegularExpressions;

namespace ChestCounter;

/// <summary>The Items tab's checklist (checklist.json): what the player ticked, unticked, and the items they added.</summary>
public sealed class Checklist
{
    /// <summary>Items ticked by the player, with the moment (UTC).</summary>
    public Dictionary<string, DateTime> Got { get; set; } = new();

    /// <summary>Items found in a replay that the player unticked: the replay is not followed for them any more.</summary>
    public List<string> NotMine { get; set; } = new();

    /// <summary>Items added by the player (later weeks: the assistant never goes online to learn them).</summary>
    public List<CustomItem> Custom { get; set; } = new();
}

public sealed class CustomItem
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Type { get; set; } = "other";
    /// <summary>Group shown in the tab ("Week 3"…).</summary>
    public string Week { get; set; } = "";
    public DateTime? StartUtc { get; set; }
    public DateTime? EndUtc { get; set; }
    /// <summary>"1 in N" per chest, when known.</summary>
    public int? OddsOneIn { get; set; }
}

/// <summary>
/// One reward of the treasure hunt. <see cref="OddsKey"/> ties it to a published odds setting (Pirate Shop token,
/// weekly vehicle, weekly gold outfit: editable in the settings, boosted by Treasure Hours); otherwise
/// <see cref="OddsOneIn"/> comes from fan sites (unconfirmed by Epic).
/// </summary>
/// <param name="Picture">File of Assets\items built into the exe (null: a drawn icon).</param>
public sealed record ItemDef(string Id, string Name, string Type, string Group, DateTime? StartUtc, DateTime? EndUtc,
    int? OddsOneIn = null, string? OddsKey = null, bool Builtin = true, string? WeekLabel = null, string? Picture = null);

/// <summary>An item as the tab shows it: got or not (ticked, or found in a replay), and the chance of having found it.</summary>
public sealed record ItemState(ItemDef Def, bool Got, bool Ticked, bool Unticked, AutoFind? Auto, double? Chance, int? Odds, bool OddsPublished);

/// <summary>Found in the replays: how many times, the first match, whether the item is only probably that one.</summary>
public sealed record AutoFind(int Count, DateTime FirstUtc, bool Probable, bool ViaTeammate);

public sealed record ItemsResult(IReadOnlyList<ItemState> Items, int Got, int Total, IReadOnlyList<DateTime> UnknownPrizes);

public static class ItemsModel
{
    /// <summary>Loaded at startup, saved when the player ticks something (like the imported files).</summary>
    public static Checklist Checklist { get; set; } = new();

    // Weekly rotation at 2 PM ET (19:00 UTC), from fan sites (Epic posts no schedule).
    public static readonly DateTime Week1Start = new(2026, 11, 1, 19, 0, 0, DateTimeKind.Utc);
    public static readonly DateTime Week2Start = new(2026, 11, 8, 19, 0, 0, DateTimeKind.Utc);
    public static readonly DateTime Week2End = new(2026, 11, 15, 19, 0, 0, DateTimeKind.Utc);

    public static readonly string[] Types = { "outfit", "pickaxe", "backbling", "loading", "jam", "car", "decal", "wheels", "token", "gizmo", "other" };

    /// <summary>Weeks 1 and 2 and the rewards of the whole season. Odds of the cosmetics other than the weekly vehicle
    /// and gold outfit, and of the gizmos, come from fan sites.</summary>
    public static readonly ItemDef[] Builtin =
    {
        new("w1-outfit", "Solid Gold A$AP Rocky", "outfit", "w1", Week1Start, Week2Start, OddsKey: "gold", Picture: "W1- Solid gold Outfit.jpg"),
        new("w1-pickaxe", "Solid Gold A$AP Rocky's Gauntlets", "pickaxe", "w1", Week1Start, Week2Start, 568, Picture: "asap rocky guntlets.png"),
        new("w1-loading", "Plunder in Paradise", "loading", "w1", Week1Start, Week2Start, 272, Picture: "plunder in paradise.webp"),
        new("w1-jam", "No Trespassing", "jam", "w1", Week1Start, Week2Start, 391),
        new("w1-car", "Mercedes-AMG G 63 'G-Wagon'", "car", "w1", Week1Start, Week2Start, OddsKey: "vehicle", Picture: "w1-car.png"),
        new("w1-decal", "Golden Llama Crew", "decal", "w1", Week1Start, Week2Start, OddsKey: "vehicle", Picture: "w1-decal.png"),
        new("w1-wheels", "Gold G-Wagon", "wheels", "w1", Week1Start, Week2Start, OddsKey: "vehicle", Picture: "golden gwagon wheels.webp"),

        new("w2-outfit", "Solid Gold Lil Uzi Vert", "outfit", "w2", Week2Start, Week2End, OddsKey: "gold", Picture: "Gold lil uzi.png"),
        new("w2-backbling", "Lil' Uzi Crew", "backbling", "w2", Week2Start, Week2End, 568, Picture: "lil uzi crew.png"),
        new("w2-jam", "London", "jam", "w2", Week2Start, Week2End, 391),
        new("w2-loading", "So Kawaii", "loading", "w2", Week2Start, Week2End, 272, Picture: "so kawaii.png"),
        new("w2-car", "Cadillac CT5-V Blackwing", "car", "w2", Week2Start, Week2End, OddsKey: "vehicle", Picture: "w2-car.png"),
        new("w2-decal", "Gold Cadillac", "decal", "w2", Week2Start, Week2End, OddsKey: "vehicle", Picture: "gold cadillac.jpg"),
        new("w2-wheels", "Cadillac CT5-V Blackwing", "wheels", "w2", Week2Start, Week2End, OddsKey: "vehicle", Picture: "cadillac wheels.jpg"),

        new("s-token26", "Pirate Shop Access (2026)", "token", "season", null, null, OddsKey: "pirate", Picture: "token.webp"),
        new("s-token27", "Pirate Shop Access (2027)", "token", "season", null, null, OddsKey: "pirate", Picture: "token.webp"),
        new("s-sprite", "Sprite Locator", "gizmo", "season", null, null, 127, Picture: "Sprite_Locator_-_Item_-_Fortnite.webp"),
        new("s-lucky", "Lucky Locator", "gizmo", "season", null, null, 127, Picture: "Lucky_Locator_-_Item_-_Fortnite.webp"),
        new("s-firework", "Golden Firework", "gizmo", "season", null, null, 127, Picture: "golden firework.png"),
        new("s-extractor", "Portable Extractor", "gizmo", "season", null, null, 127, Picture: "Portable_Extractor_-_Item_-_Fortnite.webp"),
        new("s-accel", "Extraction Accelerator", "gizmo", "season", null, null, 127, Picture: "Extraction-Accelerator-Fortnite.webp"),
    };

    public static string TypeName(string type) => type switch
    {
        "outfit" => L.T("Tenue", "Outfit", "Lbsa", "Atuendo"),
        "pickaxe" => L.T("Pioche", "Pickaxe", "Pioche", "Pico"),
        "backbling" => L.T("Sac à dos", "Back bling", "Sac (back bling)", "Accesorio mochilero"),
        "loading" => L.T("Écran de chargement", "Loading screen", "Écran de chargement", "Pantalla de carga"),
        "jam" => L.T("Piste musicale", "Jam track", "Musique (jam track)", "Pista de improvisación"),
        "car" => L.T("Voiture", "Car", "Tomobil", "Coche"),
        "decal" => L.T("Autocollant", "Decal", "Décalcomanie", "Calcomanía"),
        "wheels" => L.T("Roues", "Wheels", "Roues", "Ruedas"),
        "token" => L.T("Jeton", "Token", "Jeton", "Ficha"),
        "gizmo" => L.T("Gizmo", "Gizmo", "Gizmo", "Gizmo"),
        _ => L.T("Autre", "Other", "Akhor", "Otro"),
    };

    /// <summary>Every item: the built-in ones, then the player's.</summary>
    public static IEnumerable<ItemDef> All(Checklist checklist) => Builtin.Concat(checklist.Custom.Select(c =>
        new ItemDef(c.Id, c.Name, Types.Contains(c.Type) ? c.Type : "other", "c:" + c.Week.Trim(), c.StartUtc, c.EndUtc, c.OddsOneIn, null, false, c.Week.Trim())));

    /// <summary>"1 in N" of an item: the odds setting it follows, or its own figure.</summary>
    public static int? Odds(ItemDef d, OddsSettings s) => d.OddsKey switch
    {
        "pirate" => s.PirateShopOdds,
        "vehicle" => s.VehicleDecalOdds,
        "gold" => s.GoldOutfitOdds,
        _ => d.OddsOneIn,
    };

    private static double Boost(ItemDef d, TreasureHour h) => d.OddsKey switch
    {
        "pirate" => h.PirateShop,
        "vehicle" => h.VehicleDecal,
        "gold" => h.GoldOutfit,
        _ => 1,
    };

    /// <summary>
    /// Which item a treasure found in a replay is, when it can be told: week 1's golden chest prizes (their prize id) and
    /// the gizmos (their item). A golden chest prize of a later week is not known: the caller lists it apart.
    /// </summary>
    public static (string? Id, bool Probable, bool UnknownPrize) Identify(TreasureFind f, DateTime matchUtc)
    {
        var prize = f.PrizeId?.ToLowerInvariant();
        if (prize != null && prize.StartsWith("delilahprize:", StringComparison.Ordinal))
        {
            if (matchUtc < Week2Start)
            {
                switch (prize)
                {
                    case "delilahprize:dpid_item1": return ("w1-pickaxe", true, false);
                    case "delilahprize:dpid_item2": return ("w1-loading", false, false);
                    case "delilahprize:dpid_item3": return ("w1-car", true, false);
                    case "delilahprize:dpid_item4": return ("w1-jam", false, false);
                }
            }
            return (null, false, true);
        }
        if (prize != null && prize.Contains(':'))
        {
            return (null, false, false); // a gizmo whose exact kind is not in the replay
        }
        var item = (f.RawItem + " " + f.Item).ToLowerInvariant();
        if (item.Contains("flameceleb") || item.Contains("feu d'artifice")) return ("s-firework", false, false);
        if (item.Contains("relicfinder") || item.Contains("localisateur d'esprit")) return ("s-sprite", false, false);
        if (item.Contains("luckylocator") || item.Contains("localisateur chanceux")) return ("s-lucky", false, false);
        if (item.Contains("smugglerextractionkey") || item.Contains("extractionaccelerat") || item.Contains("accélérateur d'extraction")) return ("s-accel", false, false);
        if (item.Contains("relicextractor") || item.Contains("extracteur portable")) return ("s-extractor", false, false);
        return (null, false, false);
    }

    public static ItemsResult Build(TrackerData view, Config config, Checklist checklist, DateTime? now = null)
    {
        var s = config.Odds ?? new OddsSettings();
        var nowUtc = now ?? DateTime.UtcNow;

        // The same chests as the token odds ("chests counted" setting, ranked matches left out…).
        var chests = new List<(DateTime? Utc, double Weight)>();
        OddsModel.Build(view, config, now: nowUtc, chest: (utc, weight) => chests.Add((utc, weight)));
        var hours = (s.TreasureHours ?? new()).Select(h => (Start: OddsModel.Utc(h.StartUtc), End: OddsModel.Utc(h.EndUtc), Hour: h)).Where(h => h.End > h.Start).ToList();

        // Found in the replays (the player's own treasures and those shared by a teammate).
        var found = new Dictionary<string, AutoFind>();
        var unknown = new List<DateTime>();
        foreach (var m in view.Matches)
        {
            var when = OddsModel.Utc(m.MatchStartUtc ?? m.AnalyzedAtUtc);
            foreach (var (f, team) in m.TreasureFinds.Select(f => (f, false)).Concat(m.TeamTreasureFinds.Select(f => (f, true))))
            {
                var (id, probable, unknownPrize) = Identify(f, when);
                if (id == null)
                {
                    if (unknownPrize)
                    {
                        unknown.Add(when);
                    }
                    continue;
                }
                found[id] = found.TryGetValue(id, out var a)
                    ? a with { Count = a.Count + 1, FirstUtc = when < a.FirstUtc ? when : a.FirstUtc, Probable = a.Probable && probable, ViaTeammate = a.ViaTeammate && team }
                    : new AutoFind(1, when, probable, team);
            }
        }

        var states = new List<ItemState>();
        foreach (var d in All(checklist))
        {
            var odds = Odds(d, s);
            double? chance = null;
            if (odds is int n && n > 0)
            {
                var p = 1.0 / n;
                var start = d.StartUtc ?? Week1Start;
                var end = d.EndUtc ?? OddsModel.Utc(s.HuntEndUtc);
                var seasonLong = d.StartUtc == null && d.EndUtc == null;
                double log = 0;
                foreach (var (utc, weight) in chests)
                {
                    if (utc is DateTime u ? u < start || u >= end : !seasonLong)
                    {
                        continue; // outside the item's week (a chest of unknown moment only counts for the season)
                    }
                    double? best = null;
                    if (utc is DateTime t)
                    {
                        foreach (var h in hours.Where(h => t >= h.Start && t < h.End))
                        {
                            best = Math.Max(best ?? 0, Boost(d, h.Hour));
                        }
                    }
                    log += weight * OddsMath.LogNone(p, best ?? 1);
                }
                chance = OddsMath.FromLogNone(log);
            }
            var auto = found.GetValueOrDefault(d.Id);
            var ticked = checklist.Got.ContainsKey(d.Id);
            var unticked = checklist.NotMine.Contains(d.Id);
            var got = ticked || (auto != null && !unticked);
            states.Add(new ItemState(d, got, ticked, unticked, auto, chance, odds, d.OddsKey != null));
        }
        return new ItemsResult(states, states.Count(x => x.Got), states.Count, unknown.OrderBy(x => x).ToList());
    }

    /// <summary>Ticks or unticks an item. Unticking an item found in a replay remembers it: the replay is not followed.</summary>
    public static void Set(Checklist checklist, string id, bool got, bool foundInReplay)
    {
        checklist.NotMine.Remove(id);
        if (got)
        {
            checklist.Got[id] = DateTime.UtcNow;
        }
        else
        {
            checklist.Got.Remove(id);
            if (foundInReplay)
            {
                checklist.NotMine.Add(id);
            }
        }
    }
}

/// <summary>
/// Pictures of the items, built into the exe (Assets\items): the player cannot change them. The dashboard serves them
/// straight from the exe as img/&lt;item id&gt;.&lt;ext&gt;; for report.html (opened from the disk) they are copied into the data
/// folder's "items" folder under the same names, and copied again at each start if one was changed or deleted.
/// </summary>
public static class ItemImages
{
    private static readonly Regex SafeName = new(@"^[a-z0-9-]+\.(png|jpg|webp|gif)$", RegexOptions.Compiled);

    public static bool IsSafeName(string name) => SafeName.IsMatch(name);

    public static string ContentType(string name) => Path.GetExtension(name) switch
    {
        ".jpg" => "image/jpeg",
        ".webp" => "image/webp",
        ".gif" => "image/gif",
        _ => "image/png",
    };

    /// <summary>"w1-pickaxe.png": the name an item's picture is served and copied under (null: no picture).</summary>
    public static string? FileName(ItemDef d)
    {
        if (d.Picture == null)
        {
            return null;
        }
        var ext = Path.GetExtension(d.Picture).ToLowerInvariant().TrimStart('.');
        return $"{d.Id}.{(ext == "jpeg" ? "jpg" : ext)}";
    }

    /// <summary>The picture of a built-in item, from the exe: its bytes (null when there is none).</summary>
    public static byte[]? Read(ItemDef d)
    {
        if (d.Picture == null)
        {
            return null;
        }
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("items." + d.Picture);
        if (stream == null)
        {
            return null;
        }
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        return copy.ToArray();
    }

    /// <summary>The picture served as img/&lt;name&gt;: a built-in item's, nothing else.</summary>
    public static byte[]? ReadServed(string name) =>
        IsSafeName(name) && ItemsModel.Builtin.FirstOrDefault(d => FileName(d) == name) is { } d ? Read(d) : null;

    /// <summary>Copies the built-in pictures into the data folder for report.html (again if one was changed or deleted).</summary>
    public static void Extract(string folder)
    {
        try
        {
            Directory.CreateDirectory(folder);
            foreach (var d in ItemsModel.Builtin)
            {
                if (FileName(d) is not { } name || Read(d) is not { } bytes)
                {
                    continue;
                }
                var path = Path.Combine(folder, name);
                if (!File.Exists(path) || !File.ReadAllBytes(path).AsSpan().SequenceEqual(bytes))
                {
                    File.WriteAllBytes(path, bytes);
                }
            }
        }
        catch
        {
            // pictures are a nicety: the drawn icons stand in
        }
    }

    /// <summary>The kind of picture, from its first bytes: png, jpg, gif, webp, or null.</summary>
    public static string? Sniff(byte[] b)
    {
        if (b.Length >= 8 && b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47) return "png";
        if (b.Length >= 3 && b[0] == 0xFF && b[1] == 0xD8 && b[2] == 0xFF) return "jpg";
        if (b.Length >= 6 && b[0] == 0x47 && b[1] == 0x49 && b[2] == 0x46 && b[3] == 0x38) return "gif";
        if (b.Length >= 12 && b[0] == 0x52 && b[1] == 0x49 && b[2] == 0x46 && b[3] == 0x46 && b[8] == 0x57 && b[9] == 0x45 && b[10] == 0x42 && b[11] == 0x50) return "webp";
        return null;
    }
}
