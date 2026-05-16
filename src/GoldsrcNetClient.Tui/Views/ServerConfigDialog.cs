using System.Collections.ObjectModel;
using GoldsrcNetClient.Tui.Models;
using Terminal.Gui.App;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace GoldsrcNetClient.Tui.Views;

public sealed class ServerConfigDialog : Window
{
    private readonly TextField _nameTf;
    private readonly TextField _hostTf;
    private readonly TextField _portTf;
    private readonly DropDownList _appIdDd;

    private static readonly (string Label, uint AppId)[] AppIds =
    [
        ("Half-Life", 70),
        ("Counter-Strike", 10),
        ("Sven Co-op", 225840),
    ];

    public bool Confirmed { get; private set; }

    public ServerConfigDialog() : this(null) { }

    public ServerConfigDialog(ServerConfig? existing)
    {
        Title = "Server Configuration";
        Width = 50;
        Height = 14;

        Add(new Label { Text = "Name:", X = 1, Y = 1 });
        _nameTf = new TextField { Text = existing?.Name ?? "", X = 12, Y = 1, Width = 35 };
        Add(_nameTf);

        Add(new Label { Text = "Host:", X = 1, Y = 3 });
        _hostTf = new TextField { Text = existing?.Host ?? "127.0.0.1", X = 12, Y = 3, Width = 35 };
        Add(_hostTf);

        Add(new Label { Text = "Port:", X = 1, Y = 5 });
        _portTf = new TextField { Text = (existing?.Port ?? 27015).ToString(), X = 12, Y = 5, Width = 10 };
        Add(_portTf);

        Add(new Label { Text = "AppID:", X = 25, Y = 5 });

        List<string> appIdLabels = AppIds.Select(a => a.Label).ToList();
        _appIdDd = new DropDownList
        {
            X = 32, Y = 5, Width = 15,
            Source = new ListWrapper<string>(new ObservableCollection<string>(appIdLabels)),
            ReadOnly = true,
            Value = existing != null
                ? appIdLabels[Math.Max(0, Array.FindIndex(AppIds, a => a.AppId == existing.AppId))]
                : appIdLabels[0]
        };
        Add(_appIdDd);

        Button okBtn = new Button { Text = "OK", X = Pos.Center() - 10, Y = 8 };
        okBtn.Accepting += (s, e) =>
        {
            Confirmed = true;
            this.RequestStop();
        };

        Button cancelBtn = new Button { Text = "Cancel", X = Pos.Right(okBtn) + 2, Y = 8 };
        cancelBtn.Accepting += (s, e) =>
        {
            Confirmed = false;
            this.RequestStop();
        };

        Add(okBtn, cancelBtn);
    }

    public ServerConfig GetConfig()
    {
        string? selectedLabel = _appIdDd.Value?.ToString();
        int appIdx = Array.FindIndex(AppIds, a => a.Label == selectedLabel);
        uint appId = appIdx >= 0 ? AppIds[appIdx].AppId : 70u;
        return new ServerConfig
        {
            Name = _nameTf.Text ?? "",
            Host = _hostTf.Text ?? "127.0.0.1",
            Port = int.TryParse(_portTf.Text, out int p) ? p : 27015,
            AppId = appId
        };
    }
}
