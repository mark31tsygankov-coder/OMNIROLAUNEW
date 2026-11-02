using System.Text.Encodings.Web;
using System.Text.Json;

namespace OmniroLauncher;

public static class ConfigStore
{
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static string AppDir =>
        Path.GetDirectoryName(Environment.ProcessPath) ?? AppContext.BaseDirectory;

    private static string ConfigPath => Path.Combine(AppDir, "launcher.config.json");

    private static string SettingsPath
    {
        get
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "OmniroLauncher");
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, "settings.json");
        }
    }

    public static LauncherConfig LoadConfig()
    {
        if (File.Exists(ConfigPath))
        {
            try
            {
                var cfg = JsonSerializer.Deserialize<LauncherConfig>(File.ReadAllText(ConfigPath), JsonOptions);
                if (cfg != null) return cfg;
            }
            catch { /* повреждённый файл — используем значения по умолчанию, но не затираем его */ }
            return new LauncherConfig();
        }

        var def = new LauncherConfig();
        try { File.WriteAllText(ConfigPath, JsonSerializer.Serialize(def, JsonOptions)); } catch { }
        return def;
    }

    public static AppSettings LoadSettings()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var st = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath), JsonOptions);
                if (st != null) return st;
            }
        }
        catch { }
        return new AppSettings();
    }

    public static void SaveSettings(AppSettings st)
    {
        try { File.WriteAllText(SettingsPath, JsonSerializer.Serialize(st, JsonOptions)); } catch { }
    }
}
