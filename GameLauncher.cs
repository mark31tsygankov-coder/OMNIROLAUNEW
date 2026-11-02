using System.Diagnostics;
using Microsoft.Win32;

namespace OmniroLauncher;

public static class GameLauncher
{
    public static Process? Start(LauncherConfig cfg, AppSettings st)
    {
        var exe = Path.Combine(st.GameDir, cfg.GameExe);

        var args = cfg.LaunchArgs
            .Replace("{nick}", st.Nickname)
            .Replace("{ip}", cfg.ServerIp)
            .Replace("{port}", cfg.ServerPort.ToString());
        if (!string.IsNullOrWhiteSpace(st.ExtraArgs))
            args += " " + st.ExtraArgs.Trim();

        ApplyCompatFlags(exe, st);
        SaveNickToRegistry(st.Nickname);

        var psi = new ProcessStartInfo(exe, args)
        {
            WorkingDirectory = st.GameDir,
            UseShellExecute = true
        };
        if (st.RunAsAdmin) psi.Verb = "runas";

        var process = Process.Start(psi);
        if (process != null && st.HighPriority)
        {
            try { process.PriorityClass = ProcessPriorityClass.High; }
            catch { /* нет прав — игра всё равно запущена */ }
        }
        return process;
    }

    /// <summary>Флаги совместимости Windows для exe игры (HKCU, права администратора не нужны).</summary>
    private static void ApplyCompatFlags(string exe, AppSettings st)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(
                @"Software\Microsoft\Windows NT\CurrentVersion\AppCompatFlags\Layers");
            if (key == null) return;

            var flags = new List<string>();
            if (st.DisableFullscreenOpt) flags.Add("DISABLEDXMAXIMIZEDWINDOWEDMODE");
            if (st.HighDpiAware) flags.Add("HIGHDPIAWARE");

            if (flags.Count > 0)
                key.SetValue(exe, "~ " + string.Join(" ", flags), RegistryValueKind.String);
            else if (key.GetValue(exe) != null)
                key.DeleteValue(exe, false);
        }
        catch { }
    }

    /// <summary>SA-MP хранит ник в реестре — пишем туда же, чтобы он совпадал с лаунчером.</summary>
    private static void SaveNickToRegistry(string nick)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(@"Software\SAMP");
            key?.SetValue("PlayerName", nick, RegistryValueKind.String);
        }
        catch { }
    }
}
