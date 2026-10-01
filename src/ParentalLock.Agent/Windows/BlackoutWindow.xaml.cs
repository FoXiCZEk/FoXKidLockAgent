using System.Windows;

namespace ParentalLock.Agent.Windows;
public partial class BlackoutWindow : Window { public BlackoutWindow() => InitializeComponent(); protected override void OnDeactivated(EventArgs e) { base.OnDeactivated(e); if (IsVisible) Activate(); } }
