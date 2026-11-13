using System.Globalization;
using System.Text;

namespace ChestCounter;

public static partial class ReportWriter
{
    private const string ItemsStyle = """
        .igrid{display:grid;grid-template-columns:repeat(auto-fill,minmax(250px,1fr));gap:14px}
        .item{position:relative;display:flex;flex-direction:column;background:var(--bg2);border:1px solid var(--line);border-radius:12px;overflow:hidden;min-width:0}
        .item.got{border-color:var(--ok);box-shadow:0 0 0 1px var(--ok) inset}
        .item .pic{position:relative;height:132px;display:flex;align-items:center;justify-content:center;background:radial-gradient(circle at 50% 40%,#3b2a7a 0%,#1a1533 70%)}
        .item .pic img{max-width:100%;max-height:100%;object-fit:contain}
        .item .pic svg{width:72px;height:72px}
        .item .badge{position:absolute;top:8px;left:8px;font:700 11px Bahnschrift,sans-serif;letter-spacing:.08em;text-transform:uppercase;padding:3px 8px;border-radius:6px;background:var(--ok);color:#06281c}
        .item .info{padding:10px 12px 4px;display:flex;flex-direction:column;gap:4px;flex:1}
        .item .nm{font-weight:700;font-size:15px;line-height:1.25}
        .item .meta{font-size:12px;color:var(--cmuted)}
        .item .ch{font-size:13px}.item .ch b{color:var(--gold)}
        .item .found{font-size:12px;color:var(--ok)}
        .item .foot{display:flex;justify-content:space-between;align-items:center;gap:8px;padding:8px 12px 12px}
        .check{display:inline-flex;align-items:center;gap:8px;font-size:14px;font-weight:600;cursor:pointer;user-select:none}
        .check input{width:20px;height:20px;accent-color:var(--ok);cursor:pointer}
        .check input:disabled{cursor:default}
        .gstatus{font:700 11px Bahnschrift,sans-serif;letter-spacing:.1em;text-transform:uppercase;padding:4px 10px;border-radius:999px;border:1px solid var(--line);color:var(--cmuted)}
        .gstatus.now{border-color:var(--gold);color:var(--gold)}
        .progress{height:12px;background:var(--nobar);border-radius:6px;overflow:hidden;margin:10px 0 8px}
        .progress i{display:block;height:100%;background:linear-gradient(90deg,var(--accent),var(--ok))}
        .bigcount{font-size:44px;font-weight:700;line-height:1}
        .filters{display:inline-flex;gap:4px;background:var(--bg2);border:1px solid var(--line);border-radius:10px;padding:3px}
        .filters button{font:600 13px "Segoe UI",sans-serif;color:var(--cmuted);background:transparent;border:0;border-radius:7px;padding:6px 12px;cursor:pointer}
        .filters button.on{background:var(--accent);color:#fff}
        body.f-missing .item.got,body.f-got .item:not(.got){display:none}
        .form{display:flex;flex-wrap:wrap;gap:10px;align-items:flex-end;margin-top:12px}
        .form label{display:flex;flex-direction:column;gap:4px;font-size:12px;color:var(--cmuted)}
        .form input[type=number]{width:110px}
        .form input[type=time]{font:inherit;font-size:14px;color:var(--cink);background:var(--field);border:1px solid var(--line);border-radius:8px;padding:7px 10px}
        details.add{border:0;padding:0;margin:0}
        details.add>summary{list-style:none;display:inline-flex}
        details.add>summary::-webkit-details-marker{display:none}
        .hourlist{display:flex;flex-direction:column;gap:6px;margin:8px 0 4px}
        .hourlist div{display:flex;flex-wrap:wrap;justify-content:space-between;align-items:center;gap:8px;background:var(--bg2);border:1px solid var(--line);border-radius:8px;padding:6px 10px;font-size:13px}
        .hourchip{display:inline-flex;align-items:center;gap:6px;padding:6px 12px;border-radius:999px;background:rgba(251,191,36,.12);border:1px solid var(--gold);color:var(--gold);font-size:13px;font-weight:600}
        .goal .v{font-size:36px;font-weight:700;line-height:1}
        """;

    /// <summary>
    /// Motion: only on real events (a tab opened, a number or a bar that changed, an item just ticked), never on the page's
    /// quiet refreshes. Everything stops when Windows asks for less motion.
    /// </summary>
    private const string MotionStyle = """
        @keyframes rise{from{opacity:0;transform:translateY(12px)}to{opacity:1;transform:none}}
        @keyframes spin{to{transform:rotate(360deg)}}
        @keyframes twinkle{0%,100%{opacity:.15;transform:scale(.5)}50%{opacity:1;transform:scale(1)}}
        @keyframes drift{0%,100%{background-position:0% 50%}50%{background-position:100% 50%}}
        @keyframes shine{from{background-position:130% 0}to{background-position:-130% 0}}
        @keyframes pop{0%{transform:scale(.4);opacity:0}60%{transform:scale(1.15);opacity:1}100%{transform:scale(1)}}
        @keyframes glint{from{background-position:200% 0}to{background-position:-200% 0}}
        .panel.enter>*{animation:rise .45s cubic-bezier(.2,.8,.2,1) both}
        .panel.enter>*:nth-child(2){animation-delay:.05s}.panel.enter>*:nth-child(3){animation-delay:.1s}.panel.enter>*:nth-child(4){animation-delay:.15s}
        .panel.enter>*:nth-child(5){animation-delay:.2s}.panel.enter>*:nth-child(n+6){animation-delay:.25s}
        .logo .swirl{transform-origin:50px 50px;animation:spin 16s linear infinite}
        .logo .spark{transform-origin:71px 29px;animation:twinkle 2.6s ease-in-out infinite}
        .brand:hover .logo .swirl{animation-duration:3s}
        .tile.hero{background:linear-gradient(135deg,#2a1b5e,#43248f 55%,#1c1442);background-size:220% 220%;animation:drift 14s ease-in-out infinite}
        .box,.tile,.item,.step{transition:transform .2s ease,box-shadow .2s ease,border-color .2s ease}
        .box:hover{border-color:#3f3478}
        .tile:hover,.item:hover,.step:hover{transform:translateY(-3px);box-shadow:0 12px 28px rgba(0,0,0,.35)}
        .tile.hero:hover{box-shadow:0 12px 34px rgba(139,92,246,.35)}
        .btn{transition:transform .12s ease,filter .15s ease,border-color .15s ease,background .15s ease}
        .btn:active{transform:scale(.96)}
        .btn.primary{background:linear-gradient(135deg,#8b5cf6,#6d28d9)}
        nav.tabs button{transition:background .2s ease,color .2s ease}
        .progress i,.tokenbar i,.pbar i,.meter i{transition:width 1s cubic-bezier(.2,.8,.2,1)}
        .item.got{background-image:linear-gradient(110deg,transparent 35%,rgba(251,191,36,.10) 48%,transparent 62%);background-size:260% 100%;animation:shine 4s ease-in-out infinite}
        .item.just .badge{animation:pop .55s cubic-bezier(.3,1.5,.5,1) both}
        .item.just{box-shadow:0 0 0 2px var(--ok),0 0 26px rgba(52,211,153,.35)}
        .oddshead .pv,.tile .v.gold,.tile.hero .v{background:linear-gradient(90deg,#fbbf24 0%,#fde68a 25%,#fbbf24 50%,#f59e0b 100%);background-size:200% auto;-webkit-background-clip:text;background-clip:text;color:transparent;animation:glint 6s linear infinite}
        .status.live{animation:none;box-shadow:0 0 0 0 rgba(244,63,94,.5);transition:box-shadow .3s}
        .bump{animation:pop .4s ease both}
        @media (prefers-reduced-motion:reduce){*,*::before,*::after{animation:none!important;transition:none!important}}
        """;

    /// <summary>A drawn icon for each kind of item (when no picture exists).</summary>
    private static string TypeIcon(string type)
    {
        var (color, shape) = type switch
        {
            "outfit" => ("#fbbf24", "<circle cx='32' cy='19' r='9'/><path d='M14 55c0-11 8-19 18-19s18 8 18 19z'/>"),
            "pickaxe" => ("#c4b5fd", "<path d='M10 24c12-12 32-12 44 0'/><path d='M32 17v40'/>"),
            "backbling" => ("#c4b5fd", "<rect x='16' y='18' width='32' height='38' rx='9'/><path d='M24 18v-4a8 8 0 0 1 16 0v4M22 36h20'/>"),
            "loading" => ("#22d3ee", "<rect x='8' y='14' width='48' height='36' rx='5'/><path d='M12 46l14-14 10 10 6-6 10 10'/><circle cx='44' cy='24' r='4'/>"),
            "jam" => ("#22d3ee", "<path d='M26 46V14l24-6v30'/><circle cx='20' cy='46' r='7'/><circle cx='44' cy='40' r='7'/>"),
            "car" => ("#c4b5fd", "<path d='M8 40v-8l8-10h26l12 10h4v8z'/><circle cx='20' cy='42' r='6'/><circle cx='46' cy='42' r='6'/>"),
            "decal" => ("#fbbf24", "<path d='M32 8l7 15 16 2-12 11 3 16-14-8-14 8 3-16-12-11 16-2z'/>"),
            "wheels" => ("#c4b5fd", "<circle cx='32' cy='32' r='22'/><circle cx='32' cy='32' r='7'/><path d='M32 10v15M32 39v15M10 32h15M39 32h15'/>"),
            "token" => ("#fbbf24", "<circle cx='32' cy='32' r='23'/><circle cx='32' cy='32' r='16'/><path d='M32 22l3 7 7 3-7 3-3 7-3-7-7-3 7-3z'/>"),
            "gizmo" => ("#22d3ee", "<path d='M32 8l5 19 19 5-19 5-5 19-5-19-19-5 19-5z'/><circle cx='50' cy='14' r='3'/>"),
            _ => ("#c4b5fd", "<rect x='12' y='24' width='40' height='30' rx='4'/><path d='M8 18h48v8H8zM32 18v36M32 18c-6-10-16-6-10 0M32 18c6-10 16-6 10 0'/>"),
        };
        return $"<svg viewBox=\"0 0 64 64\" aria-hidden=\"true\" fill=\"none\" stroke=\"{color}\" stroke-width=\"3.5\" stroke-linecap=\"round\" stroke-linejoin=\"round\">{shape}</svg>";
    }

    private static string GroupName(ItemDef d) => d.Group switch
    {
        "w1" => L.T("Semaine 1", "Week 1", "Ssimana 1", "Semana 1"),
        "w2" => L.T("Semaine 2", "Week 2", "Ssimana 2", "Semana 2"),
        "season" => L.T("Toute la saison", "All season", "Saison kamla", "Toda la temporada"),
        _ => string.IsNullOrWhiteSpace(d.WeekLabel) ? L.T("Ajoutés par toi", "Added by you", "Zedtihom nta", "Añadidos por ti") : d.WeekLabel!,
    };

    private static string ItemsPanel(ItemsResult r, Config config, bool live, string imgBase)
    {
        var sb = new StringBuilder();
        var now = DateTime.UtcNow;
        string Day(DateTime utc) => OddsModel.Utc(utc).ToLocalTime().ToString(L.T("d MMM", "d MMM", "d MMM", "d MMM"), Fr);

        // The whole collection, with the filters and the "add an item" form.
        var pct = r.Total > 0 ? 100.0 * r.Got / r.Total : 0;
        var groups = r.Items.GroupBy(x => x.Def.Group).ToList();
        var chips = string.Concat(groups.Select(g => $"<span class=\"chip\">{H(GroupName(g.First().Def))}{L.T(" : ", ": ", ": ", ": ")}{g.Count(x => x.Got)} / {g.Count()}</span>"));
        var filters = "<div class=\"filters\" role=\"group\">"
            + $"<button type=\"button\" data-filter=\"all\">{L.T("Tout", "All", "Kolchi", "Todos")}</button>"
            + $"<button type=\"button\" data-filter=\"missing\">{L.T("Il me manque", "Still missing", "Mazal khassni", "Aún faltan")}</button>"
            + $"<button type=\"button\" data-filter=\"got\">{L.T("Je les ai", "Got", "3ndi", "Conseguidos")}</button></div>";
        var add = "";
        if (live)
        {
            add = $"<details class=\"add\"><summary class=\"btn small\">+ {L.T("Ajouter un objet", "Add an item", "Zid chi 7aja", "Añadir un objeto")}</summary>"
                + "<div class=\"form\" id=\"add-item\">"
                + $"<label>{L.T("Nom", "Name", "Smiya", "Nombre")}<input type=\"text\" name=\"name\" maxlength=\"80\" placeholder=\"Solid Gold …\"></label>"
                + $"<label>{L.T("Type", "Type", "Nou3", "Tipo")}<select name=\"type\">{string.Concat(ItemsModel.Types.Select(t => $"<option value=\"{t}\">{H(ItemsModel.TypeName(t))}</option>"))}</select></label>"
                + $"<label>{L.T("Groupe", "Group", "Groupe", "Grupo")}<input type=\"text\" name=\"week\" maxlength=\"40\" placeholder=\"{H(L.T("Semaine 3", "Week 3", "Ssimana 3", "Semana 3"))}\"></label>"
                + $"<label>{L.T("Du", "From", "Mn", "De")}<input type=\"date\" name=\"from\"></label>"
                + $"<label>{L.T("Au", "To", "7ta", "A")}<input type=\"date\" name=\"to\"></label>"
                + $"<label>{L.T("1 coffre sur", "1 chest in", "1 sndouq f", "1 cofre entre")}<input type=\"number\" name=\"odds\" min=\"1\" step=\"1\"></label>"
                + Button("add-item", L.T("Ajouter", "Add", "Zid", "Añadir"), cls: "primary", form: "#add-item")
                + "</div></details>";
        }
        sb.Append(Box(L.T("Ta collection", "Your collection", "L'collection dyalek", "Tu colección"),
            L.T("Coche ce que tu as eu : c'est enregistré. Ce que l'assistant voit dans tes replays se coche tout seul.",
                "Tick what you got: it is saved. What the assistant sees in your replays ticks itself.",
                "Coche dakchi li jak: kaytsejjel. Dakchi li kaychouf l'assistant f les replays kaytcocha bo7do.", "Marca lo que conseguiste: se guarda. Lo que el asistente ve en tus repeticiones se marca solo."),
            $"<div class=\"bigcount num\">{Counter("items", r.Got)} <span class=\"muted\" style=\"font-size:24px\">/ {r.Total}</span></div>"
            + $"<div class=\"progress\" role=\"img\" aria-label=\"{r.Got} / {r.Total}\"><i data-bar=\"items\" style=\"width:{pct.ToString("0.#", CultureInfo.InvariantCulture)}%\"></i></div>"
            + $"<div class=\"btns\" style=\"justify-content:space-between\"><div>{chips}</div>{filters}</div>{add}",
            null));

        if (r.UnknownPrizes.Count > 0)
        {
            var dates = string.Join(", ", r.UnknownPrizes.Select(d => H(L.DateTimeShort(OddsModel.Utc(d).ToLocalTime()))));
            sb.Append($"<div class=\"callout\">{L.T($"Tes replays montrent {L.Count(r.UnknownPrizes.Count, "prix de coffre doré", "prix de coffre doré", "", "", "", "", "", "")} que l'assistant ne sait pas encore nommer ({dates}). Si tu sais lequel c'était, coche-le ci-dessous.",
                $"Your replays show {L.Count(r.UnknownPrizes.Count, "", "", "golden chest prize", "golden chest prizes", "", "", "premio de cofre dorado", "premios de cofre dorado")} the assistant cannot name yet ({dates}). If you know which one it was, tick it below.",
                $"Les replays dyalek fihom {L.Count(r.UnknownPrizes.Count, "", "", "", "", "cadeau dyal sndouq dhabi", "cadeaux dyal sndouq dhabi", "", "")} ma 3refch l'assistant smiyto ({dates}). Ila 3reftih, cochih lta7t.", $"Tus repeticiones muestran {L.Count(r.UnknownPrizes.Count, "", "", "", "", "", "", "premio de cofre dorado", "premios de cofre dorado")} que el asistente aún no sabe nombrar ({dates}). Si sabes cuál era, márcalo abajo.")}</div>");
        }

        // One part per group: the week on now first, then the whole season, then the others (latest first).
        int Order(IGrouping<string, ItemState> g)
        {
            var d = g.First().Def;
            if (d.Group == "season") return 1;
            if (d.StartUtc is DateTime s && d.EndUtc is DateTime e) return now >= s && now < e ? 0 : now < s ? 2 : 3;
            return 2;
        }
        foreach (var g in groups.OrderBy(Order).ThenByDescending(g => g.First().Def.StartUtc ?? DateTime.MaxValue))
        {
            var d0 = g.First().Def;
            string status, sub;
            if (d0.Group == "season")
            {
                status = $"<span class=\"gstatus now\">{L.T("Toute la saison", "All season", "Saison kamla", "Toda la temporada")}</span>";
                sub = L.T($"Dans les coffres chaque semaine, jusqu'à la fin de la chasse ({Day(config.Odds.HuntEndUtc)}).",
                    $"In the chests every week, until the end of the hunt ({Day(config.Odds.HuntEndUtc)}).",
                    $"F snadeq kol ssimana, 7ta l lekher dyal la chasse ({Day(config.Odds.HuntEndUtc)}).", $"En los cofres cada semana, hasta el final de la búsqueda ({Day(config.Odds.HuntEndUtc)}).");
            }
            else if (d0.StartUtc is DateTime s && d0.EndUtc is DateTime e)
            {
                var on = now >= s && now < e;
                status = on ? $"<span class=\"gstatus now\">{L.T("En ce moment", "Now", "Daba", "Ahora")}</span>"
                    : now < s ? $"<span class=\"gstatus\">{L.T("À venir", "Coming", "Jaya", "Próximamente")}</span>"
                    : $"<span class=\"gstatus\">{L.T("Terminée", "Over", "Salat", "Terminado")}</span>";
                sub = L.T($"Dans les coffres du {H(L.DateTimeShort(OddsModel.Utc(s).ToLocalTime()))} au {H(L.DateTimeShort(OddsModel.Utc(e).ToLocalTime()))} (heure de ton PC).",
                    $"In the chests from {H(L.DateTimeShort(OddsModel.Utc(s).ToLocalTime()))} to {H(L.DateTimeShort(OddsModel.Utc(e).ToLocalTime()))} (your PC's time).",
                    $"F snadeq mn {H(L.DateTimeShort(OddsModel.Utc(s).ToLocalTime()))} 7ta {H(L.DateTimeShort(OddsModel.Utc(e).ToLocalTime()))} (sa3a dyal PC dyalek).", $"En los cofres del {H(L.DateTimeShort(OddsModel.Utc(s).ToLocalTime()))} al {H(L.DateTimeShort(OddsModel.Utc(e).ToLocalTime()))} (hora de tu PC).");
            }
            else
            {
                status = "";
                sub = L.T("Ajoutés par toi.", "Added by you.", "Zedtihom nta.", "Añadido por ti.");
            }
            var cards = new StringBuilder("<div class=\"igrid\">");
            foreach (var x in g)
            {
                cards.Append(ItemCard(x, live, imgBase));
            }
            cards.Append("</div>");
            sb.Append(Box($"{H(GroupName(d0))} <span class=\"muted\" style=\"font-size:15px\">· {g.Count(x => x.Got)} / {g.Count()}</span>", sub, cards.ToString(), status));
        }

        sb.Append($"<p class=\"items\">* {L.T("Chances données par des sites de fans, non confirmées par Epic. Celles du jeton, du véhicule et de la tenue dorée sont les chances publiées (modifiables dans les réglages).",
            "Odds given by fan sites, not confirmed by Epic. Those of the token, the vehicle and the gold outfit are the published odds (editable in the settings).",
            "Zher li 3tawh sites dyal les fans, Epic ma confirmahch. Dyal l'jeton, tomobil w lbsa dhabia homa li nachrat Epic (katbeddlo f les réglages).", "Probabilidades dadas por webs de fans, no confirmadas por Epic. Las de la ficha, el vehículo y el atuendo dorado son las probabilidades publicadas (editables en los ajustes).")} "
            + L.T("Ta chance : d'après les coffres comptés pendant la période de l'objet (le même choix que l'onglet Jeton). Images : illustrations d'Epic Games.",
                "Your chance: from the chests counted while the item was in the chests (the same choice as the Token tab). Pictures: Epic Games' art.",
                "Zher dyalek: mn snadeq m7soubin f l'wa9t dyal dak l7aja (nefs l'ikhtiyar dyal l'onglet Jeton). Tsawer: rsoumat Epic Games.", "Tu probabilidad: a partir de los cofres contados mientras el objeto estaba en los cofres (la misma elección que en la pestaña Ficha). Imágenes: arte de Epic Games.")
            + "</p>");
        return Panel("items", sb.ToString());
    }

    private static string ItemCard(ItemState x, bool live, string imgBase)
    {
        var d = x.Def;
        var id = H(d.Id);
        // Built-in pictures only (from the exe): the player cannot change them.
        var pic = ItemImages.FileName(d) is { } file
            ? $"<img src=\"{H(imgBase + file)}\" alt=\"{H(d.Name)}\" loading=\"lazy\">"
            : TypeIcon(d.Type);
        var odds = x.Odds is int n ? $" · {H(L.OneIn(1.0 / n))}{(x.OddsPublished || !d.Builtin ? "" : "*")}" : "";
        var chance = x.Chance is double c ? $"<div class=\"ch\">{L.T("Ta chance", "Your chance", "Zher dyalek", "Tu probabilidad")}{L.T(" : ", ": ", ": ", ": ")}<b class=\"num\">{H(L.Pct(c))}</b></div>" : "";
        var found = "";
        if (x.Auto is { } a && !x.Unticked)
        {
            found = $"<div class=\"found\">✔ {L.T("Vu dans ta partie du", "Seen in your match of", "Chefto f partiya dyal", "Visto en tu partida del")} {H(L.DateTimeShort(OddsModel.Utc(a.FirstUtc).ToLocalTime()))}"
                + (a.Count > 1 ? $" (×{a.Count})" : "")
                + (a.Probable ? L.T(" · probablement", " · probably", " · ghaliban", " · probablemente") : "")
                + (a.ViaTeammate ? L.T(" · grâce à un coéquipier", " · from a teammate", " · mn 3and sa7bek", " · de un compañero") : "") + "</div>";
        }
        else if (x.Auto != null && x.Unticked)
        {
            found = $"<div class=\"items\">{L.T("Vu dans un replay, décoché par toi", "Seen in a replay, unticked by you", "Chefto f replay, w nta décochitih", "Visto en una repetición, desmarcado por ti")}</div>";
        }
        var check = live
            ? $"<label class=\"check\"><input type=\"checkbox\" data-set=\"item:{id}\"{(x.Got ? " checked" : "")}> {(x.Got ? L.T("Je l'ai", "Got it", "3ndi", "Lo tengo") : L.T("Pas encore", "Not yet", "Mazal", "Todavía no"))}</label>"
            : $"<label class=\"check\"><input type=\"checkbox\" disabled{(x.Got ? " checked" : "")}> {(x.Got ? L.T("Je l'ai", "Got it", "3ndi", "Lo tengo") : L.T("Pas encore", "Not yet", "Mazal", "Todavía no"))}</label>";
        var remove = live && !d.Builtin
            ? Button("remove-item", L.T("Retirer", "Remove", "7iyed", "Quitar"), d.Id, cls: "danger small", confirm: L.T($"Retirer « {d.Name} » de la liste ?", $"Remove \"{d.Name}\" from the list?", $"T7iyed «{d.Name}» mn la liste?", $"¿Quitar \"{d.Name}\" de la lista?"))
            : "";
        return $"<div class=\"item{(x.Got ? " got" : "")}\" data-id=\"{id}\"><div class=\"pic\">{(x.Got ? $"<span class=\"badge\">✔ {L.T("Obtenu", "Got", "3ndi", "Conseguidos")}</span>" : "")}{pic}</div>"
            + $"<div class=\"info\"><div class=\"nm\">{H(d.Name)}</div><div class=\"meta\">{H(ItemsModel.TypeName(d.Type))}{odds}</div>{chance}{found}</div>"
            + $"<div class=\"foot\">{check}{remove}</div></div>";
    }

    /// <summary>What the Discord picture shows (drawn by the page itself: nothing goes online), as JSON.</summary>
    private static string DiscordCard(string player, List<MatchRecord> matches, OddsResult odds, ItemsResult items, TrackerData data, Config config)
    {
        var card = new Dictionary<string, object?>
        {
            ["app"] = AppInfo.Name,
            ["by"] = $"by {AppInfo.Author}",
            ["player"] = player,
            ["chests"] = matches.Sum(m => m.ChestsOpened).ToString("N0", Fr),
            ["chestsLabel"] = L.T("coffres ouverts", "chests opened", "sndouq m7loul", "cofres abiertos"),
            ["matches"] = L.Count(matches.Count, "partie", "parties", "match", "matches", "partiya", "partiyat", "partida", "partidas"),
            ["best"] = L.T($"record {(matches.Count > 0 ? matches.Max(m => m.ChestsOpened) : 0)} en une partie", $"best {(matches.Count > 0 ? matches.Max(m => m.ChestsOpened) : 0)} in a match", $"record {(matches.Count > 0 ? matches.Max(m => m.ChestsOpened) : 0)} f partiya", $"récord: {(matches.Count > 0 ? matches.Max(m => m.ChestsOpened) : 0)} en una partida"),
            ["tokenLabel"] = L.T("Jeton de la boutique pirate", "Pirate Shop token", "Jeton dyal l7anout", "Ficha de la tienda pirata"),
            ["token"] = L.Pct(odds.Pirate.P),
            ["oneIn"] = L.OneIn(odds.Pirate.P),
            ["tokenP"] = odds.Pirate.P,
            ["itemsLabel"] = L.T("Objets obtenus", "Items collected", "L7wayej li 3ndi", "Objetos conseguidos"),
            ["items"] = $"{items.Got} / {items.Total}",
            ["itemsP"] = items.Total > 0 ? (double)items.Got / items.Total : 0,
            ["date"] = DateTime.Now.ToString(L.T("d MMMM yyyy", "d MMMM yyyy", "d MMMM yyyy", "d 'de' MMMM 'de' yyyy"), Fr),
        };
        if (Team.Imports.Sources.Count > 0 && OddsModel.Build(data, config, scopeOverride: "squad") is { Matches: > 0 } squad)
        {
            card["squadLabel"] = L.T("Escouade", "Squad", "Squad", "Escuadrón");
            card["squad"] = $"{L.Pct(squad.Pirate.P)} · {L.Count(squad.Matches, "partie", "parties", "match", "matches", "partiya", "partiyat", "partida", "partidas")}";
        }
        return System.Text.Json.JsonSerializer.Serialize(card);
    }
}
