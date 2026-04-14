using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using Kitsune7Den.Models;
using Kitsune7Den.ViewModels;

namespace Kitsune7Den.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        // Restore saved window position/size
        var settings = AppSettings.Load();
        if (settings.WindowWidth > 0 && settings.WindowHeight > 0)
        {
            Width = settings.WindowWidth;
            Height = settings.WindowHeight;
        }
        if (settings.WindowLeft >= 0 && settings.WindowTop >= 0)
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = settings.WindowLeft;
            Top = settings.WindowTop;
        }
        if (settings.WindowMaximized)
            WindowState = WindowState.Maximized;

        Closing += OnClosing;
        KeyDown += OnKeyDown;
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        // Persist window position + size
        try
        {
            var settings = AppSettings.Load();
            if (WindowState == WindowState.Normal)
            {
                settings.WindowWidth = Width;
                settings.WindowHeight = Height;
                settings.WindowLeft = Left;
                settings.WindowTop = Top;
            }
            settings.WindowMaximized = WindowState == WindowState.Maximized;
            settings.Save();
        }
        catch { /* best effort */ }
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;

        // Ctrl+S — save config if on the config page
        if (e.Key == Key.S && Keyboard.Modifiers == ModifierKeys.Control)
        {
            if (vm.CurrentView is ConfigViewModel cvm && cvm.SaveCommand.CanExecute(null))
            {
                cvm.SaveCommand.Execute(null);
                e.Handled = true;
            }
        }
        // F5 — refresh the active list view
        else if (e.Key == Key.F5)
        {
            switch (vm.CurrentView)
            {
                case PlayersViewModel pvm:
                    pvm.RefreshCommand.Execute(null);
                    e.Handled = true;
                    break;
                case ModsViewModel mvm:
                    mvm.RefreshCommand.Execute(null);
                    e.Handled = true;
                    break;
                case BackupsViewModel bvm:
                    bvm.RefreshCommand.Execute(null);
                    e.Handled = true;
                    break;
                case LogsViewModel lvm:
                    lvm.RefreshCommand.Execute(null);
                    e.Handled = true;
                    break;
                case ConfigViewModel cvm2:
                    cvm2.LoadCommand.Execute(null);
                    e.Handled = true;
                    break;
            }
        }
    }
}
