using System.Globalization;
using System.Net;
using System.Text;

namespace ChestCounter;

public static class Pretty
{
    public static string Playlist(string? playlist)
    {
        if (string.IsNullOrEmpty(playlist))
        {
            return L.T("mode inconnu", "unknown mode", "mode ma3rofch", "modo desconocido");
        }

        var p = playlist.ToLowerInvariant();
        var parts = new List<string>();
        if (p.Contains("habanero") || p.Contains("ranked")) parts.Add(L.T("Classée", "Ranked", "Classée", "Clasificatoria"));
        if (p.Contains("nobuild")) parts.Add(L.T("Zéro construction", "Zero Build", "Zero Build", "Cero construcción"));
        if (p.Contains("solo")) parts.Add(L.T("Solo", "Solo", "Solo", "Solo"));
        else if (p.Contains("duo")) parts.Add(L.T("Duo", "Duos", "Duo", "Dúos"));
        else if (p.Contains("trio")) parts.Add(L.T("Trio", "Trios", "Trio", "Tríos"));
        else if (p.Contains("squad")) parts.Add(L.T("Section", "Squads", "Squad", "Escuadrones"));
        return parts.Count > 0 ? string.Join(" · ", parts) : playlist.Replace("Playlist_", "");
    }

    public static string Placement(MatchRecord m)
    {
        if (m.Placement is not int place || place <= 0)
        {
            return L.T("place inconnue", "unknown placement", "blassa ma3rofach", "puesto desconocido");
        }
        if (place == 1)
        {
            return L.T("Top 1 !", "Victory Royale!", "Top 1, wa3er a sat!", "¡Victoria magistral!");
        }
        return m.TotalPlayers is int total && total > 0 ? $"{L.Ordinal(place)} / {total}" : L.Ordinal(place);
    }

    public static string Clock(float seconds)
    {
        var s = (int)seconds;
        return $"{s / 60}:{s % 60:00}";
    }

    /// <summary>"à 7:16 · Coffre rare" (certain) or "vers 5:04 · Coffre rare (probable)".</summary>
    public static string Obtained(TreasureFind f)
    {
        if (f.ObtainedAtSeconds is not float t)
        {
            return L.T("moment et source inconnus", "time and source unknown", "wa9t w masdar ma3rofinch", "momento y origen desconocidos");
        }
        var when = f.TimeExact ? $"{L.T("à", "at", "f", "a")} {Clock(t)}" : $"{L.T("vers", "around", "tqriban f", "hacia")} {Clock(t)}";
        return f.SourceCertain ? $"{when} · {f.SourceLabel}" : $"{when} · {f.SourceLabel} ({Likely})";
    }

    public static string Likely => L.T("probable", "likely", "ghaliban", "probable");

    public static string ItemName(string item)
    {
        var name = item;
        foreach (var prefix in new[] { "WID_", "Athena", "AmmoData" })
        {
            name = name.Replace(prefix, "");
        }
        return name.Replace("_", " ").Trim();
    }
}

/// <summary>Replays analyzed so far / to analyze, shown as a loading card in the report.</summary>
public sealed record ScanProgress(int Done, int Total);

/// <summary>
/// One page with tabs (home, token, squad, files, matches, settings, help), each with a one-line explanation and a
/// short line under every part. The same page is the live dashboard (with its buttons, served by
/// <see cref="WebDashboard"/>) and report.html (a copy without buttons, readable while the assistant is closed).
/// </summary>
public static partial class ReportWriter
{
    public static readonly DateTime SessionStartUtc = DateTime.UtcNow;

    /// <summary>Set while several replays are being analyzed: the report shows a loading card and refreshes faster.</summary>
    public static volatile ScanProgress? Progress;

    /// <summary>What only the live dashboard shows (status, activity, buttons); null without a dashboard.</summary>
    public static Func<LiveInfo>? Live { get; set; }

    /// <summary>Receives each new dashboard page (main loop thread).</summary>
    public static Action<string>? PublishLive { get; set; }

    /// <summary>A match belongs to this session if it ended after the tool was started (backlog replays do not count).</summary>
    public static bool IsThisSession(MatchRecord m)
    {
        var start = m.MatchStartUtc ?? m.AnalyzedAtUtc;
        if (start.Kind == DateTimeKind.Unspecified)
        {
            start = DateTime.SpecifyKind(start, DateTimeKind.Utc);
        }
        var end = start.ToUniversalTime().AddSeconds(m.DurationSeconds ?? 0);
        return end >= SessionStartUtc.AddMinutes(-2);
    }
    private static CultureInfo Fr => L.Culture;

    public static void WriteAll(Store store, Config config, TrackerData data)
    {
        try
        {
            var view = Profiles.View(data, config);
            WriteAtomic(store.ReportPath, Page(config, view, Profiles.List(data), store.Folder, null));
            WriteAtomic(store.OverlayPath, Overlay(OverlayModel.For(view, config, null, false)));
            WriteAtomic(store.CsvPath, Csv(data));
        }
        catch (Exception ex)
        {
            Console.WriteLine(L.T($"Impossible d'écrire le rapport : {ex.Message}", $"Cannot write the report: {ex.Message}", $"Ma 9dertch nkteb le rapport: {ex.Message}", $"No se puede escribir el informe: {ex.Message}"));
        }
        RenderLive(store, config, data);
    }

    /// <summary>Renders the dashboard page again (status, activity, a setting changed). Main loop thread only.</summary>
    public static void RenderLive(Store store, Config config, TrackerData data)
    {
        if (Live is not { } live || PublishLive is not { } publish)
        {
            return;
        }
        try
        {
            publish(Page(config, Profiles.View(data, config), Profiles.List(data), store.Folder, live()));
        }
        catch (Exception ex)
        {
            ErrorLog.Write("Dashboard page", ex);
        }
    }

    private static void WriteAtomic(string path, string content)
    {
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, content, new UTF8Encoding(false));
        File.Move(tmp, path, overwrite: true);
    }

    private static string H(string? s) => WebUtility.HtmlEncode(s ?? "");

    private static DateTime When(MatchRecord m) => (m.MatchStartUtc ?? m.AnalyzedAtUtc).ToLocalTime();

    // "Vortex" look: deep indigo with violet and cyan accents, gold kept for the figures that matter (chests, token
    // chance). Dark by default; a light version follows Windows' light mode. Titles and figures use Bahnschrift,
    // installed with Windows (no font to embed, no download).
    private const string Style = """
        :root{--bg:#0e0b1e;--bg2:#141029;--ink:#ece9ff;--ink2:#f5f3ff;--muted:#a59fc9;--title:#c4b5fd;--accent:#8b5cf6;--accent2:#22d3ee;--card:#1a1533;--cink:#ece9ff;--cmuted:#a59fc9;--line:#2f2758;--track:#2a2350;--gold:#fbbf24;--nobar:#3d3566;--bar:#8b5cf6;--bartop:#22d3ee;--star:#c084fc;--team:#22d3ee;--ok:#34d399;--warn:#f87171;--warnbg:rgba(248,113,113,.10);--field:#100c22}
        @media (prefers-color-scheme:light){:root:not([data-theme="dark"]){--bg:#f3f1ff;--bg2:#ebe7ff;--ink:#1c1640;--ink2:#1c1640;--muted:#5b5490;--title:#5b21b6;--accent2:#0e7490;--card:#ffffff;--cink:#1c1640;--cmuted:#5b5490;--line:#e0dbf7;--track:#ece9fb;--gold:#b45309;--nobar:#d9d3f3;--bar:#7c3aed;--bartop:#0891b2;--star:#7c3aed;--team:#0e7490;--ok:#047857;--warn:#b91c1c;--warnbg:rgba(185,28,28,.08);--field:#f7f5ff}}
        :root[data-theme="light"]{--bg:#f3f1ff;--bg2:#ebe7ff;--ink:#1c1640;--ink2:#1c1640;--muted:#5b5490;--title:#5b21b6;--accent2:#0e7490;--card:#ffffff;--cink:#1c1640;--cmuted:#5b5490;--line:#e0dbf7;--track:#ece9fb;--gold:#b45309;--nobar:#d9d3f3;--bar:#7c3aed;--bartop:#0891b2;--star:#7c3aed;--team:#0e7490;--ok:#047857;--warn:#b91c1c;--warnbg:rgba(185,28,28,.08);--field:#f7f5ff}
        *{box-sizing:border-box}
        html{scroll-behavior:smooth}
        body{margin:0;background:var(--bg);color:var(--ink);font:15px/1.5 "Segoe UI",system-ui,-apple-system,sans-serif}
        #app{max-width:1180px;margin:0 auto;padding:0 16px 40px}
        .ttl{font-family:Bahnschrift,"Segoe UI",sans-serif;font-weight:700;font-stretch:87.5%;letter-spacing:.01em}
        .num{font-family:Bahnschrift,"Segoe UI",sans-serif;font-stretch:75%;font-variant-numeric:tabular-nums}
        header.top{display:flex;flex-wrap:wrap;align-items:center;justify-content:space-between;gap:12px 20px;padding:20px 0 14px}
        .brand{display:flex;align-items:center;gap:12px}
        .brand svg{width:46px;height:46px;flex:none}
        h1{margin:0;font-size:30px;line-height:1;color:var(--title)}
        .who{font-size:14px;color:var(--muted);margin-top:4px}.who b{color:var(--ink2)}
        .sig{color:var(--accent2);font-weight:600;letter-spacing:.04em}
        .muted{color:var(--muted)}
        .upd{font-size:13px}
        .status{display:inline-flex;align-items:center;gap:10px;padding:8px 14px;border-radius:999px;background:var(--card);border:1px solid var(--line);font-size:14px;color:var(--cink)}
        .status .dot{width:10px;height:10px;border-radius:50%;background:var(--ok);flex:none}
        .status.live{border-color:#f43f5e}.status.live .dot{background:#f43f5e;animation:pulse 1.2s ease-in-out infinite}
        .status.count .dot,.status.scan .dot{background:var(--accent2);animation:pulse 1.2s ease-in-out infinite}
        .status.close{border-color:var(--gold)}.status.close .dot{background:var(--gold)}
        .status.nofolder .dot{background:#fb923c}
        @keyframes pulse{50%{opacity:.35}}
        nav.tabs{position:sticky;top:0;z-index:5;display:flex;gap:4px;overflow-x:auto;padding:10px 0 12px;background:var(--bg);border-bottom:1px solid var(--line);margin-bottom:22px;scrollbar-width:thin}
        nav.tabs button{font:600 15px Bahnschrift,"Segoe UI",sans-serif;color:var(--muted);background:transparent;border:1px solid transparent;border-radius:10px;padding:8px 15px;cursor:pointer;white-space:nowrap}
        nav.tabs button:hover{color:var(--ink);background:var(--card)}
        nav.tabs button.on{color:#fff;background:var(--accent);border-color:var(--accent)}
        nav.tabs button:focus-visible,.btn:focus-visible{outline:2px solid var(--accent2);outline-offset:2px}
        .panel{display:none;flex-direction:column;gap:18px}
        .panel.on{display:flex}
        .intro h2{margin:0;font-size:28px;line-height:1.15;color:var(--title)}
        .intro p{margin:4px 0 0;color:var(--muted);font-size:15px}
        .box{background:var(--card);color:var(--cink);border:1px solid var(--line);border-radius:12px;padding:16px 18px;min-width:0}
        .bh{display:flex;flex-wrap:wrap;align-items:flex-start;justify-content:space-between;gap:8px 16px;margin-bottom:12px}
        .bh h3{margin:0;font-size:19px;line-height:1.25;color:var(--title)}
        .bh .sub{margin:3px 0 0;font-size:13px;color:var(--cmuted);max-width:720px}
        .scroll{overflow-x:auto}
        .grid2{display:grid;grid-template-columns:repeat(2,minmax(0,1fr));gap:18px}
        @media (max-width:860px){.grid2{grid-template-columns:1fr}}
        .tiles{display:grid;grid-template-columns:repeat(6,minmax(0,1fr));gap:12px}
        .tile{background:var(--card);color:var(--cink);border:1px solid var(--line);border-radius:12px;padding:14px 16px;display:flex;flex-direction:column;gap:2px;text-align:left;font:inherit;min-width:0}
        .tile .v{font-size:36px;font-weight:700;line-height:1.1}
        .tile .l{font-size:13px;color:var(--cmuted)}
        .tile .s{font-size:12px;color:var(--cmuted);margin-top:4px}
        .tile.hero{grid-column:span 2;background:#2a1b5e;color:#ece9ff;border:2px solid #8b5cf6;justify-content:space-between}
        .tile.hero .k{font-size:12px;letter-spacing:.14em;text-transform:uppercase;color:#67e8f9}
        .tile.hero .v{font-size:60px;color:#fbbf24;line-height:1}
        .tile.hero .l{color:#e9e5ff}
        .tile.go{cursor:pointer}.tile.go:hover{border-color:var(--accent)}
        .tile .v.gold{color:var(--gold)}
        @media (max-width:860px){.tiles{grid-template-columns:repeat(2,minmax(0,1fr))}}
        table{width:100%;border-collapse:collapse;font-variant-numeric:tabular-nums}
        th,td{text-align:left;padding:9px 8px;border-bottom:1px solid var(--line);vertical-align:middle}
        th{white-space:nowrap;font-size:12px;text-transform:uppercase;letter-spacing:.08em;color:var(--cmuted);font-weight:600}
        tr:last-child td{border-bottom:none}
        td.n{text-align:right;font-weight:700}
        td.nw{white-space:nowrap}
        .big{font-size:20px}
        .items{color:var(--cmuted);font-size:13px}
        .star{color:var(--star);font-weight:600}
        .gold{color:var(--gold)}
        .sw{display:inline-block;width:10px;height:10px;border-radius:2px;margin-right:10px;vertical-align:-1px}
        .meter{display:block;width:140px;height:6px;background:var(--track);border-radius:3px;overflow:hidden}
        .meter i{display:block;height:6px}
        .flag{display:flex;gap:12px;align-items:flex-start;background:#3b1530;color:#fde2e2;border:1px solid #f87171;border-radius:10px;padding:12px 14px;font-size:14px;margin-top:12px}
        tr.unusual td{background:var(--warnbg)}
        .warn{color:var(--warn)}
        .bars{display:flex;align-items:stretch;gap:4px;height:150px;padding-top:26px}
        .col{flex:1;min-width:5px;display:flex;align-items:flex-end;position:relative;cursor:default}
        .bar{width:100%;background:var(--bar);border-radius:3px 3px 0 0;transition:filter .12s}
        .bar.top{background:var(--bartop)}
        .bar.zero{background:var(--line);height:3px!important}
        .col:hover .bar{filter:brightness(1.15)}
        .tip{position:absolute;left:50%;transform:translateX(-50%);white-space:nowrap;background:var(--cink);color:var(--card);padding:3px 8px;border-radius:6px;font-size:12px;font-weight:600;opacity:0;pointer-events:none;transition:opacity .12s;z-index:2}
        .col:nth-child(-n+3) .tip{left:0;transform:none}
        .col:nth-last-child(-n+3) .tip{left:auto;right:0;transform:none}
        .col:hover .tip{opacity:1}
        .axis{display:flex;justify-content:space-between;gap:8px;color:var(--cmuted);font-size:12px;margin-top:8px;border-top:1px dashed var(--line);padding-top:6px}
        .teamtoggle{display:inline-flex;align-items:center;gap:10px;min-height:36px;font-size:14px;color:var(--cink);cursor:pointer;user-select:none}
        .teamtoggle input{width:18px;height:18px;accent-color:var(--accent);cursor:pointer}
        .team{display:none}
        body.showteam tr.team{display:table-row}
        body.showteam span.team{display:inline}
        tr.team td{opacity:.8}
        .by{white-space:nowrap}
        .by svg{vertical-align:-3px;margin-right:6px}
        footer{font-size:13px;color:var(--muted);border-top:1px dashed var(--accent);padding-top:14px;margin-top:30px}
        .loading{display:flex;gap:16px;align-items:center;background:var(--card);color:var(--cink);border:2px solid var(--accent);border-radius:12px;padding:16px 18px;margin-bottom:18px}
        .loading .muted{color:var(--cmuted)}
        .spin{width:30px;height:30px;flex:none;border:3px solid var(--line);border-top-color:var(--accent2);border-radius:50%;animation:spin .8s linear infinite}
        @keyframes spin{to{transform:rotate(360deg)}}
        .loading .grow{flex:1;min-width:0}
        .pbar{height:8px;background:var(--line);border-radius:4px;overflow:hidden;margin:8px 0 6px}
        .pbar i{display:block;height:100%;background:var(--accent)}
        .oddshead{display:flex;flex-wrap:wrap;justify-content:space-between;align-items:flex-end;gap:12px 32px}
        .oddshead .k{font-size:12px;letter-spacing:.12em;text-transform:uppercase;color:var(--cmuted)}
        .oddshead .pv{font-size:56px;font-weight:700;line-height:1.05;color:var(--gold)}
        .oddshead .uv{font-size:30px;font-weight:700;line-height:1.1}
        .oddshead .l{font-size:13px;color:var(--cmuted)}
        .oddshead .used{text-align:right}
        @media (max-width:820px){.oddshead .used{text-align:left}}
        .tokenbar{height:22px;background:var(--nobar);border-radius:6px;overflow:hidden;margin:14px 0 6px}
        .tokenbar i{display:block;height:100%;background:var(--gold)}
        .legend{display:flex;justify-content:space-between;gap:12px;font-size:13px;color:var(--cmuted)}
        .legend b{color:var(--cink)}
        .facts td:first-child{color:var(--cmuted);white-space:nowrap;width:1%;font-size:13px}
        .ok{color:var(--ok);font-weight:600}.no{color:var(--warn);font-weight:600}
        .note{font-size:13px;color:var(--cmuted);padding:10px 0 2px}
        .chip{display:inline-block;font-size:12px;padding:2px 8px;border-radius:6px;background:var(--track);color:var(--title);margin:1px 6px 1px 0;white-space:nowrap}
        .chip.x{color:var(--accent2)}
        svg.pline{display:block;width:100%;height:auto}
        svg.pline text{fill:var(--cmuted);font-size:11px;font-family:Bahnschrift,"Segoe UI",sans-serif}
        .stat3{display:grid;grid-template-columns:repeat(3,minmax(0,1fr));gap:10px}
        .stat3 .v{font-size:30px;font-weight:700;line-height:1.1}
        .stat3 .l{font-size:13px;color:var(--cmuted)}
        .lm{display:flex;gap:18px;align-items:center}
        .lm .v{font-size:54px;font-weight:700;line-height:1;color:var(--gold)}
        .kv{display:grid;grid-template-columns:auto 1fr;gap:4px 14px;font-size:14px}
        .kv span:nth-child(odd){color:var(--cmuted)}
        .btn{display:inline-flex;align-items:center;justify-content:center;gap:8px;font:600 14px "Segoe UI",system-ui,sans-serif;color:var(--cink);background:var(--bg2);border:1px solid var(--line);border-radius:9px;padding:8px 14px;cursor:pointer;text-decoration:none;white-space:nowrap}
        .btn:hover{border-color:var(--accent)}
        .btn.primary{background:var(--accent);border-color:var(--accent);color:#fff}
        .btn.primary:hover{filter:brightness(1.1)}
        .btn.danger{color:var(--warn);border-color:var(--warn);background:transparent}
        .btn.small{padding:4px 10px;font-size:13px}
        .btn.busy{opacity:.55;pointer-events:none}
        .btns{display:flex;flex-wrap:wrap;gap:10px;align-items:center}
        .steps3{display:grid;grid-template-columns:repeat(3,minmax(0,1fr));gap:14px}
        @media (max-width:860px){.steps3{grid-template-columns:1fr}}
        .step{background:var(--bg2);border:1px solid var(--line);border-radius:10px;padding:14px}
        .step .n{display:inline-flex;width:26px;height:26px;border-radius:50%;align-items:center;justify-content:center;background:var(--accent);color:#fff;font:700 14px Bahnschrift,sans-serif;margin-right:8px}
        .step b{font-size:15px}
        .step p{margin:8px 0 10px;font-size:13px;color:var(--cmuted)}
        .drop{border:2px dashed var(--line);border-radius:10px;padding:12px;text-align:center;font-size:13px;color:var(--cmuted)}
        body.dragging .drop{border-color:var(--accent2)}
        body.dragging::after{content:attr(data-droptext);position:fixed;inset:0;display:flex;align-items:center;justify-content:center;background:rgba(14,11,30,.8);color:#fff;font:700 26px Bahnschrift,sans-serif;z-index:20;border:4px dashed var(--accent2)}
        .hidden-file{position:absolute;width:1px;height:1px;opacity:0;pointer-events:none}
        .compare{display:flex;flex-wrap:wrap;align-items:center;gap:10px 22px}
        .compare .v{font-size:40px;font-weight:700;line-height:1}
        .compare .arrow{font-size:28px;color:var(--cmuted)}
        .delta{font-size:15px;font-weight:700;color:var(--ok)}
        .files{display:grid;grid-template-columns:repeat(2,minmax(0,1fr));gap:18px}
        @media (max-width:860px){.files{grid-template-columns:1fr}}
        .set{display:flex;flex-wrap:wrap;justify-content:space-between;align-items:center;gap:8px 20px;padding:12px 0;border-bottom:1px solid var(--line)}
        .set:last-child{border-bottom:none;padding-bottom:2px}
        .set .t{flex:1 1 340px;min-width:0}
        .set .lbl{font-weight:600}
        .set .desc{font-size:13px;color:var(--cmuted);margin-top:2px}
        .set .ctl{display:flex;gap:8px;align-items:center;flex-wrap:wrap}
        select,input[type=text],input[type=number],input[type=date]{font:inherit;font-size:14px;color:var(--cink);background:var(--field);border:1px solid var(--line);border-radius:8px;padding:7px 10px;max-width:100%}
        input[type=number]{width:130px}
        input[type=text]{width:min(300px,100%)}
        select:focus,input:focus{outline:2px solid var(--accent);outline-offset:0}
        .tog{position:relative;display:inline-block;width:46px;height:26px;flex:none}
        .tog input{position:absolute;opacity:0;width:100%;height:100%;margin:0;cursor:pointer;z-index:1}
        .tog span{position:absolute;inset:0;border-radius:13px;background:var(--nobar);transition:background .15s}
        .tog span::after{content:"";position:absolute;left:3px;top:3px;width:20px;height:20px;border-radius:50%;background:#fff;transition:transform .15s}
        .tog input:checked+span{background:var(--accent)}
        .tog input:checked+span::after{transform:translateX(20px)}
        .tog input:focus-visible+span{outline:2px solid var(--accent2);outline-offset:2px}
        .activity{font:13px/1.6 Consolas,"Cascadia Mono",monospace;color:var(--cmuted);max-height:240px;overflow:auto;white-space:pre-wrap;margin:0}
        .news li{margin:4px 0}
        .help p{margin:0 0 10px}.help p:last-child{margin-bottom:0}
        details{border-top:1px solid var(--line);padding-top:10px;margin-top:10px}
        summary{cursor:pointer;font-weight:600;color:var(--title)}
        code{font:13px Consolas,monospace;background:var(--bg2);padding:1px 5px;border-radius:5px}
        #toast{position:fixed;left:50%;bottom:22px;transform:translate(-50%,24px);opacity:0;pointer-events:none;max-width:min(680px,calc(100vw - 32px));background:var(--card);color:var(--cink);border:1px solid var(--line);border-left:5px solid var(--accent);border-radius:10px;padding:12px 16px;box-shadow:0 10px 30px rgba(0,0,0,.35);transition:all .2s;z-index:40;font-size:14px}
        #toast.show{opacity:1;transform:translate(-50%,0)}
        #toast.good{border-left-color:var(--ok)}#toast.bad{border-left-color:var(--warn)}
        #offline{position:fixed;left:0;right:0;bottom:0;background:#7f1d1d;color:#fff;padding:12px 16px;text-align:center;z-index:50;font-weight:600}
        .wiz{max-width:760px;margin:10px auto 0;padding:26px}
        .wiz h2{margin:0 0 6px;font-size:28px;color:var(--title)}
        .wiz p{color:var(--cmuted);margin:0 0 12px}
        .wiz .stepn{font:700 13px Bahnschrift,sans-serif;letter-spacing:.12em;color:var(--accent2);text-transform:uppercase}
        .wiz .btns{margin-top:18px}
        .langs{display:grid;grid-template-columns:repeat(3,minmax(0,1fr));gap:12px;margin-top:16px}
        @media (max-width:640px){.langs{grid-template-columns:1fr}}
        .langs .btn{padding:16px;font-size:16px}
        .callout{background:var(--bg2);border:1px solid var(--line);border-left:4px solid var(--gold);border-radius:8px;padding:10px 14px;font-size:14px;margin:12px 0}
        """;

    /// <summary>The assistant's mark: a gold token at the heart of a violet and cyan vortex (same design as the icon).</summary>
    private const string Logo = """<svg xmlns="http://www.w3.org/2000/svg" class="logo" width="56" height="56" viewBox="0 0 100 100" aria-hidden="true"><defs><radialGradient id="lgDisc" cx="50%" cy="44%" r="55%"><stop offset="0" stop-color="#4c2a9a"/><stop offset="1" stop-color="#120a2e"/></radialGradient><linearGradient id="lgRim" x1="0" y1="0" x2="1" y2="1"><stop offset="0" stop-color="#22d3ee"/><stop offset="1" stop-color="#8b5cf6"/></linearGradient><radialGradient id="lgGlow"><stop offset="0" stop-color="#fbbf24" stop-opacity=".6"/><stop offset="1" stop-color="#fbbf24" stop-opacity="0"/></radialGradient><radialGradient id="lgCoin" cx="36%" cy="32%" r="75%"><stop offset="0" stop-color="#fff6cf"/><stop offset=".45" stop-color="#f8c32c"/><stop offset="1" stop-color="#a86b12"/></radialGradient><linearGradient id="lgStar" x1="0" y1="0" x2="1" y2="1"><stop offset="0" stop-color="#8b5cf6"/><stop offset="1" stop-color="#3b0f8a"/></linearGradient></defs><circle cx="50" cy="50" r="47" fill="url(#lgDisc)" stroke="url(#lgRim)" stroke-width="4"/><g class="swirl" fill="none" stroke-linecap="round"><path d="M18 62 A34 34 0 0 1 40 17" stroke="#22d3ee" stroke-width="4" opacity=".9"/><path d="M82 38 A34 34 0 0 1 60 83" stroke="#a78bfa" stroke-width="4" opacity=".9"/></g><circle cx="50" cy="50" r="36" fill="url(#lgGlow)"/><circle cx="50" cy="52.5" r="25" fill="#0b0620" opacity=".45"/><circle cx="50" cy="50" r="25" fill="url(#lgCoin)" stroke="#7c4a03" stroke-width="1.2"/><circle cx="50" cy="50" r="20" fill="none" stroke="#fde68a" stroke-width="1" opacity=".8"/><path d="M50 35.5 L53.6 46.4 L64.5 50 L53.6 53.6 L50 64.5 L46.4 53.6 L35.5 50 L46.4 46.4 Z" fill="url(#lgStar)"/><ellipse cx="42" cy="36" rx="9" ry="5" fill="#fff" opacity=".35"/><path class="spark" d="M71 24 L72.4 27.6 L76 29 L72.4 30.4 L71 34 L69.6 30.4 L66 29 L69.6 27.6 Z" fill="#fff"/></svg>""";
    private const string TeamIcon = """<svg width="16" height="16" viewBox="0 0 24 24" aria-hidden="true"><circle cx="9" cy="8" r="3.2" fill="none" stroke="#22d3ee" stroke-width="2"/><path d="M3 20c0-3.5 2.7-6 6-6s6 2.5 6 6" fill="none" stroke="#22d3ee" stroke-width="2"/><circle cx="17" cy="9" r="2.6" fill="none" stroke="#22d3ee" stroke-width="2"/><path d="M16 14c3 0 5 2.2 5 5" fill="none" stroke="#22d3ee" stroke-width="2"/></svg>""";
    private const string FlagIcon = """<svg width="18" height="18" viewBox="0 0 24 24" aria-hidden="true" style="flex:none;margin-top:2px"><path d="M5 21V4M5 4h11l-2 4 2 4H5" fill="none" stroke="#ff8a6a" stroke-width="2" stroke-linejoin="round"/></svg>""";

    private static readonly Dictionary<string, string> KindColor = new()
    {
        [ChestKinds.Normal] = "#fbbf24",
        [ChestKinds.Rare] = "#60a5fa",
        [ChestKinds.Buried] = "#fb923c",
        [ChestKinds.Extraction] = "#22d3ee",
        [ChestKinds.PosterChest] = "#c084fc",
        [ChestKinds.Other] = "#94a3b8",
    };

    // ---- Page --------------------------------------------------------------------------------

    /// <param name="data">The current profile's matches (<see cref="Profiles.View"/>).</param>
    /// <param name="live">Null for report.html (no buttons, no settings).</param>
    public static string Page(Config config, TrackerData data, IReadOnlyList<Profile> profiles, string dataFolder, LiveInfo? live)
    {
        var progress = Progress;
        var matches = data.Matches.OrderByDescending(When).ToList();
        var player = matches.Select(m => m.PlayerName).FirstOrDefault(n => !string.IsNullOrEmpty(n)) ?? "";
        var isLive = live != null;
        var title = isLive ? AppInfo.Name : $"{AppInfo.Name} · {L.T("Rapport", "Report", "Rapport", "Informe")}";
        var refresh = progress != null ? 2 : 15;

        var sb = new StringBuilder(96 * 1024);
        sb.Append($"<!doctype html><html lang=\"{L.Code}\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">");
        if (!isLive)
        {
            sb.Append($"<noscript><meta http-equiv=\"refresh\" content=\"{refresh}\"></noscript>");
        }
        sb.Append($"<title>{H(title)}</title><style>{Style}{ItemsStyle}{MotionStyle}</style></head>");
        sb.Append($"<body data-live=\"{(isLive ? 1 : 0)}\" data-v=\"__V__\" data-refresh=\"{refresh}\" data-droptext=\"{H(L.T("Dépose le fichier de ton coéquipier", "Drop your teammate's file", "7ett l'fichier dyal sa7bek hna", "Suelta el archivo de tu compañero"))}\""
            + $" data-off=\"{H(OfflineText)}\" data-copied=\"{H(L.T("Image copiée : colle-la dans Discord (Ctrl+V).", "Image copied: paste it in Discord (Ctrl+V).", "Tswira tcopiat: 7ettha f Discord (Ctrl+V).", "Imagen copiada: pégala en Discord (Ctrl+V)."))}\""
            + $" data-saved=\"{H(L.T("Image enregistrée dans tes Téléchargements.", "Image saved in your Downloads.", "Tswira tsejjlat f Téléchargements.", "Imagen guardada en tus Descargas."))}\"><div id=\"app\">");
        sb.Append(Header(player, live, progress, OddsModel.ActiveHour(config.Odds, DateTime.UtcNow)));

        if (live?.Setup is { } setup)
        {
            sb.Append(SetupPanel(setup));
        }
        else
        {
            sb.Append(Nav(isLive));
            sb.Append(LoadingCard(progress));
            if (live?.Notice is { } notice)
            {
                sb.Append($"<div id=\"notice\" hidden data-id=\"{notice.Id}\" data-ok=\"{(notice.Ok ? 1 : 0)}\" data-tab=\"{H(notice.Tab)}\">{H(notice.Text)}</div>");
            }
            var odds = OddsModel.Build(data, config);
            var index = new ImportIndex(Team.Imports);
            var items = ItemsModel.Build(data, config, ItemsModel.Checklist);
            sb.Append(HomePanel(config, data, matches, profiles, odds, index, live, DiscordCard(player, matches, odds, items, data, config)));
            sb.Append(TokenPanel(odds, isLive));
            sb.Append(ItemsPanel(items, config, isLive, isLive ? "img/" : "items/"));
            sb.Append(SquadPanel(data, config, matches, index, isLive));
            sb.Append(FilesPanel(data, config, isLive));
            sb.Append(MatchesPanel(matches, index));
            if (live != null)
            {
                sb.Append(SettingsPanel(config, data, profiles, live));
            }
            sb.Append(HelpPanel(config, dataFolder, live));
        }
        sb.Append($"<footer>{H(AppInfo.Name)} {AppInfo.VersionText} · by <span class=\"sig\">{H(AppInfo.Author)}</span> · "
            + L.T("basé sur Chest Counter de LED-N · ne va jamais sur Internet", "based on Chest Counter by LED-N · never goes online", "mbni 3la Chest Counter dyal LED-N · 3emmro ma kaydkhol l Internet", "basado en Chest Counter de LED-N · nunca se conecta a Internet")
            + "</footer></div>");
        sb.Append("<div id=\"toast\" role=\"status\" aria-live=\"polite\"></div>");
        if (isLive)
        {
            sb.Append($"<div id=\"offline\" hidden>{H(OfflineText)}</div>");
        }
        sb.Append(Script).Append("</body></html>");
        return sb.ToString();
    }

    private static string OfflineText => L.T("L'assistant est fermé : relance ChestTokenAssistant.exe pour retrouver ton tableau de bord.",
        "The assistant is closed: start ChestTokenAssistant.exe again to get your dashboard back.",
        "L'assistant msdoud: 3awed 7ell ChestTokenAssistant.exe bach yrje3 tableau de bord dyalek.", "El asistente está cerrado: vuelve a abrir ChestTokenAssistant.exe para recuperar tu panel.");

    private static string Header(string player, LiveInfo? live, ScanProgress? progress, TreasureHour? hour)
    {
        var hourChip = hour != null
            ? $"<span class=\"hourchip\">★ {H(L.T("Heure du trésor", "Treasure Hour", "Sa3at l'kenz", "Hora del tesoro"))} ×{hour.PirateShop.ToString("0.##", Fr)} → {OddsModel.Utc(hour.EndUtc).ToLocalTime():HH:mm}</span>"
            : "";
        var who = $"<span class=\"sig\">by {H(AppInfo.Author)}</span>"
            + (player.Length > 0 && !string.Equals(player, AppInfo.Author, StringComparison.OrdinalIgnoreCase) ? $" · {L.T("profil", "profile", "profil", "perfil")} <b>{H(player)}</b>" : "");
        string right;
        if (live == null)
        {
            right = $"<span class=\"muted upd\">{L.T($"Rapport du {DateTime.Now.ToString("dd/MM/yyyy à HH:mm", Fr)} · actualisation auto", $"Report of {DateTime.Now.ToString("d MMM yyyy 'at' HH:mm", Fr)} · auto refresh", $"Rapport dyal {DateTime.Now.ToString("dd/MM/yyyy 'm3a' HH:mm", Fr)} · katbeddel bo7dha", $"Informe del {DateTime.Now.ToString("dd/MM/yyyy 'a las' HH:mm", Fr)} · actualización automática")}</span>";
        }
        else if (live.Setup != null)
        {
            right = "";
        }
        else
        {
            var kind = progress != null ? "scan" : live.Status.Kind;
            var text = progress != null
                ? L.T($"Analyse de tes replays… {progress.Done} / {progress.Total}", $"Analyzing your replays… {progress.Done} / {progress.Total}", $"Kan'analysi les replays dyalek… {progress.Done} / {progress.Total}", $"Analizando tus repeticiones… {progress.Done} / {progress.Total}")
                : live.Status.Text;
            var keep = live.Status.Kind == "close" ? $" <button type=\"button\" class=\"btn small\" data-a=\"keep-open\">{L.T("Garder ouvert", "Keep it open", "Khellih m7loul", "Mantener abierto")}</button>" : "";
            right = $"<div class=\"status {kind}\"><span class=\"dot\"></span><span>{H(text)}</span>{keep}</div>";
        }
        return $"<header class=\"top\"><div class=\"brand\">{Logo}<div><h1 class=\"ttl\">{H(AppInfo.Name)}</h1><div class=\"who\">{who}</div></div></div><div class=\"btns\">{hourChip}{right}</div></header>";
    }

    private static readonly string[] AllTabs = { "home", "token", "items", "squad", "files", "matches", "settings", "help" };

    private static string TabName(string id) => id switch
    {
        "home" => L.T("Accueil", "Home", "Dar", "Inicio"),
        "token" => L.T("Jeton", "Token", "Jeton", "Ficha"),
        "items" => L.T("Objets", "Items", "L7wayej", "Objetos"),
        "squad" => L.T("Escouade", "Squad", "Squad", "Escuadrón"),
        "files" => L.T("Fichiers", "Files", "Fichiyat", "Archivos"),
        "matches" => L.T("Parties", "Matches", "Partiyat", "Partidas"),
        "settings" => L.T("Réglages", "Settings", "Réglages", "Ajustes"),
        _ => L.T("Aide", "Help", "3awni", "Ayuda"),
    };

    private static string TabIntro(string id) => id switch
    {
        "home" => L.T("Tes coffres en un coup d'œil.", "Your chests at a glance.", "Snadeq dyalek f nadra wa7da.", "Tus cofres de un vistazo."),
        "token" => L.T("Ta chance d'avoir le jeton de la boutique pirate.", "Your chance of the Pirate Shop token.", "Zher dyalek bach tjib l'jeton dyal l7anout dyal l9ersan.", "Tu probabilidad de conseguir la ficha de la tienda pirata."),
        "items" => L.T("Ce que tu peux gagner dans les coffres, et ce que tu as déjà.", "What you can win from the chests, and what you already got.", "Chno t9der trbe7 mn snadeq, w chno déjà 3ndek.", "Lo que puedes ganar en los cofres, y lo que ya tienes."),
        "squad" => L.T("Les totaux exacts de ton équipe, grâce aux fichiers de tes amis.", "Exact team totals with your friends' files.", "L7sab exact dyal l'équipe, b les fichiers dyal s7abek.", "Los totales exactos de tu equipo, gracias a los archivos de tus amigos."),
        "files" => L.T("Chaque fichier que tu as importé, et ce qu'il change.", "Every file you imported, and what it changes.", "Kol fichier importiti, w chno kaybeddel.", "Cada archivo que importaste, y lo que cambia."),
        "matches" => L.T("Chaque partie comptée.", "Every match counted.", "Kol partiya t7sbat.", "Cada partida contada."),
        "settings" => L.T("Change le fonctionnement de l'assistant.", "Change how the assistant works.", "Beddel kifach khdam l'assistant.", "Cambia cómo funciona el asistente."),
        _ => L.T("Comment ça marche et où sont tes données.", "How it works and where your data is.", "Kifach khdam w fin kaynin les données dyalek.", "Cómo funciona y dónde están tus datos."),
    };

    private static string Nav(bool live) =>
        "<nav class=\"tabs\" role=\"tablist\">"
        + string.Concat(AllTabs.Where(t => live || t != "settings").Select(t => $"<button type=\"button\" role=\"tab\" data-tab=\"{t}\">{H(TabName(t))}</button>"))
        + "</nav>";

    private static string Panel(string id, string body) =>
        $"<section class=\"panel\" data-panel=\"{id}\" role=\"tabpanel\"><div class=\"intro\"><h2 class=\"ttl\">{H(TabName(id))}</h2><p>{H(TabIntro(id))}</p></div>{body}</section>";

    /// <summary>A part of a tab: its title, one short line saying what it shows, then its content.</summary>
    /// <summary>A whole number that counts up to its new value when it changes (data-count, keyed for the page's refreshes).</summary>
    private static string Counter(string key, int value) =>
        $"<span data-key=\"{key}\" data-count=\"{value.ToString(CultureInfo.InvariantCulture)}\">{value.ToString("N0", Fr)}</span>";

    private static string Box(string title, string? sub, string body, string? right = null, string? cls = null) =>
        $"<div class=\"box{(cls != null ? " " + cls : "")}\"><div class=\"bh\"><div><h3 class=\"ttl\">{title}</h3>{(sub != null ? $"<p class=\"sub\">{sub}</p>" : "")}</div>{right}</div>{body}</div>";

    /// <param name="from">A field whose value is sent (data-from).</param>
    /// <param name="form">A group of named fields sent together as JSON (data-form).</param>
    private static string Button(string action, string label, string? key = null, string? value = null, string cls = "", string? confirm = null, string? from = null, string? form = null) =>
        $"<button type=\"button\" class=\"btn {cls}\" data-a=\"{H(action)}\"{(key != null ? $" data-k=\"{H(key)}\"" : "")}{(value != null ? $" data-v=\"{H(value)}\"" : "")}"
        + $"{(confirm != null ? $" data-confirm=\"{H(confirm)}\"" : "")}{(from != null ? $" data-from=\"{H(from)}\"" : "")}{(form != null ? $" data-form=\"{H(form)}\"" : "")}>{H(label)}</button>";

    /// <summary>"Choose a file" + the drop area (the whole window accepts a dropped file too).</summary>
    private static string ImportControl(string id) =>
        $"<div class=\"drop\"><input type=\"file\" id=\"{id}\" class=\"hidden-file\" data-import accept=\".chesttoken,.json,.csv\" multiple>"
        + $"<div class=\"btns\" style=\"justify-content:center\"><button type=\"button\" class=\"btn primary\" data-pick=\"{id}\">{L.T("Choisir un fichier…", "Choose a file…", "Khtar fichier…", "Elegir un archivo…")}</button></div>"
        + $"<div style=\"margin-top:6px\">{L.T("ou glisse-le n'importe où sur cette fenêtre", "or drop it anywhere on this window", "wla jerro l ay blassa f had la fenêtre", "o suéltalo en cualquier parte de esta ventana")}</div></div>";

    // ---- Home --------------------------------------------------------------------------------

    private static string HomePanel(Config config, TrackerData data, List<MatchRecord> matches, IReadOnlyList<Profile> profiles, OddsResult odds, ImportIndex index, LiveInfo? live, string discord)
    {
        var sb = new StringBuilder();
        var total = matches.Sum(m => m.ChestsOpened);
        var avg = matches.Count > 0 ? (double)total / matches.Count : 0;
        var best = matches.Count > 0 ? matches.Max(m => m.ChestsOpened) : 0;
        var treasures = matches.Sum(m => m.TreasureFinds.Count);
        var teamTreasures = matches.Sum(m => m.TeamTreasureFinds.Count);
        var sessionMatches = matches.Where(IsThisSession).ToList();
        var session = sessionMatches.Sum(m => m.ChestsOpened);
        // Own treasures only: the team's are found with chests that are not counted here.
        var every = treasures > 0 ? (int)Math.Round((double)total / treasures) : 0;

        if (live is { AskProfile: true } && profiles.Count >= 2)
        {
            sb.Append(Box(L.T("Sur quel compte joues-tu ?", "Which account are you playing on?", "B ana compte kat'joui?", "¿Con qué cuenta juegas?"),
                L.T("Plusieurs comptes Fortnite ont enregistré des replays sur ce PC. Chacun garde ses propres statistiques.",
                    "Several Fortnite accounts recorded replays on this PC. Each one keeps its own statistics.",
                    "Bzaf d les comptes Fortnite sejjlou replays f had PC. Kol wa7ed kay7tafed b les stats dyalo.", "Varias cuentas de Fortnite grabaron repeticiones en este PC. Cada una conserva sus propias estadísticas."),
                $"<div class=\"btns\">{ProfileSelect(config, data, profiles)}</div>", cls: "callout"));
        }

        if (live?.WhatsNew is { Count: > 0 } news)
        {
            sb.Append(Box(L.T($"Nouveautés de la {AppInfo.VersionText}", $"What's new in {AppInfo.VersionText}", $"Jdid dyal {AppInfo.VersionText}", $"Novedades de la {AppInfo.VersionText}"),
                L.T("Ce que cette version apporte.", "What this version brings.", "Chno jabet had la version.", "Lo que trae esta versión."),
                "<ul class=\"news\">" + string.Concat(news.Select(n => $"<li>{H(n)}</li>")) + "</ul>",
                Button("whatsnew-ok", L.T("OK, compris", "OK, got it", "OK, fhemt", "OK, entendido"), cls: "small")));
        }

        string Tile(string value, string label, string? sub = null, string cls = "", string? tab = null) =>
            tab != null
                ? $"<button type=\"button\" class=\"tile go\" data-tab=\"{tab}\"><span class=\"v num {cls}\">{value}</span><span class=\"l\">{label}</span>{(sub != null ? $"<span class=\"s\">{sub}</span>" : "")}</button>"
                : $"<div class=\"tile\"><div class=\"v num {cls}\">{value}</div><div class=\"l\">{label}</div>{(sub != null ? $"<div class=\"s\">{sub}</div>" : "")}</div>";
        sb.Append("<div class=\"tiles\">");
        sb.Append($"<div class=\"tile hero\"><div class=\"k\">{L.T("Coffres au total", "Chests in total", "Snadeq f total", "Cofres en total")}</div><div class=\"v num\">{Counter("total", total)}</div>"
            + $"<div class=\"l\">{L.T($"ouverts ou détruits par toi · {session.ToString("N0", Fr)} depuis le lancement", $"opened or destroyed by you · {session.ToString("N0", Fr)} since the assistant started", $"7elltihom wla kssertihom nta · {session.ToString("N0", Fr)} men mlli t7ell", $"abiertos o destruidos por ti · {session.ToString("N0", Fr)} desde que se inició el asistente")}</div></div>");
        sb.Append(Tile(H(L.Pct(odds.Pirate.P)), L.T("chance du jeton", "token chance", "zher dyal l'jeton", "probabilidad de la ficha"), L.T("voir l'onglet Jeton →", "see the Token tab →", "chouf l'onglet Jeton →", "ver la pestaña Ficha →"), "gold", "token"));
        sb.Append(Tile(Counter("matches", matches.Count), L.T("parties analysées", "matches analyzed", "partiyat m'analysyin", "partidas analizadas"), null, "", "matches"));
        sb.Append(Tile(avg.ToString("0.0", Fr), L.T("coffres par partie", "chests per match", "snadeq f partiya", "cofres por partida"), L.T($"record : {best}", $"best: {best}", $"record: {best}", $"récord: {best}")));
        sb.Append(Tile(Counter("treasures", treasures), L.T("trésors trouvés", "treasures found", "knouz l9iti", "tesoros encontrados"),
            (every > 0 ? L.T($"1 tous les {every.ToString("N0", Fr)} coffres", $"1 every {every.ToString("N0", Fr)} chests", $"1 f kol {every.ToString("N0", Fr)} sndouq", $"1 cada {every.ToString("N0", Fr)} cofres") : null)
            + (teamTreasures > 0 ? (every > 0 ? "<br>" : "") + L.T($"+{teamTreasures} par l'équipe (estimation)", $"+{teamTreasures} by the team (estimate)", $"+{teamTreasures} dyal l'équipe (ta9dir)", $"+{teamTreasures} del equipo (estimación)") : "")));
        sb.Append("</div>");
        sb.Append($"<div class=\"btns\"><button type=\"button\" class=\"btn primary\" data-card=\"{H(discord)}\" data-card-do=\"copy\">{L.T("Copier l'image pour Discord", "Copy image for Discord", "Copier tswira l Discord", "Copiar la imagen para Discord")}</button>"
            + $"<button type=\"button\" class=\"btn\" data-card=\"{H(discord)}\" data-card-do=\"save\">{L.T("Enregistrer l'image", "Save image", "Sejjel tswira", "Guardar la imagen")}</button>"
            + $"<span class=\"items\">{L.T("Une carte avec tes coffres, ta chance du jeton et tes objets : colle-la dans Discord (Ctrl+V).", "A card with your chests, token chance and items: paste it in Discord (Ctrl+V).", "Carte fiha snadeq, zher dyal l'jeton w l7wayej: 7ettha f Discord (Ctrl+V).", "Una tarjeta con tus cofres, tu probabilidad de la ficha y tus objetos: pégala en Discord (Ctrl+V).")}</span></div>");

        // Today's goal (set in the settings).
        if (config.DailyGoal > 0)
        {
            var todayChests = matches.Where(m => When(m).Date == DateTime.Now.Date).Sum(m => m.ChestsOpened);
            var done = todayChests >= config.DailyGoal;
            var goalPct = Math.Min(100, 100.0 * todayChests / config.DailyGoal);
            sb.Append(Box(L.T("Objectif du jour", "Today's goal", "L'objectif dyal lyouma", "Objetivo del día"),
                L.T("Les coffres que tu veux ouvrir aujourd'hui (à changer dans les réglages).", "The chests you want to open today (change it in the settings).", "Snadeq li bghiti t7ell lyouma (katbeddel f les réglages).", "Los cofres que quieres abrir hoy (cámbialo en los ajustes)."),
                $"<div class=\"goal\"><span class=\"v num {(done ? "gold" : "")}\">{Counter("goal", todayChests)}</span> <span class=\"muted\">/ {config.DailyGoal.ToString("N0", Fr)}</span></div>"
                + $"<div class=\"progress\"><i data-bar=\"goal\" style=\"width:{goalPct.ToString("0.#", CultureInfo.InvariantCulture)}%\"></i></div>"
                + $"<div class=\"items\">{(done ? L.T("Objectif atteint, bien joué !", "Goal reached, well played!", "Wselti l'objectif, sa7a!", "¡Objetivo cumplido, bien jugado!") : L.T($"Encore {(config.DailyGoal - todayChests).ToString("N0", Fr)} coffres.", $"{(config.DailyGoal - todayChests).ToString("N0", Fr)} chests to go.", $"Ba9i {(config.DailyGoal - todayChests).ToString("N0", Fr)} sndouq.", $"Faltan {(config.DailyGoal - todayChests).ToString("N0", Fr)} cofres."))}</div>"));
        }

        // Last match | this session.
        sb.Append("<div class=\"grid2\">");
        if (matches.FirstOrDefault() is { } last)
        {
            var treasure = last.TreasureFinds.Count > 0 ? string.Join(", ", last.TreasureFinds.Select(f => H(f.DisplayName))) : "—";
            sb.Append(Box(L.T("Dernière partie", "Last match", "Partiya lekhra", "Última partida"), L.T("Ta partie la plus récente.", "Your most recent match.", "Akhir partiya l3bti.", "Tu partida más reciente."),
                $"<div class=\"lm\"><div><div class=\"v num\">{last.ChestsOpened}</div><div class=\"items\">{L.T("coffres", "chests", "snadeq", "cofres")}</div></div><div class=\"kv\">"
                + $"<span>{L.T("Quand", "When", "Imta", "Cuándo")}</span><span>{H(L.DateTimeShort(When(last)))}</span>"
                + $"<span>{L.T("Mode", "Mode", "Mode", "Modo")}</span><span>{H(Pretty.Playlist(last.Playlist))}</span>"
                + $"<span>{L.T("Place", "Placement", "Blassa", "Puesto")}</span><span>{H(Pretty.Placement(last))}</span>"
                + $"<span>{L.T("Équipe", "Team", "L'équipe", "Equipo")}</span><span>{TeamCell(last, index)}</span>"
                + $"<span>{L.T("Trésor", "Treasure", "Kenz", "Tesoro")}</span><span>{treasure}</span></div></div>"));
        }
        else
        {
            sb.Append(Box(L.T("Dernière partie", "Last match", "Partiya lekhra", "Última partida"), L.T("Ta partie la plus récente.", "Your most recent match.", "Akhir partiya l3bti.", "Tu partida más reciente."),
                $"<p class=\"items\">{L.T("Aucune partie pour l'instant. Joue une partie avec les replays activés : elle apparaît ici quelques secondes après la fin.", "No match yet. Play a match with replays turned on: it shows up here a few seconds after it ends.", "Mazal 7ta partiya. L3eb partiya w les replays m7loulin: katban hna chi chwiya men b3d ma tsali.", "Aún no hay partidas. Juega una partida con las repeticiones activadas: aparece aquí unos segundos después de terminar.")}</p>"));
        }
        var sessionTreasures = sessionMatches.Sum(m => m.TreasureFinds.Count);
        sb.Append(Box(L.T("Cette session", "This session", "Had la session", "Esta sesión"), L.T("Depuis que l'assistant a été lancé.", "Since the assistant was started.", "Men mlli t7ell l'assistant.", "Desde que se inició el asistente."),
            $"<div class=\"stat3\"><div><div class=\"v num gold\">{session.ToString("N0", Fr)}</div><div class=\"l\">{L.T("coffres", "chests", "snadeq", "cofres")}</div></div>"
            + $"<div><div class=\"v num\">{sessionMatches.Count}</div><div class=\"l\">{L.T("parties", "matches", "partiyat", "partidas")}</div></div>"
            + $"<div><div class=\"v num\">{sessionTreasures}</div><div class=\"l\">{L.T("trésors", "treasures", "knouz", "tesoros")}</div></div></div>"));
        sb.Append("</div>");

        // The overlay's buttons (the same as its keyboard shortcuts).
        if (live is { OverlayRunning: true })
        {
            sb.Append(Box(L.T("Overlay en jeu", "In-game overlay", "Overlay f le jeu", "Overlay en el juego"),
                L.T("Tes coffres par-dessus le jeu. Raccourcis, même en jeu : Ctrl+Maj+O afficher / masquer, Ctrl+Maj+L verrouiller.",
                    "Your chests over the game. Shortcuts, even in game: Ctrl+Shift+O show / hide, Ctrl+Shift+L lock.",
                    "Snadeq dyalek fo9 le jeu. Raccourcis, 7ta f le jeu: Ctrl+Shift+O werri / khebbi, Ctrl+Shift+L bloqui.", "Tus cofres sobre el juego. Atajos, incluso en el juego: Ctrl+Shift+O mostrar / ocultar, Ctrl+Shift+L bloquear."),
                "<div class=\"btns\">"
                + Button("overlay-toggle", live.OverlayVisible ? L.T("Masquer l'overlay", "Hide the overlay", "Khebbi l'overlay", "Ocultar el overlay") : L.T("Afficher l'overlay", "Show the overlay", "Werri l'overlay", "Mostrar el overlay"))
                + Button("overlay-lock", live.OverlayLocked ? L.T("Déverrouiller (pour le déplacer)", "Unlock (to move it)", "7ello (bach t7errko)", "Desbloquear (para moverlo)") : L.T("Verrouiller (la souris passe au travers)", "Lock (the mouse goes through)", "Bloqui (la souris katdouz)", "Bloquear (el ratón lo atraviesa)"))
                + "</div>"));
        }

        // Last 30 days, one bar per day (the best day stands out).
        var today = DateTime.Now.Date;
        var days = Enumerable.Range(0, 30).Select(i => today.AddDays(i - 29)).ToList();
        var perDay = days.Select(d => matches.Where(m => When(m).Date == d).Sum(m => m.ChestsOpened)).ToList();
        var max = Math.Max(1, perDay.Max());
        var chart = new StringBuilder("<div class=\"bars\">");
        for (var i = 0; i < days.Count; i++)
        {
            var v = perDay[i];
            var h = (int)Math.Round(100.0 * v / max);
            var tip = $"{days[i].ToString(L.T("ddd dd/MM", "ddd d MMM", "ddd dd/MM", "ddd d MMM"), Fr)}{L.T(" : ", ": ", ": ", ": ")}{L.Count(v, "coffre", "chest", "sndouq", "cofre")}";
            var cls = v == 0 ? " zero" : v == max ? " top" : "";
            chart.Append($"<div class=\"col\" aria-label=\"{H(tip)}\"><span class=\"tip\" style=\"bottom:calc({Math.Max(h, 3)}% + 6px)\">{H(tip)}</span><div class=\"bar{cls}\" style=\"height:{h}%\"></div></div>");
        }
        var bestDay = perDay.Max();
        var bestDate = days[perDay.IndexOf(bestDay)];
        chart.Append($"</div><div class=\"axis\"><span>{days[0].ToString(L.T("dd/MM", "d MMM", "dd/MM", "d MMM"), Fr)}</span>"
            + (bestDay > 0 ? $"<span>{L.T("meilleur jour", "best day", "a7ssen nhar", "mejor día")}{L.T(" : ", ": ", ": ", ": ")}{bestDate.ToString(L.T("dd/MM", "d MMM", "dd/MM", "d MMM"), Fr)} · {L.Count(bestDay, "coffre", "chest", "sndouq", "cofre")}</span>" : "")
            + $"<span>{L.T("aujourd'hui", "today", "lyouma", "hoy")}</span></div>");
        sb.Append(Box(L.T("Coffres par jour", "Chests per day", "Snadeq f nhar", "Cofres por día"),
            L.T("Les 30 derniers jours. Passe la souris sur une barre pour le détail.", "The last 30 days. Hover a bar for the details.", "30 nhar lekhrin. Douz b la souris 3la chi barre bach tchouf tafasil.", "Los últimos 30 días. Pasa el ratón sobre una barra para ver el detalle."), chart.ToString()));

        if (live != null)
        {
            var lines = live.Activity.Count > 0 ? string.Join("\n", live.Activity.Select(H)) : H(L.T("Rien pour l'instant.", "Nothing yet.", "Walou l daba.", "Nada por ahora."));
            sb.Append(Box(L.T("Activité", "Activity", "Chno w9e3", "Actividad"), L.T("Ce que l'assistant a fait dernièrement.", "What the assistant did lately.", "Chno dar l'assistant mo2akhiran.", "Lo que el asistente ha hecho últimamente."), $"<pre class=\"activity\">{lines}</pre>"));
        }
        else if (profiles.Count >= 2)
        {
            var others = profiles.Where(p => p.Key != Profiles.Current(data, config)?.Key).Select(p => H(p.Name));
            sb.Append($"<p class=\"items\">{L.T("Autres profils sur ce PC : ", "Other profiles on this PC: ", "Profils khrin f had PC: ", "Otros perfiles en este PC: ")}{string.Join(", ", others)}</p>");
        }
        return Panel("home", sb.ToString());
    }

    private static string ProfileSelect(Config config, TrackerData data, IReadOnlyList<Profile> profiles)
    {
        var current = profiles.FirstOrDefault(p => p.Key == config.Profile)?.Key ?? profiles.FirstOrDefault()?.Key;
        return "<select data-set=\"profile\">" + string.Concat(profiles.Select(p => $"<option value=\"{H(p.Key)}\"{(p.Key == current ? " selected" : "")}>{H(Profiles.Describe(p))}</option>")) + "</select>";
    }

    // ---- Token -------------------------------------------------------------------------------

    /// <summary>What each choice of "chests counted" means, in one sentence.</summary>
    internal static string ScopeHelp(string scope) => scope switch
    {
        "mine" => L.T("Seulement les coffres que tu as ouverts ou détruits toi-même.", "Only the chests you opened or destroyed yourself.", "Ghir snadeq li 7elliti wla kssserti b yeddik.", "Solo los cofres que abriste o destruiste tú mismo."),
        "estimated" => L.T("Les tiens + ceux des coéquipiers vus dans ton replay + une estimation de ceux hors de ta vue.", "Yours + your teammates' seen in your replay + an estimate of those out of your sight.", "Dyalek + dyal s7abek li chefti f le replay dyalek + ta9dir dyal li kano b3ad 3lik.", "Los tuyos + los de tus compañeros vistos en tu repetición + una estimación de los que no ves."),
        "squad" => L.T("Seulement les parties jouées avec un coéquipier importé, chacun avec son compte exact.", "Only the matches played with an imported teammate, everyone with their exact count.", "Ghir les partiyat li l3bti m3a sa7bek importé, kol wa7ed b l7sab dyalo exact.", "Solo las partidas jugadas con un compañero importado, cada uno con su recuento exacto."),
        "imported" => L.T("Les tiens + les comptes exacts des coéquipiers importés. Rien d'approximatif.", "Yours + the exact counts of imported teammates. Nothing approximate.", "Dyalek + l7sab exact dyal s7abek importés. Walou approximatif.", "Los tuyos + los recuentos exactos de los compañeros importados. Nada aproximado."),
        _ => L.T("Le jeton est partagé avec l'équipe : tes coffres + ceux de tes coéquipiers vus dans ton replay (un minimum). Un coéquipier importé compte avec son propre total.",
            "The token is shared with the team: your chests + your teammates' seen in your replay (a minimum). An imported teammate counts with their own total.",
            "L'jeton kaytqsem m3a l'équipe: snadeq dyalek + dyal s7abek li chefti f le replay dyalek (minimum). Sa7bek importé kay7seb b total dyalo.", "La ficha se comparte con el equipo: tus cofres + los de tus compañeros vistos en tu repetición (un mínimo). Un compañero importado cuenta con su propio total."),
    };

    private static string ScopeSelect(string scope) =>
        "<select data-set=\"scope\">" + string.Concat(OddsModel.Scopes.Select(s => $"<option value=\"{s}\"{(s == scope ? " selected" : "")}>{H(OddsModel.ScopeName(s))}</option>")) + "</select>";

    private static string TokenPanel(OddsResult r, bool live)
    {
        var sb = new StringBuilder();
        var s = r.Settings;
        var pirate = r.Pirate;
        string N(double v) => H(L.Num(v));
        string EndDate(DateTime utc) => H(OddsModel.Utc(utc).ToLocalTime().ToString(L.T("d MMMM yyyy", "d MMMM yyyy", "d MMMM yyyy", "d 'de' MMMM 'de' yyyy"), Fr));

        // Headline: chance, "1 in N", what the chests counted are made of, the bar (gold = token, grey = no token).
        var parts = new List<string> { $"{N(r.Mine)} {L.T("à toi", "yours", "dyalek", "tuyos")}" };
        if (r.Scope is "team" or "estimated")
        {
            parts.Add($"{N(r.TeamSeen)} {L.T("de l'équipe vus dans ton replay", "team seen in your replay", "dyal l'équipe li chefti f le replay dyalek", "del equipo vistos en tu repetición")}");
        }
        if (r.Scope != "mine" && (r.TeamImported > 0 || r.Scope == "imported"))
        {
            parts.Add($"{N(r.TeamImported)} {L.T("importés (leur compte)", "imported (their count)", "importés (l7sab dyalhom)", "importados (su recuento)")}");
        }
        if (r.Scope == "estimated")
        {
            parts.Add($"≈{N(r.TeamEstimated)} {L.T("estimés hors de ta vue", "estimated out of your sight", "m9ddrin b3ad 3la 3inik", "estimados fuera de tu vista")}");
        }
        if (r.BaselineMine + r.BaselineTeam > 0)
        {
            parts.Add($"{N(r.BaselineMine + r.BaselineTeam)} {L.T("avant le suivi", "before tracking", "9bel l'suivi", "antes del seguimiento")}");
        }
        var head = new StringBuilder();
        head.Append($"<div class=\"oddshead\"><div><div class=\"k\">{H(pirate.Def.Name)}</div><div class=\"pv num\">{H(L.Pct(pirate.P))}</div>"
            + $"<div class=\"l\">{H(L.OneIn(pirate.P))} · {L.T("pas de jeton", "no token", "bla jeton", "sin ficha")} {H(L.Pct(1 - pirate.P))}</div></div>"
            + $"<div class=\"used\"><div class=\"k\">{L.T("Coffres comptés", "Chests counted", "Snadeq m7soubin", "Cofres contados")}</div><div class=\"uv num\">{N(r.Total)}</div><div class=\"l\">{string.Join(" + ", parts)}</div></div></div>");
        var width = pirate.P > 0 ? Math.Max(0.6, pirate.P * 100) : 0;
        head.Append($"<div class=\"tokenbar\" role=\"img\" aria-label=\"{H(L.Pct(pirate.P))}\"><i data-bar=\"token\" style=\"width:{width.ToString("0.###", CultureInfo.InvariantCulture)}%\"></i></div>"
            + $"<div class=\"legend\"><span><span class=\"sw\" style=\"background:var(--gold)\"></span>{L.T("jeton", "token", "jeton", "ficha")} <b class=\"num\">{H(L.Pct(pirate.P))}</b></span>"
            + $"<span><span class=\"sw\" style=\"background:var(--nobar)\"></span>{L.T("pas de jeton", "no token", "bla jeton", "sin ficha")} <b class=\"num\">{H(L.Pct(1 - pirate.P))}</b></span></div>");
        head.Append($"<div class=\"note\"><b>{L.T("Coffres comptés", "Chests counted", "Snadeq m7soubin", "Cofres contados")}{L.T(" : ", ": ", ": ", ": ")}{H(OddsModel.ScopeName(r.Scope))}</b> · {H(ScopeHelp(r.Scope))}</div>");
        var right = live
            ? $"<label class=\"items\">{L.T("Coffres comptés", "Chests counted", "Snadeq m7soubin", "Cofres contados")}<br>{ScopeSelect(r.Scope)}</label>"
            : null;
        sb.Append(Box(L.T("Ta chance d'avoir le jeton", "Your chance of the token", "Zher dyalek f l'jeton", "Tu probabilidad de conseguir la ficha"),
            L.T("La chance que le jeton soit déjà tombé dans au moins un des coffres comptés.", "The chance that the token already dropped in at least one of the chests counted.", "Zher bach l'jeton tla3 f chi sndouq mn snadeq m7soubin.", "La probabilidad de que la ficha ya haya salido en al menos uno de los cofres contados."),
            head.ToString(), right));

        // Every treasure: chance, expected drops (λ = Σ m·p), effective odds per chest, this week for the weekly ones.
        var table = new StringBuilder($"<div class=\"scroll\"><table><tr><th>{L.T("Trésor", "Treasure", "Kenz", "Tesoro")}</th><th style=\"text-align:right\">{L.T("Chance", "Chance", "Zher", "Probabilidad")}</th><th>{L.T("Soit", "That is", "Ya3ni", "Es decir")}</th>"
            + $"<th style=\"text-align:right\">{L.T("Gains attendus", "Expected drops", "Jeton mtsnna", "Obtenciones esperadas")}</th><th>{L.T("Chances par coffre", "Odds per chest", "Zher f kol sndouq", "Probabilidad por cofre")}</th><th style=\"text-align:right\">{L.T("Cette semaine", "This week", "Had ssimana", "Esta semana")}</th></tr>");
        foreach (var t in r.Treasures)
        {
            var published = L.OneIn(1.0 / Math.Max(1, t.Def.Odds(s)));
            var effective = L.OneIn(t.PerChest);
            table.Append($"<tr><td>{H(t.Def.Name)}</td><td class=\"n num big\">{H(L.Pct(t.P))}</td><td class=\"nw num\">{H(L.OneIn(t.P))}</td>"
                + $"<td class=\"n num\">{t.Lambda.ToString("0.000", Fr)}</td><td class=\"nw num items\">{H(effective)}{(effective != published ? $" ({L.T("publié", "published", "li nachrat Epic", "publicada")} {H(published)})" : "")}</td>"
                + $"<td class=\"n num\">{(t.WeekP is double w ? H(L.Pct(w)) : "<span class=\"items\">—</span>")}</td></tr>");
        }
        table.Append("</table></div>");
        sb.Append(Box(L.T("Chaque trésor", "Every treasure", "Kol kenz", "Cada tesoro"),
            L.T("Ta chance pour chaque trésor de la chasse : sur toute la chasse, et cette semaine pour les trésors de la semaine.", "Your chance of each treasure of the hunt: over the whole hunt, and this week for the weekly ones.", "Zher dyalek f kol kenz: f la chasse kamla, w had ssimana l knouz dyal ssimana.", "Tu probabilidad de cada tesoro de la búsqueda: en toda la búsqueda, y esta semana para los semanales."),
            table.ToString()));

        // Targets.
        var plan = new StringBuilder($"<div class=\"scroll\"><table><tr><th>{L.T("Chance", "Chance", "Zher", "Probabilidad")}</th>"
            + $"<th style=\"text-align:right\">{L.T("Coffres", "Chests", "Snadeq", "Cofres")}</th><th style=\"text-align:right\">{L.T("Encore", "To go", "Ba9i", "Faltan")}</th><th style=\"text-align:right\">{L.T("Parties", "Matches", "Partiyat", "Partidas")}</th>"
            + $"<th style=\"text-align:right\">{L.T("Heures", "Hours", "Sa3at", "Horas")}</th><th>{L.T("Avant la fin ?", "Before the end?", "9bel lekher?", "¿Antes del final?")}</th></tr>");
        foreach (var p in r.Plan)
        {
            var reached = p.ToGo <= 0;
            var target = L.Pct(p.Target).Replace(".00", "").Replace(",00", "");
            plan.Append($"<tr><td class=\"num\"><b>{H(target)}</b></td><td class=\"n num\">{N(p.Needed)}</td>"
                + $"<td class=\"n num\">{(reached ? $"<span class=\"ok\">{L.T("atteint", "reached", "wselti", "alcanzado")}</span>" : N(p.ToGo))}</td>"
                + $"<td class=\"n num\">{(reached ? "" : p.Matches is double pm ? "≈ " + N(pm) : "—")}</td><td class=\"n num\">{(reached ? "" : p.Hours is double ph ? "≈ " + N(ph) : "—")}</td>"
                + $"<td class=\"nw\">{(reached ? "" : p.Reachable ? $"<span class=\"ok\">{L.T("oui, à ce rythme", "yes, at this pace", "ah, b had rythme", "sí, a este ritmo")}</span>" : $"<span class=\"no\">{L.T("pas à ce rythme", "not at this pace", "machi b had rythme", "no a este ritmo")}</span>")}</td></tr>");
        }
        plan.Append($"</table></div><div class=\"note\">{L.T("Coffres nécessaires depuis zéro : n = ln(1 − objectif) / ln(1 − p). Les coffres pendant une Heure du trésor comptent pour plus.", "Chests needed from zero: n = ln(1 − target) / ln(1 − p). Chests during a Treasure Hour count for more.", "Snadeq li khasshom mn sfer: n = ln(1 − objectif) / ln(1 − p). Snadeq f Sa3at l'kenz kayt7sbo kter.", "Cofres necesarios desde cero: n = ln(1 − objetivo) / ln(1 − p). Los cofres durante una Hora del tesoro cuentan más.")}</div>");
        sb.Append(Box(L.T("Objectifs", "Targets", "Les objectifs", "Objetivos"),
            L.T("Combien de coffres il faut pour chaque chance, ce qu'il reste, et le temps que ça prend à ton rythme.", "How many chests each chance takes, how many are left, and how long that is at your pace.", "Ch7al mn sndouq khass l kol zher, ch7al ba9i, w ch7al d l'wa9t b rythme dyalek.", "Cuántos cofres hacen falta para cada probabilidad, cuántos faltan y cuánto tiempo es a tu ritmo."),
            plan.ToString()));

        // Pace and luck.
        var facts = new List<(string Label, string Value)>();
        if (r.Matches > 0)
        {
            facts.Add((L.T("Rythme", "Pace", "Rythme", "Ritmo"),
                L.T($"{N(r.MinePerMatch)} coffres à toi par partie", $"{N(r.MinePerMatch)} chests of yours per match", $"{N(r.MinePerMatch)} sndouq dyalek f partiya", $"{N(r.MinePerMatch)} cofres tuyos por partida")
                + (r.TeamMatches > 0 && r.Scope != "mine" ? L.T($" + {N(r.TeamPerTeamMatch)} de l'équipe par partie en équipe", $" + {N(r.TeamPerTeamMatch)} team per team match", $" + {N(r.TeamPerTeamMatch)} dyal l'équipe f partiya f l'équipe", $" + {N(r.TeamPerTeamMatch)} del equipo por partida en equipo") : "")
                + $"<br>{N(r.PerHour)} {L.T("par heure de jeu", "per hour played", "f sa3a d l'3ab", "por hora jugada")} ({N(r.HoursPlayed)} h) · {N(r.PerDay)} {L.T("par jour", "per day", "f nhar", "por día")} "
                + L.T($"({N(Math.Round(r.PaceDays))} derniers jours)", $"(last {N(Math.Round(r.PaceDays))} days)", $"(f {N(Math.Round(r.PaceDays))} iyam lekhrin)", $"(últimos {N(Math.Round(r.PaceDays))} días)")));
        }
        facts.Add((L.T("Fin de la chasse", "End of the hunt", "Lekher dyal la chasse", "Fin de la búsqueda"), r.DaysLeft > 0
            ? L.T($"{EndDate(s.HuntEndUtc)} : encore {N(Math.Ceiling(r.DaysLeft))} jours. À ce rythme, +{N(r.Projected)} coffres → <b>{H(L.Pct(pirate.PEnd))}</b> d'ici là. Jeton à activer avant le {EndDate(s.ActivateByUtc)}.",
                $"{EndDate(s.HuntEndUtc)}: {N(Math.Ceiling(r.DaysLeft))} days left. At this pace, +{N(r.Projected)} chests → <b>{H(L.Pct(pirate.PEnd))}</b> by then. Activate the token by {EndDate(s.ActivateByUtc)}.",
                $"{EndDate(s.HuntEndUtc)}: ba9i {N(Math.Ceiling(r.DaysLeft))} iyam. B had rythme, +{N(r.Projected)} sndouq → <b>{H(L.Pct(pirate.PEnd))}</b> f dak nhar. Activi l'jeton 9bel {EndDate(s.ActivateByUtc)}.", $"{EndDate(s.HuntEndUtc)}: faltan {N(Math.Ceiling(r.DaysLeft))} días. A este ritmo, +{N(r.Projected)} cofres → <b>{H(L.Pct(pirate.PEnd))}</b> para entonces. Activa la ficha antes del {EndDate(s.ActivateByUtc)}.")
            : L.T($"terminée ({EndDate(s.HuntEndUtc)})", $"over ({EndDate(s.HuntEndUtc)})", $"salat ({EndDate(s.HuntEndUtc)})", $"terminada ({EndDate(s.HuntEndUtc)})")));
        if (r.SoloPerHour > 0 && r.SquadPerHour > 0)
        {
            var ratio = (r.SquadPerHour / r.SoloPerHour).ToString("0.0", Fr);
            facts.Add((L.T("Solo / équipe", "Solo / squad", "Solo / squad", "Solo / escuadrón"),
                L.T($"{H(L.Pct(r.PSoloHour))} par heure seul{(r.SoloFromSoloMatches ? "" : " (tes coffres seulement)")} · <b>{H(L.Pct(r.PSquadHour))}</b> par heure avec ton équipe (×{ratio})",
                    $"{H(L.Pct(r.PSoloHour))} per hour solo{(r.SoloFromSoloMatches ? "" : " (your own chests only)")} · <b>{H(L.Pct(r.PSquadHour))}</b> per hour with your squad (×{ratio})",
                    $"{H(L.Pct(r.PSoloHour))} f sa3a bou7dek{(r.SoloFromSoloMatches ? "" : " (ghir snadeq dyalek)")} · <b>{H(L.Pct(r.PSquadHour))}</b> f sa3a m3a l'équipe (×{ratio})", $"{H(L.Pct(r.PSoloHour))} por hora solo{(r.SoloFromSoloMatches ? "" : " (solo tus cofres)")} · <b>{H(L.Pct(r.PSquadHour))}</b> por hora con tu escuadrón (×{ratio})")));
        }
        facts.Add((L.T("Chance", "Luck", "Zher", "Suerte"), L.T($"{H(L.Pct(1 - pirate.P))} des joueurs qui ont ouvert autant de coffres n'ont pas encore de jeton non plus.",
            $"{H(L.Pct(1 - pirate.P))} of players who opened this many chests have no token yet either.",
            $"{H(L.Pct(1 - pirate.P))} mn les joueurs li 7ellou 9ed had snadeq 7ta homa ma 3ndhom jeton. Ma t7zench, khoya.", $"{H(L.Pct(1 - pirate.P))} de los jugadores que abrieron tantos cofres tampoco tienen ficha todavía.")));
        if (r.RollChests > 0)
        {
            var (lo, hi) = OddsMath.Wilson(r.RollTreasures, r.RollChests);
            facts.Add((L.T("Trésors par coffre", "Treasure rolls", "Knouz f kol sndouq", "Tesoros"), r.RollTreasures > 0
                ? L.T($"{r.RollTreasures} trésors / {N(r.RollChests)} coffres vus = 1 tous les {N((double)r.RollChests / r.RollTreasures)} (95 % : 1 / {N(1 / hi)} à 1 / {N(1 / lo)}). Estimation de la communauté : ~1 / 100 (non officielle).",
                    $"{r.RollTreasures} treasures / {N(r.RollChests)} chests seen = 1 per {N((double)r.RollChests / r.RollTreasures)} (95%: 1 / {N(1 / hi)} to 1 / {N(1 / lo)}). Community estimate: ~1 / 100 (unofficial).",
                    $"{r.RollTreasures} kenz / {N(r.RollChests)} sndouq li chefti = 1 f kol {N((double)r.RollChests / r.RollTreasures)} (95 %: 1 / {N(1 / hi)} 7ta 1 / {N(1 / lo)}). Ta9dir dyal la communauté: ~1 / 100 (machi officiel).", $"{r.RollTreasures} tesoros / {N(r.RollChests)} cofres vistos = 1 cada {N((double)r.RollChests / r.RollTreasures)} (95 %: 1 / {N(1 / hi)} a 1 / {N(1 / lo)}). Estimación de la comunidad: ~1 / 100 (no oficial).")
                : L.T($"aucun trésor en {N(r.RollChests)} coffres", $"no treasure in {N(r.RollChests)} chests", $"7ta kenz f {N(r.RollChests)} sndouq", $"ningún tesoro en {N(r.RollChests)} cofres")));
        }
        string HourText(TreasureHour h) => $"×{h.PirateShop.ToString("0.##", Fr)} · {H(L.DateTimeShort(OddsModel.Utc(h.StartUtc).ToLocalTime()))}–{OddsModel.Utc(h.EndUtc).ToLocalTime():HH:mm}{(string.IsNullOrEmpty(h.Label) ? "" : " · " + H(h.Label))}";
        facts.Add((L.T("Heures du trésor", "Treasure Hours", "Sa3at l'kenz", "Horas del tesoro"), r.HoursConfigured == 0
            ? L.T("aucune réglée (Réglages → Heures du trésor)", "none set (Settings → Treasure Hours)", "7ta wa7da (Réglages → Sa3at l'kenz)", "ninguna configurada (Ajustes → Horas del tesoro)")
            : L.T($"{N(r.BoostedChests)} coffres pendant une Heure du trésor, +{N(r.BoostExtra)} coffres de chance en plus.", $"{N(r.BoostedChests)} chests during Treasure Hours, +{N(r.BoostExtra)} chests' worth of extra chance.", $"{N(r.BoostedChests)} sndouq f Sa3at l'kenz, +{N(r.BoostExtra)} sndouq dyal zher zayed.", $"{N(r.BoostedChests)} cofres durante Horas del tesoro, +{N(r.BoostExtra)} cofres de probabilidad extra.")
              + (r.ActiveHour is { } active ? $" <b>{L.T("En cours", "Now", "Daba", "Ahora")} : {HourText(active)}</b>" : r.NextHour is { } next ? $" {L.T("Prochaine", "Next", "Jaya", "Próxima")} : {HourText(next)}" : "")));
        if (r.Scope != "mine")
        {
            var e = r.Estimate;
            facts.Add((L.T("Hors de ta vue", "Out of your sight", "B3ad 3la 3inik", "Fuera de tu vista"), e.Method switch
            {
                "rate" => L.T($"≈ {N(e.Value)} coffres de coéquipiers de plus (entre {N(e.Low ?? 0)} et {N(e.High ?? 0)}), d'après {e.Grants} trésors reçus de coffres que ton replay ne voit pas. Estimation globale{(r.Scope == "estimated" ? "" : ", comptée seulement avec « + estimation »")}.",
                    $"≈ {N(e.Value)} more teammate chests ({N(e.Low ?? 0)} to {N(e.High ?? 0)}), from {e.Grants} treasures received from chests your replay cannot see. An overall estimate{(r.Scope == "estimated" ? "" : ", only counted with \"+ estimated\"")}.",
                    $"≈ {N(e.Value)} sndouq kter dyal s7abek (bin {N(e.Low ?? 0)} w {N(e.High ?? 0)}), mn {e.Grants} kenz jawk mn snadeq ma kaybanouch f le replay dyalek. Ta9dir global{(r.Scope == "estimated" ? "" : ", kayt7seb ghir m3a «+ ta9dir»")}.", $"≈ {N(e.Value)} cofres de compañeros más (entre {N(e.Low ?? 0)} y {N(e.High ?? 0)}), a partir de {e.Grants} tesoros recibidos de cofres que tu repetición no ve. Una estimación global{(r.Scope == "estimated" ? "" : ", que solo cuenta con \"+ estimación\"")}."),
                "ratio" => L.T($"≈ {N(e.Value)} coffres de coéquipiers de plus, calibré sur {e.Basis} parties importées : ton replay voit {H(L.Pct(e.Ratio ?? 0))} de leurs coffres.",
                    $"≈ {N(e.Value)} more teammate chests, calibrated on {e.Basis} imported matches: your replay sees {H(L.Pct(e.Ratio ?? 0))} of their chests.",
                    $"≈ {N(e.Value)} sndouq kter dyal s7abek, m3ayer 3la {e.Basis} partiya importée: le replay dyalek kaychouf {H(L.Pct(e.Ratio ?? 0))} mn snadeq dyalhom.", $"≈ {N(e.Value)} cofres de compañeros más, calibrado con {e.Basis} partidas importadas: tu repetición ve el {H(L.Pct(e.Ratio ?? 0))} de sus cofres."),
                _ => L.T("pas encore assez de données pour estimer les coffres de l'équipe hors de ta vue.", "not enough data yet to estimate the team's chests out of your sight.", "mazal ma kafyinch les données bach n9edro snadeq dyal l'équipe li b3ad 3la 3inik.", "aún no hay suficientes datos para estimar los cofres del equipo fuera de tu vista."),
            }));
        }
        if (r.ExcludedMatches > 0)
        {
            facts.Add((L.T("Non comptées", "Left out", "Ma t7sbouch", "No contadas"), L.T($"{r.ExcludedMatches} parties classées ou hors de la chasse ({N(r.ExcludedChests)} coffres) : le jeton n'y tomberait pas (non confirmé).",
                $"{r.ExcludedMatches} ranked or out-of-hunt matches ({N(r.ExcludedChests)} chests): the token reportedly does not drop there (unconfirmed).",
                $"{r.ExcludedMatches} partiya classée wla barra la chasse ({N(r.ExcludedChests)} sndouq): l'jeton ma kaytla3ch temma (ma mconfirmich).", $"{r.ExcludedMatches} partidas clasificatorias o fuera de la búsqueda ({N(r.ExcludedChests)} cofres): al parecer la ficha no sale ahí (sin confirmar).")));
        }
        if (r.Scope == "squad")
        {
            facts.Add((L.T("Hors escouade", "Not squad", "Machi squad", "Fuera del escuadrón"), L.T($"{r.NonSquadMatches} parties sans coéquipier importé, non comptées.", $"{r.NonSquadMatches} matches without an imported teammate, not counted.", $"{r.NonSquadMatches} partiya bla sa7bek importé, ma t7sbatch.", $"{r.NonSquadMatches} partidas sin compañero importado, no contadas.")));
        }
        if (r.Scope is not ("mine" or "squad") && r.MatchesNoTeamData > 0)
        {
            facts.Add((L.T("Sans équipe", "No team data", "Bla équipe", "Sin datos de equipo"), L.T($"{r.MatchesNoTeamData} parties analysées avant le suivi des coéquipiers, dont le replay n'existe plus : les coffres de l'équipe y sont inconnus (pas 0).",
                $"{r.MatchesNoTeamData} matches analyzed before teammates were tracked, whose replay is gone: the team's chests are unknown there (not 0).",
                $"{r.MatchesNoTeamData} partiya t'analysat 9bel l'suivi dyal s7abek, w le replay dyalha ma b9ach: snadeq dyal l'équipe ma3rofinch (machi 0).", $"{r.MatchesNoTeamData} partidas analizadas antes del seguimiento de compañeros, cuya repetición ya no existe: los cofres del equipo son desconocidos (no 0).")));
        }
        sb.Append(Box(L.T("Rythme et chance", "Pace and luck", "Rythme w zher", "Ritmo y suerte"),
            L.T("Ton rythme, la fin de la chasse, et ta chance comparée aux autres joueurs.", "Your pace, the end of the hunt, and your luck compared with other players.", "Rythme dyalek, lekher dyal la chasse, w zher dyalek m9arn m3a les joueurs khrin.", "Tu ritmo, el fin de la búsqueda y tu suerte comparada con otros jugadores."),
            "<table class=\"facts\">" + string.Concat(facts.Select(f => $"<tr><td>{f.Label}</td><td>{f.Value}</td></tr>")) + "</table>"));

        if (OddsChart(r.Daily) is { Length: > 0 } chart)
        {
            sb.Append(Box(L.T("Jour après jour", "Day by day", "Nhar b nhar", "Día a día"), L.T("Ta chance du jeton à la fin de chaque jour.", "Your token chance at the end of each day.", "Zher dyal l'jeton f lekher dyal kol nhar.", "Tu probabilidad de la ficha al final de cada día."), chart));
        }
        return Panel("token", sb.ToString());
    }

    /// <summary>P(Pirate Shop Access) at the end of each day, as a line (one point per day).</summary>
    private static string OddsChart(List<(DateTime Day, double P)> daily)
    {
        if (daily.Count < 2)
        {
            return "";
        }
        const double w = 640, h = 180, left = 52, right = 14, top = 14, bottom = 26;
        var max = daily.Max(d => d.P);
        var scale = new[] { 1, 2, 5 }.SelectMany(m => Enumerable.Range(-6, 7).Select(e => m * Math.Pow(10, e))).OrderBy(v => v).FirstOrDefault(v => v >= max * 1.05);
        if (scale <= 0)
        {
            scale = 1;
        }
        string X(int i) => (left + i * (w - left - right) / (daily.Count - 1)).ToString("0.#", CultureInfo.InvariantCulture);
        string Y(double p) => (top + (1 - p / scale) * (h - top - bottom)).ToString("0.#", CultureInfo.InvariantCulture);
        var points = string.Join(" ", daily.Select((d, i) => $"{X(i)},{Y(d.P)}"));
        var sb = new StringBuilder();
        sb.Append($"<svg class=\"pline\" viewBox=\"0 0 {w} {h}\" role=\"img\" aria-label=\"{H(L.T("Chance du jeton par jour", "Token chance per day", "Zher dyal l'jeton f nhar", "Probabilidad de la ficha por día"))}\">");
        foreach (var g in new[] { 0, 0.5, 1 })
        {
            var y = Y(scale * g);
            sb.Append($"<line x1=\"{left}\" x2=\"{w - right}\" y1=\"{y}\" y2=\"{y}\" style=\"stroke:var(--line)\" stroke-dasharray=\"{(g == 0 ? "" : "3 4")}\"/>");
            sb.Append($"<text x=\"{left - 6}\" y=\"{y}\" dy=\"4\" text-anchor=\"end\">{H(L.Pct(scale * g))}</text>");
        }
        sb.Append($"<polygon points=\"{X(0)},{Y(0)} {points} {X(daily.Count - 1)},{Y(0)}\" style=\"fill:var(--gold);fill-opacity:.18\"/>");
        sb.Append($"<polyline points=\"{points}\" fill=\"none\" style=\"stroke:var(--gold)\" stroke-width=\"2.5\" stroke-linejoin=\"round\"/>");
        for (var i = 0; i < daily.Count; i++)
        {
            var (day, p) = daily[i];
            sb.Append($"<circle cx=\"{X(i)}\" cy=\"{Y(p)}\" r=\"{(i == daily.Count - 1 ? 4 : 2.5)}\" style=\"fill:var(--gold)\"><title>{H(day.ToString(L.T("ddd dd/MM", "ddd d MMM", "ddd dd/MM", "ddd d MMM"), Fr))} : {H(L.Pct(p))}</title></circle>");
        }
        sb.Append($"<text x=\"{left}\" y=\"{h - 6}\">{H(daily[0].Day.ToString(L.T("dd/MM", "d MMM", "dd/MM", "d MMM"), Fr))}</text>");
        sb.Append($"<text x=\"{w - right}\" y=\"{h - 6}\" text-anchor=\"end\">{H(daily[^1].Day.ToString(L.T("dd/MM", "d MMM", "dd/MM", "d MMM"), Fr))} · {H(L.Pct(daily[^1].P))}</text>");
        sb.Append("</svg>");
        return sb.ToString();
    }

    // ---- Squad -------------------------------------------------------------------------------

    private static string SquadPanel(TrackerData data, Config config, List<MatchRecord> matches, ImportIndex index, bool live)
    {
        var sb = new StringBuilder();
        var privacy = L.T("Le fichier contient ton nom, ton identifiant Epic et tes coffres de chaque partie en équipe. Pas de replay, ni le nom d'un autre joueur.",
            "The file holds your name, your Epic id and your chests of each team match. No replay, and no other player's name.",
            "L'fichier fih smitek, l'id Epic dyalek w snadeq dyalek f kol partiya f l'équipe. Ma fih 7ta replay, wla smiya dyal chi wa7ed akhor.", "El archivo contiene tu nombre, tu id de Epic y tus cofres de cada partida en equipo. Ninguna repetición, ni el nombre de otro jugador.");
        string share;
        if (live)
        {
            share = "<div class=\"steps3\">"
                + $"<div class=\"step\"><span class=\"n\">1</span><b>{L.T("Exporte ton fichier", "Export your file", "Exporti l'fichier dyalek", "Exporta tu archivo")}</b>"
                + $"<p>{L.T("Il arrive sur ton Bureau (« ton-nom-date.chesttoken »). Envoie-le à ton escouade (Discord…).", "It lands on your Desktop (\"your-name-date.chesttoken\"). Send it to your squad (Discord…).", "Kayji f le Bureau («smitek-date.chesttoken»). Siftou l squad dyalek (Discord…).", "Aparece en tu Escritorio (\"tu-nombre-fecha.chesttoken\"). Envíalo a tu escuadrón (Discord…).")}</p>"
                + Button("export", L.T("Exporter mon fichier", "Export my file", "Exporti l'fichier dyali", "Exportar mi archivo"), cls: "primary") + "</div>"
                + $"<div class=\"step\"><span class=\"n\">2</span><b>{L.T("Importe le leur", "Import theirs", "Importi dyalhom", "Importa el suyo")}</b>"
                + $"<p>{L.T("Le fichier .chesttoken qu'ils t'ont envoyé. Un double-clic sur le fichier marche aussi.", "The .chesttoken file they sent you. Double-clicking the file works too.", "L'fichier .chesttoken li siftou lik. Double-clic 3la l'fichier 7ta howa khdam.", "El archivo .chesttoken que te enviaron. Hacer doble clic en el archivo también funciona.")}</p>"
                + ImportControl("pick-squad") + "</div>"
                + $"<div class=\"step\"><span class=\"n\">3</span><b>{L.T("C'est tout", "That's it", "Safi", "Y ya está")}</b>"
                + $"<p>{L.T("Seules les parties jouées ensemble dans la même équipe sont gardées. Rien n'est compté deux fois : leur compte remplace ce que ton replay a vu d'eux.", "Only the matches played together on the same team are kept. Nothing is counted twice: their count replaces what your replay saw of them.", "Kaytkhllaw ghir les partiyat li l3btou m3a ba3d f nefs l'équipe. Walou ma kayt7seb jouj mrat: l7sab dyalhom kay3wwed dakchi li chafo le replay dyalek.", "Solo se guardan las partidas jugadas juntos en el mismo equipo. Nada se cuenta dos veces: su recuento sustituye lo que tu repetición vio de ellos.")}</p></div>"
                + "</div>"
                + $"<div class=\"note\">{H(privacy)}</div>";
        }
        else
        {
            share = $"<p>{L.T("Dans le tableau de bord de l'assistant : onglet Escouade → « Exporter mon fichier », puis envoie le fichier .chesttoken à tes coéquipiers et importe le leur (ou double-clique dessus). Seules les parties jouées ensemble dans la même équipe comptent, chacun avec son propre compte.",
                "In the assistant's dashboard: Squad tab → \"Export my file\", then send the .chesttoken file to your teammates and import theirs (or double-click it). Only the matches played together on the same team count, everyone with their own count.",
                "F tableau de bord dyal l'assistant: onglet Squad → «Exporti l'fichier dyali», w sift l'fichier .chesttoken l s7abek w importi dyalhom (wla dir 3lih double-clic). Ghir les partiyat li l3btou m3a ba3d f nefs l'équipe kayt7sbo, kol wa7ed b l7sab dyalo.", "En el panel del asistente: pestaña Escuadrón → \"Exportar mi archivo\", luego envía el archivo .chesttoken a tus compañeros e importa el suyo (o haz doble clic en él). Solo cuentan las partidas jugadas juntos en el mismo equipo, cada uno con su propio recuento.")}</p><div class=\"note\">{H(privacy)}</div>";
        }
        sb.Append(Box(L.T("Partage avec ton escouade", "Share with your squad", "Partagi m3a squad dyalek", "Comparte con tu escuadrón"),
            L.T("Ton replay ne voit que ce qui était près de toi. Échangez vos fichiers pour avoir les vrais totaux de l'équipe.", "Your replay only sees what was near you. Swap files to get the team's real totals.", "Le replay dyalek kaychouf ghir dakchi li kan 9rib lik. Tbadlou les fichiers bach tchoufo total l79i9i dyal l'équipe.", "Tu repetición solo ve lo que estaba cerca de ti. Intercambiad archivos para tener los totales reales del equipo."),
            share));

        // Squads from the imported files.
        var squads = SquadModel.Build(data, config);
        string squadBody;
        if (squads.Count == 0)
        {
            squadBody = $"<p class=\"items\">{(Team.Imports.Sources.Count == 0
                ? L.T("Importe le fichier d'un coéquipier pour voir vos parties ensemble ici.", "Import a teammate's file to see your matches together here.", "Importi l'fichier dyal sa7bek bach tchouf les partiyat dyalkom m3a ba3d hna.", "Importa el archivo de un compañero para ver aquí vuestras partidas juntos.")
                : L.T("Aucune partie ensemble dans la même équipe (hors parties classées et hors chasse).", "No match together on the same team (ranked and out-of-hunt matches left out).", "7ta partiya m3a ba3d f nefs l'équipe (bla classées w bla li barra la chasse).", "Ninguna partida juntos en el mismo equipo (sin clasificatorias ni partidas fuera de la búsqueda)."))}</p>";
        }
        else
        {
            var you = L.T("Toi", "You", "Nta", "Tú");
            var t = new StringBuilder($"<div class=\"scroll\"><table><tr><th>{L.T("Escouade", "Squad", "Squad", "Escuadrón")}</th><th style=\"text-align:right\">{L.T("Parties", "Matches", "Partiyat", "Partidas")}</th><th>{L.T("Coffres de chacun", "Everyone's chests", "Snadeq dyal kol wa7ed", "Cofres de cada uno")}</th>"
                + $"<th style=\"text-align:right\">{L.T("Total", "Total", "Total", "Total")}</th><th style=\"text-align:right\">{L.T("Chance du jeton", "Token chance", "Zher dyal l'jeton", "Probabilidad de la ficha")}</th></tr>");
            foreach (var g in squads)
            {
                var chips = $"<span class=\"chip\">{you}</span>" + string.Concat(g.Members.Select(m => $"<span class=\"chip x\">{H(m.Name)}</span>"));
                var each = $"{you} {L.N(g.MyChests)} · " + string.Join(" · ", g.Members.Select(m => $"{H(m.Name)} {L.N(m.Chests)}"))
                    + (g.OthersSeen > 0 ? $" <span class=\"items\">· {L.T($"+{g.OthersSeen} vus (non importés)", $"+{g.OthersSeen} seen (not imported)", $"+{g.OthersSeen} li chefti (machi importés)", $"+{g.OthersSeen} vistos (no importados)")}</span>" : "");
                t.Append($"<tr><td>{chips}</td><td class=\"n num\">{L.N(g.Matches)}</td><td class=\"items\">{each}</td><td class=\"n num big\">{L.N(g.Total)}</td><td class=\"n num big gold\">{H(L.Pct(g.P))}</td></tr>");
            }
            var all = OddsModel.Build(data, config, scopeOverride: "squad");
            t.Append($"</table></div><div class=\"note\"><b>{L.T("Toutes les parties en escouade", "All squad matches", "Ga3 les partiyat squad", "Todas las partidas en escuadrón")}</b>{L.T(" : ", ": ", ": ", ": ")}{L.Count(all.Matches, "partie", "parties", "match", "matches", "partiya", "partiyat", "partida", "partidas")} · {L.Count((int)all.Total, "coffre", "chest", "sndouq", "cofre")} → "
                + $"<b class=\"gold\">{H(L.Pct(all.Pirate.P))}</b> ({H(L.OneIn(all.Pirate.P))}). {L.T("Pour baser toutes tes chances sur ces parties : « parties en escouade » dans les coffres comptés.", "To base all your odds on these matches: \"squad matches\" in chests counted.", "Bach tbni zher dyalek kamel 3la had les partiyat: «partiyat squad» f snadeq m7soubin.", "Para basar todas tus probabilidades en estas partidas: \"partidas en escuadrón\" en los cofres contados.")}</div>");
            squadBody = t.ToString();
        }
        sb.Append(Box(L.T("Tes escouades", "Your squads", "Les squads dyalek", "Tus escuadrones"),
            L.T("Chaque groupe exact de coéquipiers importés, avec le compte exact de chacun et la chance du jeton de l'escouade.", "Every exact group of imported teammates, with everyone's exact count and the squad's token chance.", "Kol groupe exact dyal s7abek importés, b l7sab exact dyal kol wa7ed w zher dyal l'jeton dyal squad.", "Cada grupo exacto de compañeros importados, con el recuento exacto de cada uno y la probabilidad de la ficha del escuadrón."),
            squadBody));

        sb.Append(Box(L.T("Coéquipiers", "Teammates", "S7abek", "Compañeros"),
            L.T("Leurs coffres vus dans ton replay (un minimum), et leur vrai compte s'ils t'ont envoyé leur fichier.", "Their chests seen in your replay (a minimum), and their real count if they sent you their file.", "Snadeq dyalhom li chefti f le replay dyalek (minimum), w l7sab l79i9i ila siftou lik l'fichier.", "Sus cofres vistos en tu repetición (un mínimo), y su recuento real si te enviaron su archivo."),
            TeamTable(matches, index)));
        return Panel("squad", sb.ToString());
    }

    /// <summary>Teammates across the matches: chests seen in the player's replay (a minimum) and their own count when imported.</summary>
    private static string TeamTable(List<MatchRecord> matches, ImportIndex index)
    {
        var board = Team.Leaderboard(matches, index);
        if (board.Count == 0)
        {
            return $"<p class=\"items\">{L.T("Pas encore de coéquipier : tes parties en équipe apparaissent ici.", "No teammate yet: your team matches show up here.", "Mazal 7ta sa7b: les partiyat f l'équipe dyalek kaybanou hna.", "Aún no hay compañeros: tus partidas en equipo aparecen aquí.")}</p>";
        }
        var sb = new StringBuilder($"<div class=\"scroll\"><table><tr><th>{L.T("Coéquipier", "Teammate", "Sa7bek", "Compañero")}</th><th>{L.T("Groupe", "Party", "Groupe", "Grupo")}</th><th style=\"text-align:right\">{L.T("Parties ensemble", "Matches together", "Partiyat m3a ba3d", "Partidas juntos")}</th>"
            + $"<th style=\"text-align:right\">{L.T("Vus dans ton replay", "Seen in your replay", "Li chefti f le replay", "Vistos en tu repetición")}</th><th style=\"text-align:right\">{L.T("Vus / partie", "Seen / match", "Li chefti / partiya", "Vistos / partida")}</th>"
            + $"<th style=\"text-align:right\">{L.T("Leur compte (importé)", "Their own count (imported)", "L7sab dyalo (importé)", "Su propio recuento (importado)")}</th></tr>");
        foreach (var mate in board.Take(30))
        {
            var party = mate.InParty switch
            {
                true => L.T("ton groupe", "your party", "groupe dyalek", "tu grupo"),
                false => L.T("aléatoire", "random (fill)", "hasard (fill)", "aleatorio (relleno)"),
                _ => "—",
            };
            var name = mate.Name.Length > 0 ? H(mate.Name) : $"<span class=\"items\">{L.T("sans nom", "no name", "bla smiya", "sin nombre")}</span>";
            var perMatch = mate.SeenMatches > 0 ? L.Num((double)mate.Seen / mate.SeenMatches) : "—";
            var imported = mate.ImportedMatches > 0
                ? $"{L.N(mate.Imported)} <span class=\"items\">({L.Count(mate.ImportedMatches, "partie", "parties", "match", "matches", "partiya", "partiyat", "partida", "partidas")}, {L.Num((double)mate.Imported / mate.ImportedMatches)} / {L.T("partie", "match", "partiya", "partida")})</span>"
                : "<span class=\"items\">—</span>";
            sb.Append($"<tr><td class=\"by\">{TeamIcon}{name}</td><td class=\"items\">{party}</td><td class=\"n num\">{mate.Matches}</td><td class=\"n num big\">{(mate.SeenMatches > 0 ? L.N(mate.Seen) : "—")}</td><td class=\"n num\">{perMatch}</td><td class=\"n num\">{imported}</td></tr>");
        }
        sb.Append("</table></div>");
        return sb.ToString();
    }

    /// <summary>"12 seen" / "+ 30 (their count)" / "no team data" / "—" (solo).</summary>
    private static string TeamCell(MatchRecord m, ImportIndex index)
    {
        var mates = Team.MatesOf(m, index);
        if (m.TeammateChests == null && mates.Count == 0)
        {
            return $"<span class=\"items\">{L.T("pas de données d'équipe", "no team data", "bla données dyal l'équipe", "sin datos de equipo")}</span>";
        }
        if (mates.Count == 0)
        {
            return "—";
        }
        // Each teammate once: their own count when imported, otherwise what the replay saw.
        var parts = new List<string>();
        var seen = mates.Where(x => x.Imported == null && x.Seen != null).ToList();
        if (seen.Count > 0)
        {
            parts.Add(L.T($"{seen.Sum(x => x.Seen!.Value)} vus", $"{seen.Sum(x => x.Seen!.Value)} seen", $"{seen.Sum(x => x.Seen!.Value)} li chefti", $"{seen.Sum(x => x.Seen!.Value)} vistos"));
        }
        var imported = mates.Where(x => x.Imported != null).ToList();
        if (imported.Count > 0)
        {
            parts.Add(L.T($"{imported.Sum(x => x.Imported!.ChestsOpened)} importés", $"{imported.Sum(x => x.Imported!.ChestsOpened)} imported", $"{imported.Sum(x => x.Imported!.ChestsOpened)} importés", $"{imported.Sum(x => x.Imported!.ChestsOpened)} importados"));
        }
        return TeamIcon + string.Join(" · ", parts);
    }

    // ---- Files -------------------------------------------------------------------------------

    private static string FilesPanel(TrackerData data, Config config, bool live)
    {
        var sb = new StringBuilder();
        var impact = ImportsReport.Build(data, config);
        if (impact.Files.Count == 0)
        {
            sb.Append(Box(L.T("Aucun fichier pour l'instant", "No file yet", "Mazal 7ta fichier", "Aún no hay archivos"),
                L.T("Demande à tes coéquipiers leur fichier .chesttoken (onglet Escouade → Exporter), puis importe-le : il apparaît ici avec ce qu'il change.",
                    "Ask your teammates for their .chesttoken file (Squad tab → Export), then import it: it shows up here with what it changes.",
                    "Tleb mn s7abek l'fichier .chesttoken dyalhom (onglet Squad → Exporti), w importih: kayban hna m3a chno kaybeddel.", "Pide a tus compañeros su archivo .chesttoken (pestaña Escuadrón → Exportar) e impórtalo: aparece aquí con lo que cambia."),
                live ? ImportControl("pick-files") : ""));
            return Panel("files", sb.ToString());
        }

        var delta = impact.PAll - impact.PNone;
        var summary = $"<div class=\"compare\"><div><div class=\"items\">{L.T("sans les fichiers", "without the files", "bla les fichiers", "sin los archivos")}</div><div class=\"v num\">{H(L.Pct(impact.PNone))}</div></div>"
            + "<div class=\"arrow\">→</div>"
            + $"<div><div class=\"items\">{L.T("avec les fichiers", "with the files", "b les fichiers", "con los archivos")}</div><div class=\"v num gold\">{H(L.Pct(impact.PAll))}</div></div>"
            + $"<div class=\"delta\">{H(ImportsReport.Points(delta))}</div></div>"
            + $"<div class=\"note\">{(impact.Scope == "mine"
                ? L.T("Tes chances ne comptent que tes propres coffres : choisis d'autres « coffres comptés » (onglet Jeton) pour que les fichiers comptent.", "Your odds only count your own chests: choose other \"chests counted\" (Token tab) for the files to count.", "Zher dyalek kay7seb ghir snadeq dyalek: khtar «snadeq m7soubin» khrin (onglet Jeton) bach les fichiers y7sbo.", "Tus probabilidades solo cuentan tus propios cofres: elige otros \"cofres contados\" (pestaña Ficha) para que los archivos cuenten.")
                : $"{L.T("Coffres comptés", "Chests counted", "Snadeq m7soubin", "Cofres contados")}{L.T(" : ", ": ", ": ", ": ")}{H(OddsModel.ScopeName(impact.Scope))}")}</div>";
        sb.Append(Box(L.T($"Tes {impact.Files.Count} fichiers ensemble", $"Your {impact.Files.Count} files together", $"Les {impact.Files.Count} fichiers dyalek m3a ba3d", $"Tus {impact.Files.Count} archivos juntos"),
            L.T("Ta chance du jeton avec tous tes fichiers importés, comparée à sans eux.", "Your token chance with all your imported files, compared with without them.", "Zher dyal l'jeton b ga3 les fichiers importés, m9arn m3a blahom.", "Tu probabilidad de la ficha con todos tus archivos importados, comparada con sin ellos."),
            summary, live
                ? $"<button type=\"button\" class=\"btn small\" data-pick=\"pick-files\">{L.T("Importer un autre fichier…", "Import another file…", "Importi fichier akhor…", "Importar otro archivo…")}</button>"
                  + "<input type=\"file\" id=\"pick-files\" class=\"hidden-file\" data-import accept=\".chesttoken,.json,.csv\" multiple>"
                : null));

        sb.Append("<div class=\"files\">");
        foreach (var file in impact.Files)
        {
            var s = file.Source;
            var missed = file.TheirChests - file.SeenOfThem;
            var dates = file.MatchesUsed > 0 ? $" · {H(file.First!.Value.ToLocalTime().ToString("d MMM", Fr))} → {H(file.Last!.Value.ToLocalTime().ToString("d MMM", Fr))}" : "";
            var body = new StringBuilder("<div class=\"kv\">");
            body.Append($"<span>{L.T("Parties ensemble", "Matches together", "Partiyat m3a ba3d", "Partidas juntos")}</span><span><b class=\"num\">{L.N(s.Matches.Count)}</b> {L.T($"sur {L.N(s.MatchesInFile)} dans le fichier", $"of {L.N(s.MatchesInFile)} in the file", $"mn {L.N(s.MatchesInFile)} f l'fichier", $"de {L.N(s.MatchesInFile)} en el archivo")}{dates}</span>");
            body.Append($"<span>{L.T("Leurs coffres", "Their chests", "Snadeq dyalo", "Sus cofres")}</span><span><b class=\"num\">{L.N(file.TheirChests)}</b></span>");
            body.Append($"<span>{L.T("Vus par ton replay", "Seen by your replay", "Li chafhom le replay dyalek", "Vistos por tu repetición")}</span><span><b class=\"num\">{L.N(file.SeenOfThem)}</b>{(missed > 0 ? $" <span class=\"ok\">{L.T($"+{L.N(missed)} qu'il ratait", $"+{L.N(missed)} it missed", $"+{L.N(missed)} ma chafhomch", $"+{L.N(missed)} que no veía")}</span>" : "")}</span>");
            body.Append($"<span>{L.T("Effet sur ta chance", "Effect on your chance", "Chno kaybeddel f zher", "Efecto en tu probabilidad")}</span><span><b class=\"num gold\">{H(ImportsReport.Points(file.PWith - file.PWithout))}</b> <span class=\"items\">({H(L.Pct(file.PWithout))} → {H(L.Pct(file.PWith))})</span></span>");
            if (file.Squads.Count > 0)
            {
                body.Append($"<span>{L.T("Escouades", "Squads", "Squads", "Escuadrones")}</span><span>{string.Concat(file.Squads.Select(q => $"<span class=\"chip x\">{H(q)}</span>"))}</span>");
            }
            body.Append("</div>");
            if (s.Matches.Count > 0 && file.MatchesUsed == 0)
            {
                body.Append($"<div class=\"note warn\">{L.T("Ces parties sont dans un autre de tes profils (Réglages → Profil).", "These matches belong to another of your profiles (Settings → Profile).", "Had les partiyat f profil akhor dyalek (Réglages → Profil).", "Estas partidas pertenecen a otro de tus perfiles (Ajustes → Perfil).")}</div>");
            }
            var remove = live
                ? Button("remove-file", L.T("Retirer", "Remove", "7iyed", "Quitar"), s.Key, cls: "danger small",
                    confirm: L.T($"Retirer le fichier de {s.Name} ? Ses parties reviennent à ce que ton replay a vu. Tu pourras l'importer à nouveau.", $"Remove {s.Name}'s file? Its matches go back to what your replay saw. You can import it again later.", $"T7iyed l'fichier dyal {s.Name}? Les partiyat dyalo kayrej3o l dakchi li chafo le replay dyalek. T9der t3awed timportih.", $"¿Quitar el archivo de {s.Name}? Sus partidas vuelven a lo que vio tu repetición. Podrás importarlo de nuevo."))
                : null;
            sb.Append(Box($"{TeamIcon} {H(s.Name)}",
                $"{H(s.File)} · {L.T("importé le", "imported", "importé", "importados")} {H(L.DateTimeShort(OddsModel.Utc(s.ImportedUtc).ToLocalTime()))}",
                body.ToString(), remove));
        }
        sb.Append("</div>");
        sb.Append($"<p class=\"items\">{L.T("Un nouveau fichier de la même personne remplace l'ancien. « Vus par ton replay » : ce que ton replay voyait d'eux ; en vert, les coffres que seul leur fichier connaît.", "A newer file from the same person replaces the older one. \"Seen by your replay\": what your replay saw of them; in green, the chests only their file knows about.", "Fichier jdid mn nefs l'wa7ed kay3wwed l9dim. «Li chafhom le replay dyalek»: dakchi li chaf le replay dyalek menhom; b l'akhdar, snadeq li ghir l'fichier dyalhom 3aref.", "Un archivo más reciente de la misma persona sustituye al anterior. \"Vistos por tu repetición\": lo que tu repetición veía de ellos; en verde, los cofres que solo conoce su archivo.")}</p>");
        return Panel("files", sb.ToString());
    }

    // ---- Matches -----------------------------------------------------------------------------

    private static string MatchesPanel(List<MatchRecord> matches, ImportIndex index)
    {
        var sb = new StringBuilder();

        // Latest matches. "Team": teammates' chests seen in the player's replay (a minimum), their own count when imported.
        var list = new StringBuilder($"<div class=\"scroll\"><table><tr><th>{L.T("Date", "Date", "Date", "Fecha")}</th><th>{L.T("Mode", "Mode", "Mode", "Modo")}</th><th>{L.T("Place", "Placement", "Blassa", "Puesto")}</th><th style=\"text-align:right\">{L.T("Coffres", "Chests", "Snadeq", "Cofres")}</th><th>{L.T("Équipe", "Team", "L'équipe", "Equipo")}</th><th>{L.T("Trésor", "Treasure", "Kenz", "Tesoro")}</th></tr>");
        foreach (var m in matches.Take(100))
        {
            var treasure = string.Join("<br>", m.TreasureFinds.Select(f => $"<span class=\"{(f.FromGoldenChest ? "gold" : "star")}\">{(f.FromGoldenChest ? "★★" : "★")} {H(f.DisplayName)}</span>"))
                + string.Concat(m.TeamTreasureFinds.Select((f, i) => $"<span class=\"team\">{(i == 0 && m.TreasureFinds.Count == 0 ? "" : "<br>")}{TeamIcon} ★ {H(f.DisplayName)}</span>"));
            var win = m.Placement == 1;
            list.Append($"<tr><td class=\"nw num items\">{L.DateTimeShort(When(m))}</td><td>{H(Pretty.Playlist(m.Playlist))}</td><td class=\"nw num\"{(win ? " style=\"color:var(--gold);font-weight:700\"" : "")}>{H(Pretty.Placement(m))}</td><td class=\"n num big\">{m.ChestsOpened}</td><td class=\"nw items\">{TeamCell(m, index)}</td><td class=\"items\">{treasure}</td></tr>");
        }
        if (matches.Count == 0)
        {
            list.Append($"<tr><td colspan=\"6\" class=\"items\">{L.T("Aucune partie pour l'instant.", "No match yet.", "Mazal 7ta partiya.", "Aún no hay partidas.")}</td></tr>");
        }
        list.Append("</table></div>");
        sb.Append(Box(L.T("Dernières parties", "Latest matches", "Les partiyat lekhrin", "Últimas partidas"),
            L.T("Tes 100 dernières parties. Équipe : les coffres de tes coéquipiers vus dans ton replay, ou leur compte importé.", "Your last 100 matches. Team: your teammates' chests seen in your replay, or their imported count.", "100 partiya lekhra dyalek. L'équipe: snadeq dyal s7abek li chefti f le replay dyalek, wla l7sab dyalhom importé.", "Tus últimas 100 partidas. Equipo: los cofres de tus compañeros vistos en tu repetición, o su recuento importado."),
            list.ToString()));

        // By chest type (with the other interactions, not in the total) | where the player's treasures come from.
        var byKind = matches.SelectMany(m => m.Chests).GroupBy(c => ChestKinds.Canonical(c.Kind)).OrderByDescending(g => g.Count()).ToList();
        var kindMax = Math.Max(1, byKind.Count > 0 ? byKind.Max(g => g.Count()) : 1);
        var otherKinds = matches
            .SelectMany(m => m.OtherInteractions.Count > 0
                ? m.OtherInteractions
                : new Dictionary<string, int> { [ChestKinds.AmmoBox] = m.AmmoBoxesOpened, [ChestKinds.OtherContainer] = m.OtherContainersOpened })
            .GroupBy(kv => kv.Key)
            .Select(g => (Kind: g.Key, Count: g.Sum(kv => kv.Value)))
            .Where(x => x.Count > 0)
            .OrderByDescending(x => x.Count)
            .ToList();
        var kinds = new StringBuilder("<table>");
        foreach (var g in byKind)
        {
            var color = KindColor.GetValueOrDefault(g.Key, "#94a3b8");
            var w = Math.Max(2, (int)Math.Round(100.0 * g.Count() / kindMax));
            kinds.Append($"<tr><td><span class=\"sw\" style=\"background:{color}\"></span>{H(L.Name(g.Key))}</td><td style=\"width:150px\"><span class=\"meter\"><i style=\"width:{w}%;background:{color}\"></i></span></td><td class=\"n num big\">{g.Count().ToString("N0", Fr)}</td></tr>");
        }
        foreach (var (kind, count) in otherKinds)
        {
            kinds.Append($"<tr class=\"items\"><td>{H(L.Name(kind))} {L.T("(hors total)", "(not in the total)", "(barra total)", "(no incluido en el total)")}</td><td></td><td class=\"n num\">{count.ToString("N0", Fr)}</td></tr>");
        }
        kinds.Append("</table>");
        var destroyedTotal = matches.Sum(m => m.Chests.Count(c => c.Destroyed));
        if (destroyedTotal > 0)
        {
            kinds.Append($"<div class=\"note\">{L.T($"dont {destroyedTotal.ToString("N0", Fr)} détruits (cassés au lieu d'être ouverts)", $"of which {destroyedTotal.ToString("N0", Fr)} destroyed (broken instead of opened)", $"menhom {destroyedTotal.ToString("N0", Fr)} mkessrin (kssertihom blast ma t7elhom)", $"de ellos {destroyedTotal.ToString("N0", Fr)} destruidos (rotos en vez de abiertos)")}</div>");
        }

        var treasureRows = matches.SelectMany(m => m.TreasureFinds.Select(f => (m, f, Team: false))).ToList();
        var teamRows = matches.SelectMany(m => m.TeamTreasureFinds.Select(f => (m, f, Team: true))).ToList();
        var sources = new StringBuilder("<table>");
        if (treasureRows.Count == 0)
        {
            sources.Append($"<tr><td class=\"items\">{L.T("Pas encore de trésor.", "No treasure yet.", "Mazal 7ta kenz. Sber, ghadi yji.", "Aún no hay tesoros.")}</td></tr>");
        }
        foreach (var g in treasureRows.GroupBy(r => r.f.SourceLabel).OrderByDescending(g => g.Count()))
        {
            var isChest = g.First().f.SourceIsChest;
            sources.Append(isChest
                ? $"<tr><td>{H(g.Key)}</td><td class=\"n num big\">{g.Count()}</td></tr>"
                : $"<tr class=\"unusual\"><td class=\"warn\">{H(g.Key)} <span style=\"font-size:12px\">· {L.T("fiabilité faible", "low reliability", "ma fihach ti9a", "poco fiable")}</span></td><td class=\"n num big warn\">{g.Count()}</td></tr>");
        }
        sources.Append("</table>");
        var unusual = treasureRows.Where(r => !r.f.SourceIsChest).ToList();
        if (unusual.Count > 0)
        {
            var count = L.Count(unusual.Count, "trésor obtenu", "trésors obtenus", "treasure", "treasures", "kenz jak", "knouz jawk", "tesoro", "tesoros");
            sources.Append($"<div class=\"flag\">{FlagIcon}<span>" + L.T(
                $"<b>{count} autrement qu'avec un coffre.</b> Fiabilité faible : il s'agit très probablement d'une erreur dans la façon dont les replays sont analysés.",
                $"<b>{count} obtained other than from a chest.</b> Low reliability: most likely a mistake in the way the replays are analyzed.",
                $"<b>{count} b chi tri9a khra machi b sndouq.</b> Ma fihach ti9a: ghaliban ghalta f l'analyse dyal les replays.", $"<b>{count} obtenidos sin cofre.</b> Poco fiable: lo más probable es un error en el análisis de las repeticiones.") + "</span></div>");
        }
        sb.Append("<div class=\"grid2\">");
        sb.Append(Box(L.T("Par type de coffre", "By chest type", "3la 7sab nou3 sndouq", "Por tipo de cofre"),
            L.T("Les coffres que tu as ouverts, par type. Les caisses de munitions et autres sont à part.", "The chests you opened, by type. Ammo boxes and the like are apart.", "Snadeq li 7elliti, 3la 7sab nou3. Caisses dyal munitions w dakchi b'bou7dhom.", "Los cofres que abriste, por tipo. Las cajas de munición y similares van aparte."),
            kinds.ToString()));
        sb.Append(Box(L.T("D'où viennent tes trésors", "Where your treasures come from", "Mnin jaw knouz dyalek", "De dónde vienen tus tesoros"),
            L.T("Le type de coffre qui t'a donné chaque trésor.", "The kind of chest that gave you each treasure.", "Nou3 sndouq li 3tak kol kenz.", "El tipo de cofre que te dio cada tesoro."),
            sources.ToString()));
        sb.Append("</div>");

        // Treasures: the player's own, then (hidden unless asked) those found by teammates, shared with the whole team.
        if (treasureRows.Count > 0 || teamRows.Count > 0)
        {
            var t = new StringBuilder($"<div class=\"scroll\"><table><tr><th>{L.T("Partie", "Match", "Partiya", "Partida")}</th><th>{L.T("Trésor", "Treasure", "Kenz", "Tesoro")}</th><th>{L.T("Obtenu à", "Obtained at", "Wa9t", "Obtenido a las")}</th><th>{L.T("Source", "Source", "Masdar", "Origen")}</th><th>{L.T("Par", "By", "Chkoun", "Por")}</th></tr>");
            foreach (var (m, f, team) in treasureRows.Concat(teamRows).OrderByDescending(r => When(r.m)).ThenBy(r => r.f.ObtainedAtSeconds ?? 0))
            {
                var when = f.ObtainedAtSeconds is float sec ? (f.TimeExact ? Pretty.Clock(sec) : $"{L.T("vers", "around", "tqriban f", "hacia")} {Pretty.Clock(sec)}") : "?";
                string source;
                if (team && string.IsNullOrEmpty(f.Teammate))
                {
                    source = $"<span class=\"items\">{L.T("coffre hors de ta vue", "chest out of your sight", "sndouq b3id 3la 3inik", "cofre fuera de tu vista")}</span>";
                }
                else if (!f.SourceIsChest)
                {
                    source = $"<span class=\"warn\">{H(f.SourceLabel)} · {L.T("fiabilité faible", "low reliability", "ma fihach ti9a", "poco fiable")}</span>";
                }
                else
                {
                    source = H(f.SourceLabel) + (f.SourceCertain ? "" : $" <span class=\"items\">({Pretty.Likely})</span>");
                }
                var who = !team ? L.T("toi", "you", "nta", "tú")
                    : TeamIcon + (string.IsNullOrEmpty(f.Teammate) ? L.T("un coéquipier", "a teammate", "sa7bek", "un compañero") : H(f.Teammate));
                var cls = team ? "team" : f.SourceIsChest ? "" : "unusual";
                var stars = f.FromGoldenChest ? "<span class=\"gold\">★★</span>" : "<span class=\"star\">★</span>";
                t.Append($"<tr{(cls.Length > 0 ? $" class=\"{cls}\"" : "")}><td class=\"nw num items\">{L.DateTimeShort(When(m))}</td><td>{stars} <b>{H(f.DisplayName)}</b></td><td class=\"nw num\">{when}</td><td>{source}</td><td class=\"by\">{who}</td></tr>");
            }
            t.Append("</table></div>");
            var toggle = teamRows.Count > 0
                ? $"<label class=\"teamtoggle\"><input type=\"checkbox\" id=\"showteam\"> {L.T($"Afficher aussi l'équipe (+{teamRows.Count}, estimation)", $"Also show the team (+{teamRows.Count}, estimate)", $"Werri 7ta l'équipe (+{teamRows.Count}, ta9dir)", $"Mostrar también el equipo (+{teamRows.Count}, estimación)")}</label>"
                : null;
            sb.Append(Box($"{L.T("Trésors trouvés", "Treasures found", "Knouz l9iti", "Tesoros encontrados")} · <span class=\"num\">{treasureRows.Count}</span>",
                L.T("★★ coffre doré de la chasse au trésor · ★ trésor reçu d'un de tes coffres · l'icône équipe : trouvé par un coéquipier (estimation, hors de tes totaux).",
                    "★★ golden treasure hunt chest · ★ treasure received from one of your chests · the team icon: found by a teammate (estimate, not in your totals).",
                    "★★ sndouq dhabi dyal la chasse · ★ kenz jak mn sndouq dyalek · l'icône dyal l'équipe: l9ah sa7bek (ta9dir, barra total dyalek).", "★★ cofre dorado de la búsqueda del tesoro · ★ tesoro recibido de uno de tus cofres · el icono de equipo: encontrado por un compañero (estimación, no incluido en tus totales)."),
                t.ToString(), toggle));
        }
        return Panel("matches", sb.ToString());
    }

    private static string LoadingCard(ScanProgress? p)
    {
        if (p == null)
        {
            return "";
        }
        var pct = p.Total > 0 ? (int)Math.Round(100.0 * p.Done / p.Total) : 0;
        return L.T(
            $"""<div class="loading"><div class="spin"></div><div class="grow"><b>Analyse de tes replays… {p.Done} / {p.Total}</b><div class="pbar"><i style="width:{pct}%"></i></div><span class="muted">Cette page se met à jour toute seule : les chiffres augmentent au fil de la lecture des replays.</span></div></div>""",
            $"""<div class="loading"><div class="spin"></div><div class="grow"><b>Analyzing your replays… {p.Done} / {p.Total}</b><div class="pbar"><i style="width:{pct}%"></i></div><span class="muted">This page updates by itself: the numbers grow as the replays are read.</span></div></div>""",
            $"""<div class="loading"><div class="spin"></div><div class="grow"><b>Kan'analysi les replays dyalek… {p.Done} / {p.Total}</b><div class="pbar"><i style="width:{pct}%"></i></div><span class="muted">Had la page katbeddel bo7dha: l'ar9am kayzido m3a l9raya dyal les replays. Sber chwiya.</span></div></div>""", $"""<div class="loading"><div class="spin"></div><div class="grow"><b>Analizando tus repeticiones… {p.Done} / {p.Total}</b><div class="pbar"><i style="width:{pct}%"></i></div><span class="muted">Esta página se actualiza sola: las cifras crecen a medida que se leen las repeticiones.</span></div></div>""");
    }

    /// <summary>Rewrites only the OBS overlay page (called on every live update).</summary>
    public static void WriteOverlay(Store store, OverlayView view)
    {
        try
        {
            WriteAtomic(store.OverlayPath, Overlay(view));
        }
        catch
        {
            // the page is a convenience: never stop the tracker for it
        }
    }

    public static string Overlay(OverlayView v)
    {
        var rows = new StringBuilder();
        foreach (var r in v.Rows)
        {
            rows.Append($"<div class=\"row{(r.Treasure ? " t" : "")}\"><span>{H(r.Label)}</span><b>{H(r.Value)}{(r.Detail != null ? $" <small>{H(r.Detail)}</small>" : "")}</b></div>");
        }
        return $$"""
            <!doctype html><html lang="{{L.Code}}"><head><meta charset="utf-8"><meta http-equiv="refresh" content="10">
            <title>{{L.T("Overlay coffres", "Chest overlay", "Overlay dyal snadeq", "Overlay de cofres")}}</title>
            <style>
            html,body{margin:0;background:transparent;color:#ece9ff;font:500 16px/1.3 "Segoe UI",system-ui,sans-serif}
            .box{display:inline-block;min-width:260px;padding:14px 18px 12px;border-radius:16px;background:rgba(18,15,36,.86);border:1px solid #3b2f6b;text-shadow:0 1px 3px rgba(0,0,0,.6)}
            .head{display:flex;justify-content:space-between;gap:16px;font:700 12px "Segoe UI",sans-serif;letter-spacing:.08em;color:#c4b5fd}
            .live{color:#22d3ee}
            .counters{display:flex;align-items:stretch;gap:18px;margin:4px 0 10px}
            .counters>div{display:flex;flex-direction:column}
            .sep{width:1px;background:#2e2654;margin:8px 0 2px}
            .big{font-size:40px;font-weight:800;line-height:1.1;font-variant-numeric:tabular-nums}
            .total{color:#fbbf24}.cur{color:#67e8f9}.none{color:#4b4470}
            .cap{font-size:11px;font-weight:600;color:#a59fc9}
            .rows{border-top:1px solid #2e2654;padding-top:8px;display:grid;grid-template-columns:auto 1fr;gap:4px 12px}
            .row{display:contents}.row span{font-size:13px;font-weight:600;color:#a59fc9}.row b{font-weight:500}
            .row.t b{color:#c4b5fd}.row small{font-size:11px;font-weight:600;color:#a59fc9}
            .sig{margin-top:8px;text-align:right;font-size:10px;font-weight:600;letter-spacing:.06em;color:#22d3ee}
            </style></head><body><div class="box">
            <div class="head"><span>{{L.T("COFFRES", "CHESTS", "SNADEQ", "COFRES")}}</span>{{(v.Badge != null ? $"<span class=\"live\">{H(v.Badge)}</span>" : "")}}</div>
            <div class="counters">
              <div><span class="big total">{{H(v.Total)}}</span><span class="cap">total</span></div>
              <div class="sep"></div>
              <div><span class="big {{(v.Current == null ? "none" : v.CurrentLive ? "cur" : "")}}">{{H(v.Current ?? "—")}}</span><span class="cap">{{H(v.CurrentCaption)}}</span></div>
            </div>
            <div class="rows">{{rows}}</div>
            <div class="sig">by {{H(AppInfo.Author)}}</div>
            </div></body></html>
            """;
    }

    public static string Csv(TrackerData data)
    {
        var sb = new StringBuilder();
        // The first columns never move (existing spreadsheets keep working); team columns come after them, then the
        // session id (same header in every language: a teammate's CSV is matched on it when imported).
        sb.AppendLine(L.T("date;mode;joueur;place;joueurs;coffres;caisses_munitions;tresors;fichier;taille_equipe;coffres_equipe_vus;coffres_equipe_importes;session_id",
            "date;mode;player;placement;players;chests;ammo_boxes;treasures;file;team_size;team_chests_seen;team_chests_imported;session_id",
            "date;mode;joueur;blassa;joueurs;snadeq;caisses_munitions;knouz;fichier;taille_equipe;snadeq_equipe_vus;snadeq_equipe_importes;session_id", "date;mode;player;placement;players;chests;ammo_boxes;treasures;file;team_size;team_chests_seen;team_chests_imported;session_id"));

        var index = new ImportIndex(Team.Imports);
        foreach (var m in data.Matches.OrderBy(When))
        {
            var t = string.Join(" + ", m.TreasureFinds.Select(f => f.DisplayName));
            // Empty (not 0) when unknown: no team data for matches analyzed before teammates were tracked (analysis version 16).
            var imported = Team.MatesOf(m, index).Where(x => x.Imported != null).Select(x => x.Imported!.ChestsOpened).ToList();
            sb.AppendLine(string.Join(";",
                When(m).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
                Pretty.Playlist(m.Playlist), m.PlayerName, m.Placement, m.TotalPlayers,
                m.ChestsOpened, m.AmmoBoxesOpened, t, m.ReplayFile,
                m.TeamSize, m.TeamChestsSeen, imported.Count > 0 ? imported.Sum() : (int?)null, m.SessionId));
        }
        return sb.ToString();
    }
}
