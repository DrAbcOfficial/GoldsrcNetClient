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

        AppData appData = new AppData();
        ServerConfigStore configStore = new ServerConfigStore();
        configStore.Load();

        ConnectionManager connManager = new ConnectionManager();
        SettingsView settingsView = new SettingsView(appData);
        ConnectionView connectionView = new ConnectionView(appData, connManager, configStore, settingsView);
        settingsView.LoggedIn += () =>
        {
            settingsView.ApplyUserInfo();
            settingsView.Visible = false;
            connectionView.Visible = true;
        };

        Window window = new Window
        {
            Title = "GoldsrcNetClient TUI",
            Width = Dim.Fill(),
            Height = Dim.Fill()
        };

        View mainView = new View
        {
            Width = Dim.Fill(),
            Height = Dim.Fill()
        };

        settingsView.Visible = true;
        connectionView.Visible = false;

        View tabBar = new View
        {
            X = 0, Y = 0, Width = Dim.Fill(), Height = 2
        };

        Button settingsTabBtn = new Button { Text = "_Settings", X = 1, Y = 0 };
        Button connectionTabBtn = new Button { Text = "_Connection", X = Pos.Right(settingsTabBtn) + 1, Y = 0 };

        settingsTabBtn.Accepting += (s, e) =>
        {
            settingsView.Visible = true;
            connectionView.Visible = false;
            settingsView.FocusFirstField();
        };
        connectionTabBtn.Accepting += (s, e) =>
        {
            settingsView.ApplyUserInfo();
            settingsView.Visible = false;
            connectionView.Visible = true;
        };

        tabBar.Add(settingsTabBtn, connectionTabBtn);

        mainView.Add(settingsView);
        mainView.Add(connectionView);
        window.Add(tabBar);
        window.Add(mainView);

        AppHolder.App = app;

        app.Run(window);

        connManager.Dispose();
        appData.AuthDisposable?.Dispose();
        app.Dispose();
    }
}
