using StreamCommand.Services;
using System.Windows;
using System.Windows.Input;
using Windows.Services.Store;

namespace StreamCommand.Views
{
    /// <summary>Payload passed to ProUpgradeWindow so it can personalize the headline.</summary>
    public class UsageContext
    {
        public int StreamsCompleted     { get; init; }
        public int AutomationFiredCount { get; init; }
        public int ProGateHitCount      { get; init; }
    }

    /// <summary>
    /// The buy screen. Under the paid-app model (v1.0.29+) this sells ONE thing: the full
    /// app licence, $7.99 once, which converts a 7-day Store trial into ownership. There are
    /// no plans and no add-ons, so there is nothing to choose.
    /// </summary>
    public partial class ProUpgradeWindow : Window
    {
        public ProUpgradeWindow(UsageContext? ctx = null)
        {
            InitializeComponent();
            StreamCommand.Services.WindowFit.Apply(this);

            var s = SettingsService.Load();
            s.ProGateHitCount++;
            SettingsService.Save(s);
            StreamEvents.RaiseUsageUpdated();

            DynamicHeadline.Text = BuildHeadline(ctx ?? new UsageContext
            {
                StreamsCompleted     = s.StreamsCompleted,
                AutomationFiredCount = s.AutomationFiredCount,
                ProGateHitCount      = s.ProGateHitCount,
            });

            ShowTrialState();

            // Show the fallback immediately, then the real localized Store price when it lands.
            AppPrice.Text = StorePricingService.FallbackPrice;
            Loaded += async (_, _) => AppPrice.Text = await StorePricingService.GetAppPriceAsync();

            // Handle is IntPtr.Zero in the constructor — only valid once the OS creates the window.
            SourceInitialized += (_, _) =>
            {
                var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
                SubscriptionManager.Initialize(hwnd);
            };
        }

        /// <summary>Shows how much trial is left, when the Store says we're on one.</summary>
        private void ShowTrialState()
        {
            if (!EntitlementService.IsTrial)
            {
                TrialPanel.Visibility = Visibility.Collapsed;
                return;
            }

            var days = EntitlementService.TrialDaysRemaining;
            TrialStatus.Text = days switch
            {
                null  => "You're on the free trial. Buy once to keep Stream Command for good.",
                0     => "Your free trial ends today. Buy once to keep everything you've set up.",
                1     => "1 day left in your free trial. Buy once and it's yours for good.",
                var d => $"{d} days left in your free trial. Buy once and it's yours for good.",
            };
            TrialPanel.Visibility = Visibility.Visible;
        }

        private static string BuildHeadline(UsageContext ctx)
        {
            if (ctx.StreamsCompleted >= 5)
                return $"You've streamed {ctx.StreamsCompleted} times with Stream Command — keep it for good with a single purchase.";
            if (ctx.StreamsCompleted >= 3)
                return "You've got the streaming habit — one purchase keeps every feature, forever.";
            if (ctx.AutomationFiredCount >= 10)
                return $"Your automations have fired {ctx.AutomationFiredCount} times — keep them running with a single purchase.";
            if (ctx.AutomationFiredCount >= 1)
                return "Your automation is already working — buy once and nothing gets taken away.";
            if (ctx.ProGateHitCount >= 3)
                return "One purchase, no subscription — it's yours for less than a couple of coffees.";
            return "Switch scenes, manage audio and go live without ever alt-tabbing out of your game.";
        }

        private async void Upgrade_Click(object sender, RoutedEventArgs e)
        {
            UpgradeBtn.IsEnabled = false;
            PurchaseStatus.Visibility = Visibility.Collapsed;
            try
            {
                var status = await SubscriptionManager.PurchaseAppAsync();
                switch (status)
                {
                    case StorePurchaseStatus.Succeeded:
                    case StorePurchaseStatus.AlreadyPurchased:
                        Close();
                        return;
                    case StorePurchaseStatus.NotPurchased:
                        break;   // buyer closed the Store dialog — not an error
                    case StorePurchaseStatus.NetworkError:
                        ShowStatus("Couldn't reach the Microsoft Store. Check your connection and try again.");
                        break;
                    default:
                        ShowStatus("The Microsoft Store couldn't complete the purchase. You haven't been charged.");
                        break;
                }
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException)
            {
                ShowStatus("Purchases need the Microsoft Store version of Stream Command.");
            }
            finally
            {
                UpgradeBtn.IsEnabled = true;
            }
        }

        private async void Restore_Click(object sender, RoutedEventArgs e)
        {
            RestoreBtn.IsEnabled = false;
            await EntitlementService.RefreshAsync();
            RestoreBtn.IsEnabled = true;
            if (EntitlementService.IsPro && !EntitlementService.IsTrial) { Close(); return; }
            ShowTrialState();
            ShowStatus("No purchase found on this Microsoft account yet.");
        }

        private void ShowStatus(string text)
        {
            PurchaseStatus.Text = text;
            PurchaseStatus.Visibility = Visibility.Visible;
        }

        private void Root_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // Borderless dialog — drag from any empty area, but never steal a click meant for
            // a button (DragMove captures the mouse and the click would be lost).
            for (var d = e.OriginalSource as DependencyObject; d != null;
                 d = d is System.Windows.Media.Visual ? System.Windows.Media.VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d))
                if (d is System.Windows.Controls.Primitives.ButtonBase) return;

            if (e.ButtonState == MouseButtonState.Pressed) DragMove();
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();
    }
}
