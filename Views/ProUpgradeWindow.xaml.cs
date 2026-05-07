using StreamCommand.Services;
using System.Windows;

namespace StreamCommand.Views
{
    public partial class ProUpgradeWindow : Window
    {
        public ProUpgradeWindow()
        {
            InitializeComponent();
            // Handle is IntPtr.Zero in the constructor — it only exists after the OS creates the window.
            // SourceInitialized fires synchronously during Show/ShowDialog, before any content is visible.
            SourceInitialized += (_, _) =>
            {
                var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
                Services.SubscriptionManager.Initialize(hwnd);
            };
        }

        private async void Monthly_Click(object sender, RoutedEventArgs e)
        {
            await SubscriptionManager.PurchaseAsync("pro_monthly");
            Close();
        }

        private async void Annual_Click(object sender, RoutedEventArgs e)
        {
            await SubscriptionManager.PurchaseAsync("pro_annual");
            Close();
        }

        private async void Lifetime_Click(object sender, RoutedEventArgs e)
        {
            await SubscriptionManager.PurchaseAsync("pro_lifetime");
            Close();
        }


        private void Close_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
    }
}
