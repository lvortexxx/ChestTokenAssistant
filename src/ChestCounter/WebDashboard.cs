using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ChestCounter;

/// <summary>What a button of the dashboard asked for, and its answer (shown as a short message on the page).</summary>
public sealed record DashboardAction(string Name, string Key, string Value, string? Body = null, string? FileName = null);

public sealed record DashboardReply(bool Ok, string? Message = null, string? Tab = null)
{
    public static readonly DashboardReply Done = new(true);
}

/// <summary>
/// The dashboard: a page served by the assistant itself, to this PC only. It listens on 127.0.0.1 (the PC talking to
/// itself: nothing outside can reach it, and nothing goes online) on a port of its own, and every address starts with
/// a secret drawn at each start, so that no website open in a browser can send it anything.
/// The page is rendered by the main loop (<see cref="ReportWriter"/>) and only handed over here; a button press is
/// queued and run by the main loop too (between two replays during a long analysis), never at the same time as a scan.
/// </summary>
public sealed class WebDashboard : IDisposable
{
    private const int MaxHeader = 16 * 1024;
    private const int MaxBody = 64 * 1024 * 1024; // a teammate's whole data.json can be imported
    private readonly TcpListener _listener;
    private readonly CancellationTokenSource _stop = new();
    private readonly ConcurrentQueue<(DashboardAction Action, TaskCompletionSource<DashboardReply> Reply)> _queue = new();

    /// <summary>The page and its version (the page polls the version and fetches the page again when it changes).</summary>
    private volatile string _page = "<!doctype html><title>Chest Token Assistant</title>";
    private long _version;
    private DateTime _lastSeenUtc = DateTime.MinValue;

    public int Port { get; }
    public string Key { get; }
    public string Url => $"http://127.0.0.1:{Port}/{Key}/";

    /// <summary>Called on the dashboard's own threads when a button was pressed: the main loop should wake up.</summary>
    public Action? ActionQueued { get; set; }

    /// <summary>The page told us it is being closed (the window was closed: the assistant keeps counting in the tray).</summary>
    public Action? PageClosed { get; set; }

    /// <summary>True while a dashboard page polled less than a few seconds ago.</summary>
    public bool PageOpen => DateTime.UtcNow - _lastSeenUtc < TimeSpan.FromSeconds(6);

    public long Version => Interlocked.Read(ref _version);

    private WebDashboard(TcpListener listener, int port, string key)
    {
        _listener = listener;
        Port = port;
        Key = key;
    }

    /// <summary>
    /// Starts listening on 127.0.0.1. The same port is tried first for a given data folder (the window keeps its place
    /// from one start to the next), another free one otherwise. Null if no port could be opened.
    /// </summary>
    public static WebDashboard? Start(string dataFolder, int? port = null, string? key = null)
    {
        var preferred = port ?? 47100 + (int)(BitConverter.ToUInt32(SHA256.HashData(Encoding.UTF8.GetBytes(dataFolder.ToLowerInvariant())), 0) % 800);
        foreach (var p in new[] { preferred, 0 })
        {
            try
            {
                var listener = new TcpListener(IPAddress.Loopback, p);
                listener.Start();
                var actual = ((IPEndPoint)listener.LocalEndpoint).Port;
                var dashboard = new WebDashboard(listener, actual, key is { Length: >= 32 } ? key : Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant());
                _ = Task.Run(dashboard.AcceptLoop);
                return dashboard;
            }
            catch (SocketException)
            {
                // taken: try any free port
            }
        }
        return null;
    }

    /// <summary>A new page from the main loop. The version only changes when the page really changed.</summary>
    public void Publish(string page)
    {
        if (page == _raw)
        {
            return;
        }
        _raw = page;
        // The page carries its own version: the page knows at once whether a newer one exists.
        var next = Version + 1;
        _page = page.Replace("data-v=\"__V__\"", $"data-v=\"{next}\"");
        Interlocked.Exchange(ref _version, next);
    }

    private string _raw = "";

    /// <summary>Runs the queued button presses (main loop only). Returns true if any ran.</summary>
    public bool RunPending(Func<DashboardAction, DashboardReply> run)
    {
        var any = false;
        while (_queue.TryDequeue(out var item))
        {
            any = true;
            DashboardReply reply;
            try
            {
                reply = run(item.Action);
            }
            catch (Exception ex)
            {
                ErrorLog.Write("Dashboard action " + item.Action.Name, ex);
                reply = new DashboardReply(false, ex.Message);
            }
            item.Reply.TrySetResult(reply);
        }
        return any;
    }

    public void Dispose()
    {
        _stop.Cancel();
        try { _listener.Stop(); } catch { }
        while (_queue.TryDequeue(out var item))
        {
            item.Reply.TrySetResult(new DashboardReply(false));
        }
    }

    private async Task AcceptLoop()
    {
        while (!_stop.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await _listener.AcceptTcpClientAsync(_stop.Token);
            }
            catch
            {
                return; // stopped
            }
            _ = Task.Run(() => Serve(client));
        }
    }

    private async Task Serve(TcpClient client)
    {
        using (client)
        {
            try
            {
                client.NoDelay = true;
                var stream = client.GetStream();
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
                timeout.CancelAfter(TimeSpan.FromSeconds(90));
                var request = await ReadRequest(stream, timeout.Token);
                if (request == null)
                {
                    return;
                }
                var (status, type, body) = await Handle(request.Value);
                await Respond(stream, status, type, body, timeout.Token);
            }
            catch
            {
                // a closed window or a broken request: nothing to do
            }
        }
    }

    private readonly record struct Request(string Method, string Path, Dictionary<string, string> Headers, byte[] Body);

    private static async Task<Request?> ReadRequest(NetworkStream stream, CancellationToken token)
    {
        var buffer = new byte[MaxHeader];
        var length = 0;
        var end = -1;
        while (end < 0)
        {
            if (length == buffer.Length)
            {
                return null;
            }
            var read = await stream.ReadAsync(buffer.AsMemory(length), token);
            if (read == 0)
            {
                return null;
            }
            length += read;
            end = buffer.AsSpan(0, length).IndexOf("\r\n\r\n"u8);
        }
        var head = Encoding.ASCII.GetString(buffer, 0, end).Split("\r\n");
        var first = head[0].Split(' ');
        if (first.Length < 3)
        {
            return null;
        }
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in head.Skip(1))
        {
            var colon = line.IndexOf(':');
            if (colon > 0)
            {
                headers[line[..colon].Trim()] = line[(colon + 1)..].Trim();
            }
        }
        var contentLength = headers.TryGetValue("Content-Length", out var cl) && int.TryParse(cl, out var n) ? n : 0;
        if (contentLength < 0 || contentLength > MaxBody)
        {
            return null;
        }
        var body = new byte[contentLength];
        var already = Math.Min(contentLength, length - end - 4);
        Array.Copy(buffer, end + 4, body, 0, already);
        var got = already;
        while (got < contentLength)
        {
            var read = await stream.ReadAsync(body.AsMemory(got), token);
            if (read == 0)
            {
                return null;
            }
            got += read;
        }
        return new Request(first[0], first[1], headers, body);
    }

    private async Task<(int Status, string Type, byte[] Body)> Handle(Request r)
    {
        static (int, string, byte[]) Text(int status, string text) => (status, "text/plain; charset=utf-8", Encoding.UTF8.GetBytes(text));

        // Only this PC, by its own address: a website that points a name of its own at 127.0.0.1 is refused.
        if (!r.Headers.TryGetValue("Host", out var host) || (host != $"127.0.0.1:{Port}" && host != $"localhost:{Port}"))
        {
            return Text(403, "Forbidden");
        }
        var prefix = $"/{Key}/";
        if (!r.Path.StartsWith(prefix, StringComparison.Ordinal))
        {
            return Text(404, "Not found"); // without the secret, nothing (not even a hint that the assistant is there)
        }
        var route = r.Path[prefix.Length..];
        var query = route.IndexOf('?');
        if (query >= 0)
        {
            route = route[..query];
        }

        if (r.Method == "GET")
        {
            switch (route)
            {
                case "":
                    _lastSeenUtc = DateTime.UtcNow;
                    return (200, "text/html; charset=utf-8", Encoding.UTF8.GetBytes(_page));
                case "state":
                    _lastSeenUtc = DateTime.UtcNow;
                    return (200, "application/json", Encoding.UTF8.GetBytes($"{{\"v\":{Version}}}"));
                default:
                    // An item's picture, straight from the exe (no file of the disk is ever served).
                    if (route.StartsWith("img/", StringComparison.Ordinal) && ItemImages.ReadServed(route[4..]) is { } picture)
                    {
                        return (200, ItemImages.ContentType(route), picture);
                    }
                    return Text(404, "Not found");
            }
        }
        if (r.Method != "POST")
        {
            return Text(405, "Method not allowed");
        }
        // Sent by the page itself only: a page of another site cannot add this header without the browser asking us
        // first (and nothing here answers that question), nor read the answers.
        if (r.Headers.TryGetValue("Origin", out var origin) && origin != $"http://127.0.0.1:{Port}" && origin != $"http://localhost:{Port}")
        {
            return Text(403, "Forbidden");
        }
        if (route == "closed")
        {
            _lastSeenUtc = DateTime.MinValue;
            PageClosed?.Invoke();
            return Text(204, "");
        }
        if (!r.Headers.TryGetValue("X-CTA", out var mark) || mark != "1")
        {
            return Text(403, "Forbidden");
        }

        DashboardAction action;
        if (route == "import")
        {
            var name = r.Headers.TryGetValue("X-Name", out var encoded) ? Uri.UnescapeDataString(encoded) : "import";
            action = new DashboardAction("import", "", "", Encoding.UTF8.GetString(r.Body), Path.GetFileName(name));
        }
        else if (route == "act")
        {
            try
            {
                using var json = JsonDocument.Parse(r.Body);
                string Field(string field) => json.RootElement.TryGetProperty(field, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
                action = new DashboardAction(Field("a"), Field("k"), Field("v"));
            }
            catch (JsonException)
            {
                return Text(400, "Bad request");
            }
        }
        else
        {
            return Text(404, "Not found");
        }

        var reply = new TaskCompletionSource<DashboardReply>(TaskCreationOptions.RunContinuationsAsynchronously);
        _queue.Enqueue((action, reply));
        ActionQueued?.Invoke();
        var done = await Task.WhenAny(reply.Task, Task.Delay(TimeSpan.FromSeconds(60), _stop.Token).ContinueWith(_ => { }));
        var result = done == reply.Task ? reply.Task.Result : new DashboardReply(false, L.T("L'assistant est occupé, réessaie dans un instant.", "The assistant is busy, try again in a moment.", "L'assistant mchghoul, 3awed chwiya mn b3d.", "El asistente está ocupado, inténtalo de nuevo en un momento."));
        var answer = JsonSerializer.Serialize(new Dictionary<string, object?> { ["ok"] = result.Ok, ["msg"] = result.Message, ["tab"] = result.Tab });
        return (200, "application/json", Encoding.UTF8.GetBytes(answer));
    }

    private static async Task Respond(NetworkStream stream, int status, string type, byte[] body, CancellationToken token)
    {
        var reason = status switch { 200 => "OK", 204 => "No Content", 400 => "Bad Request", 403 => "Forbidden", 404 => "Not Found", 405 => "Method Not Allowed", _ => "Error" };
        var head = new StringBuilder()
            .Append($"HTTP/1.1 {status} {reason}\r\n")
            .Append($"Content-Type: {type}\r\n")
            .Append($"Content-Length: {body.Length}\r\n")
            .Append("Cache-Control: no-store\r\n")
            .Append("X-Content-Type-Options: nosniff\r\n")
            // The secret is in the address: never sent to another site (a link opened from the page).
            .Append("Referrer-Policy: no-referrer\r\n")
            // Everything is in the page itself: no other site, no frame around it.
            .Append("Content-Security-Policy: default-src 'none'; script-src 'unsafe-inline'; style-src 'unsafe-inline'; img-src 'self' data:; connect-src 'self'; form-action 'none'; frame-ancestors 'none'; base-uri 'none'\r\n")
            .Append("Connection: close\r\n\r\n");
        await stream.WriteAsync(Encoding.ASCII.GetBytes(head.ToString()), token);
        if (body.Length > 0)
        {
            await stream.WriteAsync(body, token);
        }
    }
}

/// <summary>
/// Opens the dashboard in its own window (Edge "app" mode: no address bar, its own taskbar button), or brings back the
/// one already open. Edge is part of Windows 10 and 11; without it, the default browser opens the page.
/// </summary>
[SupportedOSPlatform("windows")]
public static class DashboardWindow
{
    /// <summary>The dashboard's address is kept here while the assistant runs (a second start opens it instead).</summary>
    public static string AddressFile(Store store) => Path.Combine(store.Folder, "dashboard-address.txt");

    /// <summary>Brings the dashboard window to the front, or opens it. False if neither worked.</summary>
    public static bool Show(string url, bool focusExisting = true)
    {
        if (focusExisting && Focus())
        {
            return true;
        }
        if (FindEdge() is { } edge)
        {
            try
            {
                var psi = new ProcessStartInfo(edge) { UseShellExecute = false };
                psi.ArgumentList.Add($"--app={url}");
                psi.ArgumentList.Add("--window-size=1180,860");
                Process.Start(psi)?.Dispose();
                return true;
            }
            catch
            {
                // fall back to the default browser
            }
        }
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true })?.Dispose();
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Opens the dashboard of the assistant already running for this data folder (second start, double-click).</summary>
    public static bool ShowRunning(Store store)
    {
        try
        {
            var url = File.ReadAllText(AddressFile(store)).Trim();
            return url.StartsWith("http://127.0.0.1:", StringComparison.Ordinal) && Show(url);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>A window whose title is the dashboard's (its own app window, or a browser tab on it).</summary>
    private static bool Focus()
    {
        var found = IntPtr.Zero;
        EnumWindows((hwnd, _) =>
        {
            if (!IsWindowVisible(hwnd))
            {
                return true;
            }
            var text = new StringBuilder(256);
            GetWindowTextW(hwnd, text, text.Capacity);
            var title = text.ToString();
            if (title == AppInfo.Name || title.StartsWith(AppInfo.Name + " - ", StringComparison.Ordinal) || title.StartsWith(AppInfo.Name + " — ", StringComparison.Ordinal))
            {
                found = hwnd;
                return false;
            }
            return true;
        }, IntPtr.Zero);
        if (found == IntPtr.Zero)
        {
            return false;
        }
        if (IsIconic(found))
        {
            ShowWindow(found, SW_RESTORE);
        }
        SetForegroundWindow(found);
        return true;
    }

    private static string? FindEdge()
    {
        foreach (var hive in new[] { Microsoft.Win32.Registry.CurrentUser, Microsoft.Win32.Registry.LocalMachine })
        {
            try
            {
                using var key = hive.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\msedge.exe");
                if (key?.GetValue(null) is string path && File.Exists(path.Trim('"')))
                {
                    return path.Trim('"');
                }
            }
            catch
            {
                // next place
            }
        }
        foreach (var root in new[] { Environment.SpecialFolder.ProgramFilesX86, Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.LocalApplicationData })
        {
            var path = Path.Combine(Environment.GetFolderPath(root), "Microsoft", "Edge", "Application", "msedge.exe");
            if (File.Exists(path))
            {
                return path;
            }
        }
        return null;
    }

    private const int SW_RESTORE = 9;
    private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr param);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc proc, IntPtr param);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowTextW(IntPtr hwnd, StringBuilder text, int max);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hwnd, int cmd);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hwnd);
}

/// <summary>A message box, for the few things to say when there is no window at all (crash, already open).</summary>
[SupportedOSPlatform("windows")]
public static class MessageBox
{
    public static void Show(string text, bool error = false)
    {
        try
        {
            MessageBoxW(IntPtr.Zero, text, AppInfo.Name, error ? 0x10u : 0x40u);
        }
        catch
        {
            // nothing else to show it with
        }
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int MessageBoxW(IntPtr hwnd, string text, string caption, uint type);
}
