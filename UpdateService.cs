using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace OmniroLauncher;

public class UpdateService
{
    private static readonly HttpClient Http = CreateClient();
    private readonly string _manifestUrl;

    public UpdateService(string manifestUrl) => _manifestUrl = manifestUrl;

    public static Version CurrentVersion =>
        Assembly.GetExecutingAssembly().GetName().Version ?? new Version(1, 0, 0);

    private static HttpClient CreateClient()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromHours(2) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd("OmniroLauncher/1.0");
        return c;
    }

    public async Task<Manifest> GetManifestAsync(CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_manifestUrl))
            throw new InvalidOperationException("Адрес обновлений (manifestUrl) не указан в launcher.config.json");

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(20));

        var url = _manifestUrl + (_manifestUrl.Contains('?') ? "&" : "?") + "t=" +
                  DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var json = await Http.GetStringAsync(url, cts.Token);
        try
        {
            return JsonSerializer.Deserialize<Manifest>(json, ConfigStore.JsonOptions)
                   ?? throw new InvalidDataException("Пустой манифест обновлений");
        }
        catch (JsonException)
        {
            throw new InvalidDataException(
                "Сервер обновлений вернул не JSON (возможно, хостинг блокирует лаунчер как бота). " +
                "Разместите manifest.json на GitHub или на своём VPS.");
        }
    }

    /// <summary>Проверяет все файлы манифеста и возвращает те, что отсутствуют или отличаются.</summary>
    public Task<List<FileEntry>> VerifyAsync(Manifest m, string dir, IProgress<ProgressInfo> p, CancellationToken ct)
    {
        return Task.Run(() =>
        {
            var bad = new List<FileEntry>();
            for (int i = 0; i < m.Files.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                var f = m.Files[i];
                var full = SafePath(dir, f.Path);
                p.Report(new ProgressInfo($"Проверка файлов: {i + 1} из {m.Files.Count}",
                    (i + 1) * 100.0 / m.Files.Count, 0));

                if (!File.Exists(full)) { bad.Add(f); continue; }
                if (f.Size > 0 && new FileInfo(full).Length != f.Size) { bad.Add(f); continue; }
                if (!HashMatches(full, f.Sha256)) bad.Add(f);
            }
            return bad;
        }, ct);
    }

    public async Task DownloadAsync(List<FileEntry> files, string dir, IProgress<ProgressInfo> p, CancellationToken ct)
    {
        long total = files.Sum(f => Math.Max(f.Size, 0));
        long done = 0;
        var sw = Stopwatch.StartNew();
        var last = Stopwatch.StartNew();

        for (int i = 0; i < files.Count; i++)
        {
            var f = files[i];
            var full = SafePath(dir, f.Path);
            var tmp = full + ".part";
            long before = done;
            int index = i;

            for (int attempt = 1; ; attempt++)
            {
                try
                {
                    await DownloadFileAsync(f.Url, tmp, n =>
                    {
                        done += n;
                        if (last.ElapsedMilliseconds < 120) return;
                        last.Restart();
                        var pct = total > 0 ? done * 100.0 / total : (index + 1) * 100.0 / files.Count;
                        p.Report(new ProgressInfo(
                            $"Загрузка {index + 1} из {files.Count}: {Path.GetFileName(full)}",
                            Math.Min(pct, 100),
                            done / Math.Max(sw.Elapsed.TotalSeconds, 0.2)));
                    }, ct);

                    if (!string.IsNullOrEmpty(f.Sha256) && !HashMatches(tmp, f.Sha256))
                        throw new InvalidDataException($"Контрольная сумма файла {f.Path} не совпала");

                    File.Move(tmp, full, true);
                    break;
                }
                catch (Exception) when (attempt < 3 && !ct.IsCancellationRequested)
                {
                    done = before;
                    await Task.Delay(1000, ct);
                }
            }
        }

        p.Report(new ProgressInfo("Файлы актуальны", 100, 0));
    }

    public async Task<string> DownloadLauncherAsync(LauncherInfo info, IProgress<ProgressInfo> p, CancellationToken ct)
    {
        var tmp = Path.Combine(Path.GetTempPath(), "OmniroLauncher.new.exe");
        long done = 0;
        var sw = Stopwatch.StartNew();
        var last = Stopwatch.StartNew();

        await DownloadFileAsync(info.Url, tmp, n =>
        {
            done += n;
            if (last.ElapsedMilliseconds < 150) return;
            last.Restart();
            p.Report(new ProgressInfo($"Загрузка обновления лаунчера: {done / 1048576.0:F1} МБ", 0,
                done / Math.Max(sw.Elapsed.TotalSeconds, 0.2)));
        }, ct);

        if (!string.IsNullOrEmpty(info.Sha256) && !HashMatches(tmp, info.Sha256))
        {
            try { File.Delete(tmp); } catch { }
            throw new InvalidDataException("Контрольная сумма обновления лаунчера не совпала");
        }
        return tmp;
    }

    /// <summary>Подменяет exe после закрытия лаунчера и запускает новую версию.</summary>
    public static void ApplyAndRestart(string newExe)
    {
        var current = Environment.ProcessPath!;
        var bat = Path.Combine(Path.GetTempPath(), "omniro_update.bat");
        var script = $"""
@echo off
chcp 65001 >nul
set /a n=0
:retry
timeout /t 1 /nobreak >nul
copy /y "{newExe}" "{current}" >nul 2>&1
if errorlevel 1 (
  set /a n+=1
  if %n% lss 30 goto retry
  exit /b 1
)
del "{newExe}" >nul 2>&1
start "" "{current}"
del "%~f0"
""";
        File.WriteAllText(bat, script, new UTF8Encoding(false));
        Process.Start(new ProcessStartInfo("cmd.exe", $"/c \"{bat}\"")
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            WindowStyle = ProcessWindowStyle.Hidden
        });
    }

    private static async Task DownloadFileAsync(string url, string dest, Action<int> onBytes, CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
        using var resp = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        resp.EnsureSuccessStatusCode();
        await using var src = await resp.Content.ReadAsStreamAsync(ct);
        await using var dst = new FileStream(dest, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);
        var buf = new byte[81920];
        int n;
        while ((n = await src.ReadAsync(buf, ct)) > 0)
        {
            await dst.WriteAsync(buf.AsMemory(0, n), ct);
            onBytes(n);
        }
    }

    private static bool HashMatches(string file, string expected)
    {
        if (string.IsNullOrWhiteSpace(expected)) return true;
        using var fs = File.OpenRead(file);
        var hash = Convert.ToHexString(SHA256.HashData(fs));
        return hash.Equals(expected.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Защита от выхода за пределы папки игры (../ в путях манифеста).</summary>
    private static string SafePath(string dir, string rel)
    {
        var root = Path.GetFullPath(dir).TrimEnd(Path.DirectorySeparatorChar);
        var full = Path.GetFullPath(Path.Combine(root, rel.Replace('/', Path.DirectorySeparatorChar)));
        if (!full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Недопустимый путь в манифесте: {rel}");
        return full;
    }
}
