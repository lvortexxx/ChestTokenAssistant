namespace ChestCounter;

/// <summary>Name, version and names shared by the tool's processes.</summary>
public static class AppInfo
{
    /// <summary>Brand name, the same in every language.</summary>
    public const string Name = "Chest Token Assistant";

    /// <summary>Signature shown in the title bar, the report and the OBS overlay page.</summary>
    public const string Author = "LVortexxx";

    public const string ExeName = "ChestTokenAssistant.exe";

    public static Version Version { get; } = typeof(AppInfo).Assembly.GetName().Version ?? new Version(0, 1, 0);

    public static string VersionText => $"{Version.Major}.{Version.Minor}.{Math.Max(0, Version.Build)}";

    /// <summary>The project this tool is based on (credits). The tool itself never goes online.</summary>
    public const string OriginalRepoUrl = "https://github.com/LED-N/Fortnite-ChestCounter";

    /// <summary>Path of the exe when running as the published exe (null under "dotnet ChestTokenAssistant.dll").</summary>
    public static string? ExePath
    {
        get
        {
            var p = Environment.ProcessPath;
            return p != null && Path.GetFileName(p).Equals(ExeName, StringComparison.OrdinalIgnoreCase) ? p : null;
        }
    }

    // Names of the per-user kernel objects that let the processes find each other. Kept from Chest Counter, the tool
    // this one comes from: an old copy still installed and this one never run at the same time on the same data.
    /// <summary>One instance per data folder (the normal folder, or a test folder given with --data).</summary>
    public static string InstanceMutexFor(string dataDir)
    {
        var hash = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(Path.GetFullPath(dataDir).TrimEnd('\\', '/').ToLowerInvariant()));
        return @"Local\ChestCounter.Instance." + Convert.ToHexString(hash, 0, 8);
    }
    public const string StandbyMutex = @"Local\ChestCounter.Standby";
    public const string StandbyStopEvent = @"Local\ChestCounter.StandbyStop";
}
