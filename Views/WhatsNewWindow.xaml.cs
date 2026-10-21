using System.Windows;

namespace StreamCommand.Views;

public partial class WhatsNewWindow : Window
{
    public WhatsNewWindow()
    {
        InitializeComponent();
        StreamCommand.Services.WindowFit.Apply(this);
    }

    private void Close_Click(object sender, RoutedEventArgs e)
        => Close();
}
