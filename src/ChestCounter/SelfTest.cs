using System.Text;
using System.Text.Json;

namespace ChestCounter;

/// <summary>
/// "ChestTokenAssistant.exe --self-test": checks the odds formulas, the merge of imported teammates' files and the loading of
/// files written by older versions, on fixed samples (nothing is read from or written to the data folder).
/// Exit code 0 when every check passes.
/// </summary>
public static class SelfTest
{
    public static int Run()
    {
        L.Set("en");
        var failures = 0;
        void Check(string name, bool ok, string detail = "")
        {
            Console.ForegroundColor = ok ? ConsoleColor.Green : ConsoleColor.Red;
            Console.Write(ok ? "  PASS  " : "  FAIL  ");
            Console.ResetColor();
            Console.WriteLine($"{name}{(detail.Length > 0 ? "  ·  " + detail : "")}");
            if (!ok) failures++;
        }

        var now = new DateTime(2026, 11, 10, 12, 0, 0, DateTimeKind.Utc);
        var start = new DateTime(2026, 11, 8, 2, 0, 0, DateTimeKind.Utc);
        var p = 1.0 / 137_500;

        // 1) The formula itself: 1 − (1 − 1/137,500)^13,000 = 9.02 %.
        var p13k = OddsMath.AtLeastOne(p, 13_000);
        Check("P(token) after 13,000 chests = 9.02%", Math.Abs(p13k - (1 - Math.Pow(1 - p, 13_000))) < 1e-12 && L.Pct(p13k) == "9.02%",
            $"{p13k:F6} → {L.Pct(p13k)} ({L.OneIn(p13k)})");

        // 2) The model gives the same with no Treasure Hour (13,000 chests of mine, no team).
        var config = new Config();
        var r = OddsModel.Build(new TrackerData { Matches = { Mine("solo-13k", 13_000, start) } }, config, now: now, imports: new TeamImports());
        Check("Odds tab = 1 − (1 − p)^N without Treasure Hours", Math.Abs(r.Pirate.P - p13k) < 1e-12, $"{L.Pct(r.Pirate.P)}, λ = {r.Pirate.Lambda:F4}");

        // 3) Treasure Hours: a chest at 60 s inside a ×2 window, one at 3,600 s after it: 1 − (1 − 2p)(1 − p).
        var boosted = new Config();
        boosted.Odds.TreasureHours.Add(new TreasureHour { StartUtc = start, EndUtc = start.AddMinutes(30), PirateShop = 2 });
        var rb = OddsModel.Build(new TrackerData { Matches = { Mine("th", 2, start, 60, 3600) } }, boosted, now: now, imports: new TeamImports());
        var expected = 1 - (1 - 2 * p) * (1 - p);
        Check("Treasure Hour multiplier per chest", Math.Abs(rb.Pirate.P - expected) < 1e-15 && rb.BoostedChests == 1,
            $"{rb.Pirate.P:E6} vs {expected:E6}, boosted chests = {rb.BoostedChests}");

        // 4) Chests needed: n = ln(1 − target) / ln(1 − p) gives back the target.
        var n50 = OddsMath.ChestsFor(0.5, p);
        Check("Chests needed for 50%", Math.Abs(OddsMath.AtLeastOne(p, n50) - 0.5) < 1e-12, $"{n50:N0} chests");

        // 5) Teammates: an imported count replaces what the replay saw, once, however many times it is imported.
        var my = new TrackerData { Matches = { Mine("S1", 20, start) } };
        my.Matches[0].TeammateChests = new()
        {
            new TeammateChests { Name = "Alex", EpicId = "AAA", ChestsSeen = 5, InParty = true },
            new TeammateChests { Name = "Billie", EpicId = "BBB", ChestsSeen = 3, InParty = false },
        };
        my.Matches[0].TeamChestsSeen = 8;
        my.Matches[0].TeamSize = 3;
        var alexFile = JsonSerializer.Serialize(new TrackerData
        {
            Matches =
            {
                new MatchRecord { SessionId = "S1", PlayerName = "Alex", EpicId = "AAA", ChestsOpened = 12, MatchStartUtc = start },
                new MatchRecord { SessionId = "S9", PlayerName = "Alex", EpicId = "AAA", ChestsOpened = 40, MatchStartUtc = start },
            },
        });
        var (alex, message) = Team.Parse("data.json", alexFile, my);
        Check("Import a teammate's data.json", alex != null && alex.Matches.Count == 1 && alex.MatchesInFile == 2, message);
        var imports = Team.With(Team.With(new TeamImports(), alex!), alex!);
        Check("Same file imported twice is kept once", imports.Sources.Count == 1);
        // Another file (a second account of someone else) that also holds Alex's S1 match: still counted once.
        var other = new ImportedSource { Key = "ZZZ", Name = "Zed", ImportedUtc = DateTime.UtcNow.AddMinutes(1), Matches = { new ImportedMatch { SessionId = "S1", PlayerName = "Alex", EpicId = "AAA", ChestsOpened = 12 } } };
        imports = Team.With(imports, other);
        var rt = OddsModel.Build(my, new Config(), now: now, imports: imports);
        Check("Merge by SessionId: their 12 replace my 5 seen, no double count", rt.Mine == 20 && rt.TeamImported == 12 && rt.TeamSeen == 3 && rt.Total == 35,
            $"mine {rt.Mine} + imported {rt.TeamImported} + seen {rt.TeamSeen} = {rt.Total}");
        var noFill = new Config();
        noFill.Odds.IncludeFill = false;
        var rf = OddsModel.Build(my, noFill, now: now, imports: imports);
        Check("Party only leaves the fill teammate out", rf.Total == 32, $"total {rf.Total}");
        var mineOnly = new Config();
        mineOnly.Odds.ChestScope = "mine";
        Check("\"Mine only\" ignores teammates", OddsModel.Build(my, mineOnly, now: now, imports: imports).Total == 20);
        var importedOnly = new Config();
        importedOnly.Odds.ChestScope = "imported";
        Check("\"Mine + imported\" uses exact counts only", OddsModel.Build(my, importedOnly, now: now, imports: imports).Total == 32);
        var (own, ownMessage) = Team.Parse("data.json", JsonSerializer.Serialize(my), my);
        Check("Own data.json is refused", own == null, ownMessage);

        // 5b) Share files (.chesttoken): Alex exports, I import. Teammates are hashed: my id is not in the file.
        var alexData = new TrackerData
        {
            Matches =
            {
                new MatchRecord { SessionId = "S1", PlayerName = "Alex", EpicId = "AAA", ChestsOpened = 12, MatchStartUtc = start, Playlist = "Playlist_NoBuildBR_Squad", TeamSize = 3,
                    TeammateChests = new() { new TeammateChests { Name = "Me", EpicId = "MEID" }, new TeammateChests { Name = "Billie", EpicId = "BBB" } } },
                new MatchRecord { SessionId = "SOLO", PlayerName = "Alex", EpicId = "AAA", ChestsOpened = 30, MatchStartUtc = start, Playlist = "Playlist_NoBuildBR_Solo", TeamSize = 1, TeammateChests = new() },
            },
        };
        var shareJson = JsonSerializer.Serialize(ShareFormat.Build(alexData));
        Check("Export: solo matches left out, other players hashed", ShareFormat.Build(alexData).Matches.Count == 1 && !shareJson.Contains("MEID") && !shareJson.Contains("Billie"),
            $"{shareJson.Length} bytes");
        var (fromShare, shareMessage) = Team.Parse("Alex.chesttoken", shareJson, my);
        Check("Import a .chesttoken file", fromShare?.Matches.Count == 1 && fromShare.Matches[0].ChestsOpened == 12, shareMessage);
        // Same match, other team: I do not list Alex, Alex does not list me.
        var myOther = new TrackerData { Matches = { Mine("S2", 10, start) } };
        myOther.Matches[0].TeammateChests = new() { new TeammateChests { Name = "Casey", EpicId = "CCC" } };
        var alexOther = new TrackerData { Matches = { new MatchRecord { SessionId = "S2", PlayerName = "Alex", EpicId = "AAA", ChestsOpened = 9, TeamSize = 2, Playlist = "Playlist_NoBuildBR_Duo", TeammateChests = new() { new TeammateChests { Name = "Dana", EpicId = "DDD" } } } } };
        var (otherTeam, otherMessage) = Team.Parse("Alex.chesttoken", JsonSerializer.Serialize(ShareFormat.Build(alexOther)), myOther);
        Check("Same match but another team is left out", otherTeam == null, otherMessage);
        // My old record without team data: their file listing me is enough.
        var myOld = new TrackerData { Matches = { Mine("S1", 20, start) } };
        myOld.Matches[0].TeammateChests = null;
        var (viaThem, _) = Team.Parse("Alex.chesttoken", shareJson, myOld);
        Check("Same team proven by their file alone", viaThem?.Matches.Count == 1);

        // 5c) Squads: me + Alex (imported) in S1, Billie (no file) shown apart.
        var squadImports = Team.With(new TeamImports(), fromShare!);
        var squads = SquadModel.Build(my, new Config(), squadImports);
        Check("Squad grouping with exact counts", squads.Count == 1 && squads[0].Matches == 1 && squads[0].MyChests == 20 && squads[0].Total == 32 && squads[0].OthersSeen == 3
            && Math.Abs(squads[0].P - OddsMath.AtLeastOne(p, 32)) < 1e-12, squads.Count > 0 ? $"{squads[0].Total} chests, {L.Pct(squads[0].P)}" : "none");
        var withBaseline = new Config();
        withBaseline.Odds.Baselines["MEID"] = new OddsBaseline { Mine = 5000, Team = 5000 };
        var rs = OddsModel.Build(new TrackerData { Matches = { my.Matches[0], Mine("SOLO2", 40, start) } }, withBaseline, now: now, imports: squadImports, scopeOverride: "squad");
        Check("\"Squad matches only\": exact, no baseline, other matches out", rs.Total == 32 && rs.Matches == 1 && rs.NonSquadMatches == 1, $"total {rs.Total}");

        // 5d) Files tab: what Alex's file changes (1 match, their 12 instead of the 5 seen, a higher chance).
        var impact = ImportsReport.Build(my, new Config(), squadImports);
        var alexImpact = impact.Files.FirstOrDefault();
        Check("Files tab: matches, chests seen vs theirs, chance with / without", alexImpact is { MatchesUsed: 1, TheirChests: 12, SeenOfThem: 5 }
            && alexImpact.PWith > alexImpact.PWithout && Math.Abs(alexImpact.PWith - OddsMath.AtLeastOne(p, 35)) < 1e-12 && Math.Abs(impact.PNone - OddsMath.AtLeastOne(p, 28)) < 1e-12,
            alexImpact == null ? "none" : $"{ImportsReport.Points(alexImpact.PWith - alexImpact.PWithout)}");

        // 6) CSV import: needs the session_id column (written by this version).
        var csv = "date;mode;player;placement;players;chests;ammo_boxes;treasures;file;team_size;team_chests_seen;team_chests_imported;session_id\n"
                  + "2026-11-08 03:00;Squads;Alex;3;100;12;4;;a.replay;3;7;;S1\n";
        var (csvSource, csvMessage) = Team.Parse("matches.csv", csv, my);
        Check("Import a matches.csv with session_id", csvSource?.Matches.Count == 1 && csvSource.Matches[0].ChestsOpened == 12, csvMessage);
        var (oldCsv, _) = Team.Parse("matches.csv", "date;mode;player;placement;players;chests;ammo_boxes;treasures;file\n2026-11-08 03:00;Squads;Alex;3;100;12;4;;a.replay\n", my);
        Check("Old matches.csv without session_id is refused", oldCsv == null);
        var header = ReportWriter.Csv(my).Split('\n')[0];
        Check("matches.csv keeps the old columns first", header.StartsWith("date;mode;player;placement;players;chests;ammo_boxes;treasures;file;", StringComparison.Ordinal), header.Trim());

        // 7) Files written by older versions load, and old matches are "no team data", not 0.
        var old = Store.Parse<TrackerData>("""{"Version":1,"AnalyzerVersion":15,"Matches":[{"SessionId":"old1","PlayerName":"Me","EpicId":"MEID","ChestsOpened":7,"Chests":[],"TreasureFinds":[],"TeamTreasureFinds":[]}]}""");
        var oldMatch = old?.Matches.FirstOrDefault();
        Check("Old data.json loads, team data null", oldMatch != null && oldMatch.TeammateChests == null && oldMatch.TeamChestsSeen == null);
        var ro = OddsModel.Build(old!, new Config(), now: now, imports: new TeamImports());
        Check("Old match counted as \"no team data\"", ro.MatchesNoTeamData == 1 && ro.Mine == 7 && ro.Total == 7);
        var oldConfig = Store.Parse<Config>("""{"Language":"en","ShowOverlay":false}""");
        Check("Old config.json gets the default odds", oldConfig?.Odds is { PirateShopOdds: 137_500, VehicleDecalOdds: 7_813, GoldOutfitOdds: 1_562_500, ChestScope: "team" });

        // 7b) First launch after Chest Counter: its data folder is taken over (in a temporary folder, not the real one).
        var appData = Path.Combine(Path.GetTempPath(), "cta-selftest-" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            Directory.CreateDirectory(Path.Combine(appData, "ChestCounter"));
            File.WriteAllText(Path.Combine(appData, "ChestCounter", "data.json"), "{}");
            var moved = Store.DefaultFolder(appData);
            Check("Old ChestCounter data folder is moved to ChestTokenAssistant",
                moved == Path.Combine(appData, "ChestTokenAssistant") && File.Exists(Path.Combine(moved, "data.json")) && !Directory.Exists(Path.Combine(appData, "ChestCounter")));
            Check("Second launch uses the new folder", Store.DefaultFolder(appData) == moved);
        }
        finally
        {
            try { Directory.Delete(appData, recursive: true); } catch { }
        }

        // 7c) Darija: its own plurals (2 to 10) and the Moroccan number format.
        L.Set("ary");
        var darija = $"{L.Count(1, "coffre", "chest", "sndouq", "cofre")} | {L.Count(3, "coffre", "chest", "sndouq", "cofre")} | {L.Count(50, "coffre", "chest", "sndouq", "cofre")} | {L.Pct(p13k)} | {L.OneIn(p13k)}";
        L.Set("en");
        Check("Darija: 1 sndouq, 3 snadeq, 50 sndouq, 9,02 %", darija.StartsWith("1 sndouq | 3 snadeq | 50 sndouq | 9,02 %", StringComparison.Ordinal), darija);

        // 7c bis) Spanish: plurals (0 is plural), "1 entre N", the Spanish page.
        L.Set("es");
        var spanish = $"{L.Count(1, "coffre", "chest", "sndouq", "cofre")} | {L.Count(0, "coffre", "chest", "sndouq", "cofre")} | {L.Count(2, "joueur", "player", "la3ib", "jugador")} | {L.Count(3, "", "", "", "", "", "", "repetición", "repeticiones")} | {L.Pct(p13k)} | {L.OneIn(0.01)}";
        var spanishPage = ReportWriter.Page(new Config { Language = "es" }, my, Profiles.List(my), "C:\\data", null);
        L.Set("en");
        Check("Spanish: 1 cofre, 0 cofres, 2 jugadores, 3 repeticiones, 9,02 %, 1 entre 100",
            spanish == "1 cofre | 0 cofres | 2 jugadores | 3 repeticiones | 9,02 % | 1 entre 100" && spanishPage.Contains("lang=\"es\"") && spanishPage.Contains("Cofres en total"), spanish);

        // 7d) The page: 7 tabs with buttons in the dashboard, 6 without any button in report.html.
        var liveInfo = new LiveInfo(new LiveStatus("wait", "Waiting for a match"), new[] { "line" }, null, false, false, false, false, null, null, true, false);
        var livePage = ReportWriter.Page(new Config(), my, Profiles.List(my), "C:\\data", liveInfo);
        var staticPage = ReportWriter.Page(new Config(), my, Profiles.List(my), "C:\\data", null);
        int Count(string text, string part) => (text.Length - text.Replace(part, "").Length) / part.Length;
        Check("Dashboard page: 8 tabs, each with its explanation", Count(livePage, "role=\"tab\"") == 8 && Count(livePage, "class=\"intro\"") == 8 && livePage.Contains("data-a=\"export\""),
            $"{Count(livePage, "role=\"tab\"")} tabs");
        Check("report.html: 7 tabs, no button, no settings", Count(staticPage, "role=\"tab\"") == 7 && !staticPage.Contains("data-a=") && !staticPage.Contains("data-panel=\"settings\""));
        Check("Items tab: 21 items, ticks only in the dashboard", Count(livePage, "class=\"pic\"") == 21 && livePage.Contains("data-set=\"item:w2-car\"") && !staticPage.Contains("data-set=\"item:"),
            $"{Count(livePage, "class=\"pic\"")} cards");

        // 7d2) Items: which treasure is which, chance per item, ticks that are remembered.
        var w2Day = ItemsModel.Week2Start.AddDays(1);
        Check("Week 1 golden chest prize recognised, week 2's not (yet)",
            ItemsModel.Identify(new TreasureFind { PrizeId = "DelilahPrize:DPID_Item2" }, ItemsModel.Week1Start.AddDays(2)).Id == "w1-loading"
            && ItemsModel.Identify(new TreasureFind { PrizeId = "DelilahPrize:DPID_Item2" }, w2Day) is { Id: null, UnknownPrize: true });
        Check("Gizmo recognised from its item", ItemsModel.Identify(new TreasureFind { RawItem = "WID_FlameCeleb_Athena", Item = "Feu d'artifice doré" }, w2Day).Id == "s-firework");
        var week2 = new TrackerData { Matches = { Mine("W2", 127, w2Day, Enumerable.Range(0, 127).Select(i => (float)i * 5).ToArray()) } };
        week2.Matches[0].TreasureFinds.Add(new TreasureFind { RawItem = "WID_FlameCeleb_Athena", Item = "Feu d'artifice doré" });
        var checklist = new Checklist();
        var items = ItemsModel.Build(week2, new Config(), checklist, now: w2Day.AddHours(2));
        var bling = items.Items.First(x => x.Def.Id == "w2-backbling");
        var w1Pick = items.Items.First(x => x.Def.Id == "w1-pickaxe");
        Check("Chance per item: only the chests of its week", Math.Abs((bling.Chance ?? 0) - OddsMath.AtLeastOne(1.0 / 568, 127)) < 1e-12 && w1Pick.Chance == 0,
            $"back bling {L.Pct(bling.Chance ?? 0)}, week 1 pickaxe {L.Pct(w1Pick.Chance ?? 0)}");
        Check("Found in a replay = ticked by itself", items.Items.First(x => x.Def.Id == "s-firework") is { Got: true, Auto.Count: 1 } && items.Got == 1);
        ItemsModel.Set(checklist, "s-firework", false, foundInReplay: true);
        ItemsModel.Set(checklist, "w2-car", true, foundInReplay: false);
        var after = ItemsModel.Build(week2, new Config(), Store.Parse<Checklist>(System.Text.Json.JsonSerializer.Serialize(checklist))!, now: w2Day.AddHours(2));
        Check("Unticking a replay find is remembered; ticks are saved", after.Items.First(x => x.Def.Id == "s-firework").Got == false && after.Items.First(x => x.Def.Id == "w2-car").Got);
        var withPicture = ItemsModel.Builtin.Where(d => d.Picture != null).ToList();
        var badPictures = withPicture.Where(d => ItemImages.Read(d) is not { } b || ItemImages.Sniff(b) is not { } kind || !ItemImages.FileName(d)!.EndsWith("." + kind, StringComparison.Ordinal)).Select(d => d.Id).ToList();
        Check("Every built-in picture is in the exe, and is the picture its name says", withPicture.Count == 19 && badPictures.Count == 0,
            $"{withPicture.Count} pictures{(badPictures.Count > 0 ? ", wrong: " + string.Join(", ", badPictures) : "")}");
        if (OperatingSystem.IsWindows())
        {
            var cur = @"C:\Games\new\ChestTokenAssistant.exe";
            Check("Old versions: another folder, ChestCounter.exe; never this exe",
                OldVersions.IsOld("ChestTokenAssistant", @"C:\Games\next\ChestTokenAssistant.exe", cur)
                && !OldVersions.IsOld("ChestTokenAssistant", @"c:\games\NEW\ChestTokenAssistant.exe", cur)
                && OldVersions.IsOld("ChestCounter", null, cur) && !OldVersions.IsOld("ChestTokenAssistant", null, cur)
                && !OldVersions.IsOld("ChestTokenAssistant", @"C:\Games\next\ChestTokenAssistant.exe", cur, scope: @"C:\Temp\test"));
            Check("Old versions: the exe a start-with-Fortnite entry opens",
                OldVersions.RunTarget("\"C:\\My Games\\next\\ChestTokenAssistant.exe\" --standby --data \"C:\\d\"") == @"C:\My Games\next\ChestTokenAssistant.exe"
                && OldVersions.RunTarget(@"C:\Old\ChestCounter.exe --standby") == @"C:\Old\ChestCounter.exe" && OldVersions.RunTarget(null) == null);
        }
        var goalConfig = new Config { DailyGoal = 300 };
        goalConfig.Odds.TreasureHours.Add(new TreasureHour { StartUtc = DateTime.UtcNow.AddMinutes(-5), EndUtc = DateTime.UtcNow.AddHours(1), PirateShop = 1.5 });
        var overlay = OverlayModel.For(week2, goalConfig, null, false);
        Check("Overlay: Treasure Hour on and today's goal", overlay.Extras is { Count: 2 } && overlay.Extras[0].Value.StartsWith("×1.5", StringComparison.Ordinal) && overlay.Extras[1].Value.EndsWith("/ 300", StringComparison.Ordinal),
            string.Join(" | ", (overlay.Extras ?? Array.Empty<OverlayRow>()).Select(x => $"{x.Label} {x.Value}")));
        var setupPage = ReportWriter.Page(new Config(), my, Profiles.List(my), "C:\\data", liveInfo with { Setup = new SetupInfo("lang", 1, 5, "C:\\Demos", false, 0) });
        Check("First launch: the setup replaces the tabs", !setupPage.Contains("role=\"tab\"") && Count(setupPage, "data-k=\"lang\"") == L.Codes.Length);

        // 7e) The dashboard server: this PC only, only with its secret, never from another site.
        using (var web = WebDashboard.Start("selftest", port: 0))
        {
            if (web == null)
            {
                Check("Dashboard server starts on 127.0.0.1", false);
            }
            else
            {
                web.Publish("<!doctype html><body data-v=\"__V__\">Chest Token Assistant</body>");
                string Get(string path, string? host = null) => $"GET {path} HTTP/1.1\r\nHost: {host ?? $"127.0.0.1:{web.Port}"}\r\nConnection: close\r\n\r\n";
                string Post(string path, string json, string extra) => $"POST {path} HTTP/1.1\r\nHost: 127.0.0.1:{web.Port}\r\n{extra}Content-Type: application/json\r\nContent-Length: {Encoding.UTF8.GetByteCount(json)}\r\nConnection: close\r\n\r\n{json}";
                var home = $"/{web.Key}/";
                var page = Raw(web.Port, Get(home));
                Check("Dashboard: the page with its secret, its version filled in", page.Status == 200 && page.Body.Contains("Chest Token Assistant") && page.Body.Contains("data-v=\"1\""), $"{page.Status}");
                Check("Dashboard: without the secret, nothing", Raw(web.Port, Get("/")).Status == 404 && Raw(web.Port, Get("/0123456789abcdef0123456789abcdef/")).Status == 404);
                Check("Dashboard: another site's name for 127.0.0.1 is refused", Raw(web.Port, Get(home, "evil.example:" + web.Port)).Status == 403);
                Check("Dashboard: the built-in pictures, and nothing else",
                    Raw(web.Port, Get(home + "img/w2-car.png")).Status == 200 && Raw(web.Port, Get(home + "img/s-lucky.webp")).Status == 200
                    && Raw(web.Port, Get(home + "img/w1-jam.png")).Status == 404 && Raw(web.Port, Get(home + "img/config.json")).Status == 404
                    && Raw(web.Port, Get(home + "img/..%2Fconfig.json")).Status == 404 && Raw(web.Port, Get(home + "img/../data.json")).Status == 404);
                using var pumping = new CancellationTokenSource();
                var pump = Task.Run(() =>
                {
                    while (!pumping.IsCancellationRequested)
                    {
                        web.RunPending(a => new DashboardReply(true, $"pong {a.Name} {a.Key} {a.Value}"));
                        Thread.Sleep(10);
                    }
                });
                var json = "{\"a\":\"set\",\"k\":\"scope\",\"v\":\"squad\"}";
                Check("Dashboard: a button without the page's mark is refused", Raw(web.Port, Post(home + "act", json, "")).Status == 403);
                Check("Dashboard: a button from another site is refused", Raw(web.Port, Post(home + "act", json, "X-CTA: 1\r\nOrigin: http://evil.example\r\n")).Status == 403);
                var pressed = Raw(web.Port, Post(home + "act", json, $"X-CTA: 1\r\nOrigin: http://127.0.0.1:{web.Port}\r\n"));
                Check("Dashboard: a button reaches the main loop and gets its answer", pressed.Status == 200 && pressed.Body.Contains("pong set scope squad"), pressed.Body);
                pumping.Cancel();
                pump.Wait(TimeSpan.FromSeconds(2));
            }
        }

        // 7f) Wide overlay: next milestone, a record beaten during the match, today's goal.
        var wideConfig = new Config { DailyGoal = 50, OverlayStyle = "wide" };
        var wideData = new TrackerData { Matches = { Mine("R1", 20, DateTime.UtcNow.AddDays(-3)) } };
        var wide = OverlayModel.For(wideData, wideConfig, new MatchRecord { ChestsOpened = 25 }, true).Wide;
        Check("Wide overlay: next milestone, live record, today's goal",
            OverlayModel.NextMilestone(2812) == 3000 && OverlayModel.NextMilestone(3000) == 4000 && OverlayModel.NextMilestone(40) == 100
            && wide is { MessageGold: true, GoalText: "25 / 50", GoalP: 0.5 } && wide.Message!.Contains("25"),
            wide == null ? "none" : $"{wide.Message} | {wide.GoalText} | {wide.Next}");

        // 8) Treasure roll rate interval.
        var (lo, hi) = OddsMath.Wilson(24, 2800);
        Check("95% interval of 24 treasures / 2,800 chests", lo < 24 / 2800.0 && hi > 24 / 2800.0 && lo > 0.005 && hi < 0.013, $"1 per {1 / hi:F0} to 1 per {1 / lo:F0}");

        Console.WriteLine();
        Console.WriteLine(failures == 0 ? "  All checks passed." : $"  {failures} check(s) failed.");
        return failures == 0 ? 0 : 1;
    }

    /// <summary>One raw HTTP request to the dashboard (to send exactly the headers a hostile page would).</summary>
    private static (int Status, string Body) Raw(int port, string request)
    {
        try
        {
            using var client = new System.Net.Sockets.TcpClient("127.0.0.1", port);
            var stream = client.GetStream();
            stream.Write(Encoding.UTF8.GetBytes(request));
            using var reader = new StreamReader(stream, Encoding.UTF8);
            var text = reader.ReadToEnd();
            var status = int.Parse(text.Split(' ')[1], System.Globalization.CultureInfo.InvariantCulture);
            var split = text.IndexOf("\r\n\r\n", StringComparison.Ordinal);
            return (status, split >= 0 ? text[(split + 4)..] : "");
        }
        catch (Exception ex)
        {
            return (0, ex.Message);
        }
    }

    private static MatchRecord Mine(string session, int chests, DateTime start, params float[] times) => new()
    {
        SessionId = session,
        PlayerName = "Me",
        EpicId = "MEID",
        MatchStartUtc = start,
        Playlist = "Playlist_NoBuildBR_Squad",
        DurationSeconds = 1200,
        ChestsOpened = chests,
        Chests = times.Select(t => new ChestRecord { TimeSeconds = t, Kind = ChestKinds.Normal }).ToList(),
        TeammateChests = new(),
        TeamChestsSeen = 0,
        TeamSize = 1,
    };
}
