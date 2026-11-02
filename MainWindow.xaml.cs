using System.ComponentModel;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;

namespace OmniroLauncher;

public partial class MainWindow : Window
{
    private static readonly Regex NickRegex = new(@"^[A-Za-z0-9_\[\]\.\$=@]{3,20}$", RegexOptions.Compiled);

    private readonly LauncherConfig _cfg;
    private readonly AppSettings _st;
    private readonly UpdateService _upd;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(30) };

    private Manifest? _manifest;
    private LauncherInfo? _newLauncher;
    private CancellationTokenSource? _cts;
    private bool _busy;

    public MainWindow()
    {
        InitializeComponent();

        _cfg = ConfigStore.LoadConfig();
        _st = ConfigStore.LoadSettings();
        _upd = new UpdateService(_cfg.ManifestUrl);

        if (string.IsNullOrWhiteSpace(_st.GameDir))
            _st.GameDir = Path.Combine(ConfigStore.AppDir, "game");

        ApplySettingsToUi();
        ServerAddrText.Text = $"{_cfg.ServerIp}:{_cfg.ServerPort}";
        VersionText.Text = "Версия " + UpdateService.CurrentVersion.ToString(3);

        _timer.Tick += async (_, _) => await RefreshStatusAsync();
        Loaded += async (_, _) => await OnLoadedAsync();
    }

    // ───────────── Запуск ─────────────

    private async Task OnLoadedAsync()
    {
        await RefreshStatusAsync();
        _timer.Start();

        if (string.IsNullOrWhiteSpace(_cfg.ManifestUrl))
        {
            NewsEmpty.Text = "Адрес обновлений не указан в launcher.config.json (manifestUrl).";
            return;
        }
        if (_st.CheckUpdates)
            await LoadManifestInfoAsync();
    }

    private async Task LoadManifestInfoAsync()
    {
        try
        {
            _manifest = await _upd.GetManifestAsync();
            NewsList.ItemsSource = _manifest.News;
            NewsEmpty.Visibility = _manifest.News.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

            var l = _manifest.Launcher;
            if (l != null && Version.TryParse(l.Version, out var v) &&
                v > UpdateService.CurrentVersion && !string.IsNullOrWhiteSpace(l.Url))
            {
                _newLauncher = l;
                UpdateText.Text = $"Доступна новая версия лаунчера: {l.Version}";
                UpdateBanner.Visibility = Visibility.Visible;
            }
        }
        catch (Exception ex)
        {
            ProgressText.Text = "Не удалось проверить обновления: " + ex.Message;
        }
    }

    private async Task RefreshStatusAsync()
    {
        var info = await SampQuery.QueryAsync(_cfg.ServerIp, _cfg.ServerPort);
        if (info != null)
        {
            StatusDot.Fill = new SolidColorBrush(Color.FromRgb(0x2E, 0xCC, 0x71));
            StatusText.Text = $"Онлайн: {info.Players} / {info.MaxPlayers}";
            PingText.Text = $"Пинг: {info.PingMs} мс";
        }
        else
        {
            StatusDot.Fill = new SolidColorBrush(Color.FromRgb(0xF3, 0x9C, 0x12));
            StatusText.Text = "Статус сервера недоступен";
            PingText.Text = "";
        }
    }

    // ───────────── Кнопка ИГРАТЬ ─────────────

    private async void Play_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;

        var nick = NickBox.Text.Trim();
        if (!NickRegex.IsMatch(nick))
        {
            Warn("Ник должен быть от 3 до 20 символов: латинские буквы, цифры и символы _ [ ] . $ = @");
            NickBox.Focus();
            return;
        }

        CollectSettings();
        SetBusy(true);
        try
        {
            if (!string.IsNullOrWhiteSpace(_cfg.ManifestUrl))
            {
                if (!await SyncFilesAsync()) return;
            }

            var exe = Path.Combine(_st.GameDir, _cfg.GameExe);
            if (!File.Exists(exe))
            {
                Error($"Не найден файл {_cfg.GameExe} в папке игры:\n{_st.GameDir}\n\n" +
                      "Укажите правильную папку в настройках или настройте обновления (manifestUrl).");
                return;
            }

            ProgressText.Text = "Запуск игры…";
            var process = GameLauncher.Start(_cfg, _st);
            if (process == null)
            {
                Error("Не удалось запустить игру.");
                return;
            }

            ProgressText.Text = "Игра запущена. Приятной игры!";
            if (_st.CloseOnLaunch) Close();
            else WindowState = WindowState.Minimized;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            ProgressText.Text = "Запуск отменён (нет подтверждения прав администратора).";
        }
        catch (Exception ex)
        {
            Error("Ошибка запуска: " + ex.Message);
        }
        finally
        {
            SetBusy(false);
        }
    }

    /// <summary>Проверяет файлы по манифесту и докачивает отличающиеся. true — можно запускать игру.</summary>
    private async Task<bool> SyncFilesAsync()
    {
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        var progress = new Progress<ProgressInfo>(OnProgress);

        try
        {
            ProgressText.Text = "Получение списка файлов…";
            _manifest = await _upd.GetManifestAsync(ct);
        }
        catch (OperationCanceledException)
        {
            ProgressText.Text = "Отменено.";
            return false;
        }
        catch (Exception ex)
        {
            var r = MessageBox.Show(this,
                $"Не удалось проверить файлы игры:\n{ex.Message}\n\nЗапустить игру без проверки?",
                "OMNIRO ONLINE", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            return r == MessageBoxResult.Yes;
        }

        try
        {
            Directory.CreateDirectory(_st.GameDir);
            var bad = await _upd.VerifyAsync(_manifest, _st.GameDir, progress, ct);
            if (bad.Count > 0)
                await _upd.DownloadAsync(bad, _st.GameDir, progress, ct);

            ProgressBarMain.Value = 100;
            ProgressText.Text = bad.Count > 0 ? $"Обновлено файлов: {bad.Count}" : "Все файлы актуальны";
            SpeedText.Text = "";
            return true;
        }
        catch (OperationCanceledException)
        {
            ProgressText.Text = "Отменено.";
            return false;
        }
        catch (Exception ex)
        {
            Error("Ошибка обновления файлов: " + ex.Message);
            return false;
        }
    }

    private void OnProgress(ProgressInfo pi)
    {
        ProgressText.Text = pi.Text;
        ProgressBarMain.Value = pi.Percent;
        SpeedText.Text = pi.SpeedBytes > 0 ? $"{pi.SpeedBytes / 1048576.0:F1} МБ/с" : "";
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        PlayButton.IsEnabled = !busy;
        CancelButton.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        if (!busy) { SpeedText.Text = ""; }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => _cts?.Cancel();

    // ───────────── Обновление лаунчера ─────────────

    private async void UpdateLauncher_Click(object sender, RoutedEventArgs e)
    {
        if (_newLauncher == null || _busy) return;
        SetBusy(true);
        try
        {
            _cts = new CancellationTokenSource();
            var path = await _upd.DownloadLauncherAsync(_newLauncher, new Progress<ProgressInfo>(OnProgress), _cts.Token);
            ProgressText.Text = "Устанавливаю обновление…";
            UpdateService.ApplyAndRestart(path);
            Application.Current.Shutdown();
        }
        catch (OperationCanceledException)
        {
            ProgressText.Text = "Отменено.";
            SetBusy(false);
        }
        catch (Exception ex)
        {
            Error("Не удалось обновить лаунчер: " + ex.Message);
            SetBusy(false);
        }
    }

    // ───────────── Настройки ─────────────

    private void ApplySettingsToUi()
    {
        NickBox.Text = _st.Nickname;
        GameDirBox.Text = _st.GameDir;
        ExtraArgsBox.Text = _st.ExtraArgs;
        ChkPriority.IsChecked = _st.HighPriority;
        ChkFullscreen.IsChecked = _st.DisableFullscreenOpt;
        ChkDpi.IsChecked = _st.HighDpiAware;
        ChkAdmin.IsChecked = _st.RunAsAdmin;
        ChkUpdates.IsChecked = _st.CheckUpdates;
        ChkClose.IsChecked = _st.CloseOnLaunch;
    }

    private void CollectSettings()
    {
        _st.Nickname = NickBox.Text.Trim();
        _st.GameDir = GameDirBox.Text.Trim();
        _st.ExtraArgs = ExtraArgsBox.Text.Trim();
        _st.HighPriority = ChkPriority.IsChecked == true;
        _st.DisableFullscreenOpt = ChkFullscreen.IsChecked == true;
        _st.HighDpiAware = ChkDpi.IsChecked == true;
        _st.RunAsAdmin = ChkAdmin.IsChecked == true;
        _st.CheckUpdates = ChkUpdates.IsChecked == true;
        _st.CloseOnLaunch = ChkClose.IsChecked == true;
        ConfigStore.SaveSettings(_st);
    }

    private void Setting_Changed(object sender, RoutedEventArgs e) => CollectSettings();

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog { Title = "Выберите папку с игрой" };
        if (Directory.Exists(GameDirBox.Text)) dlg.InitialDirectory = GameDirBox.Text;
        if (dlg.ShowDialog(this) == true)
        {
            GameDirBox.Text = dlg.FolderName;
            CollectSettings();
        }
    }

    private async void Repair_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        if (string.IsNullOrWhiteSpace(_cfg.ManifestUrl))
        {
            Warn("Для проверки файлов укажите manifestUrl в launcher.config.json.");
            return;
        }

        CollectSettings();
        NavHome.IsChecked = true;
        SetBusy(true);
        try { await SyncFilesAsync(); }
        finally { SetBusy(false); }
    }

    private void OpenGameDir_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(_st.GameDir);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{_st.GameDir}\"") { UseShellExecute = true });
        }
        catch (Exception ex) { Error(ex.Message); }
    }

    // ───────────── Навигация и окно ─────────────

    private void Nav_Checked(object sender, RoutedEventArgs e)
    {
        // событие приходит ещё во время InitializeComponent — страницы могут быть не созданы
        if (HomePage == null || NewsPage == null || SettingsPage == null) return;

        var tag = (sender as RadioButton)?.Tag as string;
        HomePage.Visibility = tag == "Home" ? Visibility.Visible : Visibility.Collapsed;
        NewsPage.Visibility = tag == "News" ? Visibility.Visible : Visibility.Collapsed;
        SettingsPage.Visibility = tag == "Settings" ? Visibility.Visible : Visibility.Collapsed;
    }

    private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left) DragMove();
    }

    private void Min_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    protected override void OnClosing(CancelEventArgs e)
    {
        _timer.Stop();
        _cts?.Cancel();
        CollectSettings();
        base.OnClosing(e);
    }

    private void Site_Click(object sender, RoutedEventArgs e) => OpenUrl(_cfg.SiteUrl);
    private void Forum_Click(object sender, RoutedEventArgs e) => OpenUrl(_cfg.ForumUrl);
    private void Telegram_Click(object sender, RoutedEventArgs e) => OpenUrl(_cfg.TelegramUrl);

    private static void OpenUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return;
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch { }
    }

    private void Warn(string text) =>
        MessageBox.Show(this, text, "OMNIRO ONLINE", MessageBoxButton.OK, MessageBoxImage.Warning);

    private void Error(string text) =>
        MessageBox.Show(this, text, "OMNIRO ONLINE", MessageBoxButton.OK, MessageBoxImage.Error);
}
