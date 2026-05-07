using StreamCommand.Services;
using System.Windows;

namespace StreamCommand.Views
{
    public partial class ProUpgradeWindow : Window
    {
        public ProUpgradeWindow()
        {
            InitializeComponent();
            var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            Services.SubscriptionManager.Initialize(hwnd);
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
