using System.Text;
using System.Windows;

namespace OmniroLauncher;

public partial class App : Application
{
    private Mutex? _mutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        _mutex = new Mutex(true, "OmniroOnlineLauncher_SingleInstance", out bool created);
        if (!created)
        {
            MessageBox.Show("Лаунчер уже запущен.", "OMNIRO ONLINE",
                MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        // Нужна для кодировки Windows-1251 (имя сервера в ответе query).
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        base.OnStartup(e);
        new MainWindow().Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try { _mutex?.ReleaseMutex(); } catch { }
        _mutex?.Dispose();
        base.OnExit(e);
    }
}
