using System.Windows;
using BlogStudio.Presentation;

namespace BlogStudio.Windows;

public partial class SettingsWindow : Window
{
    public SettingsWindow(SettingsViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
        vm.Saved += (_, _) => DialogResult = true;
    }
}
