using Microsoft.Web.WebView2.Core;
using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace FoXKidLockAgent.Windows;

public partial class KioskWindow : Window
{
    private readonly string _serverUrl;
    public KioskWindow(string serverUrl) { InitializeComponent(); _serverUrl = serverUrl; Loaded += OnLoaded; }
    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            var profile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ParentalLockPC", "WebViewData");
            var environment = await CoreWebView2Environment.CreateAsync(null, profile);
            await WebView.EnsureCoreWebView2Async(environment);
            WebView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
            WebView.CoreWebView2.Settings.AreDevToolsEnabled = false;
            WebView.CoreWebView2.Settings.IsZoomControlEnabled = false;
            WebView.CoreWebView2.Navigate($"{_serverUrl.TrimEnd('/')}/?mode=child");
        }
        catch { Content = new TextBlock { Text = "Nelze načíst výukovou stránku. Probíhá čekání na připojení.", Foreground = System.Windows.Media.Brushes.White, FontSize = 24, HorizontalAlignment = System.Windows.HorizontalAlignment.Center, VerticalAlignment = System.Windows.VerticalAlignment.Center }; }
    }
    protected override void OnDeactivated(EventArgs e) { base.OnDeactivated(e); if (IsVisible) Activate(); }
}
