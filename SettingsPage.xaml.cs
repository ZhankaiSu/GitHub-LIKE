namespace MauiApp1;

public partial class SettingsPage : ContentPage
{
    private bool _isLoading;

    public SettingsPage()
    {
        InitializeComponent();
        LoadSettings();
    }

    private void LoadSettings()
    {
        _isLoading = true;
        IpEntry.Text = Preferences.Get("IP", "192.168.6.6");
        PortEntry.Text = Preferences.Get("Port", "5050");
        _isLoading = false;
    }

    private void SaveSettings(object sender, EventArgs e)
    {
        if (_isLoading) return;
        Preferences.Set("IP", IpEntry.Text ?? "");
        Preferences.Set("Port", PortEntry.Text ?? "");
    }
}