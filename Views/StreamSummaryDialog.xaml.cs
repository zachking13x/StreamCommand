using System;
using System.Windows;

namespace StreamCommand.Views;

public partial class StreamSummaryDialog : Window
{
    public StreamSummaryDialog(TimeSpan duration, int automationsFired, int streamsCompleted)
    {
        InitializeComponent();

        DurationText.Text    = duration.TotalHours >= 1
            ? $"{(int)duration.TotalHours}h {duration.Minutes}m"
            : $"{duration.Minutes}m";
        AutomationsText.Text = automationsFired.ToString();
        SubHeadline.Text     = streamsCompleted == 1
            ? "That was your first stream with StreamCommand!"
            : $"Stream #{streamsCompleted} in the books.";
    }

    private void ViewAnalytics_Click(object sender, RoutedEventArgs e)
    {
        Close();
        MainWindow.NavigateTo?.Invoke("analytics");
    }

    private void Done_Click(object sender, RoutedEventArgs e) => Close();
}
