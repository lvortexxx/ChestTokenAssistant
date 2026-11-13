using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32;

namespace ChestCounter;

/// <summary>
/// The file a player exports for their teammates (.chesttoken): their own chests in each team match, and nothing
/// else. No replay file names, no items, no other player's id or name: teammates are one-way hashes, enough to tell
/// "this match, same team" without saying who they are.
/// </summary>
public sealed class ShareFile
{
    public string Format { get; set; } = ShareFormat.Name;
    public int FormatVersion { get; set; } = 1;
    public string App { get; set; } = "";
    public DateTime ExportedUtc { get; set; }
    public SharePlayer Player { get; set; } = new();
    public List<ShareMatch> Matches { get; set; } = new();
}

public sealed class SharePlayer
{
    public string Name { get; set; } = "";
    public string? EpicId { get; set; }
}

public sealed class ShareMatch
{
    public string SessionId { get; set; } = "";
    public DateTime? MatchStartUtc { get; set; }
    public string? Playlist { get; set; }
    public int? TeamSize { get; set; }
    /// <summary>Chests the player opened or destroyed themselves (exact).</summary>
    public int Chests { get; set; }
    /// <summary>Replay seconds of each chest (Treasure Hours).</summary>
    public List<float> ChestTimes { get; set; } = new();
    public int Treasures { get; set; }
    /// <summary>Teammates of that match as <see cref="ShareFormat.Hashes"/>; null when the match has no team data.</summary>
    public List<string>? Teammates { get; set; }
}

public static class ShareFormat
{
    public const string Name = "chest-token-share";
    public const string Extension = ".chesttoken";

    /// <summary>A player as a short one-way hash: comparable between two files, not readable.</summary>
    public static string Hash(string? epicId, string? name)
    {
        var key = !string.IsNullOrEmpty(epicId) ? "id:" + epicId.Trim().ToLowerInvariant() : "name:" + (name ?? "").Trim().ToLowerInvariant();
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("chest-token|" + key)), 0, 10).ToLowerInvariant();
    }

    /// <summary>The hashes of a player: by Epic id and by name (one side may only know the name).</summary>
    public static IEnumerable<string> Hashes(string? epicId, string? name)
    {
        if (!string.IsNullOrEmpty(epicId))
        {
            yield return Hash(epicId, null);
        }
        if (!string.IsNullOrWhiteSpace(name))
        {
            yield return Hash(null, name);
        }
    }

    /// <summary>The share file of one profile: every match played in a team (solo matches have nothing to share).</summary>
    public static ShareFile Build(TrackerData profile)
    {
        var me = profile.Matches.OrderByDescending(m => m.MatchStartUtc ?? m.AnalyzedAtUtc).FirstOrDefault(m => !string.IsNullOrWhiteSpace(m.PlayerName));
        var file = new ShareFile
        {
            App = $"{AppInfo.Name} {AppInfo.VersionText}",
            ExportedUtc = DateTime.UtcNow,
            Player = new SharePlayer { Name = me?.PlayerName ?? "", EpicId = me?.EpicId },
        };
        foreach (var m in profile.Matches.Where(m => m.IsBattleRoyale).OrderBy(m => m.MatchStartUtc ?? m.AnalyzedAtUtc))
        {
            if (m.TeammateChests is { Count: 0 } || m.TeamSize == 1 || ReplayAnalyzer.PartyModeOf(m.Playlist, m.TeamSize) == "solo")
            {
                continue;
            }
            file.Matches.Add(new ShareMatch
            {
                SessionId = m.SessionId,
                MatchStartUtc = m.MatchStartUtc,
                Playlist = m.Playlist,
                TeamSize = m.TeamSize,
                Chests = m.ChestsOpened,
                ChestTimes = m.Chests.Select(c => c.TimeSeconds).ToList(),
                Treasures = m.TreasureFinds.Count,
                Teammates = m.TeammateChests?.SelectMany(t => Hashes(t.EpicId, t.Name)).Distinct().ToList(),
            });
        }
        return file;
    }

    /// <summary>Writes the share file of a profile into <paramref name="folder"/>: its path and how many matches it holds.</summary>
    public static (string Path, int Matches) Export(TrackerData profile, string folder)
    {
        var file = Build(profile);
        var name = new string((string.IsNullOrWhiteSpace(file.Player.Name) ? "player" : file.Player.Name)
            .Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c).ToArray()).Trim();
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, $"{name}-{DateTime.Now:yyyy-MM-dd}{Extension}");
        Store.Write(path, file);
        return (path, file.Matches.Count);
    }

    /// <summary>Where an export goes: the Desktop (easy to drag into Discord), else the data folder.</summary>
    public static string ExportFolder(Store store)
    {
        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        return !string.IsNullOrEmpty(desktop) && Directory.Exists(desktop) ? desktop : store.Folder;
    }
}

/// <summary>
/// Double-clicking a .chesttoken file opens it with the assistant ("--open"), which imports it, or hands it over to
/// the copy already running. Registered for the current Windows user only (HKCU, no admin rights), removable.
/// </summary>
[SupportedOSPlatform("windows")]
public static class ShareFiles
{
    private const string ProgId = "ChestTokenAssistant.Share";
    private const string Classes = @"Software\Classes\";

    /// <summary>Registers (or follows a moved exe). False when not run from the published exe.</summary>
    public static bool Register()
    {
        var exe = AppInfo.ExePath;
        if (exe == null)
        {
            return false;
        }
        try
        {
            var command = $"\"{exe}\" --open \"%1\"";
            using (var existing = Registry.CurrentUser.OpenSubKey(Classes + ProgId + @"\shell\open\command"))
            {
                using var ext0 = Registry.CurrentUser.OpenSubKey(Classes + ShareFormat.Extension);
                if (existing?.GetValue("") as string == command && ext0?.GetValue("") as string == ProgId)
                {
                    return true; // already right: no change, no Explorer refresh
                }
            }
            using (var ext = Registry.CurrentUser.CreateSubKey(Classes + ShareFormat.Extension))
            {
                ext.SetValue("", ProgId);
            }
            using (var prog = Registry.CurrentUser.CreateSubKey(Classes + ProgId))
            {
                prog.SetValue("", $"{AppInfo.Name} share file");
                using var icon = prog.CreateSubKey("DefaultIcon");
                icon.SetValue("", $"\"{exe}\",0");
                using var cmd = prog.CreateSubKey(@"shell\open\command");
                cmd.SetValue("", command);
            }
            SHChangeNotify(SHCNE_ASSOCCHANGED, 0, IntPtr.Zero, IntPtr.Zero);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static bool Unregister()
    {
        try
        {
            using (var ext = Registry.CurrentUser.OpenSubKey(Classes + ShareFormat.Extension))
            {
                var ours = ext?.GetValue("") as string == ProgId;
                ext?.Dispose();
                if (ours)
                {
                    Registry.CurrentUser.DeleteSubKeyTree(Classes + ShareFormat.Extension, throwOnMissingSubKey: false);
                }
            }
            Registry.CurrentUser.DeleteSubKeyTree(Classes + ProgId, throwOnMissingSubKey: false);
            SHChangeNotify(SHCNE_ASSOCCHANGED, 0, IntPtr.Zero, IntPtr.Zero);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private const int SHCNE_ASSOCCHANGED = 0x08000000;
    [DllImport("shell32.dll")] private static extern void SHChangeNotify(int eventId, uint flags, IntPtr item1, IntPtr item2);
}
