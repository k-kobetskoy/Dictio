using System.Windows;

namespace Dictio.Views;

public partial class WelcomeWindow : Window
{
    public WelcomeWindow()
    {
        InitializeComponent();
    }

    private void GotIt_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }
}
