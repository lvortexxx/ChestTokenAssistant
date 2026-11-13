using System.Diagnostics;
using System.Runtime.Versioning;
using Microsoft.Win32;

namespace ChestCounter;

/// <summary>What the clean-up of old versions did.</summary>
public sealed record CleanupReport(List<string> Stopped, List<string> Removed)
{
    public bool Any => Stopped.Count > 0 || Removed.Count > 0;

    public string Text() => L.T(
        $"Anciennes versions arrêtées ({string.Join(", ", Stopped.DefaultIfEmpty("aucune en cours"))}) et lancement avec Fortnite retiré ({string.Join(", ", Removed.DefaultIfEmpty("rien à retirer"))}).",
        $"Old versions stopped ({string.Join(", ", Stopped.DefaultIfEmpty("none running"))}) and their start with Fortnite removed ({string.Join(", ", Removed.DefaultIfEmpty("nothing to remove"))}).",
        $"Les versions l9dam t7bsou ({string.Join(", ", Stopped.DefaultIfEmpty("7ta wa7da khddama"))}) w t7iyed lancement dyalhom m3a Fortnite ({string.Join(", ", Removed.DefaultIfEmpty("walou bach yt7iyed"))}).", $"Versiones antiguas detenidas ({string.Join(", ", Stopped.DefaultIfEmpty("ninguna en marcha"))}) y su inicio con Fortnite quitado ({string.Join(", ", Removed.DefaultIfEmpty("nada que quitar"))}).");
}

/// <summary>
/// Old versions that keep running, or that start again when Fortnite starts: copies of the assistant run from another
/// folder (an older build), and the tools it comes from (ChestCounter.exe, ChestTracker.exe). This version stops them
/// and removes their start with Fortnite (the Windows logon entries pointing to another exe), so that only this exe is
/// ever opened. Run at each start on the player's own data folder, and from Settings → Advanced or --cleanup.
/// All the versions share one standby (same lock and stop event): it is restarted from this exe when "Open with
/// Fortnite" is on.
/// </summary>
[SupportedOSPlatform("windows")]
public static class OldVersions
{
    public static readonly string[] ProcessNames = { "ChestTokenAssistant", "ChestCounter", "ChestTracker" };
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValue = "ChestTokenAssistant";
    private static readonly string[] LegacyRunValues = { "ChestCounter", "ChestTracker" };

    /// <summary>
    /// Development: only the processes run from this folder are concerned, and neither the standby nor the logon entries
    /// are touched (so that a test never stops the player's own assistant).
    /// </summary>
    private static string? TestScope => Environment.GetEnvironmentVariable("CHESTTOKEN_CLEANUP_SCOPE") is { Length: > 0 } s ? Path.GetFullPath(s) : null;

    /// <summary>A test run (see <see cref="TestScope"/>): nothing of the player's own setup is touched.</summary>
    public static bool Testing => TestScope != null;

    /// <summary>Is this process an old version? (name of the process, its exe when known, this exe)</summary>
    public static bool IsOld(string processName, string? exePath, string currentExe, string? scope = null)
    {
        if (scope != null)
        {
            return exePath != null && exePath.StartsWith(scope, StringComparison.OrdinalIgnoreCase) && !SamePath(exePath, currentExe);
        }
        if (exePath == null)
        {
            // Unknown exe: the tools this one comes from are always old; a copy of this one may be this very exe.
            return !processName.Equals("ChestTokenAssistant", StringComparison.OrdinalIgnoreCase);
        }
        return !SamePath(exePath, currentExe);
    }

    /// <summary>The exe a logon entry starts: "\"C:\…\ChestTokenAssistant.exe\" --standby …" → C:\…\ChestTokenAssistant.exe.</summary>
    public static string? RunTarget(string? command)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            return null;
        }
        var c = command.Trim();
        if (c.StartsWith('"'))
        {
            var end = c.IndexOf('"', 1);
            return end > 1 ? c[1..end] : null;
        }
        var space = c.IndexOf(' ');
        return space > 0 ? c[..space] : c;
    }

    public static bool SamePath(string a, string? b)
    {
        if (b == null)
        {
            return false;
        }
        try
        {
            return string.Equals(Path.GetFullPath(a).TrimEnd('\\'), Path.GetFullPath(b).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>"next\ChestTokenAssistant.exe": short enough to be read in a message.</summary>
    private static string Short(string path)
    {
        var folder = Path.GetFileName(Path.GetDirectoryName(path) ?? "");
        return folder.Length > 0 ? $"{folder}\\{Path.GetFileName(path)}" : Path.GetFileName(path);
    }

    /// <summary>Stops the old versions and removes their start with Fortnite. Never this exe, never its own processes.
    /// <paramref name="includeLegacy"/>: also the tools this one comes from (ChestCounter.exe, ChestTracker.exe), only
    /// when the player asks for it (Settings → Advanced, --cleanup); the automatic clean-up at each start only stops
    /// older copies of this assistant.</summary>
    public static CleanupReport Run(string currentExe, bool includeLegacy)
    {
        var report = new CleanupReport(new(), new());
        var scope = TestScope;
        var me = Environment.ProcessId;
        var targets = new List<(Process Process, string Label)>();
        foreach (var name in includeLegacy ? ProcessNames : ProcessNames.Take(1))
        {
            foreach (var p in Process.GetProcessesByName(name))
            {
                string? path = null;
                try { path = p.MainModule?.FileName; } catch { /* another user's or protected: left alone below */ }
                if (p.Id != me && IsOld(name, path, currentExe, scope))
                {
                    targets.Add((p, path != null ? Short(path) : name + ".exe"));
                }
                else
                {
                    p.Dispose();
                }
            }
        }

        // Logon entries of the old versions (they start their standby, which opens the old exe with Fortnite).
        var oldEntries = new List<string>();
        RegistryKey? key = null;
        if (scope == null)
        {
            try
            {
                key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
                if (key != null)
                {
                    if (includeLegacy)
                    {
                        oldEntries.AddRange(LegacyRunValues.Where(v => key.GetValue(v) != null));
                    }
                    if (RunTarget(key.GetValue(RunValue) as string) is { } target && !SamePath(target, currentExe))
                    {
                        oldEntries.Add(RunValue);
                    }
                }
            }
            catch
            {
                key?.Dispose();
                key = null;
            }
        }

        // The standby they share: asked to stop (this exe starts its own again when "Open with Fortnite" is on).
        if (scope == null && (targets.Count > 0 || oldEntries.Count > 0))
        {
            FortniteWatch.StopStandby();
            for (var i = 0; i < 30 && Mutex.TryOpenExisting(AppInfo.StandbyMutex, out var held); i++)
            {
                held.Dispose();
                Thread.Sleep(100);
            }
        }

        foreach (var (p, label) in targets)
        {
            try
            {
                if (!p.HasExited)
                {
                    p.Kill();
                    p.WaitForExit(3000);
                }
                if (!report.Stopped.Contains(label))
                {
                    report.Stopped.Add(label);
                }
            }
            catch
            {
                // already gone, or not ours to stop
            }
            finally
            {
                p.Dispose();
            }
        }

        if (key != null)
        {
            using (key)
            {
                foreach (var value in oldEntries)
                {
                    try
                    {
                        var target = RunTarget(key.GetValue(value) as string);
                        key.DeleteValue(value, throwOnMissingValue: false);
                        report.Removed.Add(target != null ? Short(target) : value);
                    }
                    catch
                    {
                        // left as it is
                    }
                }
            }
        }
        return report;
    }
}
