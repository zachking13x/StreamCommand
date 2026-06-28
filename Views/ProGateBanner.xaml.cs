using System.Windows;
using System.Windows.Controls;

namespace StreamCommand.Views;

public partial class ProGateBanner : UserControl
{
    // ──  FeatureLabel dependency property ────────────────────────────────────
    public static readonly DependencyProperty FeatureLabelProperty =
        DependencyProperty.Register(
            nameof(FeatureLabel),
            typeof(string),
            typeof(ProGateBanner),
            new PropertyMetadata("Pro feature"));

    public string FeatureLabel
    {
        get => (string)GetValue(FeatureLabelProperty);
        set => SetValue(FeatureLabelProperty, value);
    }

    public ProGateBanner()
    {
        InitializeComponent();

        Loaded += (_, _) =>
        {
            var s = Services.SettingsService.Load();
            s.ProGateHitCount++;
            Services.SettingsService.Save(s);
            Services.StreamEvents.RaiseUsageUpdated();
        };
    }

    private void Banner_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        var s = Services.SettingsService.Load();
        var ctx = new UsageContext
        {
            StreamsCompleted     = s.StreamsCompleted,
            AutomationFiredCount = s.AutomationFiredCount,
            ProGateHitCount      = s.ProGateHitCount,
        };
        var win = new ProUpgradeWindow(ctx) { Owner = Window.GetWindow(this) };
        win.ShowDialog();
    }
}
