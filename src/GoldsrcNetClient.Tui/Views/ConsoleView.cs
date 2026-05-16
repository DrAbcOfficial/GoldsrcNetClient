using GoldsrcNetClient.Tui.Services;
using Terminal.Gui.App;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace GoldsrcNetClient.Tui.Views;

public sealed class ConsoleView : View
{
    private readonly ConnectionManager _connManager;
    private readonly TextView _logTv;
    private readonly Label _statusLbl;

    public ConsoleView(ConnectionManager connManager)
    {
        _connManager = connManager;
        Width = Dim.Fill();
        Height = Dim.Fill();

        _statusLbl = new Label
        {
            Text = "Disconnected",
            X = 1, Y = 0,
            Width = Dim.Fill()
        };

        FrameView logFrame = new FrameView
        {
            Title = "Log",
            X = 1, Y = 2,
            Width = Dim.Fill(1), Height = Dim.Fill(1)
        };
        _logTv = new TextView
        {
            X = 1, Y = 0,
            Width = Dim.Fill(), Height = Dim.Fill(),
            ReadOnly = true
        };
        logFrame.Add(_logTv);
        Add(_statusLbl, logFrame);

        if (AppHolder.App is IApplication app)
        {
            app.AddTimeout(TimeSpan.FromMilliseconds(100), () =>
            {
                FlushLog();
                UpdateStatus();
                return true;
            });
        }
    }

    private void FlushLog()
    {
        while (GlobalLog.TryRead(out string? entry))
        {
            if (entry != null)
                _logTv.Text += entry + "\n";
        }
        _logTv.MoveEnd();
    }

    private void UpdateStatus()
    {
        _statusLbl.Text = _connManager.State switch
        {
            ConnectionState.Disconnected => "Disconnected",
            ConnectionState.Connecting => $"Connecting to {_connManager.CurrentConfig?.Host}:{_connManager.CurrentConfig?.Port}...",
            ConnectionState.Connected => $"Connected to {_connManager.CurrentConfig?.Host}:{_connManager.CurrentConfig?.Port}",
            ConnectionState.Reconnecting => "Reconnecting...",
            _ => ""
        };
    }
}
