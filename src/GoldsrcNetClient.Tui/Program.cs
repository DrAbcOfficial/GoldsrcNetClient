using GoldsrcNetClient.Core;
using GoldsrcNetClient.Core.Game;
using GoldsrcNetClient.Core.Network;
using GoldsrcNetClient.Tui.Services;
using Microsoft.Extensions.DependencyInjection;
using GoldsrcNetClient.Tui.Views;
using Terminal.Gui.App;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace GoldsrcNetClient.Tui;

public static class Program
{
    public static void Main(string[] args)
    {
        // --smoke: headless render test — force the ANSI driver, render one frame, exit.
        bool smoke = args.Contains("--smoke");

        IApplication app = Application.Create();
        if (smoke)
            app.ForceDriver = "ansi";
        app.Init();
        AppHolder.App = app;

        AppData appData = new();
        ServerConfigStore configStore = new();
        configStore.Load();
        UserInfoStore userInfoStore = new();
        userInfoStore.Load();

        // Composition root for the TUI: register the client library, then resolve
        // the connection manager's dependencies from the container. AddGoldsrcClient
        // registers the built-in profiles.
        using ServiceProvider provider = new ServiceCollection()
            .AddGoldsrcClient()
            .BuildServiceProvider();

        ConnectionManager connManager = new(
            provider.GetRequiredService<IGameProfileResolver>(),
            provider.GetRequiredService<IGoldsrcConnectionFactory>());
        ServerBrowser browser = new(configStore);
        browser.Reload();

        MainView mainView = new(appData, connManager, configStore, browser, userInfoStore);

        Window window = new()
        {
            Title = "GoldsrcNetClient",
            Width = Dim.Fill(),
            Height = Dim.Fill()
        };
        window.Add(mainView);

        if (smoke)
        {
            // Render a few frames, then stop; dump the view tree for verification.
            app.StopAfterFirstIteration = false;
            app.AddTimeout(TimeSpan.FromMilliseconds(500), () =>
            {
                app.RequestStop();
                return false;
            });
        }

        app.Run(window);

        if (smoke)
        {
            DumpViewTree(window);
            return;
        }

        connManager.Dispose();
        appData.DisposeProviders();
        app.Dispose();
    }

    /// <summary>
    /// Prints every visible view with its screen position, size, title and text —
    /// a layout-level snapshot for the <c>--smoke</c> headless check.
    /// </summary>
    private static void DumpViewTree(View view, int depth = 0)
    {
        string indent = new(' ', depth * 2);
        string text = view.Text?.Replace("\n", "\\n") ?? "";
        if (text.Length > 60) text = text[..60] + "…";
        Console.WriteLine($"{indent}{view.GetType().Name} [{view.Frame.X},{view.Frame.Y} {view.Frame.Width}x{view.Frame.Height}] {(string.IsNullOrEmpty(view.Title) ? "" : $"Title='{view.Title}'")} {(string.IsNullOrEmpty(text) ? "" : $"Text='{text}'")}");
        foreach (View sub in view.SubViews)
            DumpViewTree(sub, depth + 1);
    }
}
