using System.Reflection;
using System.Windows;
using System.Windows.Markup;
using Kitsune7Den.Localization;
using Microsoft.Extensions.DependencyInjection;
using Kitsune7Den.Models;
using Kitsune7Den.Services;
using Kitsune7Den.ViewModels;
using Kitsune7Den.Views;

namespace Kitsune7Den;

public partial class App : Application
{
    /// <summary>
    /// Single source of truth for display version, read from the assembly (set via csproj Version).
    /// </summary>
    public static string AppVersion { get; } =
        $"v{Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0"}";

    private ServiceProvider? _serviceProvider;

    private void OnStartup(object sender, StartupEventArgs e)
    {
        // Clean up any leftover *.old exe from a previous self-update
        UpdateService.CleanupStaleOldExe();

        var services = new ServiceCollection();

        // Settings
        var settings = AppSettings.Load();
        services.AddSingleton(settings);

        // Language before any view is built, so the first frame is already translated
        Loc.Instance.SetLanguage(settings.Language);

        // Services
        services.AddSingleton<ServerProcessService>();
        services.AddSingleton<TelnetService>();
        services.AddSingleton<LogWatcherService>();
        services.AddSingleton<ConfigService>();
        services.AddSingleton<ModManagerService>();
        services.AddSingleton<SteamCmdService>();
        services.AddSingleton<AdminService>();
        services.AddSingleton<ThemeService>();
        services.AddSingleton<BackupService>();
        services.AddSingleton<UpdateService>();

        // ViewModels
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<DashboardViewModel>();
        services.AddSingleton<ConsoleViewModel>();
        services.AddSingleton<PlayersViewModel>();
        services.AddSingleton<ConfigViewModel>();
        services.AddSingleton<ModsViewModel>();
        services.AddSingleton<SettingsViewModel>();
        services.AddSingleton<BackupsViewModel>();
        services.AddSingleton<LogsViewModel>();

        // Window
        services.AddSingleton<MainWindow>();

        _serviceProvider = services.BuildServiceProvider();

        // Apply saved theme
        var themeService = _serviceProvider.GetRequiredService<ThemeService>();
        if (settings.Theme != "Kitsune")
            themeService.ApplyTheme(settings.Theme);

        var mainWindow = _serviceProvider.GetRequiredService<MainWindow>();
        mainWindow.DataContext = _serviceProvider.GetRequiredService<MainViewModel>();

        // xml:lang steers WPF's font fallback — without it Chinese text can pick up
        // Japanese glyph variants from whatever CJK font the fallback lands on.
        ApplyXmlLanguage(mainWindow);
        Loc.LanguageChanged += () => ApplyXmlLanguage(mainWindow);

        mainWindow.Show();
    }

    private static void ApplyXmlLanguage(FrameworkElement root) =>
        root.Language = XmlLanguage.GetLanguage(Loc.Instance.Culture.IetfLanguageTag);

    protected override void OnExit(ExitEventArgs e)
    {
        if (_serviceProvider is IDisposable disposable)
            disposable.Dispose();
        base.OnExit(e);
    }
}
