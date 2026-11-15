using System.Windows;

namespace REA.Desktop.Views;

public partial class DialogWindow : Window
{
    public string? InputValue => InputBox.Text;

    public DialogWindow(string title, string message, string confirmLabel, string? input)
    {
        InitializeComponent();
        Title = title;
        TitleText.Text = title;
        MessageText.Text = message;
        ConfirmButton.Content = confirmLabel;
        if (input is not null)
        {
            InputBox.Visibility = Visibility.Visible;
            InputBox.Text = input;
            Loaded += (_, _) => { InputBox.Focus(); InputBox.SelectAll(); };
        }
    }

    private void OnConfirm(object sender, RoutedEventArgs e) => DialogResult = true;
    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;
}
