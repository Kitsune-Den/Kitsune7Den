using System.Windows.Controls;
using System.Windows.Input;
using Kitsune7Den.ViewModels;

namespace Kitsune7Den.Views;

public partial class ConfigView : UserControl
{
    public ConfigView()
    {
        InitializeComponent();
    }

    private void OnLoaded(object sender, System.Windows.RoutedEventArgs e)
    {
        // A new view is built every time the page is opened. Reload so edits made outside
        // the app show up, but never over unsaved changes.
        if (DataContext is ConfigViewModel { HasChanges: false } vm)
            vm.LoadCommand.Execute(null);
    }

    private void TotalCycleBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        if (sender is not TextBox tb) return;
        if (DataContext is ConfigViewModel vm)
        {
            vm.SetTotalCycleCommand.Execute(tb.Text);
            e.Handled = true;
        }
    }
}
