using Terminal.Gui.App;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace GoldsrcNetClient.Tui.Views;

public sealed class MoveEditorDialog : Window
{
    private readonly CheckBox _attackCb;
    private readonly CheckBox _jumpCb;
    private readonly CheckBox _duckCb;
    private readonly CheckBox _forwardCb;
    private readonly CheckBox _backCb;
    private readonly CheckBox _moveLeftCb;
    private readonly CheckBox _moveRightCb;
    private readonly TextField _forwardMoveTf;
    private readonly TextField _sideMoveTf;
    private readonly TextField _upMoveTf;
    private readonly TextField _impulseTf;

    public bool Confirmed { get; private set; }
    public short ForwardMove => short.TryParse(_forwardMoveTf.Text, out short v) ? v : (short)0;
    public short SideMove => short.TryParse(_sideMoveTf.Text, out short v) ? v : (short)0;
    public short UpMove => short.TryParse(_upMoveTf.Text, out short v) ? v : (short)0;
    public ushort Buttons
    {
        get
        {
            ushort b = 0;
            if (_attackCb.Value == CheckState.Checked) b |= 1;
            if (_jumpCb.Value == CheckState.Checked) b |= 2;
            if (_duckCb.Value == CheckState.Checked) b |= 4;
            if (_forwardCb.Value == CheckState.Checked) b |= 8;
            if (_backCb.Value == CheckState.Checked) b |= 16;
            if (_moveLeftCb.Value == CheckState.Checked) b |= 512;
            if (_moveRightCb.Value == CheckState.Checked) b |= 1024;
            return b;
        }
    }
    public byte Impulse => byte.TryParse(_impulseTf.Text, out byte v) ? v : (byte)0;

    public MoveEditorDialog()
    {
        Title = "Move Editor";
        Width = 55;
        Height = 22;

        FrameView buttonsFrame = new FrameView
        {
            Title = "Buttons",
            X = 1, Y = 1, Width = 25, Height = 11
        };
        _attackCb = new CheckBox { Text = "Attack (IN_ATTACK)", X = 1, Y = 0, Value = CheckState.UnChecked };
        _jumpCb = new CheckBox { Text = "Jump (IN_JUMP)", X = 1, Y = 1, Value = CheckState.UnChecked };
        _duckCb = new CheckBox { Text = "Duck (IN_DUCK)", X = 1, Y = 2, Value = CheckState.UnChecked };
        _forwardCb = new CheckBox { Text = "Forward (IN_FORWARD)", X = 1, Y = 3, Value = CheckState.UnChecked };
        _backCb = new CheckBox { Text = "Back (IN_BACK)", X = 1, Y = 4, Value = CheckState.UnChecked };
        _moveLeftCb = new CheckBox { Text = "Move Left", X = 1, Y = 5, Value = CheckState.UnChecked };
        _moveRightCb = new CheckBox { Text = "Move Right", X = 1, Y = 6, Value = CheckState.UnChecked };
        buttonsFrame.Add(_attackCb, _jumpCb, _duckCb, _forwardCb, _backCb, _moveLeftCb, _moveRightCb);

        FrameView moveFrame = new FrameView
        {
            Title = "Movement",
            X = 28, Y = 1, Width = 26, Height = 11
        };
        moveFrame.Add(
            new Label { Text = "Forward:", X = 1, Y = 0 },
            new Label { Text = "Side:", X = 1, Y = 2 },
            new Label { Text = "Up:", X = 1, Y = 4 },
            new Label { Text = "Impulse:", X = 1, Y = 6 }
        );
        _forwardMoveTf = new TextField { Text = "0", X = 11, Y = 0, Width = 12 };
        _sideMoveTf = new TextField { Text = "0", X = 11, Y = 2, Width = 12 };
        _upMoveTf = new TextField { Text = "0", X = 11, Y = 4, Width = 12 };
        _impulseTf = new TextField { Text = "0", X = 11, Y = 6, Width = 12 };
        moveFrame.Add(_forwardMoveTf, _sideMoveTf, _upMoveTf, _impulseTf);

        Button okBtn = new Button { Text = "Send", X = Pos.Center(), Y = 14 };
        okBtn.Accepting += (s, e) =>
        {
            Confirmed = true;
            this.RequestStop();
        };

        Button cancelBtn = new Button { Text = "Cancel", X = Pos.Right(okBtn) + 2, Y = 14 };
        cancelBtn.Accepting += (s, e) =>
        {
            Confirmed = false;
            this.RequestStop();
        };

        Add(buttonsFrame, moveFrame, okBtn, cancelBtn);
    }
}
