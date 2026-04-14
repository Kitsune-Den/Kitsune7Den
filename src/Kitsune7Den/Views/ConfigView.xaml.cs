using System.Windows.Controls;
using System.Windows.Input;
using Kitsune7Den.ViewModels;

namespace Kitsune7Den.Views;

public partial class ConfigView : UserControl
{
    private bool _loaded;

    public ConfigView()
    {
        InitializeComponent();
    }

    private void OnLoaded(object sender, System.Windows.RoutedEventArgs e)
    {
        if (!_loaded && DataContext is ConfigViewModel vm)
        {
            vm.LoadCommand.Execute(null);
            _loaded = true;
        }
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
