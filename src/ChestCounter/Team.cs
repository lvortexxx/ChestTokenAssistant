using System.Globalization;

namespace ChestCounter;

/// <summary>
/// One teammate in one match: the chests of theirs the player's replay saw (a lower bound), and their own exact count
/// when their file was imported. <see cref="Seen"/> is null when the match has no team data.
/// </summary>
public sealed record MateInMatch(string Key, string Name, int? Seen, ImportedMatch? Imported, bool? InParty, IReadOnlyList<ChestRecord> SeenChests)
{
    /// <summary>Counted for the token odds with these settings (random teammates can be left out).</summary>
    public bool Included(OddsSettings s) => s.IncludeFill || InParty != false;
}

/// <summary>A teammate across all the matches played together (report leaderboard).</summary>
public sealed record MateStats(string Key, string Name, bool? InParty, int Matches, int Seen, int SeenMatches, int Imported, int ImportedMatches);

/// <summary>
/// Teammates' chests: imports of their own exported files (.chesttoken) or data.json, merged with the player's matches
/// by session id. Only matches played on the same team are kept. A teammate's chests in a match are counted once:
/// their own (imported) count when it is known, otherwise what the player's replay saw. The same match imported twice
/// (two files, or the same file again) is used once.
/// </summary>
public static class Team
{
    /// <summary>Imported files (imports.json): loaded at startup, replaced when a file is imported or removed.</summary>
    public static TeamImports Imports { get; set; } = new();

    /// <summary>Stable key of a player: Epic id when known (survives a name change), else the name.</summary>
    public static string Key(string? epicId, string? name) =>
        !string.IsNullOrEmpty(epicId) ? "id:" + epicId.Trim().ToLowerInvariant() : "name:" + (name ?? "").Trim().ToLowerInvariant();

    /// <summary>The teammates of a match. A match analyzed before team data existed only gets the teammates whose own
    /// imported file says they played with the player.</summary>
    public static List<MateInMatch> MatesOf(MatchRecord m, ImportIndex index)
    {
        if (m.TeammateChests != null)
        {
            return m.TeammateChests
                .Select(t => new MateInMatch(Key(t.EpicId, t.Name), t.Name, t.ChestsSeen, index.Find(m.SessionId, t.EpicId, t.Name), t.InParty, t.Chests))
                .ToList();
        }
        return index.ListingPlayer(m.SessionId, m.EpicId, m.PlayerName)
            .Select(im => new MateInMatch(Key(im.EpicId, im.PlayerName), im.PlayerName ?? "", null, im, null, Array.Empty<ChestRecord>()))
            .ToList();
    }

    /// <summary>Teammates across the matches, most matches together first.</summary>
    public static List<MateStats> Leaderboard(IEnumerable<MatchRecord> matches, ImportIndex index)
    {
        var rows = new Dictionary<string, (string Name, DateTime Last, bool AnyParty, bool AnyFill, int Matches, int Seen, int SeenMatches, int Imported, int ImportedMatches)>();
        foreach (var m in matches)
        {
            var when = m.MatchStartUtc ?? m.AnalyzedAtUtc;
            foreach (var mate in MatesOf(m, index))
            {
                var r = rows.GetValueOrDefault(mate.Key);
                if (r.Name == null || (when >= r.Last && mate.Name.Length > 0))
                {
                    r.Name = mate.Name;
                    r.Last = when;
                }
                r.AnyParty |= mate.InParty == true;
                r.AnyFill |= mate.InParty == false;
                r.Matches++;
                if (mate.Seen is int seen)
                {
                    r.Seen += seen;
                    r.SeenMatches++;
                }
                if (mate.Imported is { } im)
                {
                    r.Imported += im.ChestsOpened;
                    r.ImportedMatches++;
                }
                rows[mate.Key] = r;
            }
        }
        return rows
            .Select(kv => new MateStats(kv.Key, kv.Value.Name ?? "", kv.Value.AnyParty ? true : kv.Value.AnyFill ? false : null,
                kv.Value.Matches, kv.Value.Seen, kv.Value.SeenMatches, kv.Value.Imported, kv.Value.ImportedMatches))
            .OrderByDescending(s => s.Matches)
            .ThenByDescending(s => s.Seen)
            .ToList();
    }

    /// <summary>
    /// Reads a teammate's exported file (.chesttoken), their data.json (any version) or a matches.csv that has the
    /// session_id column, and keeps the matches the two of you played <b>on the same team</b>: in both histories, and
    /// one side lists the other as a teammate (a friend in the same match on another team is left out). Returns null
    /// and a message when the file cannot be used.
    /// </summary>
    public static (ImportedSource? Source, string Message) Parse(string path, string content, TrackerData mine)
    {
        var myKeys = mine.Matches.Select(Profiles.Key).Where(k => k.Length > 0).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var myNames = mine.Matches.Select(m => m.PlayerName).Where(n => !string.IsNullOrWhiteSpace(n)).Select(n => n!.Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var myMatches = mine.Matches.Where(m => m.SessionId.Length > 0).GroupBy(m => m.SessionId).ToDictionary(g => g.Key, g => g.First());
        var trimmed = content.TrimStart('﻿', ' ', '\t', '\r', '\n');
        List<(ImportedMatch Match, string OwnerKey, string OwnerName)> all;
        if (trimmed.StartsWith('{'))
        {
            ShareFile? share = null;
            TrackerData? theirs = null;
            try
            {
                if (trimmed.Contains($"\"{ShareFormat.Name}\"", StringComparison.Ordinal))
                {
                    share = Store.Parse<ShareFile>(trimmed);
                }
                else
                {
                    theirs = Store.Parse<TrackerData>(trimmed);
                }
            }
            catch (Exception ex)
            {
                return (null, L.T($"Fichier illisible ({ex.Message}).", $"Unreadable file ({ex.Message}).", $"Fichier ma kayt9rach ({ex.Message}).", $"Archivo ilegible ({ex.Message})."));
            }
            if (share != null)
            {
                if (share.FormatVersion > 1)
                {
                    return (null, L.T("Ce fichier vient d'une version plus récente de l'assistant : mets le tien à jour.",
                        "This file comes from a newer version of the assistant: update yours.",
                        "Had l'fichier jay mn version jdida dyal l'assistant: update dyalek.", "Este archivo viene de una versión más reciente del asistente: actualiza la tuya."));
                }
                var owner0 = Profiles.Key(new MatchRecord { EpicId = share.Player.EpicId, PlayerName = share.Player.Name });
                all = share.Matches.Select(m => (new ImportedMatch
                {
                    SessionId = m.SessionId,
                    PlayerName = share.Player.Name,
                    EpicId = share.Player.EpicId,
                    MatchStartUtc = m.MatchStartUtc,
                    ChestsOpened = m.Chests,
                    ChestTimes = m.ChestTimes,
                    TeammateHashes = m.Teammates,
                }, owner0, share.Player.Name)).ToList();
            }
            else if (theirs?.Matches is { Count: > 0 })
            {
                all = theirs.Matches.Select(m => (new ImportedMatch
                {
                    SessionId = m.SessionId,
                    PlayerName = m.PlayerName,
                    EpicId = m.EpicId,
                    MatchStartUtc = m.MatchStartUtc,
                    ChestsOpened = m.ChestsOpened,
                    ChestTimes = m.Chests.Select(c => c.TimeSeconds).ToList(),
                    TeammateHashes = m.TeammateChests?.SelectMany(t => ShareFormat.Hashes(t.EpicId, t.Name)).Distinct().ToList(),
                }, Profiles.Key(m), m.PlayerName ?? "")).ToList();
            }
            else
            {
                all = new();
            }
        }
        else
        {
            var lines = trimmed.Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.Length > 0).ToList();
            var header = lines.Count > 0 ? lines[0].Split(';') : Array.Empty<string>();
            var sessionCol = Array.FindIndex(header, h => h.Trim().Equals("session_id", StringComparison.OrdinalIgnoreCase));
            if (header.Length < 9)
            {
                // Neither JSON nor a matches.csv: not a file of the assistant at all.
                return (null, L.T("Ce n'est pas un fichier de coéquipier : importe le fichier .chesttoken qu'il a exporté (ou son data.json).",
                    "This is not a teammate's file: import the .chesttoken file they exported (or their data.json).",
                    "Hada machi fichier dyal sa7bek: importi l'fichier .chesttoken li exporta (wla data.json dyalo).", "Este no es el archivo de un compañero: importa el archivo .chesttoken que exportó (o su data.json)."));
            }
            if (sessionCol < 0)
            {
                return (null, L.T("Ce matches.csv n'a pas de colonne session_id (écrit par une version plus ancienne) : demande plutôt à ton coéquipier d'exporter son fichier (onglet Escouade).",
                    "This matches.csv has no session_id column (written by an older version): ask your teammate to export their file instead (Squad tab).",
                    "Had matches.csv ma fih colonne session_id (ktbato version 9dima): tleb mn sa7bek y'exporti l'fichier dyalo (onglet Squad).", "Este matches.csv no tiene columna session_id (lo escribió una versión antigua): pide a tu compañero que exporte su archivo (pestaña Escuadrón)."));
            }
            all = new();
            foreach (var cells in lines.Skip(1).Select(l => l.Split(';')).Where(c => c.Length > sessionCol))
            {
                if (!int.TryParse(cells[5], NumberStyles.Integer, CultureInfo.InvariantCulture, out var chests))
                {
                    continue;
                }
                var name = cells[2].Trim();
                all.Add((new ImportedMatch { SessionId = cells[sessionCol].Trim(), PlayerName = name, ChestsOpened = chests }, Key(null, name), name));
            }
        }
        if (all.Count == 0)
        {
            return (null, L.T("Ce fichier ne contient aucune partie.", "This file contains no match.", "Had l'fichier khawi, ma fih 7ta partiya.", "Este archivo no contiene ninguna partida."));
        }

        // Whose file: the account with the most matches in it.
        var owner = all.GroupBy(x => x.OwnerKey).OrderByDescending(g => g.Count()).First();
        var ownerName = owner.Select(x => x.OwnerName).FirstOrDefault(n => n.Length > 0) ?? "?";
        bool IsMe(string key, string name, string? epicId) => myKeys.Contains(key) || myNames.Contains(name) || (epicId != null && myKeys.Contains(epicId));
        if (all.All(x => IsMe(x.OwnerKey, x.OwnerName, x.Match.EpicId)))
        {
            return (null, L.T("C'est ton propre historique : importe le fichier d'un coéquipier.", "This is your own history: import a teammate's file.", "Hada l'historique dyalek nta: importi l'fichier dyal sa7bek, machi dyalek hhh.", "Este es tu propio historial: importa el archivo de un compañero."));
        }

        // Same match, then same team: my replay lists them, or their file lists me (hashed).
        var shared = all.Where(x => myMatches.ContainsKey(x.Match.SessionId) && !IsMe(x.OwnerKey, x.OwnerName, x.Match.EpicId)).ToList();
        var together = shared.Where(x => SameTeam(myMatches[x.Match.SessionId], x.Match)).Select(x => x.Match).ToList();
        var otherTeam = shared.Count - together.Count;
        var source = new ImportedSource
        {
            Key = owner.Key,
            Name = ownerName,
            File = Path.GetFileName(path),
            ImportedUtc = DateTime.UtcNow,
            MatchesInFile = all.Count,
            Matches = together,
        };
        var notTogether = otherTeam > 0
            ? L.T($" ({otherTeam} autre(s) dans la même partie mais pas dans ton équipe, ou sans données d'équipe : ignorée(s))",
                $" ({otherTeam} more in the same match but not on your team, or without team data: ignored)",
                $" ({otherTeam} khrin f nefs la partiya walakin machi f l'équipe dyalek, wla bla données dyal l'équipe: tkhllaw)", $" ({otherTeam} más en la misma partida pero no en tu equipo, o sin datos de equipo: ignoradas)")
            : "";
        if (together.Count == 0)
        {
            return (null, L.T($"{ownerName} : {L.Count(all.Count, "partie", "parties", "", "", "", "", "", "")} dans le fichier, aucune jouée avec toi dans la même équipe{notTogether}.",
                $"{ownerName}: {L.Count(all.Count, "", "", "match", "matches", "", "", "partida", "partidas")} in the file, none played with you on the same team{notTogether}.",
                $"{ownerName}: {L.Count(all.Count, "", "", "", "", "partiya", "partiyat", "", "")} f l'fichier, 7ta wa7da ma l3btoha m3a ba3d f nefs l'équipe{notTogether}.", $"{ownerName}: {L.Count(all.Count, "", "", "", "", "", "", "partida", "partidas")} en el archivo, ninguna jugada contigo en el mismo equipo{notTogether}."));
        }
        var chestsTotal = together.Sum(m => m.ChestsOpened);
        return (source, L.T($"{ownerName} importé : {L.Count(together.Count, "partie", "parties", "", "", "", "", "", "")} ensemble (même équipe), {L.Count(chestsTotal, "coffre", "chest", "sndouq", "cofre")} à eux{notTogether}.",
            $"{ownerName} imported: {L.Count(together.Count, "", "", "match", "matches", "", "", "partida", "partidas")} together (same team), {L.Count(chestsTotal, "coffre", "chest", "sndouq", "cofre")} of theirs{notTogether}.",
            $"{ownerName} importé: {L.Count(together.Count, "", "", "", "", "partiya", "partiyat", "", "")} m3a ba3d (nefs l'équipe), {L.Count(chestsTotal, "coffre", "chest", "sndouq", "cofre")} dyalo{notTogether}.", $"{ownerName} importado: {L.Count(together.Count, "", "", "", "", "", "", "partida", "partidas")} juntos (mismo equipo), {L.Count(chestsTotal, "coffre", "chest", "sndouq", "cofre")} suyos{notTogether}."));
    }

    /// <summary>On my team in that match: my replay lists them as a teammate, or their file lists me.</summary>
    public static bool SameTeam(MatchRecord mine, ImportedMatch theirs)
    {
        if (mine.TeammateChests?.Any(t => (!string.IsNullOrEmpty(t.EpicId) && string.Equals(t.EpicId, theirs.EpicId, StringComparison.OrdinalIgnoreCase))
                || (t.Name.Length > 0 && string.Equals(t.Name.Trim(), theirs.PlayerName?.Trim(), StringComparison.OrdinalIgnoreCase))) == true)
        {
            return true;
        }
        return theirs.TeammateHashes?.Intersect(ShareFormat.Hashes(mine.EpicId, mine.PlayerName)).Any() == true;
    }

    /// <summary>Adds (or replaces: same account) an imported file. The newest import of an account wins.</summary>
    public static TeamImports With(TeamImports imports, ImportedSource source) => new()
    {
        Version = imports.Version,
        Sources = imports.Sources.Where(s => !string.Equals(s.Key, source.Key, StringComparison.OrdinalIgnoreCase)).Append(source).ToList(),
    };
}

/// <summary>Looks up imported matches by session and player. Built once per report / screen.</summary>
public sealed class ImportIndex
{
    private readonly Dictionary<string, ImportedMatch> _byPlayer = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<ImportedMatch>> _bySession = new(StringComparer.Ordinal);

    public static readonly ImportIndex Empty = new(new TeamImports());

    public ImportIndex(TeamImports imports)
    {
        // Newest import first: when two files hold the same player's same match, the newest one is used, once.
        foreach (var source in imports.Sources.OrderByDescending(s => s.ImportedUtc))
        {
            foreach (var m in source.Matches)
            {
                var added = false;
                if (!string.IsNullOrEmpty(m.EpicId))
                {
                    added |= _byPlayer.TryAdd(m.SessionId + "|" + Team.Key(m.EpicId, null), m);
                }
                if (!string.IsNullOrWhiteSpace(m.PlayerName))
                {
                    added |= _byPlayer.TryAdd(m.SessionId + "|" + Team.Key(null, m.PlayerName), m);
                }
                if (added)
                {
                    if (!_bySession.TryGetValue(m.SessionId, out var list))
                    {
                        _bySession[m.SessionId] = list = new List<ImportedMatch>();
                    }
                    list.Add(m);
                }
            }
        }
    }

    public bool IsEmpty => _byPlayer.Count == 0;

    public ImportedMatch? Find(string session, string? epicId, string? name)
    {
        if (!string.IsNullOrEmpty(epicId) && _byPlayer.TryGetValue(session + "|" + Team.Key(epicId, null), out var byId))
        {
            return byId;
        }
        return !string.IsNullOrWhiteSpace(name) && _byPlayer.TryGetValue(session + "|" + Team.Key(null, name), out var byName) ? byName : null;
    }

    /// <summary>Imported matches of that session whose file says the given player was their teammate.</summary>
    public IEnumerable<ImportedMatch> ListingPlayer(string session, string? epicId, string? name)
    {
        if (!_bySession.TryGetValue(session, out var list))
        {
            return Enumerable.Empty<ImportedMatch>();
        }
        var me = ShareFormat.Hashes(epicId, name).ToList();
        return list.Where(m => m.TeammateHashes?.Intersect(me).Any() == true && Team.Key(m.EpicId, m.PlayerName) != Team.Key(epicId, name))
            .DistinctBy(m => Team.Key(m.EpicId, m.PlayerName));
    }
}

/// <summary>A squad: the player and the imported teammates who were all on the same team, and their matches together.</summary>
public sealed record SquadGroup(string Key, IReadOnlyList<(string Name, int Chests)> Members, int Matches, int MyChests, int Total, int OthersSeen, double Hours, double P);

/// <summary>
/// Squads from the imported files: matches grouped by which imported teammates were on the player's team (exactly
/// that set). Each player counts with their own exact number; a teammate who sent no file is shown apart (seen).
/// </summary>
public static class SquadModel
{
    public static List<SquadGroup> Build(TrackerData view, Config config, TeamImports? imports = null)
    {
        var s = config.Odds ?? new OddsSettings();
        var index = new ImportIndex(imports ?? Team.Imports);
        var p = 1.0 / Math.Max(1, s.PirateShopOdds);
        var pirate = TreasureDef.All[0];
        var groups = new Dictionary<string, (Dictionary<string, (string Name, int Chests)> Members, int Matches, int Mine, int Others, double Hours, double LogNone)>();
        foreach (var m in view.Matches.Where(m => OddsModel.IsEligible(m, s)))
        {
            var mates = Team.MatesOf(m, index);
            var imported = mates.Where(x => x.Imported != null).ToList();
            if (imported.Count == 0)
            {
                continue;
            }
            var key = string.Join("|", imported.Select(x => x.Key).OrderBy(k => k, StringComparer.Ordinal));
            var g = groups.GetValueOrDefault(key);
            g.Members ??= new Dictionary<string, (string Name, int Chests)>();
            g.Matches++;
            g.Mine += m.ChestsOpened;
            g.Others += mates.Where(x => x.Imported == null).Sum(x => x.Seen ?? 0);
            g.Hours += (m.DurationSeconds ?? 0) / 3600.0;
            var start = OddsModel.ChestBase(m);
            foreach (var c in m.Chests)
            {
                g.LogNone += OddsMath.LogNone(p, OddsModel.Multiplier(s, start.AddSeconds(c.TimeSeconds), pirate));
            }
            g.LogNone += Math.Max(0, m.ChestsOpened - m.Chests.Count) * OddsMath.LogNone(p);
            foreach (var x in imported)
            {
                var im = x.Imported!;
                var name = !string.IsNullOrWhiteSpace(im.PlayerName) ? im.PlayerName! : x.Name;
                var member = g.Members.GetValueOrDefault(x.Key);
                g.Members[x.Key] = (name, member.Chests + im.ChestsOpened);
                var imStart = im.MatchStartUtc is DateTime ims ? OddsModel.Utc(ims) : start;
                foreach (var t in im.ChestTimes)
                {
                    g.LogNone += OddsMath.LogNone(p, OddsModel.Multiplier(s, imStart.AddSeconds(t), pirate));
                }
                g.LogNone += Math.Max(0, im.ChestsOpened - im.ChestTimes.Count) * OddsMath.LogNone(p);
            }
            groups[key] = g;
        }
        return groups
            .Select(kv => new SquadGroup(kv.Key,
                kv.Value.Members.Values.OrderByDescending(x => x.Chests).ToList(),
                kv.Value.Matches, kv.Value.Mine, kv.Value.Mine + kv.Value.Members.Values.Sum(x => x.Chests), kv.Value.Others, kv.Value.Hours,
                OddsMath.FromLogNone(kv.Value.LogNone)))
            .OrderByDescending(g => g.Members.Count)
            .ThenByDescending(g => g.Matches)
            .ToList();
    }
}

/// <summary>One imported file and what it changes: matches it was used in, their chests next to what the player's
/// replay saw of them, and the token chance with and without it.</summary>
public sealed record FileImpact(ImportedSource Source, int MatchesUsed, int TheirChests, int SeenOfThem, DateTime? First, DateTime? Last,
    double PWith, double PWithout, IReadOnlyList<string> Squads);

/// <summary>Every imported file, plus the token chance with all of them and with none (Files tab, report).</summary>
public sealed record ImportsImpact(double PNone, double PAll, string Scope, IReadOnlyList<FileImpact> Files);

public static class ImportsReport
{
    public static ImportsImpact Build(TrackerData view, Config config, TeamImports? imports = null)
    {
        var all = imports ?? Team.Imports;
        var index = new ImportIndex(all);
        var odds = OddsModel.Build(view, config, imports: all);
        var pNone = all.Sources.Count == 0 ? odds.Pirate.P : OddsModel.Build(view, config, imports: new TeamImports()).Pirate.P;
        var squads = SquadModel.Build(view, config, all);
        var you = L.T("Toi", "You", "Nta", "Tú");
        var files = new List<FileImpact>();
        foreach (var source in all.Sources.OrderByDescending(s => s.Matches.Count))
        {
            var ofThisFile = new HashSet<ImportedMatch>(source.Matches);
            int used = 0, theirs = 0, seen = 0;
            DateTime? first = null, last = null;
            foreach (var m in view.Matches)
            {
                foreach (var mate in Team.MatesOf(m, index).Where(x => x.Imported != null && ofThisFile.Contains(x.Imported)))
                {
                    used++;
                    theirs += mate.Imported!.ChestsOpened;
                    seen += mate.Seen ?? 0;
                    var when = OddsModel.Utc(m.MatchStartUtc ?? m.AnalyzedAtUtc);
                    first = first is DateTime f && f <= when ? f : when;
                    last = last is DateTime l && l >= when ? l : when;
                }
            }
            var without = new TeamImports { Sources = all.Sources.Where(s => !ReferenceEquals(s, source)).ToList() };
            var pWithout = OddsModel.Build(view, config, imports: without).Pirate.P;
            var inSquads = squads.Where(g => g.Members.Any(x => x.Name == source.Name))
                .Select(g => $"{you} + {string.Join(" + ", g.Members.Select(x => x.Name))} ({g.Matches})")
                .ToList();
            files.Add(new FileImpact(source, used, theirs, seen, first, last, odds.Pirate.P, pWithout, inSquads));
        }
        return new ImportsImpact(pNone, odds.Pirate.P, odds.Scope, files);
    }

    /// <summary>"+0.22 pts" / "+0,22 pts": a difference of chance in percentage points.</summary>
    public static string Points(double delta) =>
        (delta >= 0 ? "+" : "−") + Math.Abs(delta * 100).ToString(Math.Abs(delta * 100) >= 0.01 ? "0.00" : "0.0000", L.Culture) + " pts";
}
