namespace ChestCounter;

/// <summary>Size and opacity choices of the overlay window (percent), offered in the settings.</summary>
public static class OverlayAppearance
{
    public static readonly int[] Sizes = { 70, 80, 90, 100, 115, 130, 150, 175 };

    /// <summary>Below 30 % the numbers can no longer be read.</summary>
    public static readonly int[] Opacities = { 100, 90, 80, 70, 60, 50, 40, 30 };

    public static int ClampSize(int percent) => Math.Clamp(percent, Sizes[0], Sizes[^1]);
    public static int ClampOpacity(int percent) => Math.Clamp(percent, Opacities[^1], 100);

    /// <summary>Next value of the list, back to the first one after the last (or for a value edited by hand).</summary>
    public static int Next(int[] values, int current) => values[(Array.IndexOf(values, current) + 1) % values.Length];
}

/// <summary>Builds what the overlays (window and OBS page) show from the saved matches and the match in progress.</summary>
public static class OverlayModel
{
    /// <summary>Caption of the right counter between two matches (also used to keep the overlay width stable).</summary>
    public static string LastMatchCaption => L.T("dernière partie", "last match", "partiya lekhra", "última partida");

    /// <param name="live">Figures of the match in progress, when the replay could be read (null otherwise).</param>
    /// <param name="inProgress">A match is being recorded (even if its figures are not readable yet).</param>
    /// <param name="token">Chance of the Pirate Shop token to show ("9.02%"), or null when that line is turned off.</param>
    public static OverlayView Build(TrackerData d, MatchRecord? live, bool inProgress, string? token = null, IReadOnlyList<OverlayRow>? extras = null)
    {
        var view = BuildCounters(d, live, inProgress);
        if (token != null)
        {
            // Classic style and OBS page: one more row. HUD style: one more line (drawn from Token).
            var label = L.T("Jeton", "Token", "Jeton", "Ficha");
            view = view with { Rows = view.Rows.Append(new OverlayRow(label, token)).ToList(), Token = token };
        }
        if (extras is { Count: > 0 })
        {
            view = view with { Rows = view.Rows.Concat(extras).ToList(), Extras = extras };
        }
        return view;
    }

    /// <summary>Everything the overlay shows for these settings: the token line, a Treasure Hour on, today's goal.</summary>
    public static OverlayView For(TrackerData view, Config config, MatchRecord? live, bool inProgress, DateTime? now = null)
    {
        var nowUtc = now ?? DateTime.UtcNow;
        var extras = new List<OverlayRow>();
        if (OddsModel.ActiveHour(config.Odds, nowUtc) is { } hour)
        {
            extras.Add(new OverlayRow(L.T("Heure du trésor", "Treasure Hour", "Sa3at l'kenz", "Hora del tesoro"),
                $"×{hour.PirateShop.ToString("0.##", L.Culture)} → {OddsModel.Utc(hour.EndUtc).ToLocalTime():HH:mm}", Treasure: true));
        }
        if (config.DailyGoal > 0)
        {
            var today = DateTime.Now.Date;
            var chests = view.Matches.Where(m => OddsModel.Utc(m.MatchStartUtc ?? m.AnalyzedAtUtc).ToLocalTime().Date == today).Sum(m => m.ChestsOpened) + (live?.ChestsOpened ?? 0);
            extras.Add(new OverlayRow(L.T("Aujourd'hui", "Today", "Lyouma", "Hoy"), $"{L.N(chests)} / {L.N(config.DailyGoal)}", Treasure: chests >= config.DailyGoal));
        }
        var result = Build(view, live, inProgress, OddsModel.TokenLine(view, config, live), extras);
        return config.OverlayStyle == "wide" ? result with { Wide = Wide(view, config, live, nowUtc) } : result;
    }

    /// <summary>Chest totals celebrated on the overlay (then every 1,000).</summary>
    public static readonly int[] Milestones = { 100, 250, 500, 1000, 1500, 2000, 2500, 3000 };

    public static int NextMilestone(int total) =>
        Milestones.FirstOrDefault(x => x > total) is var found && found > 0 ? found : (total / 1000 + 1) * 1000;

    private static DateTime LocalDay(MatchRecord m) => OddsModel.Utc(m.MatchStartUtc ?? m.AnalyzedAtUtc).ToLocalTime().Date;

    /// <summary>
    /// The wide overlay's figures: today (and the daily goal), the token chance and how much today added, the next
    /// milestone, the session, and one message chosen by priority: a record beaten live, a Treasure Hour on, a milestone
    /// reached this session, a record or best day just set, otherwise the records to beat.
    /// </summary>
    public static OverlayWide Wide(TrackerData view, Config config, MatchRecord? live, DateTime nowUtc)
    {
        var today = DateTime.Now.Date;
        var matches = view.Matches;
        var liveChests = live?.ChestsOpened ?? 0;
        var total = matches.Sum(m => m.ChestsOpened) + liveChests;
        var todayChests = matches.Where(m => LocalDay(m) == today).Sum(m => m.ChestsOpened) + liveChests;
        var session = matches.Where(ReportWriter.IsThisSession).ToList();
        var sessionChests = session.Sum(m => m.ChestsOpened) + liveChests;

        string? goalText = null;
        double? goalP = null;
        if (config.DailyGoal > 0)
        {
            goalText = $"{L.N(todayChests)} / {L.N(config.DailyGoal)}";
            goalP = Math.Min(1, (double)todayChests / config.DailyGoal);
        }

        // Token: the chance now, and what today's chests added to it.
        string? token = null, delta = null;
        if (config.Odds?.OverlayTokenLine == true)
        {
            var now = OddsModel.Build(view, config, live).Pirate.P;
            var before = OddsModel.Build(new TrackerData { Matches = matches.Where(m => LocalDay(m) < today).ToList() }, config).Pirate.P;
            token = L.Pct(now);
            var points = Math.Max(0, now - before) * 100;
            if (points > 0)
            {
                delta = "+" + points.ToString(points >= 0.01 ? "0.00" : "0.000", L.Culture) + " " + L.T("auj.", "today", "lyouma", "hoy");
            }
        }

        var next = NextMilestone(total);
        var nextText = L.T($"{L.N(next)} · encore {L.N(next - total)}", $"{L.N(next)} · {L.N(next - total)} to go", $"{L.N(next)} · ba9i {L.N(next - total)}", $"{L.N(next)} · faltan {L.N(next - total)}");

        // The message line.
        string? message = null;
        var gold = true;
        var best = matches.Count > 0 ? matches.Max(m => m.ChestsOpened) : 0;
        var days = matches.GroupBy(LocalDay).ToDictionary(g => g.Key, g => g.Sum(m => m.ChestsOpened));
        var bestOtherDay = days.Where(d => d.Key != today).Select(d => d.Value).DefaultIfEmpty(0).Max();
        var startTotal = total - sessionChests;
        var crossed = Milestones.Concat(Enumerable.Range(4, 100).Select(k => k * 1000)).Where(m => m > startTotal && m <= total).DefaultIfEmpty(0).Max();
        var lastSession = session.OrderByDescending(m => m.MatchStartUtc ?? m.AnalyzedAtUtc).FirstOrDefault();
        var bestBefore = lastSession != null ? matches.Where(m => !ReferenceEquals(m, lastSession)).Select(m => m.ChestsOpened).DefaultIfEmpty(0).Max() : best;
        if (config.OverlayMessages)
        {
            if (live != null && best > 0 && liveChests > best)
            {
                message = L.T($"Nouveau record : {liveChests} coffres dans cette partie !", $"New record: {liveChests} chests this match!", $"Record jdid: {liveChests} sndouq f had partiya!", $"¡Nuevo récord: {liveChests} cofres en esta partida!");
            }
            else if (OddsModel.ActiveHour(config.Odds, nowUtc) is { } hour)
            {
                message = L.T($"Heure du trésor ×{hour.PirateShop.ToString("0.##", L.Culture)} jusqu'à {OddsModel.Utc(hour.EndUtc).ToLocalTime():HH:mm}",
                    $"Treasure Hour ×{hour.PirateShop.ToString("0.##", L.Culture)} until {OddsModel.Utc(hour.EndUtc).ToLocalTime():HH:mm}",
                    $"Sa3at l'kenz ×{hour.PirateShop.ToString("0.##", L.Culture)} 7ta {OddsModel.Utc(hour.EndUtc).ToLocalTime():HH:mm}", $"Hora del tesoro ×{hour.PirateShop.ToString("0.##", L.Culture)} hasta las {OddsModel.Utc(hour.EndUtc).ToLocalTime():HH:mm}");
            }
            else if (crossed > 0)
            {
                message = L.T($"Cap franchi : {L.N(crossed)} coffres !", $"Milestone: {L.N(crossed)} chests!", $"Wselti {L.N(crossed)} sndouq!", $"¡Meta alcanzada: {L.N(crossed)} cofres!");
            }
            else if (lastSession != null && bestBefore > 0 && lastSession.ChestsOpened > bestBefore)
            {
                message = L.T($"Nouveau record : {lastSession.ChestsOpened} coffres en une partie", $"New record: {lastSession.ChestsOpened} chests in a match", $"Record jdid: {lastSession.ChestsOpened} sndouq f partiya", $"Nuevo récord: {lastSession.ChestsOpened} cofres en una partida");
            }
            else if (bestOtherDay > 0 && todayChests > bestOtherDay)
            {
                message = L.T($"Meilleure journée : {L.N(todayChests)} coffres", $"Best day ever: {L.N(todayChests)} chests", $"A7ssen nhar: {L.N(todayChests)} sndouq", $"Mejor día: {L.N(todayChests)} cofres");
            }
            else if (best > 0)
            {
                gold = false;
                message = L.T($"Records : {best} en une partie · {L.N(Math.Max(bestOtherDay, todayChests))} en un jour",
                    $"Records: {best} in a match · {L.N(Math.Max(bestOtherDay, todayChests))} in a day",
                    $"Records: {best} f partiya · {L.N(Math.Max(bestOtherDay, todayChests))} f nhar", $"Récords: {best} en una partida · {L.N(Math.Max(bestOtherDay, todayChests))} en un día");
            }
        }
        var sessionText = $"{L.N(sessionChests)} · {L.Count(session.Count, "partie", "parties", "match", "matches", "partiya", "partiyat", "partida", "partidas")}";
        return new OverlayWide(L.Count(todayChests, "coffre", "chest", "sndouq", "cofre"), goalText, goalP, goalP >= 1, token, delta, nextText, message, gold, sessionText);
    }

    private static OverlayView BuildCounters(TrackerData d, MatchRecord? live, bool inProgress)
    {
        var liveChests = live?.ChestsOpened ?? 0;
        var liveTreasures = live?.TreasureFinds.Count ?? 0;

        var total = d.Matches.Sum(m => m.ChestsOpened) + liveChests;
        var session = d.Matches.Where(ReportWriter.IsThisSession).ToList();
        var rows = new List<OverlayRow>();

        // Session: chests, then small grey "n parties · moy. x" (finished matches of this session).
        string? sessionDetail = null;
        if (session.Count > 0)
        {
            var avg = (double)session.Sum(m => m.ChestsOpened) / session.Count;
            var avgText = Math.Round(avg, MidpointRounding.AwayFromZero).ToString("0", L.Culture);
            sessionDetail = $"{L.Count(session.Count, "partie", "parties", "match", "matches", "partiya", "partiyat", "partida", "partidas")} · {L.T("moy.", "avg.", "moy.", "media")} {avgText}";
        }
        rows.Add(new OverlayRow(L.T("Session", "Session", "Session", "Sesión"), L.Count(session.Sum(m => m.ChestsOpened) + liveChests, "coffre", "chest", "sndouq", "cofre"), Detail: sessionDetail));

        var last = d.Matches.OrderByDescending(m => m.MatchStartUtc ?? m.AnalyzedAtUtc).FirstOrDefault();
        if (last != null)
        {
            rows.Add(new OverlayRow(L.T("Dernière", "Last", "Lekhra", "Última"), L.Count(last.ChestsOpened, "coffre", "chest", "sndouq", "cofre")));
        }

        var treasures = d.Matches.Sum(m => m.TreasureFinds.Count) + liveTreasures;
        rows.Add(new OverlayRow(L.T("Trésors", "Treasures", "Knouz", "Tesoros"), $"★ {L.N(treasures)}", Treasure: true));

        // Right counter: the match in progress when readable, "…" while it cannot be read yet, else the last match.
        if (live != null)
        {
            return new OverlayView(L.N(total), L.N(live.ChestsOpened), L.T("partie en cours", "current match", "partiya daba", "partida en curso"), true, L.T("● EN DIRECT", "● LIVE", "● LIVE DABA", "● EN DIRECTO"), rows, L.N(treasures), L.N(session.Sum(m => m.ChestsOpened) + liveChests), last != null ? L.N(last.ChestsOpened) : null);
        }
        if (inProgress)
        {
            return new OverlayView(L.N(total), "…", L.T("en cours", "in progress", "ghadya", "en curso"), true, L.T("● PARTIE EN COURS", "● MATCH IN PROGRESS", "● PARTIYA GHADYA", "● PARTIDA EN CURSO"), rows, L.N(treasures), L.N(session.Sum(m => m.ChestsOpened) + liveChests), last != null ? L.N(last.ChestsOpened) : null);
        }
        return new OverlayView(L.N(total), last != null ? L.N(last.ChestsOpened) : null, LastMatchCaption, false, null, rows, L.N(treasures), L.N(session.Sum(m => m.ChestsOpened) + liveChests), last != null ? L.N(last.ChestsOpened) : null);
    }
}
