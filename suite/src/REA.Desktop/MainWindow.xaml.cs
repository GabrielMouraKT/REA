using System.Windows;
using REA.Desktop.ViewModels;

namespace REA.Desktop;

public partial class MainWindow : Window
{
    public MainWindow(ShellViewModel shell)
    {
        InitializeComponent();
        DataContext = shell;
    }
}
