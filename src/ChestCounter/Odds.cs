namespace ChestCounter;

/// <summary>A treasure whose odds per chest are published (Pirate Shop Access, weekly vehicle, weekly gold outfit).</summary>
public sealed record TreasureDef(string Id, Func<OddsSettings, int> Odds, Func<TreasureHour, double> Boost, bool Weekly)
{
    public string Name => Id switch
    {
        "vehicle" => L.T("Véhicule et autocollant de la semaine", "Weekly Vehicle & Decal", "Tomobil w décalcomanie dyal ssimana", "Vehículo y calcomanía de la semana"),
        "gold" => L.T("Tenue dorée de la semaine", "Weekly Gold Outfit", "Lbsa dhabia dyal ssimana", "Atuendo dorado de la semana"),
        _ => L.T("Accès à la boutique pirate", "Pirate Shop Access", "Accès l l7anout dyal l9ersan", "Acceso a la tienda pirata"),
    };

    /// <summary>Short name for the console columns.</summary>
    public string ShortName => Id switch
    {
        "vehicle" => L.T("Véhicule (sem.)", "Vehicle (week)", "Tomobil (ssimana)", "Vehículo (sem.)"),
        "gold" => L.T("Tenue dorée (sem.)", "Gold Outfit (week)", "Lbsa dhabia (ssimana)", "Atuendo dorado (sem.)"),
        _ => L.T("Boutique pirate", "Pirate Shop Access", "7anout l9ersan", "Acceso a la tienda pirata"),
    };

    public static readonly TreasureDef[] All =
    {
        new("pirate", s => s.PirateShopOdds, h => h.PirateShop, false),
        new("vehicle", s => s.VehicleDecalOdds, h => h.VehicleDecal, true),
        new("gold", s => s.GoldOutfitOdds, h => h.GoldOutfit, true),
    };
}

/// <summary>Probability formulas. Logs keep the precision for tiny odds over many chests.</summary>
public static class OddsMath
{
    public static double Log1p(double x) => Math.Abs(x) < 1e-5 ? x - x * x / 2 + x * x * x / 3 : Math.Log(1 + x);
    public static double Expm1(double x) => Math.Abs(x) < 1e-5 ? x + x * x / 2 + x * x * x / 6 : Math.Exp(x) - 1;

    /// <summary>ln P(nothing from one chest) with odds p boosted by m.</summary>
    public static double LogNone(double p, double m = 1) => Log1p(-Math.Min(1, p * m));

    /// <summary>P(at least one) from ln P(nothing at all).</summary>
    public static double FromLogNone(double logNone) => -Expm1(logNone);

    /// <summary>1 − Π(1 − mᵢ·p) over every chest (mᵢ = Treasure Hour multiplier of that chest).</summary>
    public static double AtLeastOne(double p, IEnumerable<double> multipliers) => FromLogNone(multipliers.Sum(m => LogNone(p, m)));

    /// <summary>1 − (1 − p)^n.</summary>
    public static double AtLeastOne(double p, double chests) => FromLogNone(chests * LogNone(p));

    /// <summary>Chests at base odds for P(at least one) = target: n = ln(1 − target) / ln(1 − p).</summary>
    public static double ChestsFor(double target, double p) => Math.Log(1 - target) / LogNone(p);

    /// <summary>Chests at base odds giving the same chance as ln P(nothing) (Treasure Hours count for more).</summary>
    public static double Equivalent(double logNone, double p) => logNone / LogNone(p);

    /// <summary>95% Wilson score interval of a rate k / n.</summary>
    public static (double Low, double High) Wilson(int k, int n, double z = 1.96)
    {
        if (n <= 0)
        {
            return (0, 1);
        }
        var phat = (double)k / n;
        var z2 = z * z;
        var center = (phat + z2 / (2 * n)) / (1 + z2 / n);
        var half = z * Math.Sqrt(phat * (1 - phat) / n + z2 / (4.0 * n * n)) / (1 + z2 / n);
        return (Math.Max(0, center - half), Math.Min(1, center + half));
    }
}

public sealed record TreasureOdds(TreasureDef Def, double P, double Lambda, double Equivalent, double PerChest, double? WeekP, double PEnd);

/// <summary>Chests needed for a target chance, and how far away it is at the current pace.</summary>
public sealed record PlanRow(double Target, double Needed, double ToGo, double? Matches, double? Hours, bool Reachable);

/// <summary>
/// Teammates' chests out of the player's sight. "ratio": calibrated by imported files (seen ÷ their own count on matches
/// recorded by both). "rate": treasures received from chests out of sight ÷ the treasure rate of the chests seen.
/// "none": not enough data. Aggregate only: never meaningful for one match.
/// </summary>
public sealed record EstimateInfo(string Method, double Value, double? Low, double? High, int Basis, int Grants, int Treasures, int Chests, double? Ratio);

public sealed class OddsResult
{
    public string Scope { get; init; } = "team";
    public DateTime NowUtc { get; init; }
    public OddsSettings Settings { get; init; } = new();

    // Chests used, by source.
    public int Mine { get; set; }
    public int TeamSeen { get; set; }
    public int TeamImported { get; set; }
    public double TeamEstimated { get; set; }
    public int BaselineMine { get; set; }
    public int BaselineTeam { get; set; }
    public double Total => Mine + TeamSeen + TeamImported + TeamEstimated + BaselineMine + BaselineTeam;

    /// <summary>Every teammate chest seen in the player's replays (whatever the scope).</summary>
    public int TeamSeenAll { get; set; }
    public int Matches { get; set; }
    /// <summary>Eligible matches with no team data at all (analyzed before analysis version 16, replay deleted, no import).</summary>
    public int MatchesNoTeamData { get; set; }
    public int ExcludedMatches { get; set; }
    public int ExcludedChests { get; set; }
    /// <summary>"Squad matches only": eligible matches left out because no imported teammate played them.</summary>
    public int NonSquadMatches { get; set; }
    public EstimateInfo Estimate { get; set; } = new("none", 0, null, null, 0, 0, 0, 0, null);

    public List<TreasureOdds> Treasures { get; } = new();
    public TreasureOdds Pirate => Treasures[0];
    public List<PlanRow> Plan { get; } = new();

    // Pace.
    public double HoursPlayed { get; set; }
    public double MinePerMatch { get; set; }
    public double TeamPerTeamMatch { get; set; }
    public int TeamMatches { get; set; }
    public double PerMatch { get; set; }
    public double PerHour { get; set; }
    public double PerDay { get; set; }
    public double PaceDays { get; set; }

    // End of the hunt.
    public double DaysLeft { get; set; }
    public double Projected { get; set; }

    // Solo vs squad.
    public double SoloPerHour { get; set; }
    public bool SoloFromSoloMatches { get; set; }
    public double SquadPerHour { get; set; }
    public double PSoloHour { get; set; }
    public double PSquadHour { get; set; }

    // Treasure rolls (any treasure hunt treasure, per chest seen).
    public int RollTreasures { get; set; }
    public int RollChests { get; set; }

    // Treasure Hours.
    public int HoursConfigured { get; set; }
    public double BoostedChests { get; set; }
    public double BoostExtra { get; set; }
    public TreasureHour? ActiveHour { get; set; }
    public TreasureHour? NextHour { get; set; }

    /// <summary>P(Pirate Shop Access) at the end of each day (local dates), baseline included.</summary>
    public List<(DateTime Day, double P)> Daily { get; } = new();
}

/// <summary>
/// Token odds from the saved matches (one profile), the imported teammates' files and the settings. The console tab,
/// the report and the overlay all use this, so they always agree.
/// </summary>
public static class OddsModel
{
    public static readonly string[] Scopes = { "mine", "team", "estimated", "imported", "squad" };
    public static readonly double[] Targets = { 0.5, 0.75, 0.9, 0.95, 0.99 };

    /// <summary>Community estimate of treasure rolls per chest (not published by Epic).</summary>
    public const double CommunityRollRate = 1.0 / 100;

    public static string ScopeName(string scope) => scope switch
    {
        "mine" => L.T("mes coffres seulement", "mine only", "ghir dyali", "solo los míos"),
        "estimated" => L.T("les miens + équipe vue + estimation", "mine + team seen + estimated", "dyali + l'équipe li chefti + ta9dir", "los míos + equipo visto + estimación"),
        "imported" => L.T("les miens + importés", "mine + imported", "dyali + importés", "los míos + importados"),
        "squad" => L.T("parties en escouade seulement (exact)", "squad matches only (exact)", "ghir partiyat squad (exact)", "solo partidas en escuadrón (exacto)"),
        _ => L.T("les miens + équipe vue", "mine + team seen", "dyali + l'équipe li chefti", "los míos + equipo visto"),
    };

    /// <summary>Treasure Hour multiplier of one treasure at that moment (the biggest when windows overlap, 1 outside).</summary>
    public static double Multiplier(OddsSettings s, DateTime utc, TreasureDef def)
    {
        double? best = null;
        foreach (var h in s.TreasureHours ?? new())
        {
            if (utc >= Utc(h.StartUtc) && utc < Utc(h.EndUtc))
            {
                best = Math.Max(best ?? 0, def.Boost(h));
            }
        }
        return best ?? 1;
    }

    /// <summary>The Treasure Hour on at that moment (the biggest token boost when several overlap), or null.</summary>
    public static TreasureHour? ActiveHour(OddsSettings? s, DateTime utc) =>
        (s?.TreasureHours ?? new()).Where(h => utc >= Utc(h.StartUtc) && utc < Utc(h.EndUtc)).OrderByDescending(h => h.PirateShop).FirstOrDefault();

    /// <summary>"token ×1.5 · vehicle ×2" (only the boosted treasures; the token always).</summary>
    public static string BoostText(TreasureHour h)
    {
        var parts = new List<string> { $"{L.T("jeton", "token", "jeton", "ficha")} ×{h.PirateShop.ToString("0.##", L.Culture)}" };
        if (h.VehicleDecal != 1) parts.Add($"{L.T("véhicule", "vehicle", "tomobil", "vehículo")} ×{h.VehicleDecal.ToString("0.##", L.Culture)}");
        if (h.GoldOutfit != 1) parts.Add($"{L.T("tenue dorée", "gold outfit", "lbsa dhabia", "atuendo dorado")} ×{h.GoldOutfit.ToString("0.##", L.Culture)}");
        return string.Join(" · ", parts);
    }

    public static string NextScope(string scope) => Scopes[(Array.IndexOf(Scopes, scope) + 1) % Scopes.Length];

    public static bool IsRanked(string? playlist) =>
        playlist != null && (playlist.Contains("habanero", StringComparison.OrdinalIgnoreCase) || playlist.Contains("ranked", StringComparison.OrdinalIgnoreCase));

    public static bool IsEligible(MatchRecord m, OddsSettings s) =>
        m.IsBattleRoyale
        && (s.IncludeRanked || !IsRanked(m.Playlist))
        && (s.HuntStartUtc is not DateTime start || Utc(m.MatchStartUtc ?? m.AnalyzedAtUtc) >= Utc(start));

    /// <summary>Dates read from JSON without a zone are UTC (the tool always saves UTC).</summary>
    public static DateTime Utc(DateTime d) => d.Kind switch
    {
        DateTimeKind.Unspecified => DateTime.SpecifyKind(d, DateTimeKind.Utc),
        DateTimeKind.Local => d.ToUniversalTime(),
        _ => d,
    };

    /// <summary>
    /// Moment chest times count from. Chest times are replay seconds (from the start of the recording); the match start
    /// (UtcTimeStartedMatch, a UTC time) is within seconds of it, which is precise enough for Treasure Hours.
    /// </summary>
    public static DateTime ChestBase(MatchRecord m) => Utc(m.MatchStartUtc ?? m.AnalyzedAtUtc);

    /// <summary>Start of the current week of the weekly treasures (last reset before now).</summary>
    public static DateTime WeekStart(OddsSettings s, DateTime nowUtc)
    {
        var day = Enum.TryParse<DayOfWeek>(s.WeekResetDay, true, out var d) ? d : DayOfWeek.Thursday;
        var start = nowUtc.Date.AddHours(Math.Clamp(s.WeekResetHourUtc, 0, 23));
        start = start.AddDays(-(((int)start.DayOfWeek - (int)day + 7) % 7));
        return DateTime.SpecifyKind(start > nowUtc ? start.AddDays(-7) : start, DateTimeKind.Utc);
    }

    /// <summary>The overlay line: "9.02%" (Pirate Shop Access), or null when turned off.</summary>
    public static string? TokenLine(TrackerData view, Config config, MatchRecord? live) =>
        config.Odds?.OverlayTokenLine == true ? L.Pct(Build(view, config, live).Pirate.P) : null;

    /// <param name="view">Matches of the profile shown.</param>
    /// <param name="live">Match in progress (overlay), counted like a finished one.</param>
    /// <param name="scopeOverride">Another choice of chests than the settings' (the Squad tab's total uses "squad").</param>
    /// <param name="chest">Told about every chest counted: its moment (null when unknown: before tracking, estimate) and
    /// how many chests it stands for. The Items tab works out each item's chance from the same chests.</param>
    public static OddsResult Build(TrackerData view, Config config, MatchRecord? live = null, DateTime? now = null, TeamImports? imports = null, string? scopeOverride = null,
        Action<DateTime?, double>? chest = null)
    {
        var s = config.Odds ?? new OddsSettings();
        var nowUtc = now ?? DateTime.UtcNow;
        var scope = scopeOverride ?? (Scopes.Contains(s.ChestScope) ? s.ChestScope : "team");
        var withSeen = scope is "team" or "estimated";
        var index = new ImportIndex(imports ?? Team.Imports);
        var r = new OddsResult { Scope = scope, NowUtc = nowUtc, Settings = s };

        var eligible = new List<MatchRecord>();
        foreach (var m in live != null ? view.Matches.Append(live) : view.Matches)
        {
            if (IsEligible(m, s))
            {
                eligible.Add(m);
            }
            else
            {
                r.ExcludedMatches++;
                r.ExcludedChests += m.ChestsOpened;
            }
        }
        var mates = new Dictionary<MatchRecord, List<MateInMatch>>(ReferenceEqualityComparer.Instance);
        foreach (var m in eligible)
        {
            mates[m] = Team.MatesOf(m, index);
        }
        if (scope == "squad")
        {
            // Exact squad odds: only the matches played with at least one imported teammate (their own counts).
            r.NonSquadMatches = eligible.Count(m => !mates[m].Any(x => x.Imported != null));
            eligible = eligible.Where(m => mates[m].Any(x => x.Imported != null)).ToList();
        }
        r.Matches = eligible.Count;
        r.Estimate = EstimateUnseen(eligible, mates, s, out var estimates);

        var defs = TreasureDef.All;
        var p = defs.Select(d => 1.0 / Math.Max(1, d.Odds(s))).ToArray();
        var logNone = new double[defs.Length];
        var lambda = new double[defs.Length];
        var weekLog = new double[defs.Length];
        var weekStart = WeekStart(s, nowUtc);
        var hours = (s.TreasureHours ?? new()).Select(h => (Start: Utc(h.StartUtc), End: Utc(h.EndUtc), Hour: h)).Where(h => h.End > h.Start).ToList();
        var dayLog = new SortedDictionary<DateTime, double>();

        double Mult(DateTime utc, int t)
        {
            double? best = null;
            foreach (var h in hours)
            {
                if (utc >= h.Start && utc < h.End)
                {
                    best = Math.Max(best ?? 0, defs[t].Boost(h.Hour));
                }
            }
            return best ?? 1;
        }

        // One chest (or several, without a time: baseline, estimate) into every treasure's odds.
        void Add(DateTime? utc, double weight, DateTime? day)
        {
            chest?.Invoke(utc, weight);
            for (var t = 0; t < defs.Length; t++)
            {
                var mult = utc is DateTime u ? Mult(u, t) : 1;
                var ln = weight * OddsMath.LogNone(p[t], mult);
                logNone[t] += ln;
                lambda[t] += weight * Math.Min(1, mult * p[t]);
                if (defs[t].Weekly && utc is DateTime w && w >= weekStart)
                {
                    weekLog[t] += ln;
                }
                if (t == 0)
                {
                    if (day is DateTime d)
                    {
                        dayLog[d] = dayLog.GetValueOrDefault(d) + ln;
                    }
                    if (mult != 1)
                    {
                        r.BoostedChests += weight;
                        r.BoostExtra += weight * (mult - 1);
                    }
                }
            }
        }

        var tallies = new List<(MatchRecord M, DateTime Start, double Counted, double Mine, double Team, double Hours, string? Mode)>();
        foreach (var m in eligible)
        {
            var start = ChestBase(m);
            var day = Utc(m.MatchStartUtc ?? m.AnalyzedAtUtc).ToLocalTime().Date;
            foreach (var c in m.Chests)
            {
                Add(start.AddSeconds(c.TimeSeconds), 1, day);
            }
            if (m.ChestsOpened > m.Chests.Count)
            {
                Add(start, m.ChestsOpened - m.Chests.Count, day);
            }
            r.Mine += m.ChestsOpened;

            double team = 0;
            var matesHere = mates[m];
            r.TeamSeenAll += matesHere.Sum(x => x.Seen ?? 0);
            if (matesHere.Count == 0 && m.TeammateChests == null)
            {
                r.MatchesNoTeamData++;
            }
            if (scope != "mine")
            {
                foreach (var mate in matesHere.Where(x => x.Included(s)))
                {
                    if (mate.Imported is { } im)
                    {
                        // Their own count replaces what the player's replay saw of them: counted once.
                        var imStart = im.MatchStartUtc is DateTime ims ? Utc(ims) : start;
                        foreach (var t in im.ChestTimes)
                        {
                            Add(imStart.AddSeconds(t), 1, day);
                        }
                        if (im.ChestsOpened > im.ChestTimes.Count)
                        {
                            Add(start, im.ChestsOpened - im.ChestTimes.Count, day);
                        }
                        r.TeamImported += im.ChestsOpened;
                        team += im.ChestsOpened;
                    }
                    else if (withSeen && mate.Seen is int seen)
                    {
                        foreach (var c in mate.SeenChests)
                        {
                            Add(start.AddSeconds(c.TimeSeconds), 1, day);
                        }
                        if (seen > mate.SeenChests.Count)
                        {
                            Add(start, seen - mate.SeenChests.Count, day);
                        }
                        r.TeamSeen += seen;
                        team += seen;
                    }
                }
                if (scope == "estimated" && estimates.TryGetValue(m, out var est) && est > 0)
                {
                    Add(null, est, day); // no time: counted at ×1
                    r.TeamEstimated += est;
                    team += est;
                }
            }
            tallies.Add((m, Utc(m.MatchStartUtc ?? m.AnalyzedAtUtc), m.ChestsOpened + team, m.ChestsOpened, team,
                (m.DurationSeconds ?? 0) / 3600.0, m.PartyMode ?? ReplayAnalyzer.PartyModeOf(m.Playlist, m.TeamSize)));
        }

        // Chests opened before tracking: at ×1 (Treasure Hours unknown). The team part only when the team counts.
        var key = view.Matches.Select(Profiles.Key).FirstOrDefault(k => k.Length > 0) ?? config.Profile ?? "";
        var baseline = s.Baselines?.GetValueOrDefault(key) ?? new OddsBaseline();
        // Squad odds stay exact: only the matches recorded by the whole squad, no baseline.
        r.BaselineMine = scope == "squad" ? 0 : Math.Max(0, baseline.Mine);
        r.BaselineTeam = scope is "mine" or "squad" ? 0 : Math.Max(0, baseline.Team);
        if (r.BaselineMine + r.BaselineTeam > 0)
        {
            Add(null, r.BaselineMine + r.BaselineTeam, null);
        }

        // Pace: per match and per hour over everything, per day over the last 14 days (or since tracking started).
        r.HoursPlayed = tallies.Sum(x => x.Hours);
        r.PerMatch = tallies.Count > 0 ? tallies.Average(x => x.Counted) : 0;
        r.MinePerMatch = tallies.Count > 0 ? tallies.Average(x => x.Mine) : 0;
        var teamTallies = tallies.Where(x => x.Mode is not (null or "solo") && x.Team > 0).ToList();
        r.TeamMatches = teamTallies.Count;
        r.TeamPerTeamMatch = teamTallies.Count > 0 ? teamTallies.Average(x => x.Team) : 0;
        var timed = tallies.Where(x => x.Hours > 0).ToList();
        r.PerHour = timed.Sum(x => x.Hours) > 0 ? timed.Sum(x => x.Counted) / timed.Sum(x => x.Hours) : 0;
        if (tallies.Count > 0)
        {
            var windowStart = nowUtc.AddDays(-14);
            var first = tallies.Min(x => x.Start);
            r.PaceDays = Math.Clamp((nowUtc - (first > windowStart ? first : windowStart)).TotalDays, 1, 14);
            r.PerDay = tallies.Where(x => x.Start >= windowStart).Sum(x => x.Counted) / r.PaceDays;
        }
        r.DaysLeft = Math.Max(0, (Utc(s.HuntEndUtc) - nowUtc).TotalDays);
        r.Projected = r.PerDay * r.DaysLeft;

        for (var t = 0; t < defs.Length; t++)
        {
            var total = r.Total;
            r.Treasures.Add(new TreasureOdds(defs[t],
                OddsMath.FromLogNone(logNone[t]),
                lambda[t],
                OddsMath.Equivalent(logNone[t], p[t]),
                total > 0 ? lambda[t] / total : p[t],
                defs[t].Weekly ? OddsMath.FromLogNone(weekLog[t]) : null,
                OddsMath.FromLogNone(logNone[t] + r.Projected * OddsMath.LogNone(p[t]))));
        }

        // Planning (Pirate Shop Access).
        var pirate = r.Pirate;
        foreach (var target in Targets)
        {
            var needed = OddsMath.ChestsFor(target, p[0]);
            var toGo = Math.Max(0, needed - pirate.Equivalent);
            r.Plan.Add(new PlanRow(target, needed, toGo,
                r.PerMatch > 0 ? toGo / r.PerMatch : null,
                r.PerHour > 0 ? toGo / r.PerHour : null,
                toGo <= 0 || pirate.Equivalent + r.Projected >= needed));
        }

        // Solo vs squad, per hour: solo = the player's own chests (in solo matches when there are some), squad = what
        // counts in team matches (the recent ones when there are enough: the current squad).
        var solo = timed.Where(x => x.Mode == "solo").ToList();
        r.SoloFromSoloMatches = solo.Count > 0;
        var soloSet = solo.Count > 0 ? solo : timed;
        r.SoloPerHour = soloSet.Sum(x => x.Hours) > 0 ? soloSet.Sum(x => x.Mine) / soloSet.Sum(x => x.Hours) : 0;
        var squad = timed.Where(x => x.Mode is not (null or "solo")).ToList();
        var recentSquad = squad.Where(x => x.Start >= nowUtc.AddDays(-14)).ToList();
        if (recentSquad.Count >= 3)
        {
            squad = recentSquad;
        }
        r.SquadPerHour = squad.Sum(x => x.Hours) > 0 ? squad.Sum(x => x.Counted) / squad.Sum(x => x.Hours) : 0;
        r.PSoloHour = OddsMath.AtLeastOne(p[0], r.SoloPerHour);
        r.PSquadHour = OddsMath.AtLeastOne(p[0], r.SquadPerHour);

        // Treasure rolls: treasures from chests that were seen, per chest seen (own + teammates' seen in this replay).
        foreach (var m in eligible)
        {
            r.RollTreasures += m.TreasureFinds.Count(f => f.SourceIsChest);
            r.RollChests += m.ChestsOpened;
            if (m.TeammateChests != null)
            {
                r.RollTreasures += m.TeamTreasureFinds.Count(f => !string.IsNullOrEmpty(f.Teammate));
                r.RollChests += m.TeamChestsSeen ?? m.TeammateChests.Sum(t => t.ChestsSeen);
            }
        }

        r.HoursConfigured = hours.Count;
        r.ActiveHour = hours.Where(h => nowUtc >= h.Start && nowUtc < h.End).Select(h => h.Hour).FirstOrDefault();
        r.NextHour = hours.Where(h => h.Start > nowUtc).OrderBy(h => h.Start).Select(h => h.Hour).FirstOrDefault();

        // P(token) at the end of each day, from the first match day to today.
        if (dayLog.Count > 0)
        {
            var cumulative = (r.BaselineMine + r.BaselineTeam) * OddsMath.LogNone(p[0]);
            var today = nowUtc.ToLocalTime().Date;
            var last = dayLog.Keys.Max() > today ? dayLog.Keys.Max() : today;
            for (var d = dayLog.Keys.First(); d <= last; d = d.AddDays(1))
            {
                cumulative += dayLog.GetValueOrDefault(d);
                r.Daily.Add((d, OddsMath.FromLogNone(cumulative)));
            }
        }
        return r;
    }

    private static EstimateInfo EstimateUnseen(List<MatchRecord> eligible, Dictionary<MatchRecord, List<MateInMatch>> mates, OddsSettings s, out Dictionary<MatchRecord, double> perMatch)
    {
        perMatch = new Dictionary<MatchRecord, double>(ReferenceEqualityComparer.Instance);

        // 1) Calibrated by imports: on matches recorded by both, the share of a teammate's chests the player's replay saw.
        var pairs = eligible
            .SelectMany(m => mates[m].Where(x => x.Included(s) && x.Seen != null && x.Imported != null)
                .Select(x => (x.Key, Seen: x.Seen!.Value, Own: x.Imported!.ChestsOpened)))
            .ToList();
        if (pairs.Count >= 5 && pairs.Sum(x => x.Own) > 0)
        {
            var pooled = Math.Clamp((double)pairs.Sum(x => x.Seen) / pairs.Sum(x => x.Own), 0.05, 1);
            var perMate = pairs.GroupBy(x => x.Key)
                .Where(g => g.Count() >= 5 && g.Sum(x => x.Own) > 0)
                .ToDictionary(g => g.Key, g => Math.Clamp((double)g.Sum(x => x.Seen) / g.Sum(x => x.Own), 0.05, 1));
            double total = 0;
            foreach (var m in eligible)
            {
                double e = 0;
                foreach (var x in mates[m].Where(x => x.Included(s) && x.Seen != null && x.Imported == null))
                {
                    e += x.Seen!.Value * (1 / perMate.GetValueOrDefault(x.Key, pooled) - 1);
                }
                if (e > 0)
                {
                    perMatch[m] = e;
                    total += e;
                }
            }
            return new EstimateInfo("ratio", total, null, null, pairs.Count, 0, 0, 0, pooled);
        }

        // 2) Treasures received from chests out of the player's sight, divided by the treasure rate of the chests that
        //    were seen (own + teammates'). Only matches where every teammate counts and none was imported.
        var basis = eligible
            .Where(m => m.TeammateChests is { Count: > 0 } && mates[m].All(x => x.Imported == null && x.Included(s)))
            .ToList();
        var grants = basis.Sum(m => m.TeamTreasureFinds.Count(f => f.FromPrizeHandler && string.IsNullOrEmpty(f.Teammate)));
        var treasures = basis.Sum(m => m.TreasureFinds.Count(f => f.SourceIsChest) + m.TeamTreasureFinds.Count(f => !string.IsNullOrEmpty(f.Teammate)));
        var chests = basis.Sum(m => m.ChestsOpened + (m.TeamChestsSeen ?? 0));
        if (treasures < 5 || chests == 0)
        {
            return new EstimateInfo("none", 0, null, null, basis.Count, grants, treasures, chests, null);
        }
        var value = grants * (double)chests / treasures;
        double low, high;
        if (grants > 0)
        {
            // Two Poisson counts (grants, treasures): log-normal 95% range.
            var spread = Math.Exp(1.96 * Math.Sqrt(1.0 / grants + 1.0 / treasures));
            low = value / spread;
            high = value * spread;
        }
        else
        {
            low = 0;
            high = 3.69 * chests / treasures; // 95% upper bound of a Poisson count of 0
        }
        var slots = basis.Sum(m => m.TeammateChests!.Count);
        foreach (var m in basis)
        {
            perMatch[m] = value * m.TeammateChests!.Count / slots;
        }
        return new EstimateInfo("rate", value, low, high, basis.Count, grants, treasures, chests, null);
    }
}
