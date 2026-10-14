using System.Windows;
using Kitsune7Den.ViewModels;

namespace Kitsune7Den.Views;

public partial class SandboxEditorWindow : Window
{
    public SandboxEditorWindow(SandboxEditorViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        viewModel.CloseRequested += ok =>
        {
            DialogResult = ok;
            Close();
        };
    }
}
