using System.Globalization;
using System.Text;

namespace ChestCounter;

/// <summary>What the assistant is doing, shown at the top of the dashboard ("wait", "live", "count", "close", "nofolder").</summary>
public sealed record LiveStatus(string Kind, string Text);

/// <summary>A message the dashboard shows once (a file imported by a double-click…), and the tab to open with it.</summary>
public sealed record LiveNotice(int Id, string Text, bool Ok, string? Tab);

/// <summary>First launch in the dashboard: the step shown ("lang", "replays", "overlay", "fortnite", "ready").</summary>
public sealed record SetupInfo(string Step, int Number, int Steps, string DemosFolder, bool DemosFound, int Existing);

/// <summary>Everything only the live dashboard shows (report.html has none of it).</summary>
public sealed record LiveInfo(
    LiveStatus Status,
    IReadOnlyList<string> Activity,
    IReadOnlyList<string>? WhatsNew,
    bool OverlayRunning,
    bool OverlayVisible,
    bool OverlayLocked,
    bool AskProfile,
    LiveNotice? Notice,
    SetupInfo? Setup,
    bool CanUseExe,
    bool HasErrorLog);

public static partial class ReportWriter
{
    // ---- Settings (dashboard only) -------------------------------------------------------------

    private static string Set(string label, string control, string? desc) =>
        $"<div class=\"set\"><div class=\"t\"><div class=\"lbl\">{label}</div>{(desc != null ? $"<div class=\"desc\">{desc}</div>" : "")}</div><div class=\"ctl\">{control}</div></div>";

    private static string Toggle(string key, bool on, string label) =>
        $"<label class=\"tog\"><input type=\"checkbox\" data-set=\"{key}\" aria-label=\"{H(label)}\"{(on ? " checked" : "")}><span></span></label>";

    private static string Choice(string key, IEnumerable<(string Value, string Label)> options, string current) =>
        $"<select data-set=\"{key}\">" + string.Concat(options.Select(o => $"<option value=\"{H(o.Value)}\"{(o.Value == current ? " selected" : "")}>{H(o.Label)}</option>")) + "</select>";

    private static string NumberBox(string key, int value, int min) =>
        $"<input type=\"number\" min=\"{min}\" step=\"1\" data-set=\"{key}\" value=\"{value.ToString(CultureInfo.InvariantCulture)}\">";

    private static string DateBox(string key, DateTime utc) =>
        $"<input type=\"date\" data-set=\"{key}\" value=\"{OddsModel.Utc(utc).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}\">";

    private static string Pc(int p) => L.T($"{p} %", $"{p}%", $"{p} %", $"{p} %");

    private static string SettingsPanel(Config config, TrackerData data, IReadOnlyList<Profile> profiles, LiveInfo live)
    {
        var sb = new StringBuilder();
        var s = config.Odds;

        // General.
        var general = new StringBuilder();
        string Lbl(string fr, string en, string dr, string es) => H(L.T(fr, en, dr, es));
        general.Append(Set(Lbl("Langue", "Language", "Logha", "Idioma"), Choice("lang", L.Codes.Select(c => (c, L.NativeName(c))), L.Code),
            L.T("Langue du tableau de bord, du rapport et de l'overlay.", "Language of the dashboard, the report and the overlay.", "Logha dyal tableau de bord, le rapport w l'overlay.", "Idioma del panel, del informe y del overlay.")));
        if (profiles.Count >= 2)
        {
            general.Append(Set(Lbl("Profil", "Profile", "Profil", "Perfil"), ProfileSelect(config, data, profiles),
                L.T("Le compte Fortnite dont les statistiques sont affichées (plusieurs comptes ont joué sur ce PC).", "The Fortnite account whose statistics are shown (several accounts played on this PC).", "Le compte Fortnite li kaybanou les stats dyalo (bzaf d les comptes l3bou f had PC).", "La cuenta de Fortnite cuyas estadísticas se muestran (varias cuentas jugaron en este PC).")));
        }
        if (live.CanUseExe)
        {
            general.Append(Set(Lbl("Ouvrir avec Fortnite", "Open with Fortnite", "7ell m3a Fortnite", "Abrir con Fortnite"), Toggle("withFortnite", config.StartWithFortnite, "on"),
                L.T("Un tout petit processus invisible attend Fortnite et ouvre l'assistant, qui se ferme après. Mets « non » avant de supprimer ou déplacer ChestTokenAssistant.exe.",
                    "A tiny invisible process waits for Fortnite and opens the assistant, which closes afterwards. Turn it off before deleting or moving ChestTokenAssistant.exe.",
                    "Processus sghir w mkhbbi kaytsenna Fortnite w kay7ell l'assistant, li kaysed men b3d. Tfih 9bel ma tmse7 wla t7errek ChestTokenAssistant.exe.", "Un pequeño proceso invisible espera a Fortnite y abre el asistente, que se cierra después. Desactívalo antes de borrar o mover ChestTokenAssistant.exe.")));
            general.Append(Set(Lbl("Ouvrir les fichiers .chesttoken avec l'assistant", "Open .chesttoken files with the assistant", "7ell les fichiers .chesttoken b l'assistant", "Abrir los archivos .chesttoken con el asistente"), Toggle("shareFiles", config.OpenShareFiles, "on"),
                L.T("Un double-clic sur le fichier d'un coéquipier l'importe. Seulement pour ton compte Windows, sans droits administrateur.", "Double-clicking a teammate's file imports it. For your Windows account only, no admin rights.", "Double-clic 3la l'fichier dyal sa7bek kayimportih. Ghir l compte Windows dyalek, bla droits admin.", "Hacer doble clic en el archivo de un compañero lo importa. Solo para tu cuenta de Windows, sin derechos de administrador.")));
        }
        general.Append(Set(Lbl("Objectif de coffres par jour", "Daily chest goal", "Objectif dyal snadeq f nhar", "Objetivo de cofres por día"), NumberBox("dailyGoal", config.DailyGoal, 0),
            L.T("Une barre de progression dans l'accueil et une ligne « Aujourd'hui » sur l'overlay. 0 : pas d'objectif.", "A progress bar on Home and a \"Today\" line on the overlay. 0: no goal.", "Barre f Dar w ster «Lyouma» f l'overlay. 0: bla objectif.", "Una barra de progreso en Inicio y una línea \"Hoy\" en el overlay. 0: sin objetivo.")));
        general.Append(Set(Lbl("Dossier des replays", "Replays folder", "Dossier dyal les replays", "Carpeta de repeticiones"),
            $"<input type=\"text\" id=\"demos-folder\" value=\"{H(config.DemosFolder)}\" spellcheck=\"false\">" + Button("set", L.T("Enregistrer", "Save", "Sejjel", "Guardar"), "demos", from: "#demos-folder", cls: "small"),
            L.T("Là où Fortnite enregistre ses replays. À changer seulement si tu l'as déplacé.", "Where Fortnite saves its replays. Only change it if you moved it.", "Fin Fortnite kaykhbbi les replays dyalo. Beddlo ghir ila 7errektih.", "Donde Fortnite guarda sus repeticiones. Cámbialo solo si lo moviste.")));
        sb.Append(Box(L.T("Général", "General", "3am", "General"), L.T("La langue et le démarrage de l'assistant.", "The language and how the assistant starts.", "Logha w kifach kayt7ell l'assistant.", "El idioma y cómo arranca el asistente."), general.ToString()));

        // Overlay.
        var overlay = new StringBuilder();
        overlay.Append(Set(Lbl("Overlay en jeu", "In-game overlay", "Overlay f le jeu", "Overlay en el juego"), Toggle("overlay", config.ShowOverlay, "on"),
            L.T("Une petite fenêtre toujours au premier plan qui affiche tes coffres pendant que tu joues.", "A small always-on-top window that shows your chests while you play.", "Fenêtre sghira dima lfo9 katwerrik snadeq dyalek w nta kat'joui.", "Una pequeña ventana siempre visible que muestra tus cofres mientras juegas.")));
        if (config.ShowOverlay)
        {
            overlay.Append(Set(Lbl("Style", "Style", "Style", "Estilo"), Choice("overlayStyle", new[]
                {
                    ("wide", L.T("large (tout en un coup d'œil)", "wide (everything at a glance)", "3rid (kolchi f nadra)", "ancho (todo de un vistazo)")),
                    ("hud", L.T("HUD (intégré au jeu)", "HUD (blends into the game)", "HUD (mdmouj f le jeu)", "HUD (integrado en el juego)")),
                    ("classic", L.T("classique (carte sombre)", "classic (dark card)", "classique (carte k7la)", "clásico (tarjeta oscura)")),
                }, config.OverlayStyle),
                L.T("Large : un rectangle avec ton total, la partie en cours, l'objectif du jour, le jeton, le prochain cap et tes records. HUD : seulement les chiffres, sans fond. Classique : une carte sombre.",
                    "Wide: a rectangle with your total, the current match, today's goal, the token, the next milestone and your records. HUD: only the figures, no background. Classic: a dark card.",
                    "3rid: mostateel fih total, partiya daba, l'objectif dyal lyouma, l'jeton, jay w les records. HUD: ghir l'ar9am bla fond. Classique: carte k7la.", "Ancho: un rectángulo con tu total, la partida actual, el objetivo del día, la ficha, el próximo hito y tus récords. HUD: solo las cifras, sin fondo. Clásico: una tarjeta oscura.")));
            if (config.OverlayStyle == "wide")
            {
                overlay.Append(Set(Lbl("Records et caps", "Records and milestones", "Records w caps", "Récords e hitos"), Toggle("overlayMessages", config.OverlayMessages, "on"),
                    L.T("La ligne du bas : nouveau record en pleine partie, cap franchi (1 000, 2 500…), meilleure journée, Heure du trésor.",
                        "The bottom line: a record beaten mid-match, a milestone reached (1,000, 2,500…), your best day, a Treasure Hour.",
                        "Ster lta7t: record jdid f west partiya, cap (1 000, 2 500…), a7ssen nhar, Sa3at l'kenz.", "La línea de abajo: un récord batido en plena partida, un hito alcanzado (1000, 2500…), tu mejor día, una Hora del tesoro.")));
            }
            overlay.Append(Set(Lbl("Taille", "Size", "Taille", "Tamaño"), Choice("overlaySize", OverlayAppearance.Sizes.Select(v => (v.ToString(CultureInfo.InvariantCulture), Pc(v))), config.OverlaySize.ToString(CultureInfo.InvariantCulture)), null));
            overlay.Append(Set(Lbl("Opacité", "Opacity", "Opacité", "Opacidad"), Choice("overlayOpacity", OverlayAppearance.Opacities.Select(v => (v.ToString(CultureInfo.InvariantCulture), Pc(v))), config.OverlayOpacity.ToString(CultureInfo.InvariantCulture)), null));
        }
        overlay.Append(Set(Lbl("Chance du jeton sur l'overlay", "Token chance on the overlay", "Zher dyal l'jeton f l'overlay", "Probabilidad de la ficha en el overlay"), Toggle("tokenLine", s.OverlayTokenLine, "on"),
            L.T("Ajoute une ligne « Jeton : 9,02 % » à l'overlay et à overlay.html (OBS).", "Adds a \"Token: 9.02%\" line to the overlay and to overlay.html (OBS).", "Kayzid ster «Jeton: 9,02 %» l l'overlay w l overlay.html (OBS).", "Añade una línea \"Ficha: 9,02 %\" al overlay y a overlay.html (OBS).")));
        sb.Append(Box(L.T("Overlay en jeu", "In-game overlay", "Overlay f le jeu", "Overlay en el juego"),
            L.T("Raccourcis, même en jeu : Ctrl+Maj+O afficher / masquer, Ctrl+Maj+L verrouiller. Pour le voir, mets Fortnite en « Plein écran fenêtré ».",
                "Shortcuts, even in game: Ctrl+Shift+O show / hide, Ctrl+Shift+L lock. To see it, set Fortnite to \"Windowed Fullscreen\".",
                "Raccourcis, 7ta f le jeu: Ctrl+Shift+O werri / khebbi, Ctrl+Shift+L bloqui. Bach tchoufo, dir Fortnite f «Plein écran fenêtré».", "Atajos, incluso en el juego: Ctrl+Shift+O mostrar / ocultar, Ctrl+Shift+L bloquear. Para verlo, pon Fortnite en \"Pantalla completa en ventana\"."),
            overlay.ToString()));

        // Token odds.
        var key = Profiles.Current(data, config)?.Key ?? config.Profile ?? "";
        var baseline = s.Baselines.GetValueOrDefault(key) ?? new OddsBaseline();
        var odds = new StringBuilder();
        odds.Append(Set(Lbl("Coffres comptés", "Chests counted", "Snadeq m7soubin", "Cofres contados"), ScopeSelect(s.ChestScope), H(ScopeHelp(s.ChestScope))));
        odds.Append(Set(Lbl("Coéquipiers aléatoires (hors de ton groupe)", "Random teammates (not in your party)", "S7ab l'hasard (machi f groupe dyalek)", "Compañeros aleatorios (fuera de tu grupo)"), Toggle("fill", s.IncludeFill, "on"),
            L.T("Le jeton est partagé avec ton groupe ; avec un coéquipier aléatoire ce n'est pas confirmé.", "The token is shared with your party; with a random teammate it is not confirmed.", "L'jeton kaytqsem m3a groupe dyalek; m3a s7ab l'hasard mazal ma mconfirmich.", "La ficha se comparte con tu grupo; con un compañero aleatorio no está confirmado.")));
        odds.Append(Set(Lbl("Parties classées", "Ranked matches", "Partiyat classées", "Partidas clasificatorias"), Toggle("ranked", s.IncludeRanked, "on"),
            L.T("Le jeton ne tomberait pas en partie classée (non confirmé) : exclues par défaut.", "The token reportedly does not drop in ranked (unconfirmed): left out by default.", "L'jeton ma kaytla3ch f classée (ma mconfirmich): mkhrojin par défaut.", "Al parecer la ficha no sale en clasificatorias (sin confirmar): excluidas por defecto.")));
        odds.Append(Set(Lbl("Mes coffres avant le suivi", "My chests before tracking", "Snadeq dyali 9bel l'suivi", "Mis cofres antes del seguimiento"), NumberBox("baseMine", baseline.Mine, 0),
            L.T("Coffres ouverts avant que l'assistant les suive (replays supprimés…). Ajoutés aux chances.", "Chests opened before the assistant tracked them (deleted replays…). Added to the odds.", "Snadeq li 7elliti 9bel ma ybda l'assistant y7sebhom (replays tmse7ou…). Kayzado l zher.", "Cofres abiertos antes de que el asistente los contara (repeticiones borradas…). Se suman a las probabilidades.")));
        odds.Append(Set(Lbl("Coffres de l'équipe avant le suivi", "Team chests before tracking", "Snadeq dyal l'équipe 9bel l'suivi", "Cofres del equipo antes del seguimiento"), NumberBox("baseTeam", baseline.Team, 0),
            L.T("Ceux de tes coéquipiers dans ces mêmes parties, s'ils les connaissent.", "Your teammates' in those same matches, if they know them.", "Dyal s7abek f nefs les partiyat, ila 3arfinhom.", "Los de tus compañeros en esas mismas partidas, si los conocen.")));
        foreach (var def in TreasureDef.All)
        {
            odds.Append(Set(H($"{def.Name} · {L.T("1 coffre sur", "1 chest in", "1 sndouq f", "1 cofre entre")}"), NumberBox("odds-" + def.Id, def.Odds(s), 1),
                L.T("Chances de base publiées par Epic. À changer seulement si elles changent.", "Base odds published by Epic. Only change them if they change.", "Zher l'asli li nachrat Epic. Beddlo ghir ila tbeddel.", "Probabilidades base publicadas por Epic. Cámbialas solo si cambian.")));
        }
        odds.Append(Set(Lbl("Fin de la chasse", "End of the hunt", "Lekher dyal la chasse", "Fin de la búsqueda"), DateBox("huntEnd", s.HuntEndUtc),
            L.T("Pour le compte à rebours et la projection.", "For the countdown and the projection.", "L l'compte à rebours w la projection.", "Para la cuenta atrás y la proyección.")));
        odds.Append(Set(Lbl("Activer le jeton avant le", "Activate the token by", "Activi l'jeton 9bel", "Activar la ficha antes del"), DateBox("activateBy", s.ActivateByUtc),
            L.T("Date limite d'activation, rappelée dans l'onglet Jeton.", "Activation deadline, shown on the Token tab.", "Date limite dyal l'activation, mktouba f l'onglet Jeton.", "Fecha límite de activación, mostrada en la pestaña Ficha.")));
        sb.Append(Box(L.T("Chances du jeton", "Token odds", "Zher dyal l'jeton", "Probabilidades de la ficha"), L.T("Ce qui est compté pour calculer ta chance.", "What is counted to work out your chance.", "Chno kayt7seb bach n7sbo zher dyalek.", "Lo que se cuenta para calcular tu probabilidad."), odds.ToString()));

        // Treasure Hours: listed, added and removed here (no more editing config.json by hand).
        var hours = new StringBuilder();
        var now = DateTime.UtcNow;
        var list = s.TreasureHours.OrderBy(h => OddsModel.Utc(h.StartUtc)).ToList();
        if (list.Count == 0)
        {
            hours.Append($"<p class=\"items\">{L.T("Aucune pour l'instant. Epic les annonce (souvent le samedi soir) : ajoute-les ici avec l'heure de ton PC.", "None yet. Epic announces them (often on Saturday evenings): add them here in your PC's time.", "Mazal walou. Epic kay3len 3lihom (ghaliban s-sebt f lil): zidhom hna b sa3a dyal PC dyalek.", "Ninguna por ahora. Epic las anuncia (a menudo los sábados por la tarde): añádelas aquí en la hora de tu PC.")}</p>");
        }
        else
        {
            hours.Append("<div class=\"hourlist\">");
            foreach (var h in list)
            {
                var start = OddsModel.Utc(h.StartUtc).ToLocalTime();
                var end = OddsModel.Utc(h.EndUtc).ToLocalTime();
                var state = OddsModel.Utc(h.EndUtc) <= now ? $" <span class=\"items\">· {L.T("passée", "past", "fatet", "pasada")}</span>"
                    : OddsModel.Utc(h.StartUtc) <= now ? $" <b class=\"gold\">· {L.T("en cours", "now", "daba", "ahora")}</b>" : "";
                hours.Append($"<div><span><b>{H(start.ToString(L.T("ddd d MMM HH:mm", "ddd d MMM HH:mm", "ddd d MMM HH:mm", "ddd d MMM HH:mm"), Fr))}–{end:HH:mm}</b> · {H(OddsModel.BoostText(h))}{state}</span>"
                    + Button("remove-hour", L.T("Retirer", "Remove", "7iyed", "Quitar"), OddsModel.Utc(h.StartUtc).Ticks.ToString(CultureInfo.InvariantCulture), cls: "danger small") + "</div>");
            }
            hours.Append("</div>");
        }
        hours.Append("<div class=\"form\" id=\"add-hour\">"
            + $"<label>{L.T("Jour", "Day", "Nhar", "Día")}<input type=\"date\" name=\"date\" value=\"{DateTime.Now:yyyy-MM-dd}\"></label>"
            + $"<label>{L.T("De", "From", "Mn", "De")}<input type=\"time\" name=\"from\" value=\"21:00\"></label>"
            + $"<label>{L.T("À", "To", "7ta", "A")}<input type=\"time\" name=\"to\" value=\"23:00\"></label>"
            + $"<label>{L.T("Jeton ×", "Token ×", "Jeton ×", "Ficha ×")}<input type=\"number\" name=\"pirate\" value=\"1.5\" min=\"1\" max=\"100\" step=\"0.1\"></label>"
            + $"<label>{L.T("Véhicule ×", "Vehicle ×", "Tomobil ×", "Vehículo ×")}<input type=\"number\" name=\"vehicle\" value=\"1\" min=\"1\" max=\"100\" step=\"0.1\"></label>"
            + $"<label>{L.T("Tenue dorée ×", "Gold outfit ×", "Lbsa dhabia ×", "Atuendo dorado ×")}<input type=\"number\" name=\"gold\" value=\"1\" min=\"1\" max=\"100\" step=\"0.1\"></label>"
            + Button("add-hour", L.T("Ajouter", "Add", "Zid", "Añadir"), cls: "primary", form: "#add-hour")
            + "</div>");
        hours.Append($"<div class=\"note\">{L.T("Une notification arrive 10 minutes avant et au début de chaque Heure du trésor ; l'en-tête et l'overlay montrent le bonus pendant qu'elle dure. Ajoutées à la main dans config.json ?", "A notification comes 10 minutes before and at the start of each Treasure Hour; the header and the overlay show the boost while it lasts. Added by hand in config.json?", "Notification katji 10 d9aye9 9bel w f lbdya dyal kol Sa3at l'kenz; l'en-tête w l'overlay kaywerriw l'bonus 7ta tsali. Zedtihom b yeddik f config.json?", "Llega una notificación 10 minutos antes y al inicio de cada Hora del tesoro; el encabezado y el overlay muestran el aumento mientras dura. ¿Añadidas a mano en config.json?")} "
            + Button("reload-hours", L.T("Relire config.json", "Reload config.json", "3awed 9ra config.json", "Recargar config.json"), cls: "small") + "</div>");
        sb.Append(Box(L.T("Heures du trésor", "Treasure Hours", "Sa3at l'kenz", "Horas del tesoro"),
            L.T("Les heures où Epic augmente les chances. Les coffres ouverts pendant comptent plus.", "The hours when Epic boosts the odds. Chests opened during them count for more.", "Sa3at li kayzid fihom Epic zher. Snadeq li t7ello fihom kayt7sbo kter.", "Las horas en que Epic aumenta las probabilidades. Los cofres abiertos durante ellas cuentan más."),
            hours.ToString()));

        // Advanced.
        var advanced = new StringBuilder();
        if (live.CanUseExe)
        {
            advanced.Append(Set(Lbl("Arrêter les anciennes versions", "Stop old versions", "7bes les versions l9dam", "Detener las versiones antiguas"),
                Button("cleanup", L.T("Arrêter", "Stop them", "7besshom", "Detenerlas"), cls: "small"),
                L.T("Ferme les anciennes copies encore ouvertes (une version précédente, ChestCounter.exe…) et les empêche de se lancer avec Fortnite. Pour les anciennes copies de l'assistant, c'est aussi fait tout seul à chaque démarrage.",
                    "Closes old copies still running (a previous build, ChestCounter.exe…) and stops them from starting with Fortnite. Older copies of this assistant are also stopped by themselves at each start.",
                    "Kaysed les nskhat l9dam li ba9in khddamin (version 9dima, ChestCounter.exe…) w kaymn3hom yt7ello m3a Fortnite. Les nskhat l9dam dyal l'assistant kaytsedo bo7dhom f kol lancement.", "Cierra las copias antiguas que siguen abiertas (una versión anterior, ChestCounter.exe…) y evita que arranquen con Fortnite. Las copias antiguas de este asistente también se detienen solas en cada inicio.")));
        }
        advanced.Append(Set(Lbl("Recommencer la configuration", "Start the setup again", "3awed la configuration", "Volver a empezar la configuración"),
            Button("reset-setup", L.T("Recommencer", "Start again", "3awed", "Empezar de nuevo"), cls: "small", confirm: L.T("Effacer tes réglages et reposer les questions du premier lancement ? Ton historique est gardé.", "Clear your settings and ask the first-launch questions again? Your history is kept.", "Nmse7 les réglages dyalek w n3awed les questions dyal awel mra? L'historique kayb9a.", "¿Borrar tus ajustes y volver a hacer las preguntas del primer inicio? Tu historial se conserva.")),
            L.T("Efface tes réglages et repose les questions du premier lancement. Ton historique de parties est gardé.", "Clears your settings and asks the first-launch questions again. Your match history is kept.", "Kaymse7 les réglages w kay3awed les questions dyal awel mra. L'historique dyal les partiyat kayb9a.", "Borra tus ajustes y repite las preguntas del primer inicio. Tu historial de partidas se conserva.")));
        advanced.Append(Set(Lbl("Utiliser la console à la place (secours)", "Use the console instead (backup)", "Khdem b la console (secours)", "Usar la consola en su lugar (respaldo)"),
            Button("console", L.T("Ouvrir la console", "Open the console", "7ell la console", "Abrir la consola"), cls: "small", confirm: L.T("Redémarrer l'assistant dans la fenêtre de commandes ? Le tableau de bord revient au prochain lancement.", "Restart the assistant in the command window? The dashboard comes back at the next start.", "N3awed n7ell l'assistant f la fenêtre dyal les commandes? Tableau de bord kayrje3 f lbdya jaya.", "¿Reiniciar el asistente en la ventana de comandos? El panel vuelve en el próximo inicio.")),
            L.T("L'ancienne interface au clavier, si jamais le tableau de bord pose un problème.", "The old keyboard interface, in case the dashboard ever has a problem.", "L'interface l9dima b le clavier, ila kan chi mouchkil f tableau de bord.", "La antigua interfaz de teclado, por si el panel tiene algún problema.")));
        advanced.Append(Set(Lbl("Quitter l'assistant", "Quit the assistant", "Khrej mn l'assistant", "Salir del asistente"),
            Button("quit", L.T("Quitter", "Quit", "Khrej", "Salir"), cls: "danger small", confirm: L.T("Quitter l'assistant ? Les coffres ne seront plus comptés jusqu'au prochain lancement.", "Quit the assistant? Chests are no longer counted until the next start.", "Tkhrej mn l'assistant? Snadeq ma yb9awch yt7sbo 7ta t3awed t7llo.", "¿Salir del asistente? Los cofres dejan de contarse hasta el próximo inicio.")),
            L.T("Fermer cette fenêtre ne l'arrête pas : il continue de compter dans la zone de notification (icône du jeton près de l'horloge).", "Closing this window does not stop it: it keeps counting in the notification area (token icon near the clock).", "Ila sddti had la fenêtre ma kaywe9fch: kayb9a y7seb f la zone de notification (icône dyal l'jeton 7da sa3a).", "Cerrar esta ventana no lo detiene: sigue contando en el área de notificación (icono de ficha junto al reloj).")));
        sb.Append(Box(L.T("Avancé", "Advanced", "Avancé", "Avanzado"), L.T("À utiliser seulement si besoin.", "Only when needed.", "Ghir ila 7tajiti.", "Solo cuando haga falta."), advanced.ToString()));
        return Panel("settings", sb.ToString());
    }

    // ---- First launch (dashboard only) ---------------------------------------------------------

    private static string SetupPanel(SetupInfo s)
    {
        var sb = new StringBuilder("<section class=\"box wiz\">");
        sb.Append($"<div class=\"stepn\">{L.T("Étape", "Step", "Marhala", "Paso")} {s.Number} / {s.Steps}</div>");
        switch (s.Step)
        {
            case "lang":
                sb.Append("<h2 class=\"ttl\">Choisis ta langue · Choose your language · Khtar logha dyalek · Elige tu idioma</h2>");
                sb.Append("<p>Tu pourras la changer dans les réglages · You can change it in the settings · T9der tbeddelha f les réglages</p>");
                sb.Append("<div class=\"langs\">" + string.Concat(L.Codes.Select(c => Button("setup", L.NativeName(c), "lang", c, c == L.Code ? "primary" : ""))) + "</div>");
                break;

            case "replays":
                sb.Append($"<h2 class=\"ttl\">{L.T("Tes replays", "Your replays", "Les replays dyalek", "Tus repeticiones")}</h2>");
                sb.Append(s.DemosFound
                    ? $"<p><span class=\"ok\">✔ {L.T("Dossier des replays trouvé :", "Replays folder found:", "L9it dossier dyal les replays:", "Carpeta de repeticiones encontrada:")}</span><br><code>{H(s.DemosFolder)}</code></p>"
                    : $"<p><span class=\"no\">✖ {L.T("Dossier des replays introuvable :", "Replays folder not found:", "Dossier dyal les replays ma kaynch:", "Carpeta de repeticiones no encontrada:")}</span><br><code>{H(s.DemosFolder)}</code><br>"
                      + L.T("Fortnite le crée à la première partie enregistrée. Si tu viens d'activer les replays, c'est normal.", "Fortnite creates it with the first recorded match. If you just turned replays on, that is expected.", "Fortnite kaycréyih f awel partiya mregistrya. Ila ba9i 3ad 7elliti les replays, normal.", "Fortnite la crea con la primera partida grabada. Si acabas de activar las repeticiones, es normal.") + "</p>"
                      + $"<div class=\"btns\"><input type=\"text\" id=\"demos-wiz\" value=\"{H(s.DemosFolder)}\" spellcheck=\"false\">{Button("setup", L.T("Utiliser ce dossier", "Use this folder", "Khdem b had dossier", "Usar esta carpeta"), "demos", from: "#demos-wiz", cls: "small")}</div>");
                sb.Append($"<div class=\"callout\"><b>{L.T("Important", "Important", "Mohim", "Importante")}</b>{L.T(" : dans Fortnite, active ", ": in Fortnite, turn on ", ": f Fortnite, 7ell ", ": en Fortnite, activa ")}<b>{L.T("Paramètres → Jeu → Rediffusions → Enregistrer les rediffusions", "Settings → Game → Replays → Record Replays", "Paramètres → Jeu → Rediffusions → Enregistrer les rediffusions", "Configuración → Juego → Repeticiones → Grabar repeticiones")}</b>. "
                    + L.T("Sans ça, il n'y a rien à compter.", "Without it, there is nothing to count.", "Bla hadchi, ma kayn ma yt7seb.", "Sin eso, no hay nada que contar.") + "</div>");
                if (s.Existing > 0)
                {
                    var present = L.Count(s.Existing, "replay est déjà sur ton PC", "replays sont déjà sur ton PC", "replay is already on your PC", "replays are already on your PC", "replay déjà kayn f PC dyalek", "replays déjà kaynin f PC dyalek", "repetición ya está en tu PC", "repeticiones ya están en tu PC");
                    sb.Append($"<p><b>{present}.</b> {L.T("Les compter aussi ?", "Count them too?", "N7sbohom 7ta homa?", "¿Contarlos también?")}</p>");
                    sb.Append("<div class=\"btns\">" + Button("setup", L.T("Oui, les analyser maintenant", "Yes, analyze them now", "Ah, analysihom daba", "Sí, analizarlas ahora"), "existing", "1", "primary")
                        + Button("setup", L.T("Non, seulement mes prochaines parties", "No, only my next matches", "La, ghir les partiyat jayin", "No, solo mis próximas partidas"), "existing", "0") + "</div>");
                }
                else
                {
                    sb.Append("<div class=\"btns\">" + Button("setup", L.T("Continuer", "Continue", "Kemmel", "Continuar"), "existing", "0", "primary") + "</div>");
                }
                break;

            case "overlay":
                sb.Append($"<h2 class=\"ttl\">{L.T("Afficher un overlay en jeu ?", "Show an overlay in game?", "Bghiti overlay f le jeu?", "¿Mostrar un overlay en el juego?")}</h2>");
                sb.Append($"<p>{L.T("Une petite fenêtre toujours au premier plan affiche tes coffres pendant que tu joues. Sans overlay, tout reste dans le tableau de bord. Modifiable à tout moment dans les réglages.", "A small always-on-top window shows your chests while you play. Without it, everything stays in the dashboard. Can be changed at any time in the settings.", "Fenêtre sghira dima lfo9 katwerrik snadeq dyalek w nta kat'joui. Bla overlay, kolchi kayb9a f tableau de bord. T9der tbeddel f ay wa9t f les réglages.", "Una pequeña ventana siempre visible muestra tus cofres mientras juegas. Sin ella, todo queda en el panel. Se puede cambiar en cualquier momento en los ajustes.")}</p>");
                sb.Append("<div class=\"btns\">" + Button("setup", L.T("Oui, afficher l'overlay", "Yes, show the overlay", "Ah, werri l'overlay", "Sí, mostrar el overlay"), "overlay", "1", "primary")
                    + Button("setup", L.T("Non, seulement le tableau de bord", "No, only the dashboard", "La, ghir tableau de bord", "No, solo el panel"), "overlay", "0") + "</div>");
                break;

            case "fortnite":
                sb.Append($"<h2 class=\"ttl\">{L.T("Ouvrir l'assistant automatiquement quand Fortnite démarre ?", "Open the assistant automatically when Fortnite starts?", "N7ell l'assistant automatiquement mlli yt7ell Fortnite?", "¿Abrir el asistente automáticamente cuando se inicia Fortnite?")}</h2>");
                sb.Append($"<p>{L.T("Un tout petit processus invisible attend le lancement de Fortnite, puis ouvre l'assistant, qui se ferme tout seul après. Modifiable à tout moment dans les réglages.", "A tiny invisible process waits for Fortnite to start, then opens the assistant, which closes by itself afterwards. Can be changed at any time in the settings.", "Processus sghir w mkhbbi kaytsenna Fortnite, w men b3d kay7ell l'assistant, li kaysed rasso men b3d. T9der tbeddel f ay wa9t f les réglages.", "Un pequeño proceso invisible espera a que arranque Fortnite, luego abre el asistente, que se cierra solo después. Se puede cambiar en cualquier momento en los ajustes.")}</p>");
                sb.Append("<div class=\"btns\">" + Button("setup", L.T("Non, je le lancerai moi-même", "No, I will start it myself", "La, ghadi n7llo b yeddi", "No, lo abriré yo mismo"), "fortnite", "0", "primary")
                    + Button("setup", L.T("Oui, l'ouvrir avec Fortnite", "Yes, open it with Fortnite", "Ah, 7llo m3a Fortnite", "Sí, abrirlo con Fortnite"), "fortnite", "1") + "</div>");
                break;

            default:
                sb.Append($"<h2 class=\"ttl\">{L.T("C'est prêt !", "All set!", "Safi, kolchi wajed!", "¡Todo listo!")}</h2>");
                sb.Append($"<p>{L.T("Laisse l'assistant ouvert pendant que tu joues : les coffres de chaque partie sont comptés quelques secondes après sa fin.", "Keep the assistant running while you play: the chests of each match are counted a few seconds after it ends.", "Khelli l'assistant m7loul w nta kat'joui: snadeq dyal kol partiya kayt7sbo chi chwiya d les secondes men b3d ma tsali.", "Deja el asistente abierto mientras juegas: los cofres de cada partida se cuentan unos segundos después de terminar.")}</p>");
                sb.Append($"<p>{L.T("Fermer cette fenêtre ne l'arrête pas : il continue dans la zone de notification (l'icône du jeton près de l'horloge). Clique dessus pour revenir ici.", "Closing this window does not stop it: it keeps going in the notification area (the token icon near the clock). Click it to come back here.", "Ila sddti had la fenêtre ma kaywe9fch: kayb9a khdam f la zone de notification (l'icône dyal l'jeton 7da sa3a). Click 3liha bach trje3 hna.", "Cerrar esta ventana no lo detiene: sigue en el área de notificación (el icono de ficha junto al reloj). Haz clic en él para volver aquí.")}</p>");
                sb.Append("<div class=\"btns\">" + Button("setup", L.T("Ouvrir mon tableau de bord", "Open my dashboard", "7ell tableau de bord dyali", "Abrir mi panel"), "done", "1", "primary") + "</div>");
                break;
        }
        sb.Append("</section>");
        return sb.ToString();
    }

    // ---- Help ----------------------------------------------------------------------------------

    private static string HelpPanel(Config config, string dataFolder, LiveInfo? live)
    {
        var sb = new StringBuilder();
        sb.Append("<div class=\"grid2\">");
        sb.Append(Box(L.T("Comment ça marche", "How it works", "Kifach khdam", "Cómo funciona"), null,
            "<div class=\"help\">"
            + $"<p>{L.T("L'assistant lit les replays que Fortnite enregistre et compte tes coffres quelques secondes après la fin de chaque partie. Il ne touche pas au jeu et ne va jamais sur Internet.", "The assistant reads the replays Fortnite saves and counts your chests a few seconds after each match ends. It does not touch the game and never goes online.", "L'assistant kay9ra les replays li kaysejjel Fortnite w kay7seb snadeq dyalek chi chwiya men b3d kol partiya. Ma kaymessch le jeu w 3emmro ma kaydkhol l Internet.", "El asistente lee las repeticiones que guarda Fortnite y cuenta tus cofres unos segundos después de cada partida. No toca el juego y nunca se conecta a Internet.")}</p>"
            + $"<p>{L.T("Ce tableau de bord est une page que l'assistant sert à ton PC seulement (adresse 127.0.0.1) : personne d'autre ne peut l'ouvrir.", "This dashboard is a page the assistant serves to your PC only (address 127.0.0.1): nobody else can open it.", "Had tableau de bord page kay3tiha l'assistant ghir l PC dyalek (adresse 127.0.0.1): 7ta wa7ed akhor ma y9der y7elha.", "Este panel es una página que el asistente sirve solo a tu PC (dirección 127.0.0.1): nadie más puede abrirla.")}</p>"
            + $"<p>{L.T("Fermer la fenêtre ne l'arrête pas : il continue dans la zone de notification (icône du jeton près de l'horloge).", "Closing the window does not stop it: it keeps going in the notification area (token icon near the clock).", "Ila sddti la fenêtre ma kaywe9fch: kayb9a khdam f la zone de notification (icône dyal l'jeton 7da sa3a).", "Cerrar la ventana no lo detiene: sigue en el área de notificación (icono de ficha junto al reloj).")}</p></div>"));
        sb.Append(Box(L.T("Rien n'est compté ?", "Nothing is counted?", "Walou ma kayt7seb?", "¿No se cuenta nada?"), null,
            "<div class=\"help\">"
            + $"<p>{L.T("Dans Fortnite, active ", "In Fortnite, turn on ", "F Fortnite, 7ell ", "En Fortnite, activa ")}<b>{L.T("Paramètres → Jeu → Rediffusions → Enregistrer les rediffusions", "Settings → Game → Replays → Record Replays", "Paramètres → Jeu → Rediffusions → Enregistrer les rediffusions", "Configuración → Juego → Repeticiones → Grabar repeticiones")}</b>.</p>"
            + $"<p>{L.T("L'assistant doit être ouvert quand la partie se termine. Les caisses de munitions et de nourriture ne sont pas des coffres.", "The assistant must be running when the match ends. Ammo and food boxes are not chests.", "L'assistant khasso ykoun m7loul mlli tsali partiya. Caisses dyal munitions w l'makla machi snadeq.", "El asistente tiene que estar abierto cuando termina la partida. Las cajas de munición y de comida no son cofres.")}</p>"
            + $"<p>{L.T("« Impossible de lire ce replay » : une mise à jour de Fortnite a changé le format ; les parties déjà comptées sont gardées.", "\"Cannot read this replay\": a Fortnite update changed the format; matches already counted are kept.", "«Ma 9dertch n9ra had le replay»: mise à jour dyal Fortnite beddlat le format; les partiyat li t7sbo kayb9aw.", "\"No se puede leer esta repetición\": una actualización de Fortnite cambió el formato; las partidas ya contadas se conservan.")}</p></div>"));
        sb.Append("</div>");

        sb.Append(Box(L.T("Comment la chance du jeton est calculée", "How the token chance is worked out", "Kifach kayt7seb zher dyal l'jeton", "Cómo se calcula la probabilidad de la ficha"),
            L.T("Pour les curieux.", "For the curious.", "L li bgha y3ref.", "Para los curiosos."),
            "<div class=\"help\">"
            + $"<p>{L.T("Chance = 1 − Π(1 − m × p) sur chaque coffre compté : p = chances publiées (1 sur 137 500 pour la boutique pirate), m = multiplicateur de l'Heure du trésor (1 sinon).", "Chance = 1 − Π(1 − m × p) over every chest counted: p = published odds (1 in 137,500 for Pirate Shop Access), m = the Treasure Hour multiplier (1 otherwise).", "Zher = 1 − Π(1 − m × p) 3la kol sndouq m7soub: p = zher li nachrat Epic (1 f 137 500 l l7anout dyal l9ersan), m = multiplicateur dyal Sa3at l'kenz (1 ila la).", "Probabilidad = 1 − Π(1 − m × p) sobre cada cofre contado: p = probabilidad publicada (1 entre 137 500 para el Acceso a la tienda pirata), m = el multiplicador de la Hora del tesoro (1 si no).")}</p>"
            + $"<details><summary>{L.T("Coéquipiers : vus, importés, estimés", "Teammates: seen, imported, estimated", "S7abek: li chefti, importés, ta9dir", "Compañeros: vistos, importados, estimados")}</summary><p>{L.T("Vus : lus dans TON replay, qui ne contient que ce qui était près de toi (un minimum). Importés : le fichier d'un coéquipier donne son compte exact et remplace ce que ton replay a vu de lui. Estimés (au choix) : trésors reçus de coffres hors de ta vue ÷ ton taux de trésors par coffre ; un total avec une fourchette, jamais par partie.", "Seen: read from YOUR replay, which only holds what was near you (a minimum). Imported: a teammate's file gives their exact count and replaces what your replay saw of them. Estimated (optional): treasures received from chests out of your sight ÷ your treasure rate per chest; a total with a range, never per match.", "Li chefti: m9rin mn le replay DYALEK, li fih ghir dakchi li kan 9rib lik (minimum). Importés: l'fichier dyal sa7bek kay3ti l7sab exact dyalo w kay3wwed dakchi li chafo le replay dyalek. Ta9dir (ila bghiti): knouz li jawk mn snadeq b3ad 3la 3inik ÷ taux dyal knouz dyalek f kol sndouq; total b marge, 3emmro f partiya wa7da.", "Vistos: leídos de TU repetición, que solo contiene lo que estaba cerca de ti (un mínimo). Importados: el archivo de un compañero da su recuento exacto y sustituye lo que tu repetición vio de él. Estimados (opcional): tesoros recibidos de cofres fuera de tu vista ÷ tu tasa de tesoros por cofre; un total con un margen, nunca por partida.")}</p></details>"
            + $"<details><summary>{L.T("Heures du trésor", "Treasure Hours", "Sa3at l'kenz", "Horas del tesoro")}</summary><p>{L.T("Ajoute-les dans Réglages → Heures du trésor, avec l'heure de ton PC : une notification arrive 10 minutes avant. Les coffres ouverts pendant une Heure du trésor comptent plus, pour le jeton et pour les objets.", "Add them in Settings → Treasure Hours, in your PC's time: a notification comes 10 minutes before. Chests opened during a Treasure Hour count for more, for the token and for the items.", "Zidhom f Réglages → Sa3at l'kenz, b sa3a dyal PC dyalek: notification katji 10 d9aye9 9bel. Snadeq li t7ello f Sa3at l'kenz kayt7sbo kter, l l'jeton w l l7wayej.", "Añádelas en Ajustes → Horas del tesoro, en la hora de tu PC: llega una notificación 10 minutos antes. Los cofres abiertos durante una Hora del tesoro cuentan más, para la ficha y para los objetos.")}</p></details>"
            + $"<details><summary>{L.T("Parties classées, coéquipiers aléatoires, semaine", "Ranked, random teammates, the week", "Classées, s7ab l'hasard, ssimana", "Clasificatorias, compañeros aleatorios, la semana")}</summary><p>{L.T("Les parties classées sont exclues par défaut (le jeton n'y tomberait pas, non confirmé). Les coéquipiers aléatoires comptent par défaut. Pour les trésors de la semaine, la semaine commence le jeudi à 14 h UTC (une hypothèse, modifiable dans config.json). Le taux d'environ 1 trésor pour 100 coffres est une estimation de la communauté.", "Ranked matches are left out by default (the token reportedly does not drop there, unconfirmed). Random teammates count by default. For the weekly treasures, the week starts on Thursday at 14:00 UTC (an assumption, editable in config.json). The rate of about 1 treasure per 100 chests is a community estimate.", "Les partiyat classées mkhrojin par défaut (l'jeton ma kaytla3ch temma, ma mconfirmich). S7ab l'hasard kayt7sbo par défaut. L knouz dyal ssimana, ssimana katbda l khmis m3a 14h UTC (ta9dir, katbeddel f config.json). «1 kenz f kol 100 sndouq» ta9dir dyal la communauté.", "Las partidas clasificatorias quedan fuera por defecto (al parecer la ficha no sale ahí, sin confirmar). Los compañeros aleatorios cuentan por defecto. Para los tesoros semanales, la semana empieza el jueves a las 14:00 UTC (una suposición, editable en config.json). La tasa de alrededor de 1 tesoro cada 100 cofres es una estimación de la comunidad.")}</p></details>"
            + "</div>"));

        var data = new StringBuilder("<div class=\"help\">");
        data.Append($"<p><code>{H(dataFolder)}</code></p>");
        data.Append($"<p>{L.T("report.html : ce tableau de bord sans boutons, lisible même quand l'assistant est fermé · overlay.html : l'overlay pour OBS · matches.csv : une ligne par partie · data.json : tout ton historique (garde-le si tu changes de PC) · imports.json : les fichiers de tes coéquipiers · config.json : tes réglages.",
            "report.html: this dashboard without buttons, readable even when the assistant is closed · overlay.html: the overlay for OBS · matches.csv: one line per match · data.json: your whole history (keep it if you change PC) · imports.json: your teammates' files · config.json: your settings.",
            "report.html: had tableau de bord bla boutons, kayt9ra 7ta mlli l'assistant msdoud · overlay.html: l'overlay l OBS · matches.csv: ster l kol partiya · data.json: l'historique kamel (7tafed bih ila beddelti PC) · imports.json: les fichiers dyal s7abek · config.json: les réglages dyalek.", "report.html: este panel sin botones, legible incluso con el asistente cerrado · overlay.html: el overlay para OBS · matches.csv: una línea por partida · data.json: todo tu historial (consérvalo si cambias de PC) · imports.json: los archivos de tus compañeros · config.json: tus ajustes.")}</p>");
        if (live != null)
        {
            data.Append("<div class=\"btns\">" + Button("open-folder", L.T("Ouvrir le dossier des données", "Open the data folder", "7ell dossier dyal les données", "Abrir la carpeta de datos"))
                + Button("open-report", L.T("Ouvrir report.html", "Open report.html", "7ell report.html", "Abrir report.html"))
                + (live.HasErrorLog ? Button("open-errors", L.T("Ouvrir le journal d'erreurs", "Open the error log", "7ell journal dyal les erreurs", "Abrir el registro de errores")) : "") + "</div>");
        }
        data.Append("</div>");
        sb.Append("<div class=\"grid2\">");
        sb.Append(Box(L.T("Tes données", "Your data", "Les données dyalek", "Tus datos"), L.T("Tout reste sur ton PC, dans ce dossier.", "Everything stays on your PC, in this folder.", "Kolchi kayb9a f PC dyalek, f had dossier.", "Todo se queda en tu PC, en esta carpeta."), data.ToString()));
        sb.Append(Box(L.T("Crédits", "Credits", "Crédits", "Créditos"), null,
            "<div class=\"help\">"
            + $"<p>{H(AppInfo.Name)} {AppInfo.VersionText} {L.T("par", "by", "dyal", "por")} <span class=\"sig\">{H(AppInfo.Author)}</span>.</p>"
            + $"<p>{L.T("Basé sur Chest Counter de LED-N (licence MIT). Lecture des replays : FortniteReplayDecompressor de Shiqan (MIT). Ce projet n'est pas affilié à Epic Games.", "Based on Chest Counter by LED-N (MIT license). Replay reading: FortniteReplayDecompressor by Shiqan (MIT). This project is not affiliated with Epic Games.", "Mbni 3la Chest Counter dyal LED-N (licence MIT). L9raya dyal les replays: FortniteReplayDecompressor dyal Shiqan (MIT). Had le projet ma 3ndo 7ta 3ala9a b Epic Games.", "Basado en Chest Counter de LED-N (licencia MIT). Lectura de repeticiones: FortniteReplayDecompressor de Shiqan (MIT). Este proyecto no está afiliado a Epic Games.")}</p>"
            + (live != null
                ? "<div class=\"btns\">" + Button("credits", L.T("Voir le projet d'origine (navigateur)", "See the original project (browser)", "Chouf le projet l'asli (navigateur)", "Ver el proyecto original (navegador)")) + "</div>"
                : $"<p><a class=\"btn\" href=\"{H(AppInfo.OriginalRepoUrl)}\" target=\"_blank\" rel=\"noreferrer noopener\">{L.T("Voir le projet d'origine", "See the original project", "Chouf le projet l'asli", "Ver el proyecto original")}</a></p>")
            + "</div>"));
        sb.Append("</div>");
        return Panel("help", sb.ToString());
    }

    // ---- Script ------------------------------------------------------------------------------

    /// <summary>
    /// Tabs (the one shown is kept across refreshes), buttons and settings (live: sent to the assistant), a file dropped
    /// anywhere (imported), and the refresh: the dashboard asks every second whether the page changed and swaps it in
    /// place; report.html simply reloads itself, keeping the tab and the scroll position.
    /// </summary>
    private const string Script = """
        <script>
        (function(){
        var B=document.body,live=B.dataset.live==='1',v=B.dataset.v,cur=null,pend=false,off=false,OFF=B.dataset.off||'';
        function store(k,val){try{if(val===undefined)return sessionStorage.getItem(k);sessionStorage.setItem(k,val)}catch(e){return null}}
        function show(t){var ps=document.querySelectorAll('[data-panel]');if(!ps.length)return;var ok=false;ps.forEach(function(p){if(p.dataset.panel===t)ok=true});if(!ok)t=ps[0].dataset.panel;var changed=t!==cur;cur=t;
         ps.forEach(function(p){var on=p.dataset.panel===t;p.classList.toggle('on',on);if(on&&changed&&!calm){p.classList.add('enter');setTimeout(function(){p.classList.remove('enter')},900)}});
         document.querySelectorAll('nav [data-tab]').forEach(function(b){var on=b.dataset.tab===t;b.classList.toggle('on',on);b.setAttribute('aria-selected',on?'true':'false')});
         store('cta-tab',t);if(location.hash!=='#'+t){try{history.replaceState(null,'','#'+t)}catch(e){}}}
        function team(){var on=false;try{on=localStorage.getItem('cta-showteam')==='1'}catch(e){}B.classList.toggle('showteam',on);var c=document.getElementById('showteam');if(c)c.checked=on}
        function toast(msg,ok){if(!msg)return;var t=document.getElementById('toast');t.textContent=msg;t.className='show '+(ok===false?'bad':'good');clearTimeout(toast.h);toast.h=setTimeout(function(){t.className=''},ok===false?8000:5000)}
        function notice(){var n=document.getElementById('notice');if(!n)return;var id=n.dataset.id;if(id&&id!==store('cta-notice')){store('cta-notice',id);toast(n.textContent,n.dataset.ok!=='0');if(n.dataset.tab)show(n.dataset.tab)}}
        function busy(el,on){if(el&&el.classList){el.classList.toggle('busy',on)}}
        function act(a,k,val,el){if(!live)return;busy(el,true);
         fetch('act',{method:'POST',headers:{'Content-Type':'application/json','X-CTA':'1'},body:JSON.stringify({a:a,k:k,v:val})})
          .then(function(r){return r.json()}).then(function(r){busy(el,false);toast(r.msg,r.ok);if(r.tab)show(r.tab);refresh(true)})
          .catch(function(){busy(el,false);toast(OFF,false)})}
        function send(files){Array.prototype.forEach.call(files,function(f){f.text().then(function(t){return fetch('import',{method:'POST',headers:{'Content-Type':'text/plain;charset=utf-8','X-CTA':'1','X-Name':encodeURIComponent(f.name)},body:t})})
          .then(function(r){return r.json()}).then(function(r){toast(r.msg,r.ok);if(r.tab)show(r.tab);refresh(true)}).catch(function(){toast(OFF,false)})})}
        function filter(f){f=f||store('cta-filter')||'all';store('cta-filter',f);B.classList.toggle('f-missing',f==='missing');B.classList.toggle('f-got',f==='got');
         document.querySelectorAll('[data-filter]').forEach(function(b){b.classList.toggle('on',b.dataset.filter===f)})}
        var LOC={fr:'fr-FR',en:'en-GB',ary:'fr-MA',es:'es-ES'},calm=!!(window.matchMedia&&matchMedia('(prefers-reduced-motion: reduce)').matches);
        function fmt(n){try{return new Intl.NumberFormat(LOC[document.documentElement.lang]||'en-GB').format(Math.round(n))}catch(e){return String(Math.round(n))}}
        function count(el,from,to){if(calm){el.textContent=fmt(to);return}var t0=performance.now(),d=Math.min(1400,500+Math.abs(to-from)*6);
         el.classList.remove('bump');void el.offsetWidth;el.classList.add('bump');
         (function step(t){var k=Math.min(1,(t-t0)/d),e=1-Math.pow(1-k,3);el.textContent=fmt(from+(to-from)*e);if(k<1)requestAnimationFrame(step)})(t0)}
        function snapshot(){var s={c:{},b:{},g:{}};document.querySelectorAll('[data-count][data-key]').forEach(function(e){s.c[e.dataset.key]=+e.dataset.count});
         document.querySelectorAll('[data-bar]').forEach(function(e){s.b[e.dataset.bar]=e.style.width});document.querySelectorAll('.item.got[data-id]').forEach(function(e){s.g[e.dataset.id]=1});return s}
        function animate(s,first){s=s||{c:{},b:{},g:{}};document.querySelectorAll('[data-count][data-key]').forEach(function(e){var to=+e.dataset.count,from=first?0:s.c[e.dataset.key];if(from!==undefined&&from!==to)count(e,from,to)});
         if(!calm)document.querySelectorAll('[data-bar]').forEach(function(e){var target=e.style.width,from=first?'0%':s.b[e.dataset.bar];if(from===undefined||from===target)return;e.style.transition='none';e.style.width=from;void e.offsetWidth;e.style.transition='';e.style.width=target});
         if(!first)document.querySelectorAll('.item.got[data-id]').forEach(function(e){if(!s.g[e.dataset.id]){e.classList.add('just');setTimeout(function(){e.classList.remove('just')},2600)}})}
        function rr(x,X,Y,w,h,r){x.beginPath();x.moveTo(X+r,Y);x.arcTo(X+w,Y,X+w,Y+h,r);x.arcTo(X+w,Y+h,X,Y+h,r);x.arcTo(X,Y+h,X,Y,r);x.arcTo(X,Y,X+w,Y,r);x.closePath()}
        function glow(x,X,Y,r,c){var g=x.createRadialGradient(X,Y,0,X,Y,r);g.addColorStop(0,c);g.addColorStop(1,'rgba(0,0,0,0)');x.fillStyle=g;x.fillRect(X-r,Y-r,2*r,2*r)}
        function star(x,X,Y,r,k){x.beginPath();for(var i=0;i<8;i++){var a=-Math.PI/2+i*Math.PI/4,q=i%2?r*k:r;x.lineTo(X+q*Math.cos(a),Y+q*Math.sin(a))}x.closePath()}
        function coin(x,X,Y,r){glow(x,X,Y,r*1.7,'rgba(251,191,36,.45)');x.fillStyle='rgba(11,6,32,.5)';x.beginPath();x.arc(X,Y+r*.1,r,0,7);x.fill();
         var g=x.createRadialGradient(X-r*.35,Y-r*.4,r*.05,X,Y,r);g.addColorStop(0,'#fff6cf');g.addColorStop(.45,'#f8c32c');g.addColorStop(1,'#a86b12');
         x.fillStyle=g;x.beginPath();x.arc(X,Y,r,0,7);x.fill();x.lineWidth=r*.05;x.strokeStyle='#7c4a03';x.stroke();x.lineWidth=r*.04;x.strokeStyle='rgba(253,230,138,.8)';x.beginPath();x.arc(X,Y,r*.8,0,7);x.stroke();
         var s=x.createLinearGradient(X-r,Y-r,X+r,Y+r);s.addColorStop(0,'#8b5cf6');s.addColorStop(1,'#3b0f8a');x.fillStyle=s;star(x,X,Y,r*.58,.3);x.fill();
         x.fillStyle='rgba(255,255,255,.35)';x.beginPath();x.ellipse(X-r*.32,Y-r*.56,r*.36,r*.2,0,0,7);x.fill()}
        function card(d){var W=1200,H=630,cv=document.createElement('canvas');cv.width=W;cv.height=H;var x=cv.getContext('2d'),F=' Bahnschrift, "Segoe UI", sans-serif';
         var bg=x.createLinearGradient(0,0,W,H);bg.addColorStop(0,'#2a1460');bg.addColorStop(.55,'#120b2b');bg.addColorStop(1,'#0a2236');x.fillStyle=bg;x.fillRect(0,0,W,H);
         glow(x,160,110,460,'rgba(139,92,246,.40)');glow(x,1100,600,520,'rgba(34,211,238,.22)');
         x.lineCap='round';x.lineWidth=9;x.strokeStyle='rgba(34,211,238,.55)';x.beginPath();x.arc(1030,150,128,Math.PI*.85,Math.PI*1.55);x.stroke();
         x.strokeStyle='rgba(167,139,250,.55)';x.beginPath();x.arc(1030,150,128,Math.PI*-.15,Math.PI*.55);x.stroke();coin(x,1030,150,82);
         x.fillStyle='#fff';star(x,1128,62,16,.22);x.fill();x.fillStyle='rgba(165,243,252,.9)';star(x,1150,104,8,.22);x.fill();
         x.textBaseline='alphabetic';x.fillStyle='#c4b5fd';x.font='700 38px'+F;x.fillText(d.app,70,92);
         x.fillStyle='#67e8f9';x.font='600 26px'+F;x.fillText((d.player?d.player+'   ·   ':'')+d.date,70,132);
         var gg=x.createLinearGradient(70,200,700,330);gg.addColorStop(0,'#fde68a');gg.addColorStop(.5,'#fbbf24');gg.addColorStop(1,'#f59e0b');x.fillStyle=gg;x.font='700 156px'+F;x.fillText(d.chests,62,320);
         x.fillStyle='#ece9ff';x.font='600 34px'+F;x.fillText(d.chestsLabel,72,370);x.fillStyle='#a59fc9';x.font='24px "Segoe UI", sans-serif';x.fillText(d.matches+'   ·   '+d.best,72,410);
         var ps=[{l:d.tokenLabel,v:d.token,s:d.oneIn,p:Math.max(.015,d.tokenP||0),c:'#fbbf24'},{l:d.itemsLabel,v:d.items,p:d.itemsP||0,c:'#34d399'}];
         if(d.squad)ps.push({l:d.squadLabel,v:d.squad,c:'#22d3ee'});
         var gap=22,pw=(W-140-gap*(ps.length-1))/ps.length,py=448,ph=130;
         ps.forEach(function(p,i){var X=70+i*(pw+gap);rr(x,X,py,pw,ph,18);x.fillStyle='rgba(255,255,255,.06)';x.fill();x.lineWidth=2;x.strokeStyle='rgba(196,181,253,.28)';x.stroke();
          x.fillStyle='#a59fc9';x.font='20px "Segoe UI", sans-serif';x.fillText(p.l,X+24,py+36);x.fillStyle=p.c;x.font='700 '+(p.v.length>14?34:44)+'px'+F;x.fillText(p.v,X+24,py+84);
          if(p.s){x.fillStyle='#a59fc9';x.font='20px "Segoe UI", sans-serif';x.textAlign='right';x.fillText(p.s,X+pw-24,py+84);x.textAlign='left'}
          if(p.p!==undefined){rr(x,X+24,py+ph-28,pw-48,10,5);x.fillStyle='rgba(255,255,255,.10)';x.fill();rr(x,X+24,py+ph-28,Math.max(10,(pw-48)*Math.min(1,p.p)),10,5);x.fillStyle=p.c;x.fill()}});
         x.fillStyle='#8b82b8';x.font='600 20px'+F;x.textAlign='right';x.fillText(d.by,W-70,H-22);x.textAlign='left';return cv}
        function cardDo(btn){var d;try{d=JSON.parse(btn.dataset.card)}catch(e){return}var cv=card(d),name='chest-token-'+new Date().toISOString().slice(0,10)+'.png';
         function save(){var a=document.createElement('a');a.href=cv.toDataURL('image/png');a.download=name;document.body.appendChild(a);a.click();a.remove();toast(B.dataset.saved,true)}
         if(btn.dataset.cardDo==='save'||!(navigator.clipboard&&window.ClipboardItem&&window.isSecureContext)){save();return}
         cv.toBlob(function(b){navigator.clipboard.write([new ClipboardItem({'image/png':b})]).then(function(){toast(B.dataset.copied,true)},function(){save()})},'image/png')}
        document.addEventListener('click',function(e){
         var t=e.target.closest('[data-tab]');if(t){e.preventDefault();show(t.dataset.tab);window.scrollTo(0,0);return}
         var fl=e.target.closest('[data-filter]');if(fl){filter(fl.dataset.filter);return}
         var cd=e.target.closest('[data-card]');if(cd){cardDo(cd);return}
         var p=e.target.closest('[data-pick]');if(p){var i=document.getElementById(p.dataset.pick);if(i)i.click();return}
         var a=e.target.closest('[data-a]');if(a){e.preventDefault();if(a.dataset.confirm&&!confirm(a.dataset.confirm))return;var val=a.dataset.v||'';
          if(a.dataset.from){var f=document.querySelector(a.dataset.from);if(f)val=f.value}
          if(a.dataset.form){var o={},box=document.querySelector(a.dataset.form);if(box)box.querySelectorAll('[name]').forEach(function(x){o[x.name]=x.value});val=JSON.stringify(o)}
          act(a.dataset.a,a.dataset.k||'',val,a)}});
        document.addEventListener('change',function(e){var el=e.target;
         if(el.id==='showteam'){try{localStorage.setItem('cta-showteam',el.checked?'1':'0')}catch(x){}team();return}
         if(el.hasAttribute('data-import')){if(el.files&&el.files.length)send(el.files);el.value='';return}
         if(el.dataset.set){act('set',el.dataset.set,el.type==='checkbox'?(el.checked?'1':'0'):el.value,el)}});
        if(live){var depth=0;function files(e){return e.dataTransfer&&Array.prototype.indexOf.call(e.dataTransfer.types||[],'Files')>=0}
         document.addEventListener('dragenter',function(e){if(files(e)){depth++;B.classList.add('dragging')}});
         document.addEventListener('dragleave',function(e){if(files(e)&&--depth<=0){depth=0;B.classList.remove('dragging')}});
         document.addEventListener('dragover',function(e){if(files(e))e.preventDefault()});
         document.addEventListener('drop',function(e){if(!files(e))return;e.preventDefault();depth=0;B.classList.remove('dragging');if(e.dataTransfer.files.length)send(e.dataTransfer.files)})}
        function editing(){var a=document.activeElement;return !!(a&&a.matches&&a.matches('input[type=text],input[type=number],input[type=date],input[type=time],select'))}
        function refresh(now){if(!live)return;if(!now&&editing()){pend=true;return}
         fetch('./',{cache:'no-store'}).then(function(r){return r.text()}).then(function(h){var d=new DOMParser().parseFromString(h,'text/html');var app=d.getElementById('app');if(!app)return;
          var y=window.scrollY,before=snapshot();document.getElementById('app').replaceWith(app);v=d.body.dataset.v;document.documentElement.lang=d.documentElement.lang;document.title=d.title;
          B.dataset.off=d.body.dataset.off;B.dataset.droptext=d.body.dataset.droptext;B.dataset.copied=d.body.dataset.copied;OFF=B.dataset.off;var o=document.getElementById('offline');if(o)o.textContent=OFF;
          show(cur);team();filter();notice();animate(before,false);window.scrollTo(0,y)}).catch(function(){})}
        document.addEventListener('focusout',function(){setTimeout(function(){if(pend&&!editing()){pend=false;refresh()}},60)});
        function offline(on){if(on===off)return;off=on;var o=document.getElementById('offline');if(o)o.hidden=!on}
        function poll(){fetch('state',{cache:'no-store'}).then(function(r){return r.json()}).then(function(s){if(off){offline(false);refresh();}if(String(s.v)!==String(v))refresh()})
          .catch(function(){offline(true)}).then(function(){setTimeout(poll,document.hidden?4000:1000)})}
        var first=(location.hash||'').slice(1)||store('cta-tab')||'home';show(first);team();filter();notice();
        if(!store('cta-seen')){animate(null,true);store('cta-seen','1')}
        if(live){setTimeout(poll,1000);addEventListener('pagehide',function(){try{navigator.sendBeacon('closed','1')}catch(e){}})}
        else{var n=+(B.dataset.refresh||15);addEventListener('scroll',function(){store('cta-y',String(window.scrollY))});var y=+(store('cta-y')||0);if(y)window.scrollTo(0,y);setTimeout(function(){location.reload()},n*1000)}
        })();
        </script>
        """;
}
