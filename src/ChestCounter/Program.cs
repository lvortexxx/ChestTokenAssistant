using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using ChestCounter;

// --standby: the invisible process that waits for Fortnite to start ("open with Fortnite"). No console, nothing else.
if (args.Contains("--standby"))
{
    var dataIndex = Array.IndexOf(args, "--data");
    return OperatingSystem.IsWindows()
        ? FortniteWatch.RunStandby(dataIndex >= 0 && dataIndex + 1 < args.Length ? args[dataIndex + 1] : Store.DefaultFolder())
        : 1;
}

// --open <file>: a teammate's .chesttoken file was double-clicked. It goes into the inbox of the data folder: the
// assistant already open imports it from there (this process quits without any window), otherwise this process
// starts the assistant normally, which imports it at once.
var openIndex = Array.IndexOf(args, "--open");
if (openIndex >= 0 && openIndex + 1 < args.Length)
{
    var dataIndex = Array.IndexOf(args, "--data");
    var openData = dataIndex >= 0 && dataIndex + 1 < args.Length ? args[dataIndex + 1] : Store.DefaultFolder();
    try
    {
        var inbox = new Store(openData).InboxPath;
        Directory.CreateDirectory(inbox);
        var source = args[openIndex + 1];
        File.Copy(source, Path.Combine(inbox, $"{DateTime.Now:yyyyMMdd-HHmmss}-{Path.GetFileName(source)}"), overwrite: true);
    }
    catch
    {
        // unreadable file: the assistant opens normally, nothing to import
    }
    if (Mutex.TryOpenExisting(AppInfo.InstanceMutexFor(openData), out var running))
    {
        running.Dispose();
        if (OperatingSystem.IsWindows())
        {
            DashboardWindow.ShowRunning(new Store(openData)); // shows the file imported (Files tab)
        }
        return 0;
    }
}

// The dashboard (a page in its own window) is the interface; the console is only for command line options and as a
// backup (--console, or "Use the console" in the tray menu).
var commandLine = args.Any(a => a is "--once" or "--odds" or "--files" or "--import" or "--export" or "--help" or "-h" or "--overlay-preview" or "--self-test" or "--cleanup");
var consoleMode = commandLine || args.Contains("--console") || !OperatingSystem.IsWindows();

// Windows app: open a console window (or write into the terminal for command line options).
if (OperatingSystem.IsWindows() && consoleMode)
{
    NativeConsole.Ensure(attachToParent: commandLine);
}
Console.OutputEncoding = Encoding.UTF8;

// A crash must not close the window before the message can be read (the exe is usually double-clicked).
AppDomain.CurrentDomain.UnhandledException += (_, e) =>
{
    ErrorLog.Write("Crash", e.ExceptionObject);
    if (!consoleMode && OperatingSystem.IsWindows())
    {
        MessageBox.Show(L.T("Erreur inattendue : ", "Unexpected error: ", "Chi mouchkil tla3 mn walou: ", "Error inesperado: ") + (e.ExceptionObject as Exception)?.Message, error: true);
        return;
    }
    Console.ForegroundColor = ConsoleColor.Red;
    Console.WriteLine();
    Console.WriteLine(L.T("Erreur inattendue : ", "Unexpected error: ", "Chi mouchkil tla3 mn walou: ", "Error inesperado: ") + e.ExceptionObject);
    Console.ResetColor();
    if (!Console.IsInputRedirected)
    {
        Console.WriteLine(L.T("Appuie sur une touche pour fermer.", "Press any key to close.", "Wrek 3la chi bouton bach tsed.", "Pulsa cualquier tecla para cerrar."));
        try { Console.ReadKey(intercept: true); } catch { }
    }
};

// ---- Arguments ---------------------------------------------------------------------------
string? dataDir = null, demosArg = null, langArg = null, importPath = null, exportPath = null;
var once = false;
var printOdds = false;     // --odds: like --once, then prints the Odds tab
var printFiles = false;    // --files: like --once, then prints the Files tab
var reset = false;
var auto = false;          // opened by the standby when Fortnite started: closes itself after Fortnite
var afterUpdate = false;   // restarted after "start the setup again": waits for the previous instance to quit
int? dashPort = null;      // restarted: the dashboard keeps its address, so that the window open reconnects
string? dashKey = null;
var noWindow = args.Contains("--no-window"); // development: the dashboard runs without opening its window
var cleanupCommand = false;                   // --cleanup: stop the old versions and their start with Fortnite, then quit
string? previewPath = null;
for (var i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--data": dataDir = args[++i]; break;
        case "--demos": demosArg = args[++i]; break;
        case "--lang": langArg = args[++i]; break;
        case "--once": once = true; break;
        case "--odds": once = printOdds = true; break;
        case "--files": once = printFiles = true; break;
        case "--import": importPath = args[++i]; break;
        case "--open": i++; break; // handled above (inbox)
        case "--export": exportPath = args[++i]; break;
        case "--reset-config": reset = true; break;
        case "--overlay-preview": previewPath = args[++i]; break;
        case "--auto": auto = true; break;
        case "--after-update": afterUpdate = true; break;
        case "--console": break; // see consoleMode
        case "--no-window": break;
        case "--cleanup": cleanupCommand = true; break;
        case "--dash-port": dashPort = int.TryParse(args[++i], NumberStyles.None, CultureInfo.InvariantCulture, out var port) ? port : null; break;
        case "--dash-key": dashKey = args[++i]; break;
        case "--self-test": return SelfTest.Run();
        case "-h":
        case "--help":
            var langIndex = Array.IndexOf(args, "--lang");
            L.Set(langIndex >= 0 && langIndex + 1 < args.Length ? L.Normalize(args[langIndex + 1]) ?? L.SystemDefault : L.SystemDefault);
            Console.WriteLine(L.T("""
                Chest Token Assistant (Fortnite)

                Options :
                  --console           l'ancienne interface au clavier au lieu du tableau de bord
                  --lang <fr|en|ary|es>  langue de l'outil (français, anglais, darija ou espagnol)
                  --demos <dossier>   dossier des replays (par défaut %LOCALAPPDATA%\FortniteGame\Saved\Demos)
                  --data <dossier>    dossier des données (par défaut %APPDATA%\ChestTokenAssistant)
                  --once              analyse les replays terminés puis quitte
                  --odds              comme --once, puis affiche l'onglet Chances
                  --export <dossier>  exporte ton fichier .chesttoken pour tes coéquipiers puis quitte
                  --import <fichier>  importe le fichier .chesttoken (ou data.json) d'un coéquipier puis quitte
                  --reset-config      repose les questions de configuration
                  --cleanup           arrête les anciennes versions et leur lancement avec Fortnite, puis quitte
                  --overlay-preview <fichier.png>   enregistre des aperçus de l'overlay puis quitte
                  --self-test         vérifie les calculs de chances et la fusion des fichiers importés
                """, """
                Chest Token Assistant (Fortnite)

                Options:
                  --console           the old keyboard interface instead of the dashboard
                  --lang <fr|en|ary|es>  language of the tool (French, English, Darija or Spanish)
                  --demos <folder>    replays folder (default %LOCALAPPDATA%\FortniteGame\Saved\Demos)
                  --data <folder>     data folder (default %APPDATA%\ChestTokenAssistant)
                  --once              analyze the finished replays, then quit
                  --odds              like --once, then print the Odds tab
                  --export <folder>   export your .chesttoken file for your teammates, then quit
                  --import <file>     import a teammate's .chesttoken file (or data.json), then quit
                  --reset-config      ask the setup questions again
                  --cleanup           stop old versions and their start with Fortnite, then quit
                  --overlay-preview <file.png>   save previews of the overlay, then quit
                  --self-test         check the odds formulas and the merge of imported files
                """, """
                Chest Token Assistant (Fortnite)

                Options:
                  --console           l'interface l9dima b le clavier blast tableau de bord
                  --lang <fr|en|ary|es>  logha (français, English, darija wla español)
                  --demos <dossier>   dossier dyal les replays (par défaut %LOCALAPPDATA%\FortniteGame\Saved\Demos)
                  --data <dossier>    dossier dyal les données (par défaut %APPDATA%\ChestTokenAssistant)
                  --once              y'analysi les replays li salaw w ykhrej
                  --odds              b7al --once, w men b3d ywerri l'onglet Zher
                  --export <dossier>  y'exporti l'fichier .chesttoken dyalek l s7abek w ykhrej
                  --import <fichier>  y'importi l'fichier .chesttoken (wla data.json) dyal sa7bek w ykhrej
                  --reset-config      y3awed les questions dyal la configuration
                  --cleanup           y7bes les versions l9dam w lancement dyalhom m3a Fortnite, w ykhrej
                  --overlay-preview <fichier.png>   ysejjel des aperçus dyal l'overlay w ykhrej
                  --self-test         yverifi les calculs dyal zher w la fusion dyal les fichiers importés
                """, """
                Chest Token Assistant (Fortnite)

                Opciones:
                  --console           la antigua interfaz de teclado en lugar del panel
                  --lang <fr|en|ary|es>  idioma de la herramienta (francés, inglés, darija o español)
                  --demos <carpeta>   carpeta de repeticiones (por defecto %LOCALAPPDATA%\FortniteGame\Saved\Demos)
                  --data <carpeta>    carpeta de datos (por defecto %APPDATA%\ChestTokenAssistant)
                  --once              analiza las repeticiones terminadas y sale
                  --odds              como --once, y luego muestra la pestaña Probabilidades
                  --export <carpeta>  exporta tu archivo .chesttoken para tus compañeros y sale
                  --import <archivo>  importa el archivo .chesttoken (o data.json) de un compañero y sale
                  --reset-config      vuelve a hacer las preguntas de configuración
                  --cleanup           detiene las versiones antiguas y su inicio con Fortnite, y sale
                  --overlay-preview <archivo.png>   guarda vistas previas del overlay y sale
                  --self-test         comprueba los cálculos de probabilidades y la fusión de archivos importados
                """));
            return 0;
    }
}

// Old versions (an older build of this assistant run from another folder) still running or starting with Fortnite:
// stopped before anything else, so that they neither hold this data folder nor open again with the game. Only for the
// player's own data folder (a test folder given with --data never stops anything), or when asked with --cleanup, which
// also stops the tools this one comes from (ChestCounter.exe, ChestTracker.exe).
var ownDataFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ChestTokenAssistant");
CleanupReport? cleaned = null;
if (OperatingSystem.IsWindows() && AppInfo.ExePath is { } thisExe
    && (cleanupCommand || (!commandLine && previewPath == null
        && (((dataDir == null || OldVersions.SamePath(dataDir, ownDataFolder)) && !noWindow) || OldVersions.Testing))))
{
    string? oldAddress = null;
    try { oldAddress = File.ReadAllText(Path.Combine(dataDir ?? ownDataFolder, "dashboard-address.txt")).Trim(); } catch { /* no dashboard open */ }
    cleaned = OldVersions.Run(thisExe, includeLegacy: cleanupCommand);
    // An old version's dashboard window: this one takes over its address, so that the window simply reconnects.
    if (cleaned.Stopped.Count > 0 && dashPort == null && Uri.TryCreate(oldAddress, UriKind.Absolute, out var old)
        && old.Host == "127.0.0.1" && old.Segments.Length == 2 && old.Segments[1].TrimEnd('/') is { Length: >= 32 } oldKey)
    {
        dashPort = old.Port;
        dashKey = oldKey;
    }
    if (cleanupCommand)
    {
        var cleanupStore = new Store(dataDir ?? Store.DefaultFolder());
        var cleanupConfig = cleanupStore.LoadConfig();
        L.Set(cleanupConfig?.Language ?? L.SystemDefault);
        if (cleanupConfig is { StartWithFortnite: true } && !OldVersions.Testing)
        {
            FortniteWatch.Apply(true, cleanupStore.Folder); // the start with Fortnite of this exe
        }
        Console.WriteLine("  " + cleaned.Text());
        return 0;
    }
}
if (cleanupCommand)
{
    Console.WriteLine(L.T("  Impossible ici : lance ChestTokenAssistant.exe --cleanup.", "  Not possible here: run ChestTokenAssistant.exe --cleanup.", "  Ma ymkench hna: 7ell ChestTokenAssistant.exe --cleanup.", "  Imposible aquí: ejecuta ChestTokenAssistant.exe --cleanup."));
    return 1;
}

dataDir ??= Store.DefaultFolder();
var store = new Store(dataDir);
ErrorLog.FilePath = store.ErrorLogPath;
var interactive = consoleMode && ConsoleUi.Interactive;
if (interactive && !once && previewPath == null)
{
    ConsoleUi.EnsureSize(110, 40);
}

// One instance at a time: two would count the same matches and fight over the shortcuts.
using var instance = new Mutex(false, AppInfo.InstanceMutexFor(dataDir));
if (previewPath == null && !AcquireInstance(instance, afterUpdate ? TimeSpan.FromSeconds(20) : TimeSpan.Zero))
{
    if (auto)
    {
        return 0; // opened with Fortnite while the player had already opened it: nothing to do
    }
    L.Set(store.LoadConfig()?.Language ?? L.SystemDefault);
    if (!consoleMode && OperatingSystem.IsWindows())
    {
        // Started again while running: its dashboard comes to the front.
        if (!DashboardWindow.ShowRunning(store))
        {
            MessageBox.Show(L.T("Chest Token Assistant est déjà ouvert : clique sur l'icône du jeton près de l'horloge.",
                "Chest Token Assistant is already open: click the token icon near the clock.",
                "Chest Token Assistant déjà m7loul: click 3la l'icône dyal l'jeton 7da sa3a.", "Chest Token Assistant ya está abierto: haz clic en el icono de la ficha junto al reloj."));
        }
        return 0;
    }
    Console.ForegroundColor = ConsoleColor.Yellow;
    Console.WriteLine(L.T("  Chest Token Assistant est déjà ouvert : regarde dans la barre des tâches.",
        "  Chest Token Assistant is already open: look in the taskbar.",
        "  Chest Token Assistant déjà m7loul: 9elleb 3lih f la barre des tâches.", "  Chest Token Assistant ya está abierto: búscalo en la barra de tareas."));
    Console.ResetColor();
    if (interactive)
    {
        Console.WriteLine(L.T("  (cette fenêtre se ferme dans 10 secondes)", "  (this window closes in 10 seconds)", "  (had la fenêtre ghadi tsed f 10 secondes)", "  (esta ventana se cierra en 10 segundos)"));
        Thread.Sleep(10_000);
    }
    return 0;
}

// ---- Configuration -----------------------------------------------------------------------
var config = reset ? null : store.LoadConfig();
var firstRun = config == null;
var previousLanguage = reset ? store.LoadConfig()?.Language : null;
config ??= new Config();

// Language: --lang, else the saved choice, else asked once (Windows language when nobody can answer).
if (langArg != null)
{
    config.Language = L.Normalize(langArg) ?? config.Language;
}
if (L.Normalize(config.Language) == null)
{
    var suggested = L.Normalize(previousLanguage) ?? L.SystemDefault;
    if (interactive)
    {
        ConsoleUi.Screen($"{AppInfo.Name}  ·  by {AppInfo.Author}", "1/5");
        ConsoleUi.Text("Choisis ta langue  /  Choose your language  /  Khtar logha dyalek  /  Elige tu idioma");
        var choice = ConsoleUi.Menu(L.Codes.Select(L.NativeName).ToList(), Math.Max(0, Array.IndexOf(L.Codes, suggested)), "↑ ↓  +  Entrée / Enter");
        config.Language = L.Codes[choice];
    }
    else
    {
        config.Language = suggested;
    }
}
L.Set(config.Language);
var appName = AppInfo.Name;
ConsoleUi.SetTitle($"{appName} {AppInfo.VersionText} · by {AppInfo.Author}");

if (demosArg != null) config.DemosFolder = demosArg;
var defaultDemos = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FortniteGame", "Saved", "Demos");
if (string.IsNullOrWhiteSpace(config.DemosFolder))
{
    config.DemosFolder = defaultDemos;
}

if (previewPath != null)
{
    // Development: draws the overlay off-screen and saves it as PNG (idle + match in progress), then quits.
    Team.Imports = store.LoadImports();
    var previewData = Profiles.View(store.LoadData(), config);
    // In memory only: the 3 latest matches count as "this session", to show a realistic session line.
    foreach (var m in previewData.Matches.OrderByDescending(m => m.MatchStartUtc ?? m.AnalyzedAtUtc).Take(3))
    {
        m.MatchStartUtc = DateTime.UtcNow;
    }
    if (!OperatingSystem.IsWindows())
    {
        return 1;
    }
    NativeOverlay.SetAppearance(config.OverlaySize, config.OverlayOpacity);
    // Locked, as in game. Each style in its three states: idle, match in progress, live count.
    NativeOverlay.Start(-20000, -20000, true, (_, _) => { }, _ => { });
    var fakeLive = new MatchRecord { ChestsOpened = 12, TreasureFinds = { new TreasureFind { Item = "Trésor" } } };
    foreach (var style in NativeOverlay.Styles)
    {
        NativeOverlay.SetStyle(style);
        foreach (var (suffix, live, inProgress) in new[] { ("-idle", (MatchRecord?)null, false), ("-inprogress", null, true), ("-live", fakeLive, true) })
        {
            NativeOverlay.Update(OverlayModel.For(previewData, config, live, inProgress));
            Thread.Sleep(600);
            var name = Path.GetFileNameWithoutExtension(previewPath) + (style == "classic" ? "" : "-" + style) + suffix + ".png";
            var file = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(previewPath)) ?? ".", name);
            Console.WriteLine(NativeOverlay.Snapshot(file) ? L.T($"Aperçu : {file}", $"Preview: {file}", $"Aperçu: {file}", $"Vista previa: {file}") : L.T($"Échec de la capture : {file}", $"Capture failed: {file}", $"Capture ma khdmatch: {file}", $"Captura fallida: {file}"));
        }
    }
    return 0;
}

LowerPriority();
var data = store.LoadData();
Team.Imports = store.LoadImports();
ItemsModel.Checklist = store.LoadChecklist();
ItemImages.Extract(store.ItemImagesFolder); // the items' built-in pictures, for the dashboard and report.html

// --export <folder>: writes the share file of the current profile (like the Squad tab does), then quits.
if (exportPath != null)
{
    var (exported, exportedMatches) = ShareFormat.Export(Profiles.View(data, config), exportPath);
    Console.WriteLine($"  {exported} ({L.Count(exportedMatches, "partie en équipe", "parties en équipe", "team match", "team matches", "partiya f l'équipe", "partiyat f l'équipe", "partida en equipo", "partidas en equipo")})");
    return 0;
}

// --import <file>: adds a teammate's .chesttoken file (or data.json) like the Squad tab does, then quits.
if (importPath != null)
{
    string importMessage;
    var imported = false;
    try
    {
        var (source, text) = Team.Parse(importPath, File.ReadAllText(importPath), data);
        importMessage = text;
        if (source != null)
        {
            Team.Imports = Team.With(Team.Imports, source);
            store.SaveImports(Team.Imports);
            ReportWriter.WriteAll(store, config, data);
            imported = true;
        }
    }
    catch (Exception ex)
    {
        importMessage = L.T($"Impossible de lire ce fichier : {ex.Message}", $"Cannot read this file: {ex.Message}", $"Ma 9dertch n9ra had l'fichier: {ex.Message}", $"No se puede leer este archivo: {ex.Message}");
    }
    Console.WriteLine("  " + importMessage);
    return imported ? 0 : 1;
}
var tracker = new Tracker(store, config, data);
// Before any scan: otherwise replays analyzed during the setup would be analyzed a second time right after.
var reanalyzing = tracker.UpgradeIfNeeded();
var reportOpened = false;

// ---- State of the main screen and the dashboard ----------------------------------------------
var quit = false;
var restart = false;
var restartInConsole = false;
var wake = new AutoResetEvent(false);
var keys = new BlockingCollection<ConsoleKeyInfo>();
var log = new ActivityLog();
var mainThread = Environment.CurrentManagedThreadId;
var consoleScreen = false;
var lastDraw = DateTime.MinValue;
const int HomeTab = 0, OddsTab = 1, SquadTab = 2, FilesTab = 3, SettingsTab = 4, HelpTab = 5, TabCount = 6;
var tab = openIndex >= 0 ? SquadTab : HomeTab; // opened by a double-click on a teammate's file: their squad
var selection = new int[TabCount];
var showWhatsNew = false;
DateTime? autoCloseAt = null;
DateTime? fortniteClosedSince = null;
FileSystemWatcher? watcher = null;
var watcherFired = false;
FileSystemWatcher? inboxWatcher = null;
var inboxFired = true; // teammates' files left in the inbox are imported at startup
var nextScan = DateTime.UtcNow.AddSeconds(2);
WebDashboard? web = null;
var liveDirty = true;               // the dashboard page must be rendered again
var liveSignature = "";
var askProfile = false;             // several accounts: the dashboard asks which one is played
LiveNotice? notice = null;
var noticeId = 0;
string? setupStep = null;           // first launch in the dashboard: the step shown
List<FileInfo>? setupExisting = null;
var dashboardShown = false;
var hourAlerts = new HashSet<string>(); // Treasure Hours already announced (soon / now)
var overlayMoment = "";                 // Treasure Hour on + today's date: the overlay changes when it changes

// What the assistant prints (match counted, errors…) is kept for the console screen's activity and the dashboard's.
log.Changed += () =>
{
    liveDirty = true;
    if (!consoleScreen)
    {
        if (Environment.CurrentManagedThreadId != mainThread)
        {
            wake.Set();
        }
        return;
    }
    if (Environment.CurrentManagedThreadId == mainThread)
    {
        // While a scan runs (progress bar), redraw at most ~12 times a second.
        if ((DateTime.UtcNow - lastDraw).TotalMilliseconds >= 80) Draw();
    }
    else
    {
        wake.Set(); // written by another thread (overlay): the main loop redraws
    }
};

// The dashboard: served to this PC only, in both modes (the console has a menu entry for it).
if (!once && OperatingSystem.IsWindows())
{
    if (!consoleMode)
    {
        Console.SetOut(log);
    }
    web = WebDashboard.Start(dataDir, dashPort, dashKey);
    if (web != null)
    {
        web.ActionQueued = () => wake.Set();
        web.PageClosed = () =>
        {
            if (!consoleMode && !quit && OperatingSystem.IsWindows())
            {
                Tray.NotifyOnce(L.T("L'assistant continue de compter ici. Clique sur le jeton pour rouvrir le tableau de bord.",
                    "The assistant keeps counting here. Click the token to open the dashboard again.",
                    "L'assistant ba9i kay7seb hna. Click 3la l'jeton bach t3awed t7ell tableau de bord.", "El asistente sigue contando aquí. Haz clic en la ficha para volver a abrir el panel."));
            }
        };
        try { File.WriteAllText(DashboardWindow.AddressFile(store), web.Url); } catch { /* a second start opens a new window */ }
        ReportWriter.Live = BuildLive;
        ReportWriter.PublishLive = web.Publish;
        tracker.BetweenReplays = PumpDashboard;
    }
    else if (!consoleMode)
    {
        MessageBox.Show(L.T("Le tableau de bord n'a pas pu démarrer : l'assistant s'ouvre dans la console.",
            "The dashboard could not start: the assistant opens in the console.",
            "Tableau de bord ma bghach yt7ell: l'assistant kayt7ell f la console.", "El panel no pudo iniciarse: el asistente se abre en la consola."), error: true);
    }
}
if (!consoleMode && web == null && !once && OperatingSystem.IsWindows())
{
    // No dashboard: back to the console.
    consoleMode = true;
    NativeConsole.Ensure(attachToParent: false);
    Console.SetOut(ConsoleUi.Out);
    interactive = ConsoleUi.Interactive;
    if (interactive)
    {
        ConsoleUi.EnsureSize(110, 40);
    }
}

// ---- First launch: setup screens ----------------------------------------------------------
if (firstRun && !consoleMode && web != null && OperatingSystem.IsWindows())
{
    // In the dashboard: the same questions, one screen each. The assistant waits for the answers.
    setupStep = "lang";
    Tray.OpenDashboard = ShowDashboard;
    Tray.Quit = () => { quit = true; wake.Set(); };
    Tray.Start(dashboardMode: true);
    ReportWriter.RenderLive(store, config, data);
    if (!noWindow)
    {
        DashboardWindow.Show(web.Url, focusExisting: dashPort == web.Port);
    }
    dashboardShown = true;
    while (setupStep != null && !quit)
    {
        wake.WaitOne(TimeSpan.FromSeconds(1));
        PumpDashboard();
    }
    if (quit)
    {
        Thread.Sleep(300); // the last answer reaches the page
        web.Dispose();
        try { File.Delete(DashboardWindow.AddressFile(store)); } catch { }
        Tray.Stop();
        return 0;
    }
    if (Profiles.List(data).Count >= 2)
    {
        askProfile = true;
    }
}
else if (firstRun && interactive)
{
    // Step 2: the replays folder.
    ConsoleUi.Screen(appName, "2/5");
    ConsoleUi.Text(L.T("Tes replays", "Your replays", "Les replays dyalek", "Tus repeticiones"), ConsoleColor.White);
    ConsoleUi.Blank();
    if (!Directory.Exists(config.DemosFolder))
    {
        ConsoleUi.Text(L.T("✖ Dossier des replays introuvable :", "✖ Replays folder not found:", "✖ Dossier dyal les replays ma kaynch:", "✖ Carpeta de repeticiones no encontrada:"), ConsoleColor.Red);
        ConsoleUi.Hint("  " + config.DemosFolder);
        ConsoleUi.Blank();
        ConsoleUi.Text(L.T("Fortnite le crée à la première partie enregistrée. Si tu viens d'activer les replays, c'est normal.",
            "Fortnite creates it with the first recorded match. If you just turned replays on, that is expected.",
            "Fortnite kaycréyih f awel partiya mregistrya. Ila ba9i 3ad 7elliti les replays, normal.", "Fortnite la crea con la primera partida grabada. Si acabas de activar las repeticiones, es normal."));
        var where = ConsoleUi.Menu(new[]
        {
            L.T("Garder ce dossier", "Keep this folder", "Khelli had dossier", "Mantener esta carpeta"),
            L.T("Indiquer un autre dossier", "Enter another folder", "3tini dossier akhor", "Indicar otra carpeta"),
        });
        if (where == 1)
        {
            config.DemosFolder = ConsoleUi.Input(L.T("Chemin du dossier des replays : ", "Path of the replays folder: ", "Chemin dyal dossier dyal les replays: ", "Ruta de la carpeta de repeticiones: "), config.DemosFolder);
            ConsoleUi.Blank();
        }
    }
    else
    {
        ConsoleUi.Text(L.T("✔ Dossier des replays trouvé :", "✔ Replays folder found:", "✔ L9it dossier dyal les replays:", "✔ Carpeta de repeticiones encontrada:"), ConsoleColor.Green);
        ConsoleUi.Hint("  " + config.DemosFolder);
    }
    ConsoleUi.Blank();
    ConsoleUi.Text(L.T("Important : dans Fortnite, active  Paramètres → Jeu → Rediffusions → Enregistrer les rediffusions.",
        "Important: in Fortnite, turn on  Settings → Game → Replays → Record Replays.",
        "Mohim: f Fortnite, 7ell  Paramètres → Jeu → Rediffusions → Enregistrer les rediffusions.", "Importante: en Fortnite, activa  Configuración → Juego → Repeticiones → Grabar repeticiones."), ConsoleColor.Yellow);
    ConsoleUi.Hint(L.T("Sans ça, Fortnite n'enregistre aucun replay (« rediffusion ») et l'outil n'a rien à compter.",
        "Without it, Fortnite saves no replay and the tool has nothing to count.",
        "Bla hadchi, Fortnite ma kaysejjel 7ta replay w l'outil ma 3ando ma y7seb.", "Sin eso, Fortnite no guarda ninguna repetición y la herramienta no tiene nada que contar."));
    ConsoleUi.Blank();

    // Replays already on the PC.
    var existing = tracker.UnprocessedFiles().Where(f => ReplayHeaderProbe.IsLive(f.FullName) != true).ToList();
    var analyzeNow = false;
    if (existing.Count > 0)
    {
        var present = L.Count(existing.Count, "replay est déjà présent", "replays sont déjà présents", "replay is already on your PC", "replays are already on your PC",
            "replay déjà kayn f PC dyalek", "replays déjà kaynin f PC dyalek", "repetición ya está en tu PC", "repeticiones ya están en tu PC");
        ConsoleUi.Text(L.T($"{present} sur ton PC. Les compter aussi ?", $"{present}. Count them too?", $"{present}. N7sbohom 7ta homa?", $"{present}. ¿Contarlas también?"));
        analyzeNow = ConsoleUi.Menu(new[]
        {
            L.T("Oui, les analyser maintenant", "Yes, analyze them now", "Ah, analysihom daba", "Sí, analizarlas ahora"),
            L.T("Non, seulement mes prochaines parties", "No, only my next matches", "La, ghir les partiyat jayin", "No, solo mis próximas partidas"),
        }) == 0;
        if (!analyzeNow)
        {
            tracker.MarkSkipped(existing);
        }
    }
    else
    {
        ConsoleUi.WaitEnter(L.T("Entrée pour continuer", "Enter to continue", "Entrée bach tkemmel", "Intro para continuar"));
    }
    store.SaveConfig(config);

    if (analyzeNow)
    {
        ConsoleUi.Screen(appName, L.T("analyse", "analysis", "analyse", "análisis"));
        ConsoleUi.Text(L.T("Analyse de tes replays…", "Analyzing your replays…", "Kan'analysi les replays dyalek…", "Analizando tus repeticiones…"), ConsoleColor.White);
        ConsoleUi.Hint(L.T("Le rapport s'ouvre dans ton navigateur et se remplit au fur et à mesure.",
            "The report opens in your browser and fills in as the replays are read.",
            "Le rapport kayt7ell f navigateur w kaytmla m3a l9raya dyal les replays.", "El informe se abre en tu navegador y se llena a medida que se leen las repeticiones."));
        ConsoleUi.Blank();
        ReportWriter.Progress = new ScanProgress(0, existing.Count);
        ReportWriter.WriteAll(store, config, data);
        if (config.OpenReportOnStart)
        {
            Open(store.ReportPath);
            reportOpened = true;
        }
        tracker.Scan();
        tracker.NewProfilesFound = false;
        ConsoleUi.Blank();
        if (Profiles.List(data).Count < 2)
        {
            ConsoleUi.WaitEnter(L.T("Entrée pour continuer", "Enter to continue", "Entrée bach tkemmel", "Intro para continuar"));
        }
    }

    if (Profiles.List(data).Count >= 2)
    {
        ConsoleUi.Screen(appName, L.T("profil", "profile", "profil", "perfil"));
        ChooseProfile(data, config, store);
    }

    // Step 3: the overlay (some players only want the report).
    if (OperatingSystem.IsWindows())
    {
        ConsoleUi.Screen(appName, "3/5");
        ConsoleUi.Text(L.T("Afficher un overlay en jeu ?", "Show an overlay in game?", "Bghiti overlay f le jeu?", "¿Mostrar un overlay en el juego?"), ConsoleColor.White);
        ConsoleUi.Blank();
        ConsoleUi.Text(L.T("Une petite fenêtre toujours au premier plan affiche tes coffres pendant que tu joues.",
            "A small always-on-top window shows your chests while you play.",
            "Fenêtre sghira dima lfo9 katwerrik snadeq dyalek w nta kat'joui.", "Una pequeña ventana siempre visible muestra tus cofres mientras juegas."));
        ConsoleUi.Hint(L.T("Sans overlay, tes statistiques restent dans le rapport. Modifiable à tout moment dans les réglages.",
            "Without it, your statistics are in the report. Can be changed at any time in the settings.",
            "Bla overlay, les stats dyalek f le rapport. T9der tbeddel f ay wa9t f les réglages.", "Sin él, tus estadísticas están en el informe. Se puede cambiar en cualquier momento en los ajustes."));
        config.ShowOverlay = ConsoleUi.Menu(new[]
        {
            L.T("Oui, afficher l'overlay", "Yes, show the overlay", "Ah, werri l'overlay", "Sí, mostrar el overlay"),
            L.T("Non, seulement le rapport", "No, only the report", "La, ghir le rapport", "No, solo el informe"),
        }) == 0;
        store.SaveConfig(config);
    }

    // Step 4: open with Fortnite. Nothing is recommended: most players simply start it themselves.
    if (OperatingSystem.IsWindows() && AppInfo.ExePath != null)
    {
        ConsoleUi.Screen(appName, "4/5");
        ConsoleUi.Text(L.T("Ouvrir l'assistant automatiquement quand Fortnite démarre ?", "Open the assistant automatically when Fortnite starts?", "N7ell l'assistant automatiquement mlli yt7ell Fortnite?", "¿Abrir el asistente automáticamente cuando se inicia Fortnite?"), ConsoleColor.White);
        ConsoleUi.Blank();
        ConsoleUi.Text(L.T("Un tout petit processus invisible attend le lancement de Fortnite, puis ouvre l'assistant.\nL'assistant se ferme tout seul après Fortnite.",
            "A tiny invisible process waits for Fortnite to start, then opens the assistant.\nThe assistant closes by itself after Fortnite.",
            "Processus sghir w mkhbbi kaytsenna Fortnite, w men b3d kay7ell l'assistant.\nL'assistant kaysed rasso men b3d Fortnite.", "Un pequeño proceso invisible espera a que se inicie Fortnite y luego abre el asistente.\nEl asistente se cierra solo después de Fortnite."));
        ConsoleUi.Hint(L.T("Modifiable à tout moment dans les réglages. Désactive-le avant de supprimer ChestTokenAssistant.exe.",
            "Can be changed at any time in the settings. Turn it off before deleting ChestTokenAssistant.exe.",
            "T9der tbeddel f ay wa9t f les réglages. Tfih 9bel ma tmse7 ChestTokenAssistant.exe.", "Se puede cambiar en cualquier momento en los ajustes. Desactívalo antes de borrar ChestTokenAssistant.exe."));
        var withFortnite = ConsoleUi.Menu(new[]
        {
            L.T("Non, je le lancerai moi-même", "No, I will start it myself", "La, ghadi n7llo b yeddi", "No, lo abriré yo mismo"),
            L.T("Oui, l'ouvrir avec Fortnite", "Yes, open it with Fortnite", "Ah, 7llo m3a Fortnite", "Sí, abrirlo con Fortnite"),
        }) == 1;
        config.StartWithFortnite = withFortnite && FortniteWatch.Apply(true, dataDir);
        store.SaveConfig(config);
    }

    // Step 5: ready.
    ConsoleUi.Screen(appName, "5/5");
    ConsoleUi.Text(L.T("C'est prêt !", "All set!", "Safi, kolchi wajed!", "¡Todo listo!"), ConsoleColor.Green);
    ConsoleUi.Blank();
    ConsoleUi.Text(L.T("Laisse cette fenêtre ouverte (tu peux la réduire) pendant que tu joues :\nles coffres de chaque partie sont comptés quelques secondes après sa fin.",
        "Keep this window open (you can minimize it) while you play:\nthe chests of each match are counted a few seconds after it ends.",
        "Khelli had la fenêtre m7loula (t9der tminimizeha) w nta kat'joui:\nsnadeq dyal kol partiya kayt7sbo chi chwiya d les secondes men b3d ma tsali.", "Deja esta ventana abierta (puedes minimizarla) mientras juegas:\nlos cofres de cada partida se cuentan unos segundos después de que termine."));
    ConsoleUi.Blank();
    ConsoleUi.Text(L.T("Tout se fait avec les flèches et Entrée. Les réglages et l'aide sont dans les onglets en haut de l'écran.",
        "Everything works with the arrow keys and Enter. Settings and help are in the tabs at the top of the screen.",
        "Kolchi b les flèches w Entrée. Les réglages w l'3awn f les onglets lfo9.", "Todo funciona con las flechas e Intro. Los ajustes y la ayuda están en las pestañas de arriba."));
    if (config.ShowOverlay)
    {
        ConsoleUi.Blank();
        ConsoleUi.Text(L.T("Overlay : Ctrl+Maj+O pour l'afficher ou le masquer, Ctrl+Maj+L pour le verrouiller (même en jeu).",
            "Overlay: Ctrl+Shift+O to show or hide it, Ctrl+Shift+L to lock it (even in game).",
            "Overlay: Ctrl+Shift+O bach twerrih wla tkhbbih, Ctrl+Shift+L bach tbloquih (7ta f le jeu).", "Overlay: Ctrl+Shift+O para mostrarlo u ocultarlo, Ctrl+Shift+L para bloquearlo (incluso en el juego)."));
        ConsoleUi.Hint(L.T("Pour le voir en jeu, mets Fortnite en « Plein écran fenêtré » (Paramètres → Vidéo → Mode de fenêtre).",
            "To see it in game, set Fortnite to \"Windowed Fullscreen\" (Settings → Video → Window Mode).",
            "Bach tchoufo f le jeu: Paramètres → Vidéo → Mode de fenêtre → «Plein écran fenêtré».", "Para verlo en el juego, pon Fortnite en \"Pantalla completa en ventana\" (Configuración → Vídeo → Modo de ventana)."));
    }
    ConsoleUi.WaitEnter(L.T("Appuie sur Entrée pour démarrer", "Press Enter to start", "Wrek 3la Entrée bach nbdaw", "Pulsa Intro para empezar"));
}
else
{
    store.SaveConfig(config);
    if (interactive && !once && Profiles.List(data).Count >= 2)
    {
        ConsoleUi.Screen(appName, L.T("profil", "profile", "profil", "perfil"));
        ChooseProfile(data, config, store);
    }
    else if (!consoleMode && config.Profile == null && Profiles.List(data).Count >= 2)
    {
        askProfile = true; // never chosen: the dashboard asks once
    }
}

// ---- Command line: analyze once and quit ---------------------------------------------------
if (once)
{
    ConsoleUi.Screen(appName, $"v{AppInfo.VersionText}");
    tracker.Scan(ignoreQuietPeriod: true);
    ReportWriter.WriteAll(store, config, data);
    tracker.PrintSummary();
    Console.WriteLine(L.T($"  Rapport : {store.ReportPath}", $"  Report: {store.ReportPath}", $"  Rapport: {store.ReportPath}", $"  Informe: {store.ReportPath}"));
    if (printOdds || printFiles)
    {
        // --odds / --files: that tab as text.
        Console.WriteLine();
        foreach (var line in printFiles ? FilesLines() : OddsLines())
        {
            foreach (var seg in line)
            {
                Console.ForegroundColor = seg.Fg;
                Console.Write(seg.Text);
            }
            Console.ResetColor();
            Console.WriteLine();
        }
    }
    return 0;
}

// ---- Main screen ---------------------------------------------------------------------------
// Console: one screen, redrawn in place: tabs, a menu driven by the arrow keys, and an activity area that shows what
// the assistant prints (captured from Console.Out). Dashboard: the same, as a page (WebDashboard).
showWhatsNew = !firstRun && config.LastVersion != AppInfo.VersionText;
if (!showWhatsNew && config.LastVersion != AppInfo.VersionText)
{
    config.LastVersion = AppInfo.VersionText;
    store.SaveConfig(config);
}

if (interactive)
{
    Console.SetOut(log);
    consoleScreen = true;
    try { Console.Clear(); } catch { }
    Frames.Invalidate();
    Draw();
}

if (reanalyzing)
{
    Console.ForegroundColor = ConsoleColor.Cyan;
    Console.WriteLine(L.T("Nouvelle version de l'analyse : les replays encore présents vont être réanalysés (sans doublon).",
        "New analysis version: the replays still on disk will be analyzed again (no duplicates).",
        "Version jdida dyal l'analyse: les replays li f disque ghadi yt3awdo yt'analysaw (bla doublons).", "Nueva versión del análisis: las repeticiones que siguen en el disco se analizarán de nuevo (sin duplicados)."));
    Console.ResetColor();
}

if (config.ShowOverlay)
{
    StartOverlay();
}

if (cleaned is { Any: true })
{
    // Told once, in the activity and on the dashboard.
    Console.ForegroundColor = ConsoleColor.Green;
    Console.WriteLine(cleaned.Text());
    Console.ResetColor();
    notice = new LiveNotice(++noticeId, cleaned.Text(), true, null);
}

if (Store.MovedFrom is { } movedFrom)
{
    Console.ForegroundColor = ConsoleColor.Cyan;
    Console.WriteLine(L.T($"Ton historique a été repris de {movedFrom} (rien n'est perdu).",
        $"Your history was taken over from {movedFrom} (nothing is lost).",
        $"L'historique dyalek jbednah mn {movedFrom} (walou ma ttlef).", $"Tu historial se ha recuperado de {movedFrom} (no se pierde nada)."));
    Console.ResetColor();
}

if (OperatingSystem.IsWindows())
{
    if (config.StartWithFortnite)
    {
        FortniteWatch.Apply(true, dataDir); // follows the exe if it was moved, and restarts the standby
    }
    if (config.OpenShareFiles)
    {
        ShareFiles.Register(); // follows the exe if it was moved
    }
    foreach (var (process, exe) in new[] { ("ChestCounter", "ChestCounter.exe"), ("ChestTracker", "ChestTracker.exe") })
    {
        if (Process.GetProcessesByName(process) is { Length: > 0 } oldTool)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine(L.T($"L'ancien outil ({exe}) est encore ouvert : ferme-le, sinon les deux comptent en même temps.",
                $"The old tool ({exe}) is still open: close it, otherwise both count at the same time.",
                $"L'outil l9dim ({exe}) ba9i m7loul: seddo, wla jouj ghadi y7sbo f nefs l'wa9t.", $"La herramienta antigua ({exe}) sigue abierta: ciérrala, si no las dos cuentan a la vez."));
            Console.ResetColor();
            foreach (var p in oldTool) p.Dispose();
        }
    }
}

// Nothing to read yet: explain why, so that a new user is not left wondering.
if (!tracker.ReplayFiles().Any())
{
    Console.ForegroundColor = ConsoleColor.DarkYellow;
    Console.WriteLine(Directory.Exists(config.DemosFolder)
        ? L.T("Aucun replay pour l'instant : c'est normal si tu n'as pas encore joué depuis que les replays sont activés.",
              "No replay yet: that is expected if you have not played since replays were turned on.",
              "Mazal ma kayn 7ta replay: normal ila ma l3abtich men mlli 7elliti les replays.", "Aún no hay repeticiones: es normal si no has jugado desde que activaste las repeticiones.")
        : L.T("Le dossier des replays n'existe pas encore : Fortnite le crée à la première partie enregistrée.",
              "The replays folder does not exist yet: Fortnite creates it with the first recorded match.",
              "Dossier dyal les replays mazal ma kaynch: Fortnite kaycréyih f awel partiya mregistrya.", "La carpeta de repeticiones aún no existe: Fortnite la crea con la primera partida grabada."));
    Console.WriteLine(L.T("Vérifie dans Fortnite : Paramètres → Jeu → Rediffusions → Enregistrer les rediffusions.",
        "Check in Fortnite: Settings → Game → Replays → Record Replays.",
        "Chouf f Fortnite: Paramètres → Jeu → Rediffusions → Enregistrer les rediffusions.", "Comprueba en Fortnite: Configuración → Juego → Repeticiones → Grabar repeticiones."));
    Console.ResetColor();
}

// Replays left from earlier: the report opens with a loading card, the screen shows a progress bar.
var backlog = tracker.BacklogCount();
if (backlog > 1)
{
    ReportWriter.Progress = new ScanProgress(0, backlog);
}
ReportWriter.WriteAll(store, config, data);
if (!consoleMode)
{
    // The dashboard is the assistant's window: it opens at each start (not with Fortnite: the game keeps the screen).
    if (!auto && !dashboardShown)
    {
        ShowDashboard();
    }
}
else if (config.OpenReportOnStart && !reportOpened && !auto)
{
    Open(store.ReportPath);
}
if (backlog > 0)
{
    tracker.Scan();
    ReportWriter.Progress = null;
    ReportWriter.WriteAll(store, config, data);
}
AnnounceNewProfiles();
UpdateOverlay(store, config, data, tracker.LiveMatch, tracker.IsLive);

if (auto && OperatingSystem.IsWindows() && !(config.MinimizeToTray && interactive))
{
    NativeConsole.Minimize(); // opened with Fortnite: the game keeps the screen
}

Console.CancelKeyPress += (_, e) => { e.Cancel = true; quit = true; wake.Set(); };

// Dashboard: the token icon stays in the notification area for as long as the assistant runs.
if (!consoleMode && OperatingSystem.IsWindows())
{
    Tray.OpenDashboard = ShowDashboard;
    Tray.UseConsole = () => { restartInConsole = restart = quit = true; tracker.StopRequested = true; wake.Set(); };
    Tray.Quit = () => { quit = true; tracker.StopRequested = true; wake.Set(); };
    Tray.Start(dashboardMode: true);
}

// Minimized: into the notification area (classic console window only).
if (interactive && OperatingSystem.IsWindows())
{
    Tray.Enabled = config.MinimizeToTray;
    Tray.OpenDashboard = () => { if (web != null) ShowDashboard(); else Open(store.ReportPath); };
    Tray.Quit = () => { quit = true; wake.Set(); };
    Tray.Start();
    if (auto && config.MinimizeToTray && Tray.Available)
    {
        Tray.Hide(notify: false); // opened with Fortnite: straight to the tray, the game keeps the screen
    }
    else if (auto && config.MinimizeToTray)
    {
        NativeConsole.Minimize(); // no tray for this window (terminal): minimized as before
    }
}

// Keyboard: one thread blocked on ReadKey (no polling). Menus opened from the main loop read the same queue.
if (interactive)
{
    new Thread(() =>
    {
        while (true)
        {
            try { keys.Add(Console.ReadKey(intercept: true)); wake.Set(); }
            catch { return; }
        }
    }) { IsBackground = true, Name = "Keyboard" }.Start();
    ConsoleUi.ReadKey = () => keys.Take();
}

WatchReplays();
WatchInbox();

nextScan = DateTime.UtcNow.AddSeconds(2);
while (!quit)
{
    if (DateTime.UtcNow >= nextScan)
    {
        try
        {
            tracker.Scan();
            AnnounceNewProfiles();
            // Match in progress: re-read the replay being recorded (throttled inside).
            tracker.UpdateLive();
            UpdateOverlay(store, config, data, tracker.LiveMatch, tracker.IsLive);
        }
        catch (Exception ex)
        {
            Console.WriteLine(L.T($"Erreur pendant la surveillance : {ex.Message}", $"Error while watching: {ex.Message}", $"Erreur w ana kanraqeb: {ex.Message}", $"Error durante la vigilancia: {ex.Message}"));
        }
        // A replay being written: check again soon. Otherwise only a safety scan once a minute.
        nextScan = DateTime.UtcNow.AddSeconds(tracker.IsFinishing ? 2 : tracker.HasPending ? 15 : (watcher != null && !auto ? 60 : 10));
    }

    if (auto && OperatingSystem.IsWindows())
    {
        CheckAutoClose();
    }

    // A teammate's file opened while the assistant runs (double-click): it waits in the inbox.
    if (inboxFired)
    {
        inboxFired = false;
        ImportInbox();
    }

    // Treasure Hours: announced before and at the start; the overlay and the dashboard show the boost while it lasts.
    TreasureHourAlerts();

    // The dashboard's buttons, and its page when something changed.
    PumpDashboard();

    if (quit)
    {
        break;
    }
    Draw();

    // Sleep until something happens (file change, key press, a button of the dashboard) or the next scheduled scan.
    // At most one second, so that a resized window or the auto-close countdown are redrawn.
    var wait = nextScan - DateTime.UtcNow;
    if ((interactive || web != null) && wait > TimeSpan.FromSeconds(1))
    {
        wait = TimeSpan.FromSeconds(1);
    }
    if (wait > TimeSpan.Zero && wake.WaitOne(wait))
    {
        // A file change: check again within 2 s (the readiness checks decide when a replay is complete).
        if (keys.Count == 0 && !quit && watcherFired)
        {
            watcherFired = false;
            nextScan = DateTime.UtcNow.AddSeconds(2) < nextScan ? DateTime.UtcNow.AddSeconds(2) : nextScan;
        }
    }

    while (!quit && keys.TryTake(out var key))
    {
        if (autoCloseAt != null)
        {
            // Any key keeps the assistant open after Fortnite.
            autoCloseAt = null;
            auto = false;
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine(L.T("L'assistant reste ouvert.", "The assistant stays open.", "L'assistant ba9i m7loul.", "El asistente sigue abierto."));
            Console.ResetColor();
            continue;
        }
        HandleKey(key);
    }
}

consoleScreen = false;
watcher?.Dispose();
inboxWatcher?.Dispose();
if (web != null)
{
    PumpDashboard();
    Thread.Sleep(300); // the last answer (quit, restart) reaches the page
    ReportWriter.Live = null;
    web.Dispose();
    if (OperatingSystem.IsWindows())
    {
        try { File.Delete(DashboardWindow.AddressFile(store)); } catch { }
    }
}
if (OperatingSystem.IsWindows())
{
    NativeOverlay.Stop();
    Tray.Stop();
}
Console.SetOut(ConsoleUi.Out);
if (interactive)
{
    try { Console.Clear(); } catch { }
    ConsoleUi.RestoreCursor(true);
}

if (restart && AppInfo.ExePath is { } newExe)
{
    try { instance.ReleaseMutex(); } catch { }
    var psi = new ProcessStartInfo(newExe) { UseShellExecute = false };
    for (var i = 0; i < args.Length; i++)
    {
        switch (args[i])
        {
            case "--after-update" or "--reset-config" or "--console":
                continue;
            case "--dash-port" or "--dash-key" or "--open":
                i++;
                continue;
        }
        psi.ArgumentList.Add(args[i]);
    }
    psi.ArgumentList.Add("--after-update");
    if (restartInConsole)
    {
        psi.ArgumentList.Add("--console");
    }
    if (web != null)
    {
        // Same address: the dashboard window already open simply reconnects.
        psi.ArgumentList.Add("--dash-port");
        psi.ArgumentList.Add(web.Port.ToString(CultureInfo.InvariantCulture));
        psi.ArgumentList.Add("--dash-key");
        psi.ArgumentList.Add(web.Key);
    }
    Process.Start(psi)?.Dispose();
    return 0;
}
if (restart)
{
    Console.WriteLine(L.T("  Relance l'assistant pour continuer.", "  Start the assistant again to continue.", "  3awed 7ell l'assistant bach tkemmel.", "  Vuelve a abrir el asistente para continuar."));
}

Console.WriteLine(L.T("  À bientôt !", "  See you soon!", "  Bslama, nchoufok mn ba3d!", "  ¡Hasta pronto!"));
return 0;

// ---- Main screen: drawing ---------------------------------------------------------------------

// A menu entry of the main screen: its label, what Enter does, and a line that explains it.
static (string Label, Action Run, string? About) Item(string label, Action run, string? about = null) => (label, run, about);

List<(string Label, Action Run, string? About)> Items()
{
    var list = new List<(string Label, Action Run, string? About)>();
    switch (tab)
    {
        case HomeTab:
            if (web != null)
            {
                list.Add(Item(L.T("Ouvrir le tableau de bord", "Open the dashboard", "7ell tableau de bord", "Abrir el panel"), ShowDashboard,
                    L.T("Tout l'assistant dans une fenêtre, avec des onglets et des boutons. Le prochain lancement l'ouvre directement.",
                        "The whole assistant in a window, with tabs and buttons. The next start opens it directly.",
                        "L'assistant kamel f fenêtre, b les onglets w les boutons. F lbdya jaya kayt7ell nichan.", "Todo el asistente en una ventana, con pestañas y botones. El próximo inicio lo abre directamente.")));
            }
            list.Add(Item(L.T("Ouvrir le rapport (statistiques détaillées)", "Open the report (detailed statistics)", "7ell le rapport (stats b tafasil)", "Abrir el informe (estadísticas detalladas)"),
                () => Open(store.ReportPath)));
            if (OperatingSystem.IsWindows() && NativeOverlay.IsRunning)
            {
                list.Add(Item(NativeOverlay.IsVisible ? L.T("Masquer l'overlay", "Hide the overlay", "Khebbi l'overlay", "Ocultar el overlay") : L.T("Afficher l'overlay", "Show the overlay", "Werri l'overlay", "Mostrar el overlay"),
                    NativeOverlay.Toggle));
                list.Add(Item(NativeOverlay.IsLocked ? L.T("Déverrouiller l'overlay (pour le déplacer)", "Unlock the overlay (to move it)", "7ell l'overlay (bach t7errko)", "Desbloquear el overlay (para moverlo)")
                        : L.T("Verrouiller l'overlay (la souris passe au travers)", "Lock the overlay (the mouse goes through it)", "Bloqui l'overlay (la souris katdouz mn foqo)", "Bloquear el overlay (el ratón lo atraviesa)"),
                    NativeOverlay.ToggleLock));
            }
            if (Profiles.List(data).Count >= 2)
            {
                list.Add(Item(L.T("Changer de profil", "Switch profile", "Beddel le profil", "Cambiar de perfil"), () =>
                {
                    ConsoleUi.Screen(appName, L.T("profil", "profile", "profil", "perfil"));
                    ChooseProfile(data, config, store);
                    ReportWriter.WriteAll(store, config, data);
                    UpdateOverlay(store, config, data, tracker.LiveMatch, tracker.IsLive);
                }));
            }
            if (showWhatsNew)
            {
                list.Add(Item(L.T("OK, masquer les nouveautés", "OK, hide what's new", "OK, khebbi jdid", "OK, ocultar las novedades"), () =>
                {
                    showWhatsNew = false;
                    config.LastVersion = AppInfo.VersionText;
                    store.SaveConfig(config);
                }));
            }
            list.Add(Item(L.T("Quitter", "Quit", "Khrej", "Salir"), () => quit = true));
            break;

        case OddsTab:
            list.Add(Item(L.T("Ouvrir le rapport (chances et coéquipiers en détail)", "Open the report (odds and teammates in detail)", "7ell le rapport (zher w s7abek b tafasil)", "Abrir el informe (probabilidades y compañeros en detalle)"),
                () => Open(store.ReportPath)));
            list.Add(Item($"{L.T("Coffres comptés", "Chests counted", "Snadeq m7soubin", "Cofres contados")} : {OddsModel.ScopeName(config.Odds.ChestScope)}",
                () => config.Odds.ChestScope = OddsModel.NextScope(config.Odds.ChestScope), ScopeAbout(config.Odds.ChestScope)));
            list.Add(Item(L.T("Réglages des chances…", "Odds settings…", "Réglages dyal zher…", "Ajustes de probabilidades…"), OddsSettingsScreen,
                L.T("Chances publiées, fin de la chasse, coffres ouverts avant le suivi, coéquipiers aléatoires, parties classées, Heures du trésor.",
                    "Published odds, end of the hunt, chests opened before tracking, random teammates, ranked matches, Treasure Hours.",
                    "Les chances li nachrat Epic, lekher dyal la chasse, snadeq li t7ello 9bel l'suivi, s7ab l'hasard (fill), partiyat classées, Sa3at l'kenz.", "Probabilidades publicadas, fin de la búsqueda, cofres abiertos antes del seguimiento, compañeros aleatorios, partidas clasificatorias, Horas del tesoro.")));
            break;

        case SquadTab:
            list.Add(Item(L.T("Exporter mon fichier pour mes coéquipiers", "Export my file for my teammates", "Exporti l'fichier dyali l s7abi", "Exportar mi archivo para mis compañeros"), ExportMine,
                L.T("Crée « ton-nom-date.chesttoken » sur ton Bureau, à envoyer (Discord…). Il contient seulement tes coffres de chaque partie en équipe : pas de replay, ni le nom ou l'identifiant d'un autre joueur.",
                    "Creates \"your-name-date.chesttoken\" on your Desktop, to send (Discord…). It only holds your chests of each team match: no replay, no other player's name or id.",
                    "Kaydir «smitek-date.chesttoken» f le Bureau, bach tsiftou (Discord…). Fih ghir snadeq dyalek f kol partiya f l'équipe: ma fih 7ta replay, wla smiya wla id dyal chi wa7ed akhor.", "Crea \"tu-nombre-fecha.chesttoken\" en tu Escritorio, para enviarlo (Discord…). Solo contiene tus cofres de cada partida en equipo: ni repetición, ni el nombre o id de otro jugador.")));
            list.Add(Item(L.T("Importer le fichier d'un coéquipier", "Import a teammate's file", "Importi l'fichier dyal sa7bek", "Importar el archivo de un compañero"), ImportTeammate,
                L.T("Son fichier .chesttoken (ou son data.json). Seules les parties jouées ensemble dans la même équipe sont gardées. Un double-clic sur le fichier marche aussi.",
                    "Their .chesttoken file (or their data.json). Only the matches played together on the same team are kept. Double-clicking the file works too.",
                    "L'fichier .chesttoken dyalo (wla data.json dyalo). Kaytkhllaw ghir les partiyat li l3btou m3a ba3d f nefs l'équipe. Double-clic 3la l'fichier 7ta howa khdam.", "Su archivo .chesttoken (o su data.json). Solo se guardan las partidas jugadas juntos en el mismo equipo. Hacer doble clic en el archivo también funciona.")));
            if (Team.Imports.Sources.Count > 0)
            {
                list.Add(Item(L.T($"Fichiers importés ({Team.Imports.Sources.Count}) : voir / retirer", $"Imported files ({Team.Imports.Sources.Count}): view / remove", $"Fichiers importés ({Team.Imports.Sources.Count}): chouf / 7iyed", $"Archivos importados ({Team.Imports.Sources.Count}): ver / quitar"), ManageImports,
                    string.Join(", ", Team.Imports.Sources.Select(s => s.Name))));
            }
            list.Add(Item(L.T("Voir les fichiers importés et ce qu'ils changent (onglet Fichiers)", "See the imported files and what they change (Files tab)", "Chouf les fichiers importés w chno kaybeddlo (onglet Fichiyat)", "Ver los archivos importados y lo que cambian (pestaña Archivos)"), () => tab = FilesTab));
            list.Add(Item(L.T("Ouvrir le rapport (escouades en détail)", "Open the report (squads in detail)", "7ell le rapport (les squads b tafasil)", "Abrir el informe (escuadrones en detalle)"), () => Open(store.ReportPath)));
            break;

        case FilesTab:
            list.Add(Item(L.T("Importer le fichier d'un coéquipier", "Import a teammate's file", "Importi l'fichier dyal sa7bek", "Importar el archivo de un compañero"), ImportTeammate,
                L.T("Son fichier .chesttoken (ou son data.json). Il apparaît ici avec ce qu'il change. Un nouveau fichier de la même personne remplace l'ancien.",
                    "Their .chesttoken file (or their data.json). It shows up here with what it changes. A newer file from the same person replaces the older one.",
                    "L'fichier .chesttoken dyalo (wla data.json dyalo). Kayban hna m3a chno kaybeddel. Fichier jdid mn nefs l'wa7ed kay3wwed l9dim.", "Su archivo .chesttoken (o su data.json). Aparece aquí con lo que cambia. Un archivo más reciente de la misma persona sustituye al anterior.")));
            if (Team.Imports.Sources.Count > 0)
            {
                list.Add(Item(L.T("Retirer un fichier…", "Remove a file…", "7iyed chi fichier…", "Quitar un archivo…"), ManageImports,
                    L.T("Le fichier ne compte plus (ses parties reviennent à ce que ton replay a vu). Tu pourras l'importer à nouveau.",
                        "The file no longer counts (its matches go back to what your replay saw). You can import it again later.",
                        "L'fichier ma yb9ach y7seb (les partiyat dyalo kayrej3o l dakchi li chafo le replay dyalek). T9der t3awed timportih.", "El archivo deja de contar (sus partidas vuelven a lo que vio tu repetición). Podrás importarlo de nuevo.")));
            }
            list.Add(Item(L.T("Exporter mon fichier pour mes coéquipiers", "Export my file for my teammates", "Exporti l'fichier dyali l s7abi", "Exportar mi archivo para mis compañeros"), ExportMine,
                L.T("Pour qu'ils voient aussi vos parties ensemble avec les comptes exacts.", "So that they see your matches together with the exact counts too.", "Bach 7ta homa ychoufo les partiyat dyalkom m3a ba3d b l7sab exact.", "Para que ellos también vean vuestras partidas juntos con los recuentos exactos.")));
            list.Add(Item(L.T("Ouvrir le rapport (fichiers importés en détail)", "Open the report (imported files in detail)", "7ell le rapport (les fichiers importés b tafasil)", "Abrir el informe (archivos importados en detalle)"), () => Open(store.ReportPath)));
            break;

        case SettingsTab:
            string YesNo(bool b) => b ? L.T("oui", "yes", "ah", "sí") : L.T("non", "no", "la", "no");
            string Percent(int p) => L.T($"{p} %", $"{p}%", $"{p} %", $"{p} %");
            list.Add(Item($"{L.T("Langue", "Language", "Logha", "Idioma")} : {L.NativeName(L.Code)}", CycleLanguage,
                L.T("Langue de l'assistant, du rapport et de l'overlay. Entrée pour passer à la suivante.",
                    "Language of the assistant, the report and the overlay. Enter for the next one.",
                    "Logha dyal l'assistant, le rapport w l'overlay. Entrée bach dduz l li mora.", "Idioma del asistente, del informe y del overlay. Intro para pasar al siguiente.")));
            if (OperatingSystem.IsWindows())
            {
                list.Add(Item($"{L.T("Overlay en jeu", "In-game overlay", "Overlay f le jeu", "Overlay en el juego")} : {(config.ShowOverlay ? L.T("activé", "on", "khdam", "activado") : L.T("désactivé", "off", "mtafi", "desactivado"))}",
                    ToggleOverlay,
                    L.T("Petite fenêtre toujours au premier plan qui affiche tes coffres en jeu. Désactivé : aucune fenêtre ni raccourci, tout reste dans le rapport.",
                        "Small always-on-top window that shows your chests in game. Off: no window and no shortcut, everything stays in the report.",
                        "Fenêtre sghira dima lfo9 katwerrik snadeq dyalek f le jeu. Mtafi: la fenêtre la raccourci, kolchi kayb9a f le rapport.", "Pequeña ventana siempre visible que muestra tus cofres en el juego. Desactivado: ni ventana ni atajo, todo queda en el informe.")));
                if (config.ShowOverlay)
                {
                    string StyleName(string s) => s switch
                    {
                        "classic" => L.T("classique (carte sombre)", "classic (dark card)", "classique (carte k7la)", "clásico (tarjeta oscura)"),
                        "wide" => L.T("large (tout en un coup d'œil)", "wide (everything at a glance)", "3rid (kolchi f nadra)", "ancho (todo de un vistazo)"),
                        _ => L.T("HUD (intégré au jeu)", "HUD (blends into the game)", "HUD (mdmouj f le jeu)", "HUD (integrado en el juego)"),
                    };
                    list.Add(Item($"{L.T("Style de l'overlay", "Overlay style", "Style dyal l'overlay", "Estilo del overlay")} : {StyleName(config.OverlayStyle)}", () =>
                    {
                        var styles = NativeOverlay.Styles;
                        config.OverlayStyle = styles[(Array.IndexOf(styles, config.OverlayStyle) + 1) % styles.Length];
                        NativeOverlay.SetStyle(config.OverlayStyle);
                    }, L.T("HUD : seuls les chiffres restent, sans fond, comme ceux du jeu (place-le sous la minimap ; un fond apparaît quand il est déverrouillé, pour le déplacer). Classique : la carte sombre des versions précédentes. Entrée pour changer, appliqué tout de suite.",
                        "HUD: only the figures remain, with no background, like the game's (put it under the minimap; a background shows while it is unlocked, to move it). Classic: the dark card of earlier versions. Enter to switch, applied at once.",
                        "HUD: kayb9aw ghir l'ar9am, bla fond, b7al dyal le jeu (7ttou ta7t la minimap; kayban fond mlli ykoun m7loul, bach t7errko). Classique: la carte k7la dyal les versions l9dam. Entrée bach tbeddel, kaytapplika f blasstou.", "HUD: solo quedan las cifras, sin fondo, como las del juego (ponlo bajo el minimapa; aparece un fondo cuando está desbloqueado, para moverlo). Clásico: la tarjeta oscura de versiones anteriores. Intro para cambiar, se aplica al instante.")));
                    list.Add(Item($"{L.T("Taille de l'overlay", "Overlay size", "Taille dyal l'overlay", "Tamaño del overlay")} : {Percent(config.OverlaySize)}", () =>
                    {
                        config.OverlaySize = OverlayAppearance.Next(OverlayAppearance.Sizes, config.OverlaySize);
                        ApplyOverlayAppearance();
                    }, L.T("Entrée pour passer à la taille suivante (appliquée tout de suite).", "Enter for the next size (applied at once).", "Entrée bach dduz l la taille li mora (kattapplika f blasstha).", "Intro para el siguiente tamaño (se aplica al instante).")));
                    list.Add(Item($"{L.T("Opacité de l'overlay", "Overlay opacity", "Opacité dyal l'overlay", "Opacidad del overlay")} : {Percent(config.OverlayOpacity)}", () =>
                    {
                        config.OverlayOpacity = OverlayAppearance.Next(OverlayAppearance.Opacities, config.OverlayOpacity);
                        ApplyOverlayAppearance();
                    }, L.T("Entrée pour passer à l'opacité suivante (appliquée tout de suite).", "Enter for the next opacity (applied at once).", "Entrée bach dduz l l'opacité li mora (kattapplika f blasstha).", "Intro para la siguiente opacidad (se aplica al instante).")));
                }
            }
            if (OperatingSystem.IsWindows())
            {
                list.Add(Item($"{L.T("Réduire dans la zone de notification", "Minimize to the notification area", "Minimizi f la zone de notification", "Minimizar en el área de notificación")} : {YesNo(config.MinimizeToTray)}",
                    () => Tray.Enabled = config.MinimizeToTray = !config.MinimizeToTray,
                    L.T("Oui : réduire la fenêtre la range à côté de l'horloge (icône du jeton) au lieu de la barre des tâches. Un clic sur l'icône la rouvre.",
                        "Yes: minimizing the window puts it next to the clock (token icon) instead of the taskbar. A click on the icon opens it again.",
                        "Ah: mlli tminimizi la fenêtre, katmchi 7da sa3a (icône dyal l'jeton) machi f la barre des tâches. Click 3la l'icône w katrje3.", "Sí: al minimizar la ventana, va junto al reloj (icono de la ficha) en lugar de la barra de tareas. Un clic en el icono la vuelve a abrir.")));
            }
            list.Add(Item($"{L.T("Ouvrir le rapport au démarrage", "Open the report at startup", "7ell le rapport f lbdya", "Abrir el informe al iniciar")} : {YesNo(config.OpenReportOnStart)}",
                () => config.OpenReportOnStart = !config.OpenReportOnStart,
                L.T("Ouvre le rapport dans ton navigateur à chaque lancement de l'assistant.", "Opens the report in your browser each time the assistant starts.", "Kay7ell le rapport f navigateur kol mra kayt7ell l'assistant.", "Abre el informe en tu navegador cada vez que se inicia el asistente.")));
            list.Add(Item($"{L.T("Chances du jeton : coffres comptés", "Token odds: chests counted", "Zher dyal l'jeton: snadeq m7soubin", "Probabilidades de la ficha: cofres contados")} : {OddsModel.ScopeName(config.Odds.ChestScope)}",
                () => config.Odds.ChestScope = OddsModel.NextScope(config.Odds.ChestScope), ScopeAbout(config.Odds.ChestScope)));
            list.Add(Item($"{L.T("Chance du jeton sur l'overlay", "Token chance on the overlay", "Zher dyal l'jeton f l'overlay", "Probabilidad de la ficha en el overlay")} : {YesNo(config.Odds.OverlayTokenLine)}",
                () => config.Odds.OverlayTokenLine = !config.Odds.OverlayTokenLine,
                L.T("Ajoute une ligne « Jeton : 9,02 % » (accès à la boutique pirate) à l'overlay et à overlay.html.",
                    "Adds a \"Token: 9.02%\" line (Pirate Shop Access) to the overlay and to overlay.html.",
                    "Kayzid ster «Jeton: 9,02 %» (accès l l7anout dyal l9ersan) l l'overlay w l overlay.html.", "Añade una línea \"Ficha: 9,02 %\" (acceso a la tienda pirata) al overlay y a overlay.html.")));
            list.Add(Item(L.T("Réglages des chances…", "Odds settings…", "Réglages dyal zher…", "Ajustes de probabilidades…"), OddsSettingsScreen,
                L.T("Chances publiées, fin de la chasse, coffres ouverts avant le suivi, coéquipiers aléatoires, parties classées, Heures du trésor.",
                    "Published odds, end of the hunt, chests opened before tracking, random teammates, ranked matches, Treasure Hours.",
                    "Les chances li nachrat Epic, lekher dyal la chasse, snadeq li t7ello 9bel l'suivi, s7ab l'hasard (fill), partiyat classées, Sa3at l'kenz.", "Probabilidades publicadas, fin de la búsqueda, cofres abiertos antes del seguimiento, compañeros aleatorios, partidas clasificatorias, Horas del tesoro.")));
            if (OperatingSystem.IsWindows())
            {
                list.Add(Item($"{L.T("Ouvrir avec Fortnite", "Open with Fortnite", "7ell m3a Fortnite", "Abrir con Fortnite")} : {YesNo(config.StartWithFortnite)}", ToggleStartWithFortnite,
                    L.T("Un petit processus invisible attend Fortnite et ouvre l'assistant, qui se ferme après. Il reste actif quand tu quittes l'assistant : mets « non » avant de supprimer ou déplacer ChestTokenAssistant.exe.",
                        "A tiny invisible process waits for Fortnite and opens the assistant, which closes afterwards. It keeps running when you quit the assistant: set this to \"no\" before deleting or moving ChestTokenAssistant.exe.",
                        "Processus sghir w mkhbbi kaytsenna Fortnite w kay7ell l'assistant, li kaysed men b3d. Kayb9a khdam mlli tkhrej mn l'assistant: dir «la» 9bel ma tmse7 wla t7errek ChestTokenAssistant.exe.", "Un pequeño proceso invisible espera a Fortnite y abre el asistente, que se cierra después. Sigue activo cuando sales del asistente: pon \"no\" antes de borrar o mover ChestTokenAssistant.exe.")));
            }
            if (OperatingSystem.IsWindows())
            {
                list.Add(Item($"{L.T("Ouvrir les fichiers .chesttoken avec l'assistant", "Open .chesttoken files with the assistant", "7ell les fichiers .chesttoken b l'assistant", "Abrir los archivos .chesttoken con el asistente")} : {YesNo(config.OpenShareFiles)}", ToggleShareFiles,
                    L.T("Oui : un double-clic sur le fichier envoyé par un coéquipier l'importe (même si l'assistant est déjà ouvert). Seulement pour ton compte Windows, sans droits administrateur.",
                        "Yes: double-clicking the file a teammate sent you imports it (even while the assistant is open). For your Windows account only, no admin rights.",
                        "Ah: double-clic 3la l'fichier li siftlek sa7bek kayimportih (7ta ila kan l'assistant m7loul). Ghir l compte Windows dyalek, bla droits admin.", "Sí: hacer doble clic en el archivo que te envió un compañero lo importa (aunque el asistente esté abierto). Solo para tu cuenta de Windows, sin derechos de administrador.")));
            }
            list.Add(Item($"{L.T("Dossier des replays", "Replays folder", "Dossier dyal les replays", "Carpeta de repeticiones")} : {config.DemosFolder}", ChangeDemosFolder,
                L.T("Là où Fortnite enregistre ses replays. À changer seulement si tu l'as déplacé. Entrée pour le modifier.",
                    "Where Fortnite saves its replays. Only change it if you moved it. Enter to edit.",
                    "Fin Fortnite kaykhbbi les replays dyalo. Beddlo ghir ila 7errektih. Entrée bach tbeddlo.", "Donde Fortnite guarda sus repeticiones. Cámbiala solo si la moviste. Intro para editarla.")));
            list.Add(Item(L.T("Recommencer la configuration", "Start the setup again", "3awed la configuration", "Volver a empezar la configuración"), ResetConfiguration,
                L.T("Efface tes réglages et relance les questions du premier lancement. Ton historique de parties est conservé.",
                    "Clears your settings and asks the first-launch questions again. Your match history is kept.",
                    "Kaymse7 les réglages dyalek w kay3awed les questions dyal awel mra. L'historique dyal les partiyat kayb9a.", "Borra tus ajustes y repite las preguntas del primer inicio. Tu historial de partidas se conserva.")));
            break;

        case HelpTab:
            list.Add(Item(L.T("Ouvrir le dossier des données", "Open the data folder", "7ell dossier dyal les données", "Abrir la carpeta de datos"), () => Open(store.Folder),
                L.T($"Historique, réglages et rapport : {store.Folder}", $"History, settings and report: {store.Folder}", $"Historique, réglages w rapport: {store.Folder}", $"Historial, ajustes e informe: {store.Folder}")));
            if (File.Exists(store.ErrorLogPath))
            {
                list.Add(Item(L.T("Ouvrir le journal d'erreurs", "Open the error log", "7ell journal dyal les erreurs", "Abrir el registro de errores"), () => Open(store.ErrorLogPath),
                    L.T("À joindre si tu signales un problème.", "Attach it when you report a problem.", "Zido m3ak ila bghiti tsignali chi mouchkil.", "Adjúntalo si informas de un problema.")));
            }
            list.Add(Item(L.T("Projet d'origine : Chest Counter de LED-N (crédits)", "Original project: Chest Counter by LED-N (credits)", "Le projet l'asli: Chest Counter dyal LED-N (crédits)", "Proyecto original: Chest Counter de LED-N (créditos)"), () => Open(AppInfo.OriginalRepoUrl),
                L.T($"{AppInfo.Name} par {AppInfo.Author}, basé sur {AppInfo.OriginalRepoUrl} (licence MIT). L'assistant lui-même ne va jamais sur Internet.",
                    $"{AppInfo.Name} by {AppInfo.Author}, based on {AppInfo.OriginalRepoUrl} (MIT license). The assistant itself never goes online.",
                    $"{AppInfo.Name} dyal {AppInfo.Author}, mbni 3la {AppInfo.OriginalRepoUrl} (licence MIT). L'assistant 3emmro ma kaydkhol l Internet.", $"{AppInfo.Name} por {AppInfo.Author}, basado en {AppInfo.OriginalRepoUrl} (licencia MIT). El asistente nunca se conecta a Internet.")));
            break;
    }
    return list;
}

string[] TabNames() => new[]
{
    L.T("Accueil", "Home", "Dar", "Inicio"),
    L.T("Chances", "Odds", "Zher", "Probabilidades"),
    L.T("Escouade", "Squad", "Squad", "Escuadrón"),
    L.T("Fichiers", "Files", "Fichiyat", "Archivos"),
    L.T("Réglages", "Settings", "Réglages", "Ajustes"),
    L.T("Aide", "Help", "3awni", "Ayuda"),
};

static string ScopeAbout(string scope) => scope switch
{
    "mine" => L.T("Seulement les coffres que tu as ouverts ou détruits toi-même. Entrée pour changer.",
        "Only the chests you opened or destroyed yourself. Enter to change.",
        "Ghir snadeq li 7elliti wla kssserti b yeddik. Entrée bach tbeddel.", "Solo los cofres que abriste o destruiste tú mismo. Intro para cambiar."),
    "estimated" => L.T("Les tiens + ceux des coéquipiers vus dans ton replay + une estimation de ceux hors de ta vue (voir l'aide). Entrée pour changer.",
        "Yours + your teammates' seen in your replay + an estimate of those out of your sight (see help). Enter to change.",
        "Dyalek + dyal s7abek li chefti f le replay dyalek + ta9dir dyal li kano b3ad 3lik (chouf 3awni). Entrée bach tbeddel.", "Los tuyos + los de tus compañeros vistos en tu repetición + una estimación de los que no ves (ver ayuda). Intro para cambiar."),
    "squad" => L.T("Seulement les parties jouées avec un coéquipier importé, chacun avec son propre compte exact (onglet Escouade). Entrée pour changer.",
        "Only the matches played with an imported teammate, everyone with their own exact count (Squad tab). Enter to change.",
        "Ghir les partiyat li l3bti m3a sa7bek importé, kol wa7ed b l7sab dyalo exact (onglet Squad). Entrée bach tbeddel.", "Solo las partidas jugadas con un compañero importado, cada uno con su propio recuento exacto (pestaña Escuadrón). Intro para cambiar."),
    "imported" => L.T("Les tiens + les comptes exacts des coéquipiers importés (data.json). Rien d'approximatif. Entrée pour changer.",
        "Yours + the exact counts of imported teammates (data.json). Nothing approximate. Enter to change.",
        "Dyalek + l7sab exact dyal s7abek importés (data.json). Walou approximatif. Entrée bach tbeddel.", "Los tuyos + los recuentos exactos de los compañeros importados (data.json). Nada aproximado. Intro para cambiar."),
    _ => L.T("Le jeton est partagé avec l'équipe : tes coffres + ceux de tes coéquipiers vus dans ton replay (minimum : ceux ouverts loin de toi manquent). Un coéquipier importé compte avec son propre total. Entrée pour changer.",
        "The token is shared with the team: your chests + your teammates' seen in your replay (a minimum: those opened far from you are missing). An imported teammate counts with their own total. Enter to change.",
        "L'jeton kaytqsem m3a l'équipe: snadeq dyalek + dyal s7abek li chefti f le replay dyalek (minimum: kaykhssou li t7ello b3id 3lik). Sa7bek importé kay7seb b total dyalo. Entrée bach tbeddel.", "La ficha se comparte con el equipo: tus cofres + los de tus compañeros vistos en tu repetición (un mínimo: faltan los abiertos lejos de ti). Un compañero importado cuenta con su propio total. Intro para cambiar."),
};

void Draw()
{
    if (!consoleScreen || !interactive)
    {
        return;
    }
    lastDraw = DateTime.UtcNow;
    var height = ConsoleUi.Height;
    var width = ConsoleUi.Width;
    var f = new Frame();
    f.Blank();
    f.Add(new Seg($"  {appName}", Theme.Title), new Seg($"   v{AppInfo.VersionText}", ConsoleColor.DarkGray), new Seg($"   by {AppInfo.Author}", Theme.Signature));
    f.Add("  " + new string('─', Math.Min(width - 4, 76)), Theme.Rule);

    // Tabs.
    var names = TabNames();
    var tabLine = new List<Seg> { new("  ") };
    for (var i = 0; i < names.Length; i++)
    {
        tabLine.Add(i == tab ? new Seg($" {names[i]} ", Theme.SelectedFg, Theme.SelectedBg) : new Seg($" {names[i]} ", ConsoleColor.Gray));
        tabLine.Add(new Seg("  "));
    }
    f.Add(tabLine.ToArray());
    f.Blank();

    var items = Items();
    selection[tab] = Math.Clamp(selection[tab], 0, items.Count - 1);

    // Footer (kept at the bottom of the window).
    var footer = new List<Seg[]>();
    if (items[selection[tab]].About is { Length: > 0 } about)
    {
        footer.Add(Array.Empty<Seg>());
        footer.AddRange(ConsoleUi.Wrap(about, width - 6).Select(l => new[] { new Seg("  " + l, ConsoleColor.Gray) }));
    }
    footer.Add(Array.Empty<Seg>());
    footer.Add(new[] { new Seg("  " + L.T("↑ ↓ choisir   Entrée valider   ← → onglets", "↑ ↓ choose   Enter confirm   ← → tabs", "↑ ↓ khtar   Entrée confirmi   ← → les onglets", "↑ ↓ elegir   Intro confirmar   ← → pestañas"), ConsoleColor.DarkGray) });
    if (OperatingSystem.IsWindows() && NativeOverlay.IsRunning)
    {
        var o = L.T("Ctrl+Maj+O", "Ctrl+Shift+O", "Ctrl+Shift+O", "Ctrl+Shift+O") + (NativeOverlay.HotkeyRegistered ? "" : L.T(" (indisponible)", " (unavailable)", " (ma kaynch)", " (no disponible)"));
        var l = L.T("Ctrl+Maj+L", "Ctrl+Shift+L", "Ctrl+Shift+L", "Ctrl+Shift+L") + (NativeOverlay.LockHotkeyRegistered ? "" : L.T(" (indisponible)", " (unavailable)", " (ma kaynch)", " (no disponible)"));
        footer.Add(new[] { new Seg("  " + L.T($"Overlay, même en jeu : {o} afficher / masquer   {l} verrouiller", $"Overlay, even in game: {o} show / hide   {l} lock", $"Overlay, 7ta f le jeu: {o} werri / khebbi   {l} bloqui", $"Overlay, incluso en el juego: {o} mostrar / ocultar   {l} bloquear"), ConsoleColor.DarkGray) });
    }

    // What's new gets the room left once the menu, the footer and a few lines of activity are placed.
    var noteRoom = height - 1 - f.Lines.Count - 7 - items.Count - footer.Count - 4;

    switch (tab)
    {
        case HomeTab: DrawHome(f, noteRoom); break;
        case OddsTab: DrawOdds(f, height - 1 - f.Lines.Count - items.Count - footer.Count - 1); break;
        case SquadTab: DrawSquad(f, height - 1 - f.Lines.Count - items.Count - footer.Count - 1); break;
        case FilesTab: DrawFiles(f, height - 1 - f.Lines.Count - items.Count - footer.Count - 1); break;
        case HelpTab: DrawHelp(f, height - 1 - f.Lines.Count - items.Count - footer.Count - 1); break;
    }

    // Menu.
    for (var i = 0; i < items.Count; i++)
    {
        f.Add(i == selection[tab]
            ? new Seg($"  › {items[i].Label} ", Theme.SelectedFg, Theme.SelectedBg)
            : new Seg($"    {items[i].Label}", ConsoleColor.Gray));
    }

    // Activity (home tab): what the assistant did lately, in the space left.
    var room = height - 1 - f.Lines.Count - footer.Count;
    if (tab == HomeTab && room >= 3)
    {
        f.Blank();
        f.Add("  " + L.T("Activité", "Activity", "Chno w9e3", "Actividad"), ConsoleColor.DarkGray);
        foreach (var line in log.Last(room - 2))
        {
            f.Add(new[] { new Seg("  ") }.Concat(line).ToArray());
        }
    }
    while (f.Lines.Count < height - 1 - footer.Count)
    {
        f.Blank();
    }
    foreach (var line in footer)
    {
        f.Add(line);
    }
    Frames.Draw(f);
}

void DrawHome(Frame f, int noteRoom)
{
    var view = Profiles.View(data, config);
    var matches = view.Matches;
    var profile = Profiles.Current(data, config);
    var profiles = Profiles.List(data);
    var labelWidth = new[] { L.T("Profil", "Profile", "Profil", "Perfil"), L.T("État", "Status", "L7ala", "Estado"), L.T("Dernière partie", "Last match", "Partiya lekhra", "Última partida") }.Max(x => x.Length) + 3;
    void Row(string label, params Seg[] value) => f.Add(new[] { new Seg("  " + label.PadRight(labelWidth), ConsoleColor.DarkGray) }.Concat(value).ToArray());

    Row(L.T("Profil", "Profile", "Profil", "Perfil"), new Seg(profile?.Name ?? L.T("créé à ta première partie", "created with your first match", "kaytcréya m3a awel partiya dyalek", "se crea con tu primera partida"), ConsoleColor.White),
        new Seg(profiles.Count >= 2 ? L.T($"   ({profiles.Count} comptes sur ce PC)", $"   ({profiles.Count} accounts on this PC)", $"   ({profiles.Count} comptes f had PC)", $"   ({profiles.Count} cuentas en este PC)") : "", ConsoleColor.DarkGray));

    // State: what the assistant is doing right now.
    if (autoCloseAt is { } closeAt)
    {
        var s = Math.Max(0, (int)Math.Ceiling((closeAt - DateTime.UtcNow).TotalSeconds));
        Row(L.T("État", "Status", "L7ala", "Estado"), new Seg(L.T($"Fortnite est fermé : l'assistant se ferme dans {s} s. Appuie sur une touche pour le garder ouvert.",
            $"Fortnite is closed: the assistant closes in {s} s. Press any key to keep it open.",
            $"Fortnite tsed: l'assistant ghadi ysed f {s} s. Wrek 3la chi bouton bach yb9a m7loul.", $"Fortnite está cerrado: el asistente se cierra en {s} s. Pulsa cualquier tecla para mantenerlo abierto."), ConsoleColor.Yellow));
    }
    else if (tracker.IsLive)
    {
        var live = tracker.LiveMatch;
        Row(L.T("État", "Status", "L7ala", "Estado"), new Seg("● " + L.T("Partie en cours", "Match in progress", "Partiya ghadya", "Partida en curso"), ConsoleColor.Red),
            new Seg(live != null
                ? L.T($" : {L.Count(live.ChestsOpened, "coffre", "chest", "sndouq", "cofre")} pour l'instant", $": {L.Count(live.ChestsOpened, "coffre", "chest", "sndouq", "cofre")} so far", $": {L.Count(live.ChestsOpened, "coffre", "chest", "sndouq", "cofre")} 7ta l daba", $": {L.Count(live.ChestsOpened, "coffre", "chest", "sndouq", "cofre")} por ahora")
                : L.T(" : ses coffres seront comptés à la fin", ": its chests will be counted at the end", ": snadeq dyalha ghadi yt7sbo f lekher", ": sus cofres se contarán al final"), ConsoleColor.Gray));
    }
    else if (tracker.IsFinishing || tracker.HasPending)
    {
        Row(L.T("État", "Status", "L7ala", "Estado"), new Seg(L.T("Partie terminée : comptage des coffres…", "Match over: counting the chests…", "Partiya salat: kan7seb snadeq…", "Partida terminada: contando los cofres…"), ConsoleColor.Cyan));
    }
    else
    {
        Row(L.T("État", "Status", "L7ala", "Estado"), Directory.Exists(config.DemosFolder)
            ? new Seg(L.T("En attente d'une partie", "Waiting for a match", "Kantsenna partiya", "Esperando una partida"), ConsoleColor.Green)
            : new Seg(L.T("Dossier des replays introuvable (voir l'aide)", "Replays folder not found (see help)", "Dossier dyal les replays ma kaynch (chouf 3awni)", "Carpeta de repeticiones no encontrada (ver ayuda)"), ConsoleColor.DarkYellow));
    }

    var chests = matches.Sum(m => m.ChestsOpened);
    var treasures = matches.Sum(m => m.TreasureFinds.Count);
    var avg = matches.Count > 0 ? ((double)chests / matches.Count).ToString("F1", L.Culture) : "0";
    Row(L.T("Total", "Total", "Total", "Total"), new Seg(L.Count(chests, "coffre", "chest", "sndouq", "cofre"), ConsoleColor.Yellow),
        new Seg($"  ·  {L.Count(matches.Count, "partie", "parties", "match", "matches", "partiya", "partiyat", "partida", "partidas")}  ·  {L.T($"{avg} par partie", $"{avg} per match", $"{avg} f partiya", $"{avg} por partida")}", ConsoleColor.Gray),
        new Seg(treasures > 0 ? $"  ·  ★ {L.Count(treasures, "trésor", "treasure", "kenz", "tesoro")}" : "", ConsoleColor.Magenta),
        new Seg(treasures > 0 ? L.T($" (1 tous les {L.N((int)Math.Round((double)chests / treasures))} coffres)", $" (1 every {L.N((int)Math.Round((double)chests / treasures))} chests)", $" (1 f kol {L.N((int)Math.Round((double)chests / treasures))} sndouq)", $" (1 cada {L.N((int)Math.Round((double)chests / treasures))} cofres)") : "", ConsoleColor.DarkGray));
    var session = matches.Where(ReportWriter.IsThisSession).ToList();
    Row(L.T("Session", "Session", "Session", "Sesión"), new Seg(L.Count(session.Sum(m => m.ChestsOpened), "coffre", "chest", "sndouq", "cofre"), ConsoleColor.Yellow),
        new Seg($"  ·  {L.Count(session.Count, "partie", "parties", "match", "matches", "partiya", "partiyat", "partida", "partidas")}", ConsoleColor.Gray));
    if (matches.OrderByDescending(m => m.MatchStartUtc ?? m.AnalyzedAtUtc).FirstOrDefault() is { } last)
    {
        Row(L.T("Dernière partie", "Last match", "Partiya lekhra", "Última partida"), new Seg(L.Count(last.ChestsOpened, "coffre", "chest", "sndouq", "cofre"), ConsoleColor.Yellow),
            new Seg($"  ·  {L.DateTimeShort((last.MatchStartUtc ?? last.AnalyzedAtUtc).ToLocalTime())}  ·  {Pretty.Playlist(last.Playlist)}  ·  {Pretty.Placement(last)}", ConsoleColor.Gray));
    }
    f.Blank();

    // First launch of a new version: what it brings (the notes are built into the exe).
    if (showWhatsNew && ReleaseNotes.WhatsNew(ReleaseNotes.Embedded, L.Code) is { Count: > 0 } current)
    {
        NotesBox(f, noteRoom, L.T($"Nouveautés de la {AppInfo.VersionText}", $"What's new in {AppInfo.VersionText}", $"Jdid dyal {AppInfo.VersionText}", $"Novedades de la {AppInfo.VersionText}"),
            L.T("  (nouvelle version)", "  (new version)", "  (version jdida)", "  (nueva versión)"), current, null);
    }
}

void NotesBox(Frame f, int noteRoom, string title, string subtitle, IReadOnlyList<string> notes, string? pageUrl)
{
    {
        f.Add(new Seg("  ┌ ", ConsoleColor.Green), new Seg(title, ConsoleColor.Green), new Seg(subtitle, ConsoleColor.DarkGray));
        var shown = 0;
        foreach (var note in notes)
        {
            var wrapped = ConsoleUi.Wrap(note, ConsoleUi.Width - 10).ToList();
            if (shown + wrapped.Count > Math.Clamp(noteRoom - 3, 1, 12))
            {
                f.Add(new Seg("  │ ", ConsoleColor.Green), new Seg(pageUrl != null ? L.T($"… et plus : {pageUrl}", $"… and more: {pageUrl}", $"… w kter: {pageUrl}", $"… y más: {pageUrl}") : "…", ConsoleColor.DarkGray));
                break;
            }
            for (var i = 0; i < wrapped.Count; i++)
            {
                f.Add(new Seg("  │ ", ConsoleColor.Green), new Seg((i == 0 ? "• " : "  ") + wrapped[i], ConsoleColor.Gray));
            }
            shown += wrapped.Count;
        }
        if (notes.Count == 0 && pageUrl != null)
        {
            f.Add(new Seg("  │ ", ConsoleColor.Green), new Seg(L.T($"Nouveautés : {pageUrl}", $"What's new: {pageUrl}", $"Jdid: {pageUrl}", $"Novedades: {pageUrl}"), ConsoleColor.Gray));
        }
        f.Add("  └", ConsoleColor.Green);
        f.Blank();
    }
}

// Odds tab: the Pirate Shop token and the weekly treasures, planning, pace, luck. The report shows the same in detail.
void DrawOdds(Frame f, int room)
{
    // The menu must stay on screen: a small window shows fewer figures (all of them are in the report).
    foreach (var line in OddsLines().Take(Math.Max(0, room)))
    {
        f.Add(line);
    }
}

List<Seg[]> OddsLines()
{
    var r = OddsModel.Build(Profiles.View(data, config), config);
    var s = r.Settings;
    var lines = new List<Seg[]>();
    var labels = new[]
    {
        L.T("Coffres comptés", "Chests counted", "Snadeq m7soubin", "Cofres contados"), L.T("Objectif", "Target", "Objectif", "Objetivo"), L.T("Rythme", "Pace", "Rythme", "Ritmo"),
        L.T("Fin de la chasse", "End of the hunt", "Lekher dyal la chasse", "Fin de la búsqueda"), L.T("Solo / équipe", "Solo / squad", "Solo / squad", "Solo / escuadrón"), L.T("Chance", "Luck", "Zher", "Suerte"),
        L.T("Trésors", "Treasure rolls", "Knouz", "Tesoros"), L.T("Heures du trésor", "Treasure Hours", "Sa3at l'kenz", "Horas del tesoro"), L.T("Équipe", "Team", "L'équipe", "Equipo"),
    }.Concat(TreasureDef.All.Select(d => d.ShortName));
    var labelWidth = labels.Max(x => x.Length) + 3;
    void Row(string label, params Seg[] value) => lines.Add(new[] { new Seg("  " + label.PadRight(labelWidth), ConsoleColor.DarkGray) }.Concat(value).ToArray());
    string EndDate(DateTime utc) => OddsModel.Utc(utc).ToLocalTime().ToString(L.T("d MMM", "d MMM", "d MMM", "d MMM"), L.Culture);

    // What the odds are computed from.
    var parts = new List<string> { $"{L.N(r.Mine)} {L.T("à toi", "yours", "dyalek", "tuyos")}" };
    if (r.Scope is "team" or "estimated")
    {
        parts.Add($"{L.N(r.TeamSeen)} {L.T("équipe (vus)", "team (seen)", "équipe (li chefti)", "equipo (vistos)")}");
    }
    if (r.Scope != "mine" && (r.TeamImported > 0 || r.Scope == "imported"))
    {
        parts.Add($"{L.N(r.TeamImported)} {L.T("importés", "imported", "importés", "importados")}");
    }
    if (r.Scope == "estimated")
    {
        parts.Add($"≈{L.Num(r.TeamEstimated)} {L.T("estimés", "estimated", "ta9diran", "estimados")}");
    }
    if (r.BaselineMine + r.BaselineTeam > 0)
    {
        parts.Add($"{L.N(r.BaselineMine + r.BaselineTeam)} {L.T("avant le suivi", "before tracking", "9bel l'suivi", "antes del seguimiento")}");
    }
    Row(L.T("Coffres comptés", "Chests counted", "Snadeq m7soubin", "Cofres contados"), new Seg(L.Num(r.Total), ConsoleColor.Yellow), new Seg("  =  " + string.Join(" + ", parts), ConsoleColor.Gray));
    // (Which chests count is shown on the menu line below.)
    var notes = new List<string>();
    if (r.ExcludedMatches > 0)
    {
        notes.Add(L.T($"{L.Count(r.ExcludedMatches, "partie exclue", "parties exclues", "", "", "", "", "", "")} (classées / hors chasse, {L.Count(r.ExcludedChests, "coffre", "chest", "sndouq", "cofre")})",
            $"{L.Count(r.ExcludedMatches, "", "", "match left out", "matches left out", "", "", "partida excluida", "partidas excluidas")} (ranked / outside the hunt, {L.Count(r.ExcludedChests, "coffre", "chest", "sndouq", "cofre")})",
            $"{L.Count(r.ExcludedMatches, "", "", "", "", "partiya mkhroja", "partiyat mkhrojin", "", "")} (classées / barra la chasse, {L.Count(r.ExcludedChests, "coffre", "chest", "sndouq", "cofre")})", $"{L.Count(r.ExcludedMatches, "", "", "", "", "", "", "partida excluida", "partidas excluidas")} (clasificatorias / fuera de la búsqueda, {L.Count(r.ExcludedChests, "coffre", "chest", "sndouq", "cofre")})"));
    }
    if (r.Scope is not ("mine" or "squad") && r.MatchesNoTeamData > 0)
    {
        notes.Add(L.T($"{r.MatchesNoTeamData} sans données d'équipe", $"{r.MatchesNoTeamData} without team data", $"{r.MatchesNoTeamData} bla données dyal l'équipe", $"{r.MatchesNoTeamData} sin datos de equipo"));
    }
    if (r.Scope == "squad")
    {
        notes.Add(L.T($"{r.NonSquadMatches} sans coéquipier importé, non comptées", $"{r.NonSquadMatches} without an imported teammate, not counted", $"{r.NonSquadMatches} bla sa7bek importé, ma t7sbouch", $"{r.NonSquadMatches} sin compañero importado, no contadas"));
    }
    if (notes.Count > 0)
    {
        Row("", new Seg(string.Join(" · ", notes), ConsoleColor.DarkGray));
    }
    lines.Add(Array.Empty<Seg>());

    // Pirate Shop Access, with its bar (yellow = token, grey = no token), then the weekly treasures.
    var pirate = r.Pirate;
    const int barWidth = 30;
    var filled = pirate.P > 0 ? Math.Clamp((int)Math.Round(pirate.P * barWidth), 1, barWidth) : 0;
    Row(pirate.Def.ShortName, new Seg(L.Pct(pirate.P), ConsoleColor.Yellow), new Seg($"  {L.OneIn(pirate.P)}   ", ConsoleColor.Gray),
        new Seg(new string('█', filled), ConsoleColor.Yellow), new Seg(new string('░', barWidth - filled), ConsoleColor.DarkGray),
        new Seg($"   {L.T("pas de jeton", "no token", "bla jeton", "sin ficha")} {L.Pct(1 - pirate.P)}", ConsoleColor.DarkGray));
    Row("", new Seg(L.T($"{pirate.Lambda.ToString("0.000", L.Culture)} jeton attendu · chances effectives {L.OneIn(pirate.PerChest)} par coffre",
        $"{pirate.Lambda.ToString("0.000", L.Culture)} drops expected · effective odds {L.OneIn(pirate.PerChest)} per chest",
        $"{pirate.Lambda.ToString("0.000", L.Culture)} jeton mtsnna · zher l79i9i {L.OneIn(pirate.PerChest)} f kol sndouq", $"{pirate.Lambda.ToString("0.000", L.Culture)} fichas esperadas · probabilidad efectiva {L.OneIn(pirate.PerChest)} por cofre"), ConsoleColor.DarkGray));
    foreach (var t in r.Treasures.Where(t => t.Def.Weekly))
    {
        Row(t.Def.ShortName, new Seg($"{L.T("cette semaine", "this week", "had ssimana", "esta semana")} {L.Pct(t.WeekP ?? 0)}", ConsoleColor.White),
            new Seg($"  ·  {L.T("toute la chasse", "whole hunt", "la chasse kamla", "toda la búsqueda")} {L.Pct(t.P)}  ·  {L.OneIn(1.0 / Math.Max(1, t.Def.Odds(s)))} {L.T("par coffre", "per chest", "f kol sndouq", "por cofre")}", ConsoleColor.Gray));
    }
    lines.Add(Array.Empty<Seg>());

    // Planning: chests needed for each target, what is left, matches / hours at the current pace, reachable in time?
    var first = true;
    foreach (var p in r.Plan)
    {
        var target = L.Pct(p.Target).Replace(",00", "").Replace(".00", "");
        var segs = new List<Seg> { new($"{target,-6}", ConsoleColor.White), new(first ? $"{L.Num(p.Needed)} {L.T("coffres", "chests", "snadeq", "cofres")}" : L.Num(p.Needed), ConsoleColor.Gray) };
        if (p.ToGo <= 0)
        {
            segs.Add(new Seg($"  ✔ {L.T("atteint", "reached", "wselti", "alcanzado")}", ConsoleColor.Green));
        }
        else
        {
            segs.Add(new Seg($" · {L.T("encore", "to go", "ba9i", "faltan")} {L.Num(p.ToGo)}"
                + (p.Matches is double pm ? $" · ~{L.Num(pm)} {L.T("parties", "matches", "partiyat", "partidas")}" : "")
                + (p.Hours is double ph ? $" · ~{L.Num(ph)} h" : ""), ConsoleColor.Gray));
            if (!p.Reachable)
            {
                segs.Add(new Seg(L.T($"  ✖ pas d'ici le {EndDate(s.HuntEndUtc)}", $"  ✖ not by {EndDate(s.HuntEndUtc)}", $"  ✖ machi 9bel {EndDate(s.HuntEndUtc)}", $"  ✖ no antes del {EndDate(s.HuntEndUtc)}"), ConsoleColor.DarkYellow));
            }
        }
        Row(first ? L.T("Objectif", "Target", "Objectif", "Objetivo") : "", segs.ToArray());
        first = false;
    }

    // Pace, end of the hunt, solo vs squad.
    if (r.Matches > 0)
    {
        var paceDays = L.N((int)Math.Round(r.PaceDays));
        Row(L.T("Rythme", "Pace", "Rythme", "Ritmo"), new Seg(
            L.T($"{L.Num(r.MinePerMatch)} / partie à toi", $"{L.Num(r.MinePerMatch)} / match yours", $"{L.Num(r.MinePerMatch)} / partiya dyalek", $"{L.Num(r.MinePerMatch)} / partida tuyos")
            + (r.TeamMatches > 0 && r.Scope != "mine" ? L.T($" · +{L.Num(r.TeamPerTeamMatch)} de l'équipe (en équipe)", $" · +{L.Num(r.TeamPerTeamMatch)} team (team matches)", $" · +{L.Num(r.TeamPerTeamMatch)} dyal l'équipe", $" · +{L.Num(r.TeamPerTeamMatch)} del equipo (en equipo)") : "")
            + $" · {L.Num(r.PerHour)} / h · {L.Num(r.PerDay)} / {L.T("jour", "day", "nhar", "día")}"
            + L.T($" (sur {paceDays} jours)", $" (over {paceDays} days)", $" (f {paceDays} iyam)", $" (en {paceDays} días)"), ConsoleColor.Gray));
    }
    if (r.DaysLeft > 0)
    {
        Row(L.T("Fin de la chasse", "End of the hunt", "Lekher dyal la chasse", "Fin de la búsqueda"),
            new Seg(L.T($"{L.Num(Math.Ceiling(r.DaysLeft))} jours ({EndDate(s.HuntEndUtc)})", $"{L.Num(Math.Ceiling(r.DaysLeft))} days left ({EndDate(s.HuntEndUtc)})", $"{L.Num(Math.Ceiling(r.DaysLeft))} iyam ({EndDate(s.HuntEndUtc)})", $"{L.Num(Math.Ceiling(r.DaysLeft))} días ({EndDate(s.HuntEndUtc)})"), ConsoleColor.White),
            new Seg(L.T($" · +{L.Num(r.Projected)} coffres à ce rythme → {L.Pct(pirate.PEnd)} · à activer avant le {EndDate(s.ActivateByUtc)}",
                $" · +{L.Num(r.Projected)} chests at this pace → {L.Pct(pirate.PEnd)} · activate by {EndDate(s.ActivateByUtc)}",
                $" · +{L.Num(r.Projected)} sndouq b had rythme → {L.Pct(pirate.PEnd)} · activi 9bel {EndDate(s.ActivateByUtc)}", $" · +{L.Num(r.Projected)} cofres a este ritmo → {L.Pct(pirate.PEnd)} · activar antes del {EndDate(s.ActivateByUtc)}"), ConsoleColor.Gray));
    }
    else
    {
        Row(L.T("Fin de la chasse", "End of the hunt", "Lekher dyal la chasse", "Fin de la búsqueda"), new Seg(L.T($"terminée ({EndDate(s.HuntEndUtc)})", $"over ({EndDate(s.HuntEndUtc)})", $"salat ({EndDate(s.HuntEndUtc)})", $"terminada ({EndDate(s.HuntEndUtc)})"), ConsoleColor.DarkGray));
    }
    if (r.SquadPerHour > 0 && r.SoloPerHour > 0)
    {
        Row(L.T("Solo / équipe", "Solo / squad", "Solo / squad", "Solo / escuadrón"), new Seg(
            L.T($"{L.Pct(r.PSoloHour)} par heure seul{(r.SoloFromSoloMatches ? "" : " (tes coffres)")}  ·  {L.Pct(r.PSquadHour)} par heure avec ton équipe (×{(r.SquadPerHour / r.SoloPerHour).ToString("0.0", L.Culture)})",
                $"{L.Pct(r.PSoloHour)} per hour solo{(r.SoloFromSoloMatches ? "" : " (your own chests)")}  ·  {L.Pct(r.PSquadHour)} per hour with your squad (×{(r.SquadPerHour / r.SoloPerHour).ToString("0.0", L.Culture)})",
                $"{L.Pct(r.PSoloHour)} f sa3a bou7dek{(r.SoloFromSoloMatches ? "" : " (snadeq dyalek)")}  ·  {L.Pct(r.PSquadHour)} f sa3a m3a l'équipe (×{(r.SquadPerHour / r.SoloPerHour).ToString("0.0", L.Culture)})", $"{L.Pct(r.PSoloHour)} por hora solo{(r.SoloFromSoloMatches ? "" : " (tus cofres)")}  ·  {L.Pct(r.PSquadHour)} por hora con tu escuadrón (×{(r.SquadPerHour / r.SoloPerHour).ToString("0.0", L.Culture)})"), ConsoleColor.Gray));
    }

    // Luck and treasure rolls.
    Row(L.T("Chance", "Luck", "Zher", "Suerte"), new Seg(L.T($"{L.Pct(1 - pirate.P)} des joueurs avec autant de coffres n'ont pas encore de jeton non plus",
        $"{L.Pct(1 - pirate.P)} of players with as many chests have no token yet either",
        $"{L.Pct(1 - pirate.P)} mn li 7ellou 9ed had snadeq 7ta homa bla jeton, ma t7zench", $"{L.Pct(1 - pirate.P)} de los jugadores con tantos cofres tampoco tienen ficha todavía"), ConsoleColor.Gray));
    if (r.RollChests > 0)
    {
        var (lo, hi) = OddsMath.Wilson(r.RollTreasures, r.RollChests);
        Row(L.T("Trésors", "Treasure rolls", "Knouz", "Tesoros"), new Seg(r.RollTreasures > 0
            ? L.T($"1 tous les {L.Num((double)r.RollChests / r.RollTreasures)} coffres (95 % : {L.Num(1 / hi)} à {L.Num(1 / lo)}) · communauté : ~1 / 100 (non officiel)",
                $"1 per {L.Num((double)r.RollChests / r.RollTreasures)} chests (95%: {L.Num(1 / hi)} to {L.Num(1 / lo)}) · community: ~1 per 100 (unofficial)",
                $"1 f kol {L.Num((double)r.RollChests / r.RollTreasures)} sndouq (95 %: {L.Num(1 / hi)} 7ta {L.Num(1 / lo)}) · communauté: ~1/100 (machi officiel)", $"1 cada {L.Num((double)r.RollChests / r.RollTreasures)} cofres (95 %: {L.Num(1 / hi)} a {L.Num(1 / lo)}) · comunidad: ~1 cada 100 (no oficial)")
            : L.T($"aucun trésor en {L.N(r.RollChests)} coffres", $"no treasure in {L.N(r.RollChests)} chests", $"7ta kenz f {L.N(r.RollChests)} sndouq", $"ningún tesoro en {L.N(r.RollChests)} cofres"), ConsoleColor.Gray));
    }

    // Treasure Hours.
    string HourText(TreasureHour h) => $"×{h.PirateShop.ToString("0.##", L.Culture)} {L.DateTimeShort(OddsModel.Utc(h.StartUtc).ToLocalTime())}–{OddsModel.Utc(h.EndUtc).ToLocalTime():HH:mm}";
    Row(L.T("Heures du trésor", "Treasure Hours", "Sa3at l'kenz", "Horas del tesoro"), new Seg(r.HoursConfigured == 0
        ? L.T("aucune réglée (config.json → Odds → TreasureHours, voir l'aide)", "none set (config.json → Odds → TreasureHours, see help)", "7ta wa7da (config.json → Odds → TreasureHours, chouf 3awni)", "ninguna configurada (Ajustes → Horas del tesoro)")
        : L.T($"{L.Num(r.BoostedChests)} coffres boostés (+{L.Num(r.BoostExtra)} en plus)", $"{L.Num(r.BoostedChests)} boosted chests (+{L.Num(r.BoostExtra)} extra)", $"{L.Num(r.BoostedChests)} sndouq boosté (+{L.Num(r.BoostExtra)} extra)", $"{L.Num(r.BoostedChests)} cofres potenciados (+{L.Num(r.BoostExtra)} extra)")
          + (r.ActiveHour is { } now ? L.T($" · EN COURS {HourText(now)}", $" · NOW {HourText(now)}", $" · DABA {HourText(now)}", $" · AHORA {HourText(now)}")
              : r.NextHour is { } next ? L.T($" · prochaine {HourText(next)}", $" · next {HourText(next)}", $" · jaya {HourText(next)}", $" · próxima {HourText(next)}") : ""),
        r.ActiveHour != null ? ConsoleColor.Yellow : ConsoleColor.Gray));

    // Team: what was seen is a minimum; the estimate (optional) says how much is probably missing.
    if (r.Scope != "mine")
    {
        Row(L.T("Équipe", "Team", "L'équipe", "Equipo"), new Seg(L.T($"{L.N(r.TeamSeenAll)} coffres de coéquipiers vus dans tes replays (minimum : sans ceux au loin)",
            $"{L.N(r.TeamSeenAll)} teammate chests seen in your replays (a minimum: far-away chests are missing)",
            $"{L.N(r.TeamSeenAll)} sndouq dyal s7abek chefthom f les replays dyalek (ghir minimum)", $"{L.N(r.TeamSeenAll)} cofres de compañeros vistos en tus repeticiones (un mínimo: faltan los lejanos)"), ConsoleColor.Gray));
        var e = r.Estimate;
        Row("", new Seg(e.Method switch
        {
            "rate" => L.T($"≈ {L.Num(e.Value)} de plus hors de ta vue ({L.Num(e.Low ?? 0)}–{L.Num(e.High ?? 0)}), estimé d'après {L.Count(e.Grants, "trésor", "trésors", "", "", "", "", "", "")}",
                $"≈ {L.Num(e.Value)} more out of your sight ({L.Num(e.Low ?? 0)}–{L.Num(e.High ?? 0)}), estimated from {L.Count(e.Grants, "", "", "treasure", "treasures", "", "", "tesoro", "tesoros")}",
                $"≈ {L.Num(e.Value)} kter b3ad 3la 3inik ({L.Num(e.Low ?? 0)}–{L.Num(e.High ?? 0)}), m7soub mn {L.Count(e.Grants, "", "", "", "", "kenz", "knouz", "", "")}", $"≈ {L.Num(e.Value)} más fuera de tu vista ({L.Num(e.Low ?? 0)}–{L.Num(e.High ?? 0)}), estimado a partir de {L.Count(e.Grants, "", "", "", "", "", "", "tesoro", "tesoros")}"),
            "ratio" => L.T($"≈ {L.Num(e.Value)} de plus hors de ta vue, calibré sur {e.Basis} parties importées (tu vois {L.Pct(e.Ratio ?? 0)})",
                $"≈ {L.Num(e.Value)} more out of your sight, calibrated on {e.Basis} imported matches (you see {L.Pct(e.Ratio ?? 0)})",
                $"≈ {L.Num(e.Value)} kter b3ad 3la 3inik, m3ayer 3la {e.Basis} partiya importée (katchouf {L.Pct(e.Ratio ?? 0)})", $"≈ {L.Num(e.Value)} más fuera de tu vista, calibrado con {e.Basis} partidas importadas (ves el {L.Pct(e.Ratio ?? 0)})"),
            _ => L.T("estimation des coffres hors de ta vue : pas encore assez de données", "estimate of chests out of your sight: not enough data yet", "ta9dir dyal snadeq li b3ad 3la 3inik: mazal ma kafyinch les données", "estimación de los cofres fuera de tu vista: aún no hay suficientes datos"),
        }, ConsoleColor.DarkGray));
    }
    lines.Add(Array.Empty<Seg>());
    return lines;
}

// Squad tab: who was imported, and each squad (the exact set of imported teammates on your team) with exact counts.
void DrawSquad(Frame f, int room)
{
    var view = Profiles.View(data, config);
    var squads = SquadModel.Build(view, config);
    var lines = new List<Seg[]>();
    var labelWidth = new[] { L.T("Ton fichier", "Your file", "L'fichier dyalek", "Tu archivo"), L.T("Importés", "Imported", "Importés", "Importados"), L.T("Escouades", "Squads", "Les squads", "Escuadrones"), L.T("Toutes", "All of them", "Kamlin", "Todas") }.Max(x => x.Length) + 3;
    void Row(string label, params Seg[] value) => lines.Add(new[] { new Seg("  " + label.PadRight(labelWidth), ConsoleColor.DarkGray) }.Concat(value).ToArray());

    Row(L.T("Ton fichier", "Your file", "L'fichier dyalek", "Tu archivo"), new Seg(L.T("exporte-le ci-dessous et envoie-le à tes coéquipiers ; importe le leur", "export it below and send it to your teammates; import theirs", "exportih lta7t w siftou l s7abek; importi dyalhom", "expórtalo abajo y envíalo a tus compañeros; importa el suyo"), ConsoleColor.Gray));
    var sources = Team.Imports.Sources.OrderByDescending(s => s.Matches.Count).ToList();
    Row(L.T("Importés", "Imported", "Importés", "Importados"), sources.Count == 0
        ? new Seg(L.T("personne pour l'instant", "nobody yet", "7ta wa7ed l daba", "nadie todavía"), ConsoleColor.DarkGray)
        : new Seg(string.Join("  ·  ", sources.Select(s => $"{s.Name} ({L.Count(s.Matches.Count, "partie", "parties", "match", "matches", "partiya", "partiyat", "partida", "partidas")})")), Theme.Signature));
    lines.Add(Array.Empty<Seg>());

    if (squads.Count == 0)
    {
        Row(L.T("Escouades", "Squads", "Les squads", "Escuadrones"), new Seg(sources.Count == 0
            ? L.T("importe le fichier d'un coéquipier pour voir vos parties ensemble, avec le compte exact de chacun",
                "import a teammate's file to see your matches together, with everyone's exact count",
                "importi l'fichier dyal sa7bek bach tchouf les partiyat dyalkom m3a ba3d, b l7sab exact dyal kol wa7ed", "importa el archivo de un compañero para ver vuestras partidas juntos, con el recuento exacto de cada uno")
            : L.T("aucune partie ensemble dans la même équipe (hors parties classées et hors chasse)",
                "no match together on the same team (ranked and out-of-hunt matches left out)",
                "7ta partiya m3a ba3d f nefs l'équipe (bla classées w bla li barra la chasse)", "ninguna partida juntos en el mismo equipo (sin clasificatorias ni partidas fuera de la búsqueda)"), ConsoleColor.DarkGray));
    }
    else
    {
        Row(L.T("Escouades", "Squads", "Les squads", "Escuadrones"), new Seg(L.T("parties que vous avez tous enregistrées · le compte exact de chacun", "matches you all recorded · everyone's exact count", "partiyat li sejjeltouhom kamlin · l7sab exact dyal kol wa7ed", "partidas que grabasteis todos · el recuento exacto de cada uno"), ConsoleColor.DarkGray));
        var you = L.T("Toi", "You", "Nta", "Tú");
        var nameWidth = Math.Min(40, squads.Max(g => (you + " + " + string.Join(" + ", g.Members.Select(m => m.Name))).Length) + 2);
        foreach (var g in squads)
        {
            var title = you + " + " + string.Join(" + ", g.Members.Select(m => m.Name));
            var detail = $"{you} {L.N(g.MyChests)} · " + string.Join(" · ", g.Members.Select(m => $"{m.Name} {L.N(m.Chests)}"));
            lines.Add(new[]
            {
                new Seg("    " + (title.Length > nameWidth - 2 ? title[..(nameWidth - 3)] + "…" : title).PadRight(nameWidth), ConsoleColor.White),
                new Seg($"{L.Count(g.Matches, "partie", "parties", "match", "matches", "partiya", "partiyat", "partida", "partidas"),-12}", ConsoleColor.Gray),
                new Seg($"{L.Count(g.Total, "coffre", "chest", "sndouq", "cofre"),-14}", Theme.Value),
                new Seg($"{L.T("jeton", "token", "jeton", "ficha")} {L.Pct(g.P)}", ConsoleColor.Yellow),
            });
            lines.Add(new[] { new Seg("    " + new string(' ', nameWidth) + detail + (g.OthersSeen > 0 ? L.T($" · +{g.OthersSeen} vus (non importés)", $" · +{g.OthersSeen} seen (not imported)", $" · +{g.OthersSeen} li chefti (machi importés)", $" · +{g.OthersSeen} vistos (no importados)") : ""), ConsoleColor.DarkGray) });
        }
        lines.Add(Array.Empty<Seg>());
        var all = OddsModel.Build(view, config, scopeOverride: "squad");
        Row(L.T("Toutes", "All of them", "Kamlin", "Todas"), new Seg($"{L.Count(all.Matches, "partie", "parties", "match", "matches", "partiya", "partiyat", "partida", "partidas")} · {L.Count((int)all.Total, "coffre", "chest", "sndouq", "cofre")} → ", ConsoleColor.Gray),
            new Seg($"{L.T("jeton", "token", "jeton", "ficha")} {L.Pct(all.Pirate.P)}", ConsoleColor.Yellow),
            new Seg(L.T("   (onglet Chances : « parties en escouade seulement »)", "   (Odds tab: \"squad matches only\")", "   (onglet Zher: «ghir partiyat squad»)", "   (pestaña Probabilidades: \"solo partidas en escuadrón\")"), ConsoleColor.DarkGray));
    }
    lines.Add(Array.Empty<Seg>());
    foreach (var line in lines.Take(Math.Max(0, room)))
    {
        f.Add(line);
    }
}

// Files tab: every teammate's file you saved, and what it changes (matches, chests your replay missed, token chance).
void DrawFiles(Frame f, int room)
{
    foreach (var line in FilesLines().Take(Math.Max(0, room)))
    {
        f.Add(line);
    }
}

List<Seg[]> FilesLines()
{
    var view = Profiles.View(data, config);
    var impact = ImportsReport.Build(view, config);
    var lines = new List<Seg[]>();
    var labelWidth = Math.Max(new[] { L.T("Fichiers", "Files", "Fichiyat", "Archivos") }.Max(x => x.Length), Math.Min(18, impact.Files.Select(x => x.Source.Name.Length).DefaultIfEmpty(0).Max())) + 3;
    void Row(string label, params Seg[] value) => lines.Add(new[] { new Seg("  " + (label.Length > labelWidth - 2 ? label[..(labelWidth - 3)] + "…" : label).PadRight(labelWidth), label.Length > 0 && impact.Files.Any(x => x.Source.Name == label) ? Theme.Signature : ConsoleColor.DarkGray) }.Concat(value).ToArray());
    string Day(DateTime? utc) => utc is DateTime d ? d.ToLocalTime().ToString(L.T("d MMM", "d MMM", "d MMM", "d MMM"), L.Culture) : "?";

    if (impact.Files.Count == 0)
    {
        Row(L.T("Fichiers", "Files", "Fichiyat", "Archivos"), new Seg(L.T("aucun fichier importé pour l'instant", "no file imported yet", "mazal 7ta fichier importé", "ningún archivo importado todavía"), ConsoleColor.Gray));
        foreach (var l in ConsoleUi.Wrap(L.T("Demande à tes coéquipiers leur fichier .chesttoken (onglet Escouade → Exporter mon fichier), puis importe-le ici ou double-clique dessus. Chaque fichier apparaît ici avec ce qu'il change : les parties gardées, les coffres que ton replay ne voyait pas, et ta chance du jeton avec et sans lui.",
                     "Ask your teammates for their .chesttoken file (Squad tab → Export my file), then import it here or double-click it. Each file shows up here with what it changes: the matches kept, the chests your replay could not see, and your token chance with and without it.",
                     "Tleb mn s7abek l'fichier .chesttoken dyalhom (onglet Squad → Exporti l'fichier dyali), w importih hna wla dir 3lih double-clic. Kol fichier kayban hna m3a chno kaybeddel: les partiyat li tkhllaw, snadeq li ma kanch kaychoufhom le replay dyalek, w zher dyal l'jeton b l'fichier w bla bih.", "Pide a tus compañeros su archivo .chesttoken (pestaña Escuadrón → Exportar mi archivo), luego impórtalo aquí o haz doble clic en él. Cada archivo aparece aquí con lo que cambia: las partidas guardadas, los cofres que tu repetición no veía y tu probabilidad de la ficha con y sin él."), ConsoleUi.Width - labelWidth - 6))
        {
            Row("", new Seg(l, ConsoleColor.DarkGray));
        }
        lines.Add(Array.Empty<Seg>());
        return lines;
    }

    // All files together: the token chance with them and without any.
    Row(L.T("Fichiers", "Files", "Fichiyat", "Archivos"),
        new Seg($"{impact.Files.Count} · {L.T("chance du jeton", "token chance", "zher dyal l'jeton", "probabilidad de la ficha")} ", ConsoleColor.Gray),
        new Seg(L.Pct(impact.PAll), ConsoleColor.Yellow),
        new Seg(L.T($" avec eux, {L.Pct(impact.PNone)} sans ({ImportsReport.Points(impact.PAll - impact.PNone)})", $" with them, {L.Pct(impact.PNone)} without ({ImportsReport.Points(impact.PAll - impact.PNone)})", $" bihom, {L.Pct(impact.PNone)} blahom ({ImportsReport.Points(impact.PAll - impact.PNone)})", $" con ellos, {L.Pct(impact.PNone)} sin ellos ({ImportsReport.Points(impact.PAll - impact.PNone)})"), ConsoleColor.Gray));
    Row("", new Seg(impact.Scope == "mine"
        ? L.T("tes chances ne comptent que tes coffres : choisis d'autres coffres comptés (onglet Chances) pour que les fichiers comptent",
            "your odds only count your own chests: choose other chests counted (Odds tab) for the files to count",
            "zher dyalek kay7seb ghir snadeq dyalek: khtar snadeq m7soubin khrin (onglet Zher) bach les fichiers y7sbo", "tus probabilidades solo cuentan tus propios cofres: elige otros cofres contados (pestaña Probabilidades) para que los archivos cuenten")
        : $"{L.T("coffres comptés", "chests counted", "snadeq m7soubin", "cofres contados")}{L.T(" : ", ": ", ": ", ": ")}{OddsModel.ScopeName(impact.Scope)}", ConsoleColor.DarkGray));
    lines.Add(Array.Empty<Seg>());

    foreach (var file in impact.Files)
    {
        var s = file.Source;
        Row(s.Name, new Seg(s.File, ConsoleColor.White),
            new Seg(L.T($" · importé le {L.DateTimeShort(OddsModel.Utc(s.ImportedUtc).ToLocalTime())}", $" · imported {L.DateTimeShort(OddsModel.Utc(s.ImportedUtc).ToLocalTime())}", $" · importé {L.DateTimeShort(OddsModel.Utc(s.ImportedUtc).ToLocalTime())}", $" · importado el {L.DateTimeShort(OddsModel.Utc(s.ImportedUtc).ToLocalTime())}"), ConsoleColor.DarkGray));
        Row("", new Seg(L.T($"{L.Count(s.Matches.Count, "partie ensemble", "parties ensemble", "", "", "", "", "", "")} ({s.MatchesInFile} dans le fichier)",
                $"{L.Count(s.Matches.Count, "", "", "match together", "matches together", "", "", "partida juntos", "partidas juntos")} ({s.MatchesInFile} in the file)",
                $"{L.Count(s.Matches.Count, "", "", "", "", "partiya m3a ba3d", "partiyat m3a ba3d", "", "")} ({s.MatchesInFile} f l'fichier)", $"{L.Count(s.Matches.Count, "", "", "", "", "", "", "partida juntos", "partidas juntos")} ({s.MatchesInFile} en el archivo)")
            + (file.MatchesUsed > 0 ? $" · {Day(file.First)} → {Day(file.Last)}" : ""), ConsoleColor.Gray));
        var missed = file.TheirChests - file.SeenOfThem;
        Row("", new Seg(L.T($"leurs coffres {L.N(file.TheirChests)} · ton replay en voyait {L.N(file.SeenOfThem)}", $"their chests {L.N(file.TheirChests)} · your replay saw {L.N(file.SeenOfThem)}", $"snadeq dyalo {L.N(file.TheirChests)} · le replay dyalek chaf {L.N(file.SeenOfThem)}", $"sus cofres {L.N(file.TheirChests)} · tu repetición vio {L.N(file.SeenOfThem)}"), ConsoleColor.Gray),
            new Seg(missed > 0 ? L.T($" · +{L.N(missed)} qu'il ratait", $" · +{L.N(missed)} it missed", $" · +{L.N(missed)} ma chafhomch", $" · +{L.N(missed)} que no veía") : "", ConsoleColor.Green));
        Row("", new Seg(L.T("chance du jeton ", "token chance ", "zher dyal l'jeton ", "probabilidad de la ficha "), ConsoleColor.Gray),
            new Seg(ImportsReport.Points(file.PWith - file.PWithout), file.PWith > file.PWithout ? ConsoleColor.Green : ConsoleColor.DarkGray),
            new Seg(L.T($" avec ce fichier ({L.Pct(file.PWith)} au lieu de {L.Pct(file.PWithout)})", $" with this file ({L.Pct(file.PWith)} instead of {L.Pct(file.PWithout)})", $" b had l'fichier ({L.Pct(file.PWith)} blast {L.Pct(file.PWithout)})", $" con este archivo ({L.Pct(file.PWith)} en lugar de {L.Pct(file.PWithout)})"), ConsoleColor.Gray));
        if (file.Squads.Count > 0)
        {
            Row("", new Seg($"{L.T("escouades", "squads", "squads", "escuadrones")}{L.T(" : ", ": ", ": ", ": ")}{string.Join(" · ", file.Squads)}", ConsoleColor.DarkGray));
        }
        if (s.Matches.Count > 0 && file.MatchesUsed == 0)
        {
            Row("", new Seg(L.T("ces parties sont dans un autre de tes profils (« Changer de profil » dans l'accueil)", "these matches belong to another of your profiles (\"Switch profile\" on the home tab)", "had les partiyat f profil akhor dyalek («Beddel le profil» f Dar)", "estas partidas pertenecen a otro de tus perfiles (\"Cambiar de perfil\" en el inicio)"), ConsoleColor.DarkYellow));
        }
        lines.Add(Array.Empty<Seg>());
    }
    return lines;
}

void ExportMine()
{
    var view = Profiles.View(data, config);
    try
    {
        var (path, matches) = ShareFormat.Export(view, ShareFormat.ExportFolder(store));
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine(L.T($"Fichier exporté ({L.Count(matches, "partie en équipe", "parties en équipe", "", "", "", "", "", "")}) : {path}. Envoie-le à tes coéquipiers.",
            $"File exported ({L.Count(matches, "", "", "team match", "team matches", "", "", "partida en equipo", "partidas en equipo")}): {path}. Send it to your teammates.",
            $"Fichier exporté ({L.Count(matches, "", "", "", "", "partiya f l'équipe", "partiyat f l'équipe", "", "")}): {path}. Siftou l s7abek.", $"Archivo exportado ({L.Count(matches, "", "", "", "", "", "", "partida en equipo", "partidas en equipo")}): {path}. Envíalo a tus compañeros."));
        Console.ResetColor();
        if (OperatingSystem.IsWindows())
        {
            try { Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = false })?.Dispose(); } catch { }
        }
    }
    catch (Exception ex)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine(L.T($"Export impossible : {ex.Message}", $"Export failed: {ex.Message}", $"Export ma khdamch: {ex.Message}", $"Exportación fallida: {ex.Message}"));
        Console.ResetColor();
    }
}

// Files handed over by a double-click (--open): imported, then removed from the inbox. A file still being copied
// (written less than a second ago) waits for the next pass.
void ImportInbox()
{
    string[] files;
    try
    {
        files = Directory.Exists(store.InboxPath) ? Directory.GetFiles(store.InboxPath) : Array.Empty<string>();
    }
    catch
    {
        return;
    }
    var imported = false;
    foreach (var file in files.OrderBy(x => x, StringComparer.Ordinal))
    {
        if (DateTime.UtcNow - File.GetLastWriteTimeUtc(file) < TimeSpan.FromSeconds(1))
        {
            inboxFired = true;
            continue;
        }
        string message;
        var ok = false;
        try
        {
            var original = Path.GetFileName(file);
            original = original.Length > 16 && original[8] == '-' && original[15] == '-' ? original[16..] : original;
            var (source, text) = Team.Parse(original, File.ReadAllText(file), data);
            message = text;
            if (source != null)
            {
                Team.Imports = Team.With(Team.Imports, source);
                store.SaveImports(Team.Imports);
                ok = imported = true;
            }
        }
        catch (Exception ex)
        {
            message = L.T($"Impossible de lire ce fichier : {ex.Message}", $"Cannot read this file: {ex.Message}", $"Ma 9dertch n9ra had l'fichier: {ex.Message}", $"No se puede leer este archivo: {ex.Message}");
        }
        try { File.Delete(file); } catch { /* removed next time */ }
        Console.ForegroundColor = ok ? ConsoleColor.Green : ConsoleColor.DarkYellow;
        Console.WriteLine(message);
        Console.ResetColor();
        notice = new LiveNotice(++noticeId, message, ok, "files"); // the dashboard shows it, on the Files tab
    }
    if (imported)
    {
        tab = SquadTab;
        ReportWriter.WriteAll(store, config, data);
        UpdateOverlay(store, config, data, tracker.LiveMatch, tracker.IsLive);
    }
}

// Inbox of teammates' files: Windows tells us when one arrives (double-click while the assistant is open).
void WatchInbox()
{
    try
    {
        Directory.CreateDirectory(store.InboxPath);
        var w = new FileSystemWatcher(store.InboxPath) { NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite, EnableRaisingEvents = true };
        w.Created += (_, _) => { inboxFired = true; wake.Set(); };
        w.Changed += (_, _) => { inboxFired = true; wake.Set(); };
        inboxWatcher = w;
    }
    catch
    {
        inboxWatcher = null; // files still imported at the next start
    }
}

void ImportTeammate()
{
    ConsoleUi.Screen(appName, L.T("importer un coéquipier", "import a teammate", "importi sa7bek", "importar un compañero"));
    ConsoleUi.Text(L.T("Le fichier .chesttoken qu'un coéquipier a exporté (onglet Escouade → Exporter), ou son data.json.",
        "The .chesttoken file a teammate exported (Squad tab → Export), or their data.json.",
        "L'fichier .chesttoken li exporta sa7bek (onglet Squad → Exporti), wla data.json dyalo.", "El archivo .chesttoken que exportó un compañero (pestaña Escuadrón → Exportar), o su data.json."));
    ConsoleUi.Hint(L.T("Seules les parties jouées ensemble dans la même équipe sont gardées. Pour une partie enregistrée par vous deux, son propre compte remplace ce que ton replay a vu de lui : rien n'est compté deux fois. Un nouveau fichier de la même personne remplace l'ancien.",
        "Only the matches played together on the same team are kept. For a match you both recorded, their own count replaces what your replay saw of them: nothing is counted twice. A newer file from the same person replaces the older one.",
        "Kaytkhllaw ghir les partiyat li l3btou m3a ba3d f nefs l'équipe. F partiya li sejjeltouha b jouj, l7sab dyalo kay3wwed dakchi li chafo le replay dyalek: walou ma kayt7seb jouj mrat. Fichier jdid mn nefs l'wa7ed kay3wwed l9dim.", "Solo se guardan las partidas jugadas juntos en el mismo equipo. En una partida que grabasteis los dos, su propio recuento sustituye lo que tu repetición vio de él: nada se cuenta dos veces. Un archivo más reciente de la misma persona sustituye al anterior."));
    ConsoleUi.Blank();
    ConsoleUi.Hint(L.T("Glisse le fichier sur cette fenêtre (ou tape son chemin) puis Entrée. Échap pour annuler.",
        "Drag the file onto this window (or type its path), then Enter. Esc to cancel.",
        "Jerr l'fichier l had la fenêtre (wla kteb chemin dyalo) w wrek 3la Entrée. Échap bach tlghi.", "Arrastra el archivo a esta ventana (o escribe su ruta) y pulsa Intro. Esc para cancelar."));
    ConsoleUi.Blank();
    var path = Environment.ExpandEnvironmentVariables(ConsoleUi.Input(L.T("Fichier : ", "File: ", "Fichier: ", "Archivo: ")));
    if (path.Length == 0)
    {
        return;
    }
    string message;
    var ok = false;
    try
    {
        var (source, text) = Team.Parse(path, File.ReadAllText(path), data);
        message = text;
        if (source != null)
        {
            Team.Imports = Team.With(Team.Imports, source);
            store.SaveImports(Team.Imports);
            ok = true;
        }
    }
    catch (Exception ex)
    {
        message = L.T($"Impossible de lire ce fichier : {ex.Message}", $"Cannot read this file: {ex.Message}", $"Ma 9dertch n9ra had l'fichier: {ex.Message}", $"No se puede leer este archivo: {ex.Message}");
    }
    ConsoleUi.Blank();
    ConsoleUi.Text(message, ok ? ConsoleColor.Green : ConsoleColor.DarkYellow);
    Console.ForegroundColor = ok ? ConsoleColor.Green : ConsoleColor.DarkYellow;
    Console.WriteLine(message);
    Console.ResetColor();
    ConsoleUi.WaitEnter(L.T("Entrée pour continuer", "Enter to continue", "Entrée bach tkemmel", "Intro para continuar"));
}

void ManageImports()
{
    var selected = 0;
    while (Team.Imports.Sources.Count > 0)
    {
        ConsoleUi.Screen(appName, L.T("fichiers importés", "imported files", "les fichiers importés", "archivos importados"));
        ConsoleUi.Text(L.T("Entrée sur un fichier pour le retirer (il ne compte plus). Tu pourras l'importer à nouveau.",
            "Enter on a file to remove it (it no longer counts). You can import it again later.",
            "Entrée 3la fichier bach t7iydo (ma yb9ach y7seb). T9der t3awed timportih.", "Intro sobre un archivo para quitarlo (deja de contar). Podrás importarlo de nuevo."));
        var sources = Team.Imports.Sources.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToList();
        var options = sources.Select(x => $"{x.Name}  ·  {L.Count(x.Matches.Count, "partie avec toi", "parties avec toi", "match with you", "matches with you", "partiya m3ak", "partiyat m3ak", "partida contigo", "partidas contigo")}  ·  {L.Count(x.Matches.Sum(m => m.ChestsOpened), "coffre", "chest", "sndouq", "cofre")}  ·  {L.DateTimeShort(OddsModel.Utc(x.ImportedUtc).ToLocalTime())}")
            .Append(L.T("Retour", "Back", "Rje3", "Volver"))
            .ToList();
        selected = ConsoleUi.Menu(options, Math.Min(selected, options.Count - 1), escapeIndex: options.Count - 1);
        if (selected >= sources.Count)
        {
            return;
        }
        var source = sources[selected];
        if (ConsoleUi.Confirm(L.T($"Oui, retirer {source.Name}", $"Yes, remove {source.Name}", $"Ah, 7iyed {source.Name}", $"Sí, quitar a {source.Name}"), L.T("Annuler", "Cancel", "Lghi", "Cancelar")))
        {
            Team.Imports = new TeamImports { Sources = Team.Imports.Sources.Where(x => !ReferenceEquals(x, source)).ToList() };
            try { store.SaveImports(Team.Imports); } catch (Exception ex) { Console.WriteLine(ex.Message); }
        }
    }
}

void OddsSettingsScreen()
{
    var selected = 0;
    while (true)
    {
        var s = config.Odds;
        var key = Profiles.Current(data, config)?.Key ?? config.Profile ?? "";
        var baseline = s.Baselines.GetValueOrDefault(key) ?? new OddsBaseline();
        string YesNo(bool b) => b ? L.T("oui", "yes", "ah", "sí") : L.T("non", "no", "la", "no");
        string Date(DateTime utc) => OddsModel.Utc(utc).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        int AskNumber(string prompt, int current, int min)
        {
            var text = ConsoleUi.Input(prompt, current.ToString(CultureInfo.InvariantCulture));
            var digits = new string(text.Where(char.IsAsciiDigit).ToArray());
            return int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var v) && v >= min ? v : current;
        }
        DateTime AskDate(string prompt, DateTime current)
        {
            var text = ConsoleUi.Input(prompt, Date(current));
            return DateTime.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
                ? DateTime.SpecifyKind(d.AddHours(23).AddMinutes(59), DateTimeKind.Utc)
                : current;
        }
        void Ask(string title, Action run)
        {
            ConsoleUi.Blank();
            ConsoleUi.Hint(title);
            run();
        }

        var items = new List<(string Label, string About, Action? Run)>
        {
            ($"{L.T("Coffres comptés", "Chests counted", "Snadeq m7soubin", "Cofres contados")} : {OddsModel.ScopeName(s.ChestScope)}", ScopeAbout(s.ChestScope),
                () => s.ChestScope = OddsModel.NextScope(s.ChestScope)),
            ($"{L.T("Coéquipiers aléatoires (hors de ton groupe)", "Random teammates (not in your party)", "S7ab l'hasard (machi f groupe dyalek)", "Compañeros aleatorios (fuera de tu grupo)")} : {(s.IncludeFill ? L.T("comptés", "counted", "m7soubin", "contados") : L.T("exclus", "left out", "mkhrojin", "excluidos"))}",
                L.T("Le replay dit qui est dans ton groupe. Le jeton est partagé avec le groupe ; pour un coéquipier aléatoire ce n'est pas confirmé. Entrée pour changer.",
                    "The replay says who is in your party. The token is shared with the party; for a random teammate it is not confirmed. Enter to change.",
                    "Le replay kay9ol chkoun f groupe dyalek. L'jeton kaytqsem m3a l'groupe; m3a s7ab l'hasard mazal ma mconfirmich. Entrée bach tbeddel.", "La repetición dice quién está en tu grupo. La ficha se comparte con el grupo; con un compañero aleatorio no está confirmado. Intro para cambiar."),
                () => s.IncludeFill = !s.IncludeFill),
            ($"{L.T("Parties classées", "Ranked matches", "Partiyat classées", "Partidas clasificatorias")} : {(s.IncludeRanked ? L.T("comptées", "counted", "m7soubin", "contados") : L.T("exclues", "left out", "mkhrojin", "excluidos"))}",
                L.T("Le jeton ne tomberait pas en partie classée (non confirmé) : exclues par défaut.", "The token reportedly does not drop in ranked (unconfirmed): left out by default.", "L'jeton ma kaytla3ch f classée (ma mconfirmich): mkhrojin par défaut.", "Al parecer la ficha no sale en clasificatorias (sin confirmar): excluidas por defecto."),
                () => s.IncludeRanked = !s.IncludeRanked),
            ($"{L.T("Mes coffres avant le suivi", "My chests before tracking", "Snadeq dyali 9bel l'suivi", "Mis cofres antes del seguimiento")} : {L.N(baseline.Mine)}",
                L.T("Coffres ouverts avant que l'assistant les suive (replays supprimés…). Ajoutés aux chances à ×1. Entrée pour saisir le nombre.",
                    "Chests opened before the assistant tracked them (deleted replays…). Added to the odds at ×1. Enter to type the number.",
                    "Snadeq li 7elliti 9bel ma ybda l'assistant y7sebhom (replays tmse7ou…). Kayzado l zher b ×1. Entrée bach tkteb l'3adad.", "Cofres abiertos antes de que el asistente los siguiera (repeticiones borradas…). Se añaden a las probabilidades a ×1. Intro para escribir el número."),
                () => Ask(L.T("Nombre de coffres (0 pour aucun)", "Number of chests (0 for none)", "3adad dyal snadeq (0 ila walou)", "Número de cofres (0 si ninguno)"),
                    () => baseline.Mine = AskNumber(L.T("Mes coffres : ", "My chests: ", "Snadeq dyali: ", "Mis cofres: "), baseline.Mine, 0))),
            ($"{L.T("Coffres de l'équipe avant le suivi", "Team chests before tracking", "Snadeq dyal l'équipe 9bel l'suivi", "Cofres del equipo antes del seguimiento")} : {L.N(baseline.Team)}",
                L.T("Coffres de tes coéquipiers dans ces mêmes parties (s'ils les connaissent). Comptés seulement quand l'équipe compte.",
                    "Your teammates' chests in those same matches (if they know them). Only counted when the team counts.",
                    "Snadeq dyal s7abek f nefs les partiyat (ila 3arfinhom). Kayt7sbo ghir mlli l'équipe kat7seb.", "Los cofres de tus compañeros en esas mismas partidas (si los conocen). Solo cuentan cuando cuenta el equipo."),
                () => Ask(L.T("Nombre de coffres (0 pour aucun)", "Number of chests (0 for none)", "3adad dyal snadeq (0 ila walou)", "Número de cofres (0 si ninguno)"),
                    () => baseline.Team = AskNumber(L.T("Coffres de l'équipe : ", "Team chests: ", "Snadeq dyal l'équipe: ", "Cofres del equipo: "), baseline.Team, 0))),
        };
        foreach (var t in TreasureDef.All)
        {
            var def = t;
            items.Add(($"{def.Name} : {L.OneIn(1.0 / Math.Max(1, def.Odds(s)))} {L.T("par coffre", "per chest", "f kol sndouq", "por cofre")}",
                L.T("Chances de base publiées par Epic. À changer seulement si elles changent.", "Base odds published by Epic. Only change them if they change.", "Zher l'asli li nachrat Epic. Beddlo ghir ila tbeddel.", "Probabilidades base publicadas por Epic. Cámbialas solo si cambian."),
                () => Ask(L.T("1 coffre sur combien ?", "1 chest in how many?", "1 sndouq f kol 9addach?", "¿1 cofre de cuántos?"), () =>
                {
                    var v = AskNumber("1 / ", def.Odds(s), 1);
                    switch (def.Id)
                    {
                        case "vehicle": s.VehicleDecalOdds = v; break;
                        case "gold": s.GoldOutfitOdds = v; break;
                        default: s.PirateShopOdds = v; break;
                    }
                })));
        }
        items.Add(($"{L.T("Fin de la chasse", "End of the hunt", "Lekher dyal la chasse", "Fin de la búsqueda")} : {Date(s.HuntEndUtc)}",
            L.T("Pour le compte à rebours et la projection. Entrée pour la changer (AAAA-MM-JJ).", "For the countdown and the projection. Enter to change it (YYYY-MM-DD).", "L l'compte à rebours w la projection. Entrée bach tbeddlo (AAAA-MM-JJ).", "Para la cuenta atrás y la proyección. Intro para cambiarla (AAAA-MM-DD)."),
            () => Ask("", () => s.HuntEndUtc = AskDate(L.T("Date de fin : ", "End date: ", "Date dyal lekher: ", "Fecha de fin: "), s.HuntEndUtc))));
        items.Add(($"{L.T("Activer le jeton avant le", "Activate the token by", "Activi l'jeton 9bel", "Activar la ficha antes del")} : {Date(s.ActivateByUtc)}",
            L.T("Date limite d'activation, rappelée dans l'onglet Chances.", "Activation deadline, shown on the Odds tab.", "Date limite dyal l'activation, mktouba f l'onglet Zher.", "Fecha límite de activación, indicada en la pestaña Probabilidades."),
            () => Ask("", () => s.ActivateByUtc = AskDate(L.T("Date : ", "Date: ", "Date: ", "Fecha: "), s.ActivateByUtc))));
        items.Add(($"{L.T("Chance du jeton sur l'overlay", "Token chance on the overlay", "Zher dyal l'jeton f l'overlay", "Probabilidad de la ficha en el overlay")} : {YesNo(s.OverlayTokenLine)}",
            L.T("Ajoute « Jeton : 9,02 % » à l'overlay et à overlay.html.", "Adds \"Token: 9.02%\" to the overlay and to overlay.html.", "Kayzid «Jeton: 9,02 %» l l'overlay w l overlay.html.", "Añade \"Ficha: 9,02 %\" al overlay y a overlay.html."),
            () => s.OverlayTokenLine = !s.OverlayTokenLine));
        items.Add(($"{L.T("Relire les Heures du trésor dans config.json", "Reload Treasure Hours from config.json", "3awed 9ra Sa3at l'kenz mn config.json", "Recargar las Horas del tesoro desde config.json")} ({s.TreasureHours.Count})",
            L.T($"Ajoute-les dans {store.ConfigPath} (Odds → TreasureHours, heures UTC), puis Entrée ici. L'assistant réécrit ce fichier quand un réglage change : sans cette relecture, tes ajouts seraient perdus.",
                $"Add them in {store.ConfigPath} (Odds → TreasureHours, UTC times), then Enter here. The assistant rewrites this file when a setting changes: without this reload, your edits would be lost.",
                $"Zidhom f {store.ConfigPath} (Odds → TreasureHours, sa3a UTC) w wrek 3la Entrée hna. L'assistant kay3awed ykteb had l'fichier mlli kaytbeddel chi réglage: bla had l9raya, ghadi ytlfo les changements dyalek.", $"Añádelas en {store.ConfigPath} (Odds → TreasureHours, horas UTC) y pulsa Intro aquí. El asistente reescribe este archivo cuando cambia un ajuste: sin esta recarga, tus cambios se perderían."),
            ReloadOddsFromFile));
        items.Add((L.T("Retour", "Back", "Rje3", "Volver"), "", null));

        ConsoleUi.Screen(appName, L.T("réglages des chances", "odds settings", "réglages dyal zher", "ajustes de probabilidades"));
        selected = ConsoleUi.Menu(items.Select(i => i.Label).ToList(), Math.Min(selected, items.Count - 1), descriptions: items.Select(i => (string?)i.About).ToList(), escapeIndex: items.Count - 1);
        if (items[selected].Run is not { } run)
        {
            return;
        }
        run();
        if (baseline.Mine > 0 || baseline.Team > 0 || s.Baselines.ContainsKey(key))
        {
            s.Baselines[key] = baseline;
        }
        try { store.SaveConfig(config); } catch { /* saved again with the next change */ }
    }
}

// Treasure Hours (and the other odds fields only edited by hand) from config.json, kept in the running settings.
void ReloadOddsFromFile()
{
    var fromFile = store.LoadConfig()?.Odds;
    if (fromFile == null)
    {
        return;
    }
    config.Odds.TreasureHours = fromFile.TreasureHours ?? new();
    config.Odds.HuntStartUtc = fromFile.HuntStartUtc;
    config.Odds.WeekResetDay = fromFile.WeekResetDay;
    config.Odds.WeekResetHourUtc = fromFile.WeekResetHourUtc;
    Console.WriteLine(L.T($"Heures du trésor relues : {config.Odds.TreasureHours.Count}.", $"Treasure Hours reloaded: {config.Odds.TreasureHours.Count}.", $"Sa3at l'kenz t3awdo t9raw: {config.Odds.TreasureHours.Count}.", $"Horas del tesoro recargadas: {config.Odds.TreasureHours.Count}."));
}

void DrawHelp(Frame frame, int room)
{
    // Drawn into a scratch frame first: a small window keeps the menu on screen and cuts the end of the help.
    var f = new Frame();
    void Para(string text, ConsoleColor color = ConsoleColor.Gray)
    {
        foreach (var l in ConsoleUi.Wrap(text, ConsoleUi.Width - 6))
        {
            f.Add("  " + l, color);
        }
    }
    Para(L.T("L'assistant lit les replays que Fortnite enregistre et compte tes coffres quelques secondes après la fin de chaque partie. Laisse cette fenêtre ouverte pendant que tu joues (tu peux la réduire).",
        "The assistant reads the replays Fortnite saves and counts your chests a few seconds after each match ends. Keep this window open while you play (you can minimize it).",
        "L'assistant kay9ra les replays li kaysejjel Fortnite w kay7seb snadeq dyalek chi chwiya d les secondes men b3d kol partiya. Khelli had la fenêtre m7loula w nta kat'joui (t9der tminimizeha).", "El asistente lee las repeticiones que guarda Fortnite y cuenta tus cofres unos segundos después de cada partida. Deja esta ventana abierta mientras juegas (puedes minimizarla)."));
    f.Blank();
    Para(L.T("Rien n'est compté ? Dans Fortnite, active  Paramètres → Jeu → Rediffusions → Enregistrer les rediffusions.",
        "Nothing is counted? In Fortnite, turn on  Settings → Game → Replays → Record Replays.",
        "Walou ma kayt7seb? F Fortnite, 7ell  Paramètres → Jeu → Rediffusions → Enregistrer les rediffusions.", "¿No se cuenta nada? En Fortnite, activa  Configuración → Juego → Repeticiones → Grabar repeticiones."), ConsoleColor.Yellow);
    if (config.ShowOverlay && OperatingSystem.IsWindows())
    {
        f.Blank();
        f.Add("  " + L.T("Overlay", "Overlay", "Overlay", "Overlay"), ConsoleColor.White);
        Para(L.T("Ctrl+Maj+O : afficher / masquer.  Ctrl+Maj+L : verrouiller / déverrouiller. Ces raccourcis marchent partout, même en jeu.",
            "Ctrl+Shift+O: show / hide.  Ctrl+Shift+L: lock / unlock. These shortcuts work anywhere, even in game.",
            "Ctrl+Shift+O: werri / khebbi.  Ctrl+Shift+L: bloqui / débloqui. Had les raccourcis khdamin f ay blassa, 7ta f le jeu.", "Ctrl+Shift+O: mostrar / ocultar.  Ctrl+Shift+L: bloquear / desbloquear. Estos atajos funcionan en cualquier lugar, incluso en el juego."));
        Para(L.T("Verrouillé, la souris passe au travers. Déverrouillé, clic gauche maintenu pour le déplacer, clic droit pour le masquer.",
            "Locked, the mouse goes through it. Unlocked, hold the left button to move it, right-click to hide it.",
            "Msdoud: la souris katdouz mn foqo. M7loul: jerro b click lisser, click limen bach tkhbbih.", "Bloqueado, el ratón lo atraviesa. Desbloqueado, mantén pulsado el botón izquierdo para moverlo, clic derecho para ocultarlo."));
        Para(L.T("Pour le voir en jeu, mets Fortnite en « Plein écran fenêtré » (Paramètres → Vidéo → Mode de fenêtre).",
            "To see it in game, set Fortnite to \"Windowed Fullscreen\" (Settings → Video → Window Mode).",
            "Bach tchoufo f le jeu: Paramètres → Vidéo → Mode de fenêtre → «Plein écran fenêtré».", "Para verlo en el juego, pon Fortnite en \"Pantalla completa en ventana\" (Configuración → Vídeo → Modo de ventana)."), ConsoleColor.DarkGray);
    }
    f.Blank();
    f.Add("  " + L.T("Chances du jeton et coffres de l'équipe", "Token odds and team chests", "Zher dyal l'jeton w snadeq dyal l'équipe", "Probabilidades de la ficha y cofres del equipo"), ConsoleColor.White);
    Para(L.T("Chance = 1 − Π(1 − m × p) sur chaque coffre compté : p = chances publiées (1 sur 137 500 pour la boutique pirate), m = multiplicateur de l'Heure du trésor de ce coffre (1 sinon). Le jeton est partagé avec l'équipe : les coffres des coéquipiers comptent aussi.",
        "Chance = 1 − Π(1 − m × p) over every chest counted: p = published odds (1 in 137,500 for Pirate Shop Access), m = that chest's Treasure Hour multiplier (1 otherwise). The token is shared with the team: teammates' chests count too.",
        "Zher = 1 − Π(1 − m × p) 3la kol sndouq m7soub: p = zher li nachrat Epic (1 f 137 500 l l7anout dyal l9ersan), m = multiplicateur dyal Sa3at l'kenz dyal dak sndouq (1 ila la). L'jeton kaytqsem m3a l'équipe: snadeq dyal s7abek 7ta homa kayt7sbo.", "Probabilidad = 1 − Π(1 − m × p) sobre cada cofre contado: p = probabilidad publicada (1 entre 137 500 para el acceso a la tienda pirata), m = multiplicador de la Hora del tesoro de ese cofre (1 si no). La ficha se comparte con el equipo: los cofres de los compañeros también cuentan."));
    Para(L.T("Vus : coffres des coéquipiers lus dans TON replay, qui ne contient que ce qui était près de toi : un minimum. Importés : le data.json d'un coéquipier donne son compte exact et remplace ce que ton replay a vu de lui. Estimés (au choix) : trésors reçus de coffres hors de ta vue ÷ ton taux de trésors par coffre ; un total avec une fourchette, jamais par partie (calibré sur les parties importées dès qu'il y en a 5).",
        "Seen: teammates' chests read from YOUR replay, which only holds what was near you: a minimum. Imported: a teammate's data.json gives their exact count and replaces what your replay saw of them. Estimated (optional): treasures received from chests out of your sight ÷ your treasure rate per chest; a total with a range, never per match (calibrated on imported matches once there are 5).",
        "Li chefti: snadeq dyal s7abek m9rin mn le replay DYALEK, li fih ghir dakchi li kan 9rib lik: minimum. Importés: l'fichier dyal sa7bek kay3ti l7sab exact dyalo w kay3wwed dakchi li chafo le replay dyalek. Ta9dir (ila bghiti): knouz li jawk mn snadeq b3ad 3la 3inik ÷ taux dyal knouz dyalek f kol sndouq; total b marge, 3emmro f partiya wa7da (m3ayer 3la les partiyat importées mlli ykounou 5).", "Vistos: cofres de compañeros leídos en TU repetición, que solo contiene lo que estaba cerca de ti: un mínimo. Importados: el data.json de un compañero da su recuento exacto y sustituye lo que tu repetición vio de él. Estimados (opcional): tesoros recibidos de cofres fuera de tu vista ÷ tu tasa de tesoros por cofre; un total con un margen, nunca por partida (calibrado con las partidas importadas cuando hay 5)."));
    Para(L.T("Heures du trésor : config.json → Odds → TreasureHours (heures UTC), par ex. {\"StartUtc\":\"2026-11-20T18:00:00Z\",\"EndUtc\":\"2026-11-20T20:00:00Z\",\"PirateShop\":1.5}, puis « Relire les Heures du trésor » dans les réglages des chances.",
        "Treasure Hours: config.json → Odds → TreasureHours (UTC times), e.g. {\"StartUtc\":\"2026-11-20T18:00:00Z\",\"EndUtc\":\"2026-11-20T20:00:00Z\",\"PirateShop\":1.5}, then \"Reload Treasure Hours\" in the odds settings.",
        "Sa3at l'kenz: config.json → Odds → TreasureHours (sa3a UTC), matalan {\"StartUtc\":\"2026-11-20T18:00:00Z\",\"EndUtc\":\"2026-11-20T20:00:00Z\",\"PirateShop\":1.5}, w men b3d «3awed 9ra Sa3at l'kenz» f les réglages dyal zher.", "Horas del tesoro: config.json → Odds → TreasureHours (horas UTC), p. ej. {\"StartUtc\":\"2026-11-20T18:00:00Z\",\"EndUtc\":\"2026-11-20T20:00:00Z\",\"PirateShop\":1.5}, y luego \"Recargar las Horas del tesoro\" en los ajustes de probabilidades."), ConsoleColor.DarkGray);
    f.Blank();
    foreach (var line in f.Lines.Take(Math.Max(0, room)))
    {
        frame.Add(line);
    }
}

// ---- Main screen: keys and actions ----------------------------------------------------------

void HandleKey(ConsoleKeyInfo key)
{
    var items = Items();
    switch (key.Key)
    {
        case ConsoleKey.LeftArrow:
            tab = (tab + TabCount - 1) % TabCount;
            break;
        case ConsoleKey.RightArrow:
        case ConsoleKey.Tab:
            tab = (tab + 1) % TabCount;
            break;
        case ConsoleKey.Escape:
            tab = HomeTab;
            break;
        case ConsoleKey.UpArrow:
            selection[tab] = (selection[tab] + items.Count - 1) % items.Count;
            break;
        case ConsoleKey.DownArrow:
            selection[tab] = (selection[tab] + 1) % items.Count;
            break;
        case ConsoleKey.Home:
            selection[tab] = 0;
            break;
        case ConsoleKey.End:
            selection[tab] = items.Count - 1;
            break;
        case ConsoleKey.Enter:
            var index = Math.Clamp(selection[tab], 0, items.Count - 1);
            items[index].Run();
            if (tab is SettingsTab or OddsTab or SquadTab or FilesTab)
            {
                // A setting changed: save it and show it everywhere at once.
                store.SaveConfig(config);
                ReportWriter.WriteAll(store, config, data);
                UpdateOverlay(store, config, data, tracker.LiveMatch, tracker.IsLive);
            }
            break;
    }
}

void StartOverlay()
{
    if (!OperatingSystem.IsWindows() || NativeOverlay.IsRunning)
    {
        return;
    }
    NativeOverlay.SetAppearance(config.OverlaySize, config.OverlayOpacity);
    if (Array.IndexOf(NativeOverlay.Styles, config.OverlayStyle) < 0)
    {
        config.OverlayStyle = "hud"; // a style tried in a test version and removed since
    }
    NativeOverlay.SetStyle(config.OverlayStyle);
    NativeOverlay.Start(config.OverlayX, config.OverlayY, config.OverlayLocked,
        (x, y) =>
        {
            config.OverlayX = x;
            config.OverlayY = y;
            try { store.SaveConfig(config); } catch { /* ignore */ }
        },
        locked =>
        {
            config.OverlayLocked = locked;
            try { store.SaveConfig(config); } catch { /* ignore */ }
        });
    UpdateOverlay(store, config, data, tracker.LiveMatch, tracker.IsLive);
}

void ToggleOverlay()
{
    config.ShowOverlay = !config.ShowOverlay;
    if (config.ShowOverlay)
    {
        StartOverlay();
    }
    else if (OperatingSystem.IsWindows())
    {
        NativeOverlay.Stop();
    }
}

void CycleLanguage()
{
    config.Language = L.Codes[(Array.IndexOf(L.Codes, L.Code) + 1) % L.Codes.Length]; // fr → en → ary → es → fr
    L.Set(config.Language);
    appName = AppInfo.Name;
    ConsoleUi.SetTitle($"{appName} {AppInfo.VersionText} · by {AppInfo.Author}");
}

void ToggleStartWithFortnite()
{
    var on = !config.StartWithFortnite;
    if (OperatingSystem.IsWindows() && FortniteWatch.Apply(on, dataDir))
    {
        config.StartWithFortnite = on;
        return;
    }
    Console.ForegroundColor = ConsoleColor.Red;
    Console.WriteLine(L.T("Ouvrir avec Fortnite : impossible ici, lance l'assistant depuis ChestTokenAssistant.exe.",
        "Open with Fortnite: not possible here, run the assistant from ChestTokenAssistant.exe.",
        "7ell m3a Fortnite: ma ymkench hna, 7ell l'assistant mn ChestTokenAssistant.exe.", "Abrir con Fortnite: imposible aquí, ejecuta el asistente desde ChestTokenAssistant.exe."));
    Console.ResetColor();
}

void ChangeDemosFolder()
{
    ConsoleUi.Screen(appName, L.T("dossier des replays", "replays folder", "dossier dyal les replays", "carpeta de repeticiones"));
    ConsoleUi.Text(L.T("Dossier actuel :", "Current folder:", "Dossier daba:", "Carpeta actual:"));
    ConsoleUi.Hint("  " + config.DemosFolder);
    ConsoleUi.Blank();
    ConsoleUi.Hint(L.T("Tape le nouveau chemin puis Entrée. Échap pour annuler.", "Type the new path, then Enter. Esc to cancel.", "Kteb chemin jdid w wrek 3la Entrée. Échap bach tlghi.", "Escribe la nueva ruta y pulsa Intro. Esc para cancelar."));
    ConsoleUi.Blank();
    var folder = ConsoleUi.Input(L.T("Nouveau dossier : ", "New folder: ", "Dossier jdid: ", "Nueva carpeta: "), config.DemosFolder);
    if (folder != config.DemosFolder)
    {
        config.DemosFolder = folder;
        WatchReplays();
        Console.WriteLine(L.T($"Dossier des replays : {folder}", $"Replays folder: {folder}", $"Dossier dyal les replays: {folder}", $"Carpeta de repeticiones: {folder}"));
        nextScan = DateTime.UtcNow;
    }
}

void ResetConfiguration()
{
    ConsoleUi.Screen(appName, L.T("recommencer la configuration", "start the setup again", "3awed la configuration", "volver a empezar la configuración"));
    ConsoleUi.Text(L.T("Tes réglages vont être effacés et l'assistant va redémarrer sur les questions du premier lancement.",
        "Your settings will be cleared and the assistant will restart with the first-launch questions.",
        "Les réglages dyalek ghadi ytmse7o w l'assistant ghadi y3awed ybda b les questions dyal awel mra.", "Tus ajustes se borrarán y el asistente se reiniciará con las preguntas del primer inicio."));
    ConsoleUi.Hint(L.T("Ton historique de parties et tes statistiques sont conservés.", "Your match history and statistics are kept.", "L'historique dyal les partiyat w les stats dyalek kayb9aw.", "Tu historial de partidas y tus estadísticas se conservan."));
    if (!ConsoleUi.Confirm(L.T("Oui, recommencer la configuration", "Yes, start the setup again", "Ah, 3awed la configuration", "Sí, volver a empezar la configuración"), L.T("Annuler", "Cancel", "Lghi", "Cancelar")))
    {
        return;
    }
    if (OperatingSystem.IsWindows())
    {
        if (config.StartWithFortnite)
        {
            FortniteWatch.Apply(false, dataDir);
        }
        NativeOverlay.Stop();
    }
    try { File.Delete(store.ConfigPath); } catch { /* the setup will overwrite it */ }
    restart = true;
    quit = true;
}

void ToggleShareFiles()
{
    if (!OperatingSystem.IsWindows())
    {
        return;
    }
    config.OpenShareFiles = !config.OpenShareFiles;
    var ok = config.OpenShareFiles ? ShareFiles.Register() : ShareFiles.Unregister();
    if (!ok)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine(L.T($"Fichiers .chesttoken : impossible ici, lance l'assistant depuis {AppInfo.ExeName}.",
            $".chesttoken files: not possible here, run the assistant from {AppInfo.ExeName}.",
            $"Fichiers .chesttoken: ma ymkench hna, 7ell l'assistant mn {AppInfo.ExeName}.", $"Archivos .chesttoken: imposible aquí, ejecuta el asistente desde {AppInfo.ExeName}."));
        Console.ResetColor();
    }
}

void AnnounceNewProfiles()
{
    if (!tracker.NewProfilesFound)
    {
        return;
    }
    tracker.NewProfilesFound = false;
    askProfile = true;
    Console.ForegroundColor = ConsoleColor.DarkYellow;
    Console.WriteLine(L.T("Plusieurs comptes Fortnite ont enregistré des replays sur ce PC : chacun a son profil. « Changer de profil » dans l'accueil pour choisir le tien.",
        "Several Fortnite accounts recorded replays on this PC: each one has its own profile. \"Switch profile\" on the home tab to choose yours.",
        "Bzaf d les comptes Fortnite sejjlou replays f had PC: kol wa7ed 3ando profil dyalo. «Beddel le profil» f Dar bach tkhtar dyalek.", "Varias cuentas de Fortnite grabaron repeticiones en este PC: cada una tiene su perfil. \"Cambiar de perfil\" en el inicio para elegir el tuyo."));
    Console.ResetColor();
}

// Replays folder: Windows tells us when a file changes, instead of us re-reading the folder all the time.
void WatchReplays()
{
    watcher?.Dispose();
    watcher = null;
    try
    {
        if (Directory.Exists(config.DemosFolder))
        {
            var w = new FileSystemWatcher(config.DemosFolder, "*.replay")
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
                EnableRaisingEvents = true,
            };
            w.Created += (_, _) => { watcherFired = true; wake.Set(); };
            w.Changed += (_, _) => { watcherFired = true; wake.Set(); };
            w.Renamed += (_, _) => { watcherFired = true; wake.Set(); };
            watcher = w;
        }
    }
    catch
    {
        watcher = null; // fall back to the periodic scan
    }
}

void CheckAutoClose()
{
    var now = DateTime.UtcNow;
    if (FortniteWatch.IsFortniteRunning())
    {
        fortniteClosedSince = null;
        autoCloseAt = null; // Fortnite started again
        return;
    }
    fortniteClosedSince ??= now;
    // Wait for the last match to be counted (at most 5 minutes, in case a replay was left unfinished by a crash).
    var lastMatchDone = !tracker.IsLive && !tracker.HasPending && !tracker.IsFinishing;
    if (!lastMatchDone && now - fortniteClosedSince < TimeSpan.FromMinutes(5))
    {
        return;
    }
    if (autoCloseAt == null)
    {
        autoCloseAt = now.AddSeconds(30);
    }
    else if (now >= autoCloseAt)
    {
        quit = true;
    }
}

// Shows the new size / opacity on the overlay right away (it stays visible above the console).
void ApplyOverlayAppearance()
{
    if (OperatingSystem.IsWindows() && NativeOverlay.IsRunning)
    {
        NativeOverlay.SetAppearance(config.OverlaySize, config.OverlayOpacity);
    }
}

static bool AcquireInstance(Mutex m, TimeSpan wait)
{
    try
    {
        return m.WaitOne(wait);
    }
    catch (AbandonedMutexException)
    {
        return true; // the previous instance was killed: it is ours now
    }
}

// Several accounts recorded replays on this PC: which one is played now (the overlay and the report show it).
static void ChooseProfile(TrackerData d, Config c, Store s)
{
    var list = Profiles.List(d);
    var current = Math.Max(0, list.FindIndex(p => p.Key == Profiles.Current(d, c)?.Key));
    ConsoleUi.Text(L.T("Plusieurs comptes Fortnite ont enregistré des replays sur ce PC.", "Several Fortnite accounts recorded replays on this PC.", "Bzaf d les comptes Fortnite sejjlou replays f had PC.", "Varias cuentas de Fortnite grabaron repeticiones en este PC."));
    ConsoleUi.Text(L.T("Sur quel compte joues-tu ?", "Which account are you playing on?", "B ana compte kat'joui?", "¿Con qué cuenta juegas?"), ConsoleColor.White);
    ConsoleUi.Hint(L.T("Chaque compte garde ses propres statistiques. Tu pourras changer à tout moment depuis l'accueil (« Changer de profil »).",
        "Each account keeps its own statistics. You can switch at any time from the home tab (\"Switch profile\").",
        "Kol compte kay7tafed b les stats dyalo. T9der tbeddel f ay wa9t mn Dar («Beddel le profil»).", "Cada cuenta conserva sus propias estadísticas. Puedes cambiar en cualquier momento desde el inicio (\"Cambiar de perfil\")."));
    var choice = ConsoleUi.Menu(list.Select(Profiles.Describe).ToList(), current, escapeIndex: current);
    c.Profile = list[choice].Key;
    try { s.SaveConfig(c); } catch { /* ignore */ }
}

static void UpdateOverlay(Store store, Config config, TrackerData d, MatchRecord? live, bool inProgress)
{
    var profile = Profiles.View(d, config);
    var view = OverlayModel.For(profile, config, live, inProgress);
    ReportWriter.WriteOverlay(store, view);
    if (OperatingSystem.IsWindows() && NativeOverlay.IsRunning)
    {
        NativeOverlay.Update(view);
    }
}

// Below-normal priority + Windows 11 "efficiency mode" (EcoQoS): the game always comes first.
static void LowerPriority()
{
    if (!OperatingSystem.IsWindows())
    {
        return;
    }
    try { Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.BelowNormal; } catch { }
    try { EcoQos.Enable(); } catch { }
}

static void Open(string path)
{
    try
    {
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true })?.Dispose();
    }
    catch
    {
        Console.WriteLine(L.T($"Ouvre ce fichier dans ton navigateur : {path}", $"Open this file in your browser: {path}", $"7ell had l'fichier f navigateur dyalek: {path}", $"Abre este archivo en tu navegador: {path}"));
    }
}

// ---- Dashboard ---------------------------------------------------------------------------------

void ShowDashboard()
{
    if (OperatingSystem.IsWindows() && web != null && !noWindow)
    {
        DashboardWindow.Show(web.Url);
    }
}

// What the assistant is doing right now (top of the dashboard).
LiveStatus StatusNow()
{
    if (autoCloseAt is { } closeAt)
    {
        var s = Math.Max(0, (int)Math.Ceiling((closeAt - DateTime.UtcNow).TotalSeconds));
        return new LiveStatus("close", L.T($"Fortnite est fermé : l'assistant se ferme dans {s} s", $"Fortnite is closed: the assistant closes in {s} s", $"Fortnite tsed: l'assistant ghadi ysed f {s} s", $"Fortnite está cerrado: el asistente se cierra en {s} s"));
    }
    if (tracker.IsLive)
    {
        var live = tracker.LiveMatch;
        return new LiveStatus("live", live != null
            ? L.T($"Partie en cours : {L.Count(live.ChestsOpened, "coffre", "chest", "sndouq", "cofre")} pour l'instant", $"Match in progress: {L.Count(live.ChestsOpened, "coffre", "chest", "sndouq", "cofre")} so far", $"Partiya ghadya: {L.Count(live.ChestsOpened, "coffre", "chest", "sndouq", "cofre")} 7ta l daba", $"Partida en curso: {L.Count(live.ChestsOpened, "coffre", "chest", "sndouq", "cofre")} por ahora")
            : L.T("Partie en cours : ses coffres seront comptés à la fin", "Match in progress: its chests are counted at the end", "Partiya ghadya: snadeq dyalha ghadi yt7sbo f lekher", "Partida en curso: sus cofres se cuentan al final"));
    }
    if (tracker.IsFinishing || tracker.HasPending)
    {
        return new LiveStatus("count", L.T("Partie terminée : comptage des coffres…", "Match over: counting the chests…", "Partiya salat: kan7seb snadeq…", "Partida terminada: contando los cofres…"));
    }
    return Directory.Exists(config.DemosFolder)
        ? new LiveStatus("wait", L.T("En attente d'une partie", "Waiting for a match", "Kantsenna partiya", "Esperando una partida"))
        : new LiveStatus("nofolder", L.T("Dossier des replays introuvable (voir Réglages)", "Replays folder not found (see Settings)", "Dossier dyal les replays ma kaynch (chouf Réglages)", "Carpeta de repeticiones no encontrada (ver Ajustes)"));
}

LiveInfo BuildLive()
{
    SetupInfo? setup = null;
    if (setupStep != null)
    {
        var steps = OperatingSystem.IsWindows() && AppInfo.ExePath != null ? 5 : 4;
        var number = setupStep switch { "lang" => 1, "replays" => 2, "overlay" => 3, "fortnite" => 4, _ => steps };
        setup = new SetupInfo(setupStep, number, steps, config.DemosFolder, Directory.Exists(config.DemosFolder), setupStep == "replays" ? SetupExisting().Count : 0);
    }
    var activity = log.Last(14).Select(l => string.Concat(l.Select(s => s.Text)).Trim()).Where(t => t.Length > 0).ToList();
    return new LiveInfo(
        StatusNow(),
        activity,
        showWhatsNew ? ReleaseNotes.WhatsNew(ReleaseNotes.Embedded, L.Code) : null,
        OperatingSystem.IsWindows() && NativeOverlay.IsRunning,
        OperatingSystem.IsWindows() && NativeOverlay.IsVisible,
        OperatingSystem.IsWindows() && NativeOverlay.IsLocked,
        askProfile && Profiles.List(data).Count >= 2,
        notice,
        setup,
        OperatingSystem.IsWindows() && AppInfo.ExePath != null,
        File.Exists(store.ErrorLogPath));
}

// Replays already on the PC at the first launch (the setup asks whether to count them).
List<FileInfo> SetupExisting() => setupExisting ??= tracker.UnprocessedFiles().Where(f => ReplayHeaderProbe.IsLive(f.FullName) != true).ToList();

// Runs the dashboard's button presses, then renders its page again if anything changed (main loop thread only).
void PumpDashboard()
{
    if (web == null)
    {
        return;
    }
    var ran = web.RunPending(RunAction);
    // What changes without a scan: the status (match in progress, countdown) and the overlay's state.
    var signature = $"{StatusNow()}|{OperatingSystem.IsWindows() && NativeOverlay.IsVisible}|{OperatingSystem.IsWindows() && NativeOverlay.IsLocked}|{ReportWriter.Progress}";
    if (ran || liveDirty || signature != liveSignature)
    {
        liveDirty = false;
        liveSignature = signature;
        ReportWriter.RenderLive(store, config, data);
    }
}

DashboardReply RunAction(DashboardAction a)
{
    liveDirty = true;
    switch (a.Name)
    {
        case "setup":
            return SetupAnswer(a.Key, a.Value);
        case "set":
            return ChangeSetting(a.Key, a.Value);
        case "export":
            return ExportFromDashboard();
        case "import":
            return ImportFromDashboard(a.FileName ?? "", a.Body ?? "");
        case "remove-file":
        {
            var source = Team.Imports.Sources.FirstOrDefault(s => s.Key == a.Key);
            if (source == null)
            {
                return new DashboardReply(false, L.T("Ce fichier n'est plus là.", "This file is no longer there.", "Had l'fichier ma b9ach.", "Este archivo ya no está."));
            }
            Team.Imports = new TeamImports { Sources = Team.Imports.Sources.Where(x => !ReferenceEquals(x, source)).ToList() };
            store.SaveImports(Team.Imports);
            ReportWriter.WriteAll(store, config, data);
            UpdateOverlay(store, config, data, tracker.LiveMatch, tracker.IsLive);
            return new DashboardReply(true, L.T($"Le fichier de {source.Name} est retiré.", $"{source.Name}'s file is removed.", $"L'fichier dyal {source.Name} t7iyed.", $"El archivo de {source.Name} se ha quitado."));
        }
        case "reload-hours":
            ReloadOddsFromFile();
            store.SaveConfig(config);
            ReportWriter.WriteAll(store, config, data);
            UpdateOverlay(store, config, data, tracker.LiveMatch, tracker.IsLive);
            return new DashboardReply(true, L.T($"Heures du trésor relues : {config.Odds.TreasureHours.Count}.", $"Treasure Hours reloaded: {config.Odds.TreasureHours.Count}.", $"Sa3at l'kenz t3awdo t9raw: {config.Odds.TreasureHours.Count}.", $"Horas del tesoro recargadas: {config.Odds.TreasureHours.Count}."));
        case "add-hour":
            return AddTreasureHour(a.Value);
        case "remove-hour":
        {
            var removed = config.Odds.TreasureHours.RemoveAll(h => OddsModel.Utc(h.StartUtc).Ticks.ToString(CultureInfo.InvariantCulture) == a.Key);
            store.SaveConfig(config);
            ReportWriter.WriteAll(store, config, data);
            UpdateOverlay(store, config, data, tracker.LiveMatch, tracker.IsLive);
            return new DashboardReply(removed > 0, removed > 0 ? L.T("Heure du trésor retirée.", "Treasure Hour removed.", "Sa3at l'kenz t7iydat.", "Hora del tesoro quitada.") : "?");
        }
        case "add-item":
            return AddItem(a.Value);
        case "remove-item":
        {
            var item = ItemsModel.Checklist.Custom.FirstOrDefault(c => c.Id == a.Key);
            if (item == null)
            {
                return new DashboardReply(false, "?");
            }
            ItemsModel.Checklist.Custom.Remove(item);
            ItemsModel.Checklist.Got.Remove(item.Id);
            ItemsModel.Checklist.NotMine.Remove(item.Id);
            store.SaveChecklist(ItemsModel.Checklist);
            ReportWriter.WriteAll(store, config, data);
            return new DashboardReply(true, L.T($"« {item.Name} » est retiré.", $"\"{item.Name}\" is removed.", $"«{item.Name}» t7iyed.", $"\"{item.Name}\" se ha quitado."));
        }
        case "overlay-toggle":
            if (OperatingSystem.IsWindows() && NativeOverlay.IsRunning)
            {
                NativeOverlay.Toggle();
            }
            return DashboardReply.Done;
        case "overlay-lock":
            if (OperatingSystem.IsWindows() && NativeOverlay.IsRunning)
            {
                NativeOverlay.ToggleLock();
            }
            return DashboardReply.Done;
        case "whatsnew-ok":
            showWhatsNew = false;
            config.LastVersion = AppInfo.VersionText;
            store.SaveConfig(config);
            return DashboardReply.Done;
        case "keep-open":
            autoCloseAt = null;
            auto = false;
            Console.WriteLine(L.T("L'assistant reste ouvert.", "The assistant stays open.", "L'assistant ba9i m7loul.", "El asistente sigue abierto."));
            return DashboardReply.Done;
        case "cleanup":
        {
            if (!OperatingSystem.IsWindows() || AppInfo.ExePath is not { } exe)
            {
                return new DashboardReply(false, L.T("Impossible ici : lance l'assistant depuis ChestTokenAssistant.exe.", "Not possible here: run the assistant from ChestTokenAssistant.exe.", "Ma ymkench hna: 7ell l'assistant mn ChestTokenAssistant.exe.", "Imposible aquí: ejecuta el asistente desde ChestTokenAssistant.exe."));
            }
            var report = OldVersions.Run(exe, includeLegacy: true);
            if (config.StartWithFortnite && !OldVersions.Testing)
            {
                FortniteWatch.Apply(true, dataDir); // this exe's own start with Fortnite, in place of theirs
            }
            Console.WriteLine(report.Text());
            return new DashboardReply(true, report.Any ? report.Text()
                : L.T("Aucune ancienne version en cours ni lancée avec Fortnite.", "No old version running or starting with Fortnite.", "7ta version 9dima khddama wla kat7ell m3a Fortnite.", "Ninguna versión antigua en marcha ni iniciándose con Fortnite."));
        }
        case "open-folder":
            Open(store.Folder);
            return DashboardReply.Done;
        case "open-report":
            Open(store.ReportPath);
            return DashboardReply.Done;
        case "open-errors":
            Open(store.ErrorLogPath);
            return DashboardReply.Done;
        case "credits":
            Open(AppInfo.OriginalRepoUrl);
            return DashboardReply.Done;
        case "reset-setup":
            if (OperatingSystem.IsWindows())
            {
                if (config.StartWithFortnite)
                {
                    FortniteWatch.Apply(false, dataDir);
                }
                NativeOverlay.Stop();
            }
            try { File.Delete(store.ConfigPath); } catch { /* the setup will overwrite it */ }
            restart = quit = true;
            tracker.StopRequested = true;
            return new DashboardReply(true, L.T("L'assistant redémarre sur la configuration…", "The assistant restarts with the setup…", "L'assistant kay3awed ybda b la configuration…", "El asistente se reinicia con la configuración…"));
        case "console":
            restartInConsole = restart = quit = true;
            tracker.StopRequested = true;
            return new DashboardReply(true, L.T("L'assistant redémarre dans la console…", "The assistant restarts in the console…", "L'assistant kay3awed yt7ell f la console…", "El asistente se reinicia en la consola…"));
        case "quit":
            quit = true;
            tracker.StopRequested = true;
            return new DashboardReply(true, L.T("L'assistant est fermé. À bientôt !", "The assistant is closed. See you soon!", "L'assistant tsed. Bslama!", "El asistente está cerrado. ¡Hasta pronto!"));
    }
    return new DashboardReply(false, "?");
}

// First launch in the dashboard: one answer per screen.
DashboardReply SetupAnswer(string key, string value)
{
    switch (key)
    {
        case "lang":
            if (L.Normalize(value) is { } code)
            {
                config.Language = code;
                L.Set(code);
                appName = AppInfo.Name;
            }
            setupStep = "replays";
            break;
        case "demos":
        {
            var folder = Environment.ExpandEnvironmentVariables(value.Trim().Trim('"'));
            if (folder.Length > 0)
            {
                config.DemosFolder = folder;
                setupExisting = null;
            }
            break;
        }
        case "existing":
            if (value != "1")
            {
                tracker.MarkSkipped(SetupExisting()); // "only my next matches"
            }
            store.SaveConfig(config);
            setupStep = OperatingSystem.IsWindows() ? "overlay" : "ready";
            break;
        case "overlay":
            config.ShowOverlay = value == "1";
            store.SaveConfig(config);
            setupStep = OperatingSystem.IsWindows() && AppInfo.ExePath != null ? "fortnite" : "ready";
            break;
        case "fortnite":
            config.StartWithFortnite = value == "1" && OperatingSystem.IsWindows() && FortniteWatch.Apply(true, dataDir);
            store.SaveConfig(config);
            setupStep = "ready";
            break;
        case "done":
            store.SaveConfig(config);
            setupStep = null;
            break;
    }
    return DashboardReply.Done;
}

// A setting changed in the dashboard: saved, and shown everywhere at once.
DashboardReply ChangeSetting(string key, string value)
{
    var on = value == "1";
    string? message = null;
    int Number(int min) => int.TryParse(value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) && n >= min ? n : -1;
    DashboardReply Bad() => new(false, L.T("Valeur non valide.", "Invalid value.", "Had la valeur ma mzyanach.", "Valor no válido."));
    if (key.StartsWith("item:", StringComparison.Ordinal))
    {
        // The Items tab's checklist (saved apart from the settings).
        var state = ItemsModel.Build(Profiles.View(data, config), config, ItemsModel.Checklist).Items.FirstOrDefault(x => x.Def.Id == key[5..]);
        if (state == null)
        {
            return Bad();
        }
        ItemsModel.Set(ItemsModel.Checklist, state.Def.Id, on, foundInReplay: state.Auto != null);
        store.SaveChecklist(ItemsModel.Checklist);
        ReportWriter.WriteAll(store, config, data);
        return new DashboardReply(true, on
            ? L.T($"✔ {state.Def.Name} : coché.", $"✔ {state.Def.Name}: ticked.", $"✔ {state.Def.Name}: tcocha.", $"✔ {state.Def.Name}: marcado.")
            : L.T($"{state.Def.Name} : décoché.", $"{state.Def.Name}: unticked.", $"{state.Def.Name}: tdécocha.", $"{state.Def.Name}: desmarcado."));
    }
    switch (key)
    {
        case "dailyGoal":
        {
            var goal = Number(0);
            if (goal < 0 || goal > 100_000)
            {
                return Bad();
            }
            config.DailyGoal = goal;
            break;
        }
        case "lang":
            if (L.Normalize(value) is not { } code)
            {
                return Bad();
            }
            config.Language = code;
            L.Set(code);
            appName = AppInfo.Name;
            ConsoleUi.SetTitle($"{appName} {AppInfo.VersionText} · by {AppInfo.Author}");
            break;
        case "profile":
            if (!Profiles.List(data).Any(p => p.Key == value))
            {
                return Bad();
            }
            config.Profile = value;
            askProfile = false;
            break;
        case "withFortnite":
            if (!OperatingSystem.IsWindows() || !FortniteWatch.Apply(on, dataDir))
            {
                return new DashboardReply(false, L.T("Ouvrir avec Fortnite : impossible ici, lance l'assistant depuis ChestTokenAssistant.exe.",
                    "Open with Fortnite: not possible here, run the assistant from ChestTokenAssistant.exe.",
                    "7ell m3a Fortnite: ma ymkench hna, 7ell l'assistant mn ChestTokenAssistant.exe.", "Abrir con Fortnite: imposible aquí, ejecuta el asistente desde ChestTokenAssistant.exe."));
            }
            config.StartWithFortnite = on;
            break;
        case "shareFiles":
            if (!OperatingSystem.IsWindows() || !(on ? ShareFiles.Register() : ShareFiles.Unregister()))
            {
                return new DashboardReply(false, L.T($"Fichiers .chesttoken : impossible ici, lance l'assistant depuis {AppInfo.ExeName}.",
                    $".chesttoken files: not possible here, run the assistant from {AppInfo.ExeName}.",
                    $"Fichiers .chesttoken: ma ymkench hna, 7ell l'assistant mn {AppInfo.ExeName}.", $"Archivos .chesttoken: imposible aquí, ejecuta el asistente desde {AppInfo.ExeName}."));
            }
            config.OpenShareFiles = on;
            break;
        case "demos":
        {
            var folder = Environment.ExpandEnvironmentVariables(value.Trim().Trim('"'));
            if (folder.Length == 0)
            {
                return Bad();
            }
            config.DemosFolder = folder;
            WatchReplays();
            nextScan = DateTime.UtcNow;
            message = Directory.Exists(folder)
                ? L.T($"Dossier des replays : {folder}", $"Replays folder: {folder}", $"Dossier dyal les replays: {folder}", $"Carpeta de repeticiones: {folder}")
                : L.T($"Enregistré, mais ce dossier n'existe pas encore : {folder}", $"Saved, but this folder does not exist yet: {folder}", $"Tsejjel, walakin had dossier mazal ma kaynch: {folder}", $"Guardado, pero esta carpeta aún no existe: {folder}");
            break;
        }
        case "overlay":
            if (on != config.ShowOverlay)
            {
                ToggleOverlay();
            }
            break;
        case "overlayStyle":
            if (value is not ("wide" or "hud" or "classic"))
            {
                return Bad();
            }
            config.OverlayStyle = value;
            if (OperatingSystem.IsWindows())
            {
                NativeOverlay.SetStyle(value);
            }
            break;
        case "overlaySize":
        {
            var size = Number(1);
            if (size < 0)
            {
                return Bad();
            }
            config.OverlaySize = OverlayAppearance.ClampSize(size);
            ApplyOverlayAppearance();
            break;
        }
        case "overlayOpacity":
        {
            var opacity = Number(1);
            if (opacity < 0)
            {
                return Bad();
            }
            config.OverlayOpacity = OverlayAppearance.ClampOpacity(opacity);
            ApplyOverlayAppearance();
            break;
        }
        case "tokenLine":
            config.Odds.OverlayTokenLine = on;
            break;
        case "overlayMessages":
            config.OverlayMessages = on;
            break;
        case "scope":
            if (!OddsModel.Scopes.Contains(value))
            {
                return Bad();
            }
            config.Odds.ChestScope = value;
            break;
        case "fill":
            config.Odds.IncludeFill = on;
            break;
        case "ranked":
            config.Odds.IncludeRanked = on;
            break;
        case "baseMine" or "baseTeam":
        {
            var n = Number(0);
            if (n < 0)
            {
                return Bad();
            }
            var profileKey = Profiles.Current(data, config)?.Key ?? config.Profile ?? "";
            var baseline = config.Odds.Baselines.GetValueOrDefault(profileKey) ?? new OddsBaseline();
            if (key == "baseMine") baseline.Mine = n; else baseline.Team = n;
            config.Odds.Baselines[profileKey] = baseline;
            break;
        }
        case "odds-pirate" or "odds-vehicle" or "odds-gold":
        {
            var n = Number(1);
            if (n < 1)
            {
                return Bad();
            }
            switch (key)
            {
                case "odds-vehicle": config.Odds.VehicleDecalOdds = n; break;
                case "odds-gold": config.Odds.GoldOutfitOdds = n; break;
                default: config.Odds.PirateShopOdds = n; break;
            }
            break;
        }
        case "huntEnd" or "activateBy":
        {
            if (!DateTime.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
            {
                return Bad();
            }
            var utc = DateTime.SpecifyKind(d.AddHours(23).AddMinutes(59), DateTimeKind.Utc);
            if (key == "huntEnd") config.Odds.HuntEndUtc = utc; else config.Odds.ActivateByUtc = utc;
            break;
        }
        default:
            return Bad();
    }
    store.SaveConfig(config);
    ReportWriter.WriteAll(store, config, data);
    UpdateOverlay(store, config, data, tracker.LiveMatch, tracker.IsLive);
    return new DashboardReply(true, message ?? L.T("Enregistré.", "Saved.", "Tsejjel.", "Guardado."));
}

// Settings → Treasure Hours → Add: a day and two times in the PC's time, and the boosts.
DashboardReply AddTreasureHour(string json)
{
    DashboardReply Bad() => new(false, L.T("Vérifie le jour, les heures et les bonus (×1 à ×100).", "Check the day, the times and the boosts (×1 to ×100).", "Chouf nhar, sa3at w l'bonus (×1 7ta ×100).", "Revisa el día, las horas y los multiplicadores (×1 a ×100)."));
    try
    {
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        string Field(string name) => doc.RootElement.TryGetProperty(name, out var v) && v.ValueKind == System.Text.Json.JsonValueKind.String ? v.GetString() ?? "" : "";
        double Boost(string name) => double.TryParse(Field(name).Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var b) ? b : double.NaN;
        if (!DateTime.TryParseExact(Field("date"), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day)
            || !TimeSpan.TryParseExact(Field("from"), @"hh\:mm", CultureInfo.InvariantCulture, out var from)
            || !TimeSpan.TryParseExact(Field("to"), @"hh\:mm", CultureInfo.InvariantCulture, out var to))
        {
            return Bad();
        }
        var (pirate, vehicle, gold) = (Boost("pirate"), Boost("vehicle"), Boost("gold"));
        if (new[] { pirate, vehicle, gold }.Any(b => double.IsNaN(b) || b < 1 || b > 100))
        {
            return Bad();
        }
        var start = DateTime.SpecifyKind(day + from, DateTimeKind.Local).ToUniversalTime();
        var end = DateTime.SpecifyKind(day + to, DateTimeKind.Local).ToUniversalTime();
        if (end <= start)
        {
            end = end.AddDays(1); // ends after midnight
        }
        var hour = new TreasureHour { StartUtc = start, EndUtc = end, PirateShop = pirate, VehicleDecal = vehicle, GoldOutfit = gold };
        config.Odds.TreasureHours.Add(hour);
        store.SaveConfig(config);
        ReportWriter.WriteAll(store, config, data);
        UpdateOverlay(store, config, data, tracker.LiveMatch, tracker.IsLive);
        var when = $"{start.ToLocalTime().ToString("ddd d MMM HH:mm", L.Culture)}–{end.ToLocalTime().ToString("HH:mm", L.Culture)}";
        return new DashboardReply(true, L.T($"Heure du trésor ajoutée : {when} ({OddsModel.BoostText(hour)}).",
            $"Treasure Hour added: {when} ({OddsModel.BoostText(hour)}).",
            $"Sa3at l'kenz tzadet: {when} ({OddsModel.BoostText(hour)}).", $"Hora del tesoro añadida: {when} ({OddsModel.BoostText(hour)})."));
    }
    catch (System.Text.Json.JsonException)
    {
        return Bad();
    }
}

void TreasureHourAlerts()
{
    var now = DateTime.UtcNow;
    var moment = $"{OddsModel.ActiveHour(config.Odds, now)?.StartUtc.Ticks}|{DateTime.Now:yyyyMMdd}";
    if (moment != overlayMoment)
    {
        var first = overlayMoment.Length == 0;
        overlayMoment = moment;
        if (!first)
        {
            UpdateOverlay(store, config, data, tracker.LiveMatch, tracker.IsLive);
            ReportWriter.WriteAll(store, config, data); // the header's Treasure Hour, today's goal
        }
    }
    foreach (var h in config.Odds.TreasureHours.ToList())
    {
        var start = OddsModel.Utc(h.StartUtc);
        var end = OddsModel.Utc(h.EndUtc);
        if (end <= now || start - now > TimeSpan.FromMinutes(10))
        {
            continue;
        }
        var id = start.Ticks.ToString(CultureInfo.InvariantCulture);
        string? text = null;
        if (now < start && hourAlerts.Add("soon" + id))
        {
            var minutes = Math.Max(1, (int)Math.Ceiling((start - now).TotalMinutes));
            text = L.T($"Heure du trésor dans {minutes} min ({OddsModel.BoostText(h)}), jusqu'à {end.ToLocalTime():HH:mm} : prépare-toi à ouvrir des coffres !",
                $"Treasure Hour in {minutes} min ({OddsModel.BoostText(h)}), until {end.ToLocalTime():HH:mm}: get ready to open chests!",
                $"Sa3at l'kenz f {minutes} d9i9a ({OddsModel.BoostText(h)}), 7ta {end.ToLocalTime():HH:mm}: wjjed rassek t7ell snadeq!", $"Hora del tesoro en {minutes} min ({OddsModel.BoostText(h)}), hasta las {end.ToLocalTime():HH:mm}: ¡prepárate para abrir cofres!");
        }
        else if (now >= start && hourAlerts.Add("now" + id))
        {
            hourAlerts.Add("soon" + id);
            text = L.T($"Heure du trésor en cours ({OddsModel.BoostText(h)}) jusqu'à {end.ToLocalTime():HH:mm} : chaque coffre compte plus !",
                $"Treasure Hour on ({OddsModel.BoostText(h)}) until {end.ToLocalTime():HH:mm}: every chest counts for more!",
                $"Sa3at l'kenz bdat ({OddsModel.BoostText(h)}) 7ta {end.ToLocalTime():HH:mm}: kol sndouq kayt7seb kter!", $"Hora del tesoro activa ({OddsModel.BoostText(h)}) hasta las {end.ToLocalTime():HH:mm}: ¡cada cofre cuenta más!");
        }
        if (text != null)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine(text);
            Console.ResetColor();
            if (OperatingSystem.IsWindows())
            {
                Tray.Notify(text);
            }
        }
    }
}

// Items tab → Add an item (a later week: the assistant never goes online to learn it).
DashboardReply AddItem(string json)
{
    try
    {
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        string Field(string name) => doc.RootElement.TryGetProperty(name, out var v) && v.ValueKind == System.Text.Json.JsonValueKind.String ? (v.GetString() ?? "").Trim() : "";
        DateTime? Day(string name, int addDays) => DateTime.TryParseExact(Field(name), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? DateTime.SpecifyKind(d.AddDays(addDays), DateTimeKind.Local).ToUniversalTime() : null;
        var name = Field("name");
        if (name.Length == 0 || name.Length > 80)
        {
            return new DashboardReply(false, L.T("Donne un nom à l'objet.", "Give the item a name.", "3ti smiya l had l7aja.", "Ponle un nombre al objeto."));
        }
        var odds = int.TryParse(Field("odds"), NumberStyles.None, CultureInfo.InvariantCulture, out var n) && n >= 1 ? n : (int?)null;
        var week = Field("week");
        var item = new CustomItem
        {
            Id = "c-" + Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(4)).ToLowerInvariant(),
            Name = name,
            Type = ItemsModel.Types.Contains(Field("type")) ? Field("type") : "other",
            Week = week.Length > 40 ? week[..40] : week,
            StartUtc = Day("from", 0),
            EndUtc = Day("to", 1), // the whole last day
            OddsOneIn = odds,
        };
        ItemsModel.Checklist.Custom.Add(item);
        store.SaveChecklist(ItemsModel.Checklist);
        ReportWriter.WriteAll(store, config, data);
        return new DashboardReply(true, L.T($"« {name} » ajouté.", $"\"{name}\" added.", $"«{name}» tzad.", $"\"{name}\" añadido."), "items");
    }
    catch (System.Text.Json.JsonException)
    {
        return new DashboardReply(false, "?");
    }
}

DashboardReply ExportFromDashboard()
{
    try
    {
        var (path, matches) = ShareFormat.Export(Profiles.View(data, config), ShareFormat.ExportFolder(store));
        if (OperatingSystem.IsWindows())
        {
            try { Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = false })?.Dispose(); } catch { }
        }
        var message = L.T($"Fichier exporté ({L.Count(matches, "partie en équipe", "parties en équipe", "", "", "", "", "", "")}) : {Path.GetFileName(path)}, montré dans l'Explorateur. Envoie-le à tes coéquipiers.",
            $"File exported ({L.Count(matches, "", "", "team match", "team matches", "", "", "partida en equipo", "partidas en equipo")}): {Path.GetFileName(path)}, shown in Explorer. Send it to your teammates.",
            $"Fichier exporté ({L.Count(matches, "", "", "", "", "partiya f l'équipe", "partiyat f l'équipe", "", "")}): {Path.GetFileName(path)}, kayban f l'Explorateur. Siftou l s7abek.", $"Archivo exportado ({L.Count(matches, "", "", "", "", "", "", "partida en equipo", "partidas en equipo")}): {Path.GetFileName(path)}, mostrado en el Explorador. Envíalo a tus compañeros.");
        Console.WriteLine(message);
        return new DashboardReply(true, message);
    }
    catch (Exception ex)
    {
        return new DashboardReply(false, L.T($"Export impossible : {ex.Message}", $"Export failed: {ex.Message}", $"Export ma khdamch: {ex.Message}", $"Exportación fallida: {ex.Message}"));
    }
}

// A teammate's file chosen or dropped on the dashboard (the page reads it and sends its text).
DashboardReply ImportFromDashboard(string name, string text)
{
    try
    {
        var (source, message) = Team.Parse(name, text, data);
        if (source == null)
        {
            return new DashboardReply(false, message);
        }
        Team.Imports = Team.With(Team.Imports, source);
        store.SaveImports(Team.Imports);
        ReportWriter.WriteAll(store, config, data);
        UpdateOverlay(store, config, data, tracker.LiveMatch, tracker.IsLive);
        Console.WriteLine(message);
        return new DashboardReply(true, message, "files");
    }
    catch (Exception ex)
    {
        return new DashboardReply(false, L.T($"Impossible de lire ce fichier : {ex.Message}", $"Cannot read this file: {ex.Message}", $"Ma 9dertch n9ra had l'fichier: {ex.Message}", $"No se puede leer este archivo: {ex.Message}"));
    }
}
