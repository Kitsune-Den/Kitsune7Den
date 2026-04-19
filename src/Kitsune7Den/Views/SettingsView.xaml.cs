using System.Diagnostics;
using System.Windows.Controls;
using System.Windows.Navigation;
using Kitsune7Den.ViewModels;

namespace Kitsune7Den.Views;

public partial class SettingsView : UserControl
{
    public SettingsView()
    {
        InitializeComponent();
    }

    // WPF PasswordBox doesn't support binding, so we wire it manually
    private void TelnetPasswordBox_Loaded(object sender, System.Windows.RoutedEventArgs e)
    {
        if (DataContext is SettingsViewModel vm && sender is PasswordBox pb)
        {
            pb.Password = vm.TelnetPassword;
        }
    }

    private void TelnetPasswordBox_Changed(object sender, System.Windows.RoutedEventArgs e)
    {
        if (DataContext is SettingsViewModel vm && sender is PasswordBox pb)
        {
            vm.TelnetPassword = pb.Password;
        }
    }

    private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        e.Handled = true;
    }

    private void KofiButton_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo("https://ko-fi.com/T6T57VRO7") { UseShellExecute = true });
        }
        catch { /* best effort — browser just won't open */ }
    }
}
