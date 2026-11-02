namespace OmniroLauncher;

/// <summary>Настройки сервера и ссылок. Лежит рядом с exe в launcher.config.json.</summary>
public class LauncherConfig
{
    public string ServerName { get; set; } = "OMNIRO ONLINE";
    public string ServerIp { get; set; } = "188.127.241.74";
    public int ServerPort { get; set; } = 4337;
    public string GameExe { get; set; } = "samp.exe";
    public string LaunchArgs { get; set; } = "-c -n {nick} -h {ip} -p {port}";
    public string ManifestUrl { get; set; } = "";
    public string SiteUrl { get; set; } = "https://omniro-online.gt.tc/?i=1";
    public string ForumUrl { get; set; } = "https://omniroonline-forum.gt.tc/?page=home";
    public string TelegramUrl { get; set; } = "https://t.me/omniro_news";
}

/// <summary>Пользовательские настройки (хранятся в %AppData%\OmniroLauncher\settings.json).</summary>
public class AppSettings
{
    public string Nickname { get; set; } = "";
    public string GameDir { get; set; } = "";
    public bool HighPriority { get; set; } = true;
    public bool DisableFullscreenOpt { get; set; } = true;
    public bool HighDpiAware { get; set; } = true;
    public bool RunAsAdmin { get; set; } = false;
    public bool CloseOnLaunch { get; set; } = false;
    public bool CheckUpdates { get; set; } = true;
    public string ExtraArgs { get; set; } = "";
}

public class LauncherInfo
{
    public string Version { get; set; } = "";
    public string Url { get; set; } = "";
    public string Sha256 { get; set; } = "";
}

public class FileEntry
{
    public string Path { get; set; } = "";
    public string Url { get; set; } = "";
    public string Sha256 { get; set; } = "";
    public long Size { get; set; }
}

public class NewsItem
{
    public string Title { get; set; } = "";
    public string Date { get; set; } = "";
    public string Text { get; set; } = "";
}

public class Manifest
{
    public LauncherInfo? Launcher { get; set; }
    public List<FileEntry> Files { get; set; } = new();
    public List<NewsItem> News { get; set; } = new();
}

public record ProgressInfo(string Text, double Percent, double SpeedBytes);

public record ServerInfo(int Players, int MaxPlayers, string Name, int PingMs);
