using System.Windows;
using System.Windows.Controls;
using StreamCommand.Services;

namespace StreamCommand.Views;

public partial class ToolsHubView : UserControl
{
    public ToolsHubView() => InitializeComponent();

    private void Tool_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string url })
            AppLaunchService.OpenUrl(url);
    }
}
