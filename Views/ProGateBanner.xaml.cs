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
    }

    private void Banner_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        var win = new ProUpgradeWindow { Owner = Window.GetWindow(this) };
        win.ShowDialog();
    }
}
