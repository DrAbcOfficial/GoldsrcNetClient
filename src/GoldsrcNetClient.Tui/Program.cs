using GoldsrcNetClient.Tui.Services;
using GoldsrcNetClient.Tui.Views;
using Terminal.Gui.App;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace GoldsrcNetClient.Tui;

public static class Program
{
    public static void Main(string[] args)
    {
        IApplication app = Application.Create();
        app.Init();
        AppHolder.App = app;

        AppData appData = new AppData();
        ServerConfigStore configStore = new ServerConfigStore();
        configStore.Load();

        ConnectionManager connManager = new ConnectionManager();
        UserInfoStore userInfoStore = new UserInfoStore();
        SettingsView settingsView = new SettingsView(appData, userInfoStore);
        ConnectionView connectionView = new ConnectionView(appData, connManager, configStore, settingsView);
        ConsoleView consoleView = new ConsoleView(connManager);

        Window window = new Window
        {
            Title = "GoldsrcNetClient TUI",
            Width = Dim.Fill(),
            Height = Dim.Fill()
        };

        settingsView.Title = "Settings";
        connectionView.Title = "Connection";
        consoleView.Title = "Console";

        Tabs tabs = new()
        {
            X = 0, Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill()
        };

        tabs.Add(settingsView);
        tabs.Add(connectionView);
        tabs.Add(consoleView);
        tabs.Value = settingsView;

        tabs.ValueChanged += (s, e) =>
        {
            if (e.NewValue == settingsView)
                settingsView.FocusFirstField();
            else if (e.NewValue != null)
            {
                settingsView.ApplyUserInfo();
                connectionView.SyncUserInfoFromSettings();
                connectionView.UpdateSteamInfo();
            }
        };

        settingsView.LoggedIn += () =>
        {
            settingsView.ApplyUserInfo();
            tabs.Value = connectionView;
        };

        window.Add(tabs);

        app.Run(window);

        connManager.Dispose();
        appData.AuthDisposable?.Dispose();
        app.Dispose();
    }
}
