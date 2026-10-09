using StreamCommand.Services;
using System.Windows;

namespace StreamCommand.Views
{
    /// <summary>Payload passed to ProUpgradeWindow so it can personalize the headline.</summary>
    public class UsageContext
    {
        public int StreamsCompleted     { get; init; }
        public int AutomationFiredCount { get; init; }
        public int ProGateHitCount      { get; init; }
    }

    public partial class ProUpgradeWindow : Window
    {
        public ProUpgradeWindow(UsageContext? ctx = null)
        {
            InitializeComponent();

            // Increment ProGateHitCount every time this window opens
            var s = SettingsService.Load();
            s.ProGateHitCount++;
            SettingsService.Save(s);
            StreamEvents.RaiseUsageUpdated();

            // Personalize headline using the context passed by the caller (or fall back to loaded settings)
            DynamicHeadline.Text = BuildHeadline(ctx ?? new UsageContext
            {
                StreamsCompleted     = s.StreamsCompleted,
                AutomationFiredCount = s.AutomationFiredCount,
                ProGateHitCount      = s.ProGateHitCount,
            });

            // Handle is IntPtr.Zero in the constructor — only valid after OS creates the window.
            SourceInitialized += (_, _) =>
            {
                var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
                Services.SubscriptionManager.Initialize(hwnd);
            };
        }

        private static string BuildHeadline(UsageContext ctx)
        {
            if (ctx.StreamsCompleted >= 5)
                return $"You've streamed {ctx.StreamsCompleted} times — unlock live preview and stop alt-tabbing to check your scene.";
            if (ctx.StreamsCompleted >= 3)
                return "You've got the streaming habit — see your stream live inside the app with Pro.";
            if (ctx.AutomationFiredCount >= 10)
                return $"Your automations have fired {ctx.AutomationFiredCount} times — go unlimited with Pro.";
            if (ctx.AutomationFiredCount >= 1)
                return "Your one free rule is working — Pro unlocks unlimited automation.";
            if (ctx.ProGateHitCount >= 3)
                return "You keep finding Pro features — they're yours for less than a coffee a month.";
            return "Run your whole broadcast from one window — no alt-tabbing, no missed moments.";
        }

        private async void Monthly_Click(object sender, RoutedEventArgs e)
        {
            if (await SubscriptionManager.PurchaseAsync("pro_monthly")) Close();
        }

        private async void Annual_Click(object sender, RoutedEventArgs e)
        {
            if (await SubscriptionManager.PurchaseAsync("pro_annual")) Close();
        }

        private async void Lifetime_Click(object sender, RoutedEventArgs e)
        {
            if (await SubscriptionManager.PurchaseAsync("pro_lifetime")) Close();
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
    }
}
