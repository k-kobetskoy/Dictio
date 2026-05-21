using System.Windows;

namespace Dictio.Views;

public partial class ErrorPopup : Window
{
    public ErrorPopup(string message)
    {
        InitializeComponent();
        MessageText.Text = message;
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
