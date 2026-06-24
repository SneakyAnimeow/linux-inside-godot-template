using Godot;
using System.Collections.Concurrent;

namespace RealHackerEvolution.Scripts;

public partial class ScreenInteract : Area3D
{
    private SubViewport _viewport;
    private bool _isFocused;
    private PlayerMovementScript _player;
    private Label3D _hintLabel;
    private Label _terminalLabel;
    
    private ConcurrentQueue<string> _terminalOutput = new();
    private VirtualTerminal _vt = new VirtualTerminal(80, 25);
    private bool _needsRender = false;

    public override void _Ready()
    {
        _viewport = GetNode<SubViewport>("../TerminalViewport");
        
        _player = GetNode<PlayerMovementScript>("/root/3dScene/Player");
        
        _hintLabel = new Label3D();
        _hintLabel.Text = "[E] Use Terminal";
        _hintLabel.PixelSize = 0.005f;
        _hintLabel.Position = new Vector3(0, 5.0f, 0.0f); // Float above the monitor
        _hintLabel.Billboard = BaseMaterial3D.BillboardModeEnum.Enabled;
        _hintLabel.NoDepthTest = true;
        _hintLabel.Visible = false;
        AddChild(_hintLabel);

        // Hide the buggy CodeEdit
        var codeEdit = _viewport.GetNode<CodeEdit>("CodeEdit");
        if (codeEdit != null) {
            codeEdit.Visible = false;
        }

        // Setup a background and a raw Label for pure 2D VT100 rendering
        var bg = new ColorRect();
        bg.Color = new Color(0.02f, 0.0f, 0.05f, 0.95f);
        bg.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _viewport.AddChild(bg);

        _terminalLabel = new Label();
        _terminalLabel.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _terminalLabel.AddThemeColorOverride("font_color", new Color(0.2f, 1.0f, 0.2f, 1.0f));
        
        var sysFont = new SystemFont();
        sysFont.FontNames = new string[] { "Courier New", "Menlo", "Consolas", "Monospace" };
        _terminalLabel.AddThemeFontOverride("font", sysFont);
        _terminalLabel.AddThemeFontSizeOverride("font_size", 22);
        _viewport.AddChild(_terminalLabel);

        var program = GetNode<Program>("/root/Program");
        program.OnOutputReceived += (outputChar) => {
            _terminalOutput.Enqueue(outputChar);
        };
    }

    public override void _Process(double delta)
    {
        if (_player == null) return;
        
        if (!_isFocused)
        {
            float dist = GlobalPosition.DistanceTo(_player.GlobalPosition);
            _hintLabel.Visible = dist < 10.0f;
        }
        else
        {
            _hintLabel.Visible = false;
        }

        if (_terminalOutput.Count > 0)
        {
            while (_terminalOutput.TryDequeue(out var outputStr))
            {
                if (string.IsNullOrEmpty(outputStr)) continue;
                _vt.ProcessChar(outputStr[0]);
                _needsRender = true;
            }
        }

        if (_needsRender)
        {
            _terminalLabel.Text = _vt.Render();
            _needsRender = false;
        }
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (_player == null) return;

        if (!_isFocused)
        {
            float dist = GlobalPosition.DistanceTo(_player.GlobalPosition);
            if (dist < 10.0f && @event is InputEventKey eKey && eKey.PhysicalKeycode == Key.E && eKey.Pressed && !eKey.Echo)
            {
                _isFocused = true;
                _player.IsTyping = true;
                _hintLabel.Visible = false;
                GetViewport().SetInputAsHandled();
            }
        }
        else
        {
            if (@event is InputEventKey escKey && escKey.PhysicalKeycode == Key.Escape && escKey.Pressed && !escKey.Echo)
            {
                if (escKey.ShiftPressed)
                {
                    _isFocused = false;
                    _player.IsTyping = false;
                    GetViewport().SetInputAsHandled();
                }
                else
                {
                    var program = GetNode<Program>("/root/Program");
                    program.SendToVm("\x1b");
                    GetViewport().SetInputAsHandled();
                }
            }
            else if (@event is InputEventKey keyEvent && keyEvent.Pressed)
            {
                var program = GetNode<Program>("/root/Program");

                if (keyEvent.PhysicalKeycode == Key.Enter) {
                    program.SendToVm("\n");
                }
                else if (keyEvent.PhysicalKeycode == Key.Backspace) {
                    program.SendToVm("\b");
                }
                else if (keyEvent.PhysicalKeycode == Key.Tab) {
                    program.SendToVm("\t");
                }
                else if (keyEvent.PhysicalKeycode == Key.Up) {
                    program.SendToVm("\x1b[A");
                }
                else if (keyEvent.PhysicalKeycode == Key.Down) {
                    program.SendToVm("\x1b[B");
                }
                else if (keyEvent.PhysicalKeycode == Key.Right) {
                    program.SendToVm("\x1b[C");
                }
                else if (keyEvent.PhysicalKeycode == Key.Left) {
                    program.SendToVm("\x1b[D");
                }
                else if (keyEvent.CtrlPressed && keyEvent.PhysicalKeycode >= Key.A && keyEvent.PhysicalKeycode <= Key.Z) {
                    char ctrlChar = (char)(keyEvent.PhysicalKeycode - Key.A + 1);
                    program.SendToVm(ctrlChar.ToString());
                }
                else if (keyEvent.Unicode != 0) {
                    // Do not send if Ctrl is pressed and it's not a handled control char
                    if (!keyEvent.CtrlPressed) {
                        program.SendToVm(((char)keyEvent.Unicode).ToString());
                    }
                }

                GetViewport().SetInputAsHandled();
            }
        }
    }

    // --- Embedded VT100 Terminal Emulator ---
    public class VirtualTerminal
    {
        public int Cols { get; private set; }
        public int Rows { get; private set; }
        public int CursorX { get; private set; }
        public int CursorY { get; private set; }
        private char[,] _buffer;

        private enum AnsiState { Normal, Escape, Csi }
        private AnsiState _ansiState = AnsiState.Normal;
        private string _ansiParams = "";
        private char _ansiFinal = '\0';

        public VirtualTerminal(int cols, int rows)
        {
            Cols = cols;
            Rows = rows;
            _buffer = new char[rows, cols];
            Clear();
        }

        public void Clear()
        {
            for (int y = 0; y < Rows; y++)
                for (int x = 0; x < Cols; x++)
                    _buffer[y, x] = ' ';
            CursorX = 0; CursorY = 0;
        }

        public void ProcessChar(char c)
        {
            switch (_ansiState)
            {
                case AnsiState.Normal:
                    if (c == '\x1b')
                    {
                        _ansiState = AnsiState.Escape;
                    }
                    else if (c == '\b')
                    {
                        if (CursorX > 0) CursorX--;
                    }
                    else if (c == '\r')
                    {
                        CursorX = 0;
                    }
                    else if (c == '\n')
                    {
                        CursorY++;
                        if (CursorY >= Rows) ScrollUp();
                    }
                    else if (c == '\t')
                    {
                        CursorX = (CursorX / 8 + 1) * 8;
                        if (CursorX >= Cols) CursorX = Cols - 1;
                    }
                    else
                    {
                        if (CursorX >= Cols)
                        {
                            CursorX = 0;
                            CursorY++;
                            if (CursorY >= Rows) ScrollUp();
                        }
                        _buffer[CursorY, CursorX] = c;
                        CursorX++;
                    }
                    break;

                case AnsiState.Escape:
                    if (c == '[')
                    {
                        _ansiState = AnsiState.Csi;
                        _ansiParams = "";
                    }
                    else
                    {
                        _ansiState = AnsiState.Normal;
                    }
                    break;

                case AnsiState.Csi:
                    if (c >= 0x40 && c <= 0x7E)
                    {
                        _ansiFinal = c;
                        ExecuteCsi();
                        _ansiState = AnsiState.Normal;
                    }
                    else
                    {
                        _ansiParams += c;
                    }
                    break;
            }
        }

        private void ScrollUp()
        {
            for (int y = 0; y < Rows - 1; y++)
            {
                for (int x = 0; x < Cols; x++)
                {
                    _buffer[y, x] = _buffer[y + 1, x];
                }
            }
            for (int x = 0; x < Cols; x++)
            {
                _buffer[Rows - 1, x] = ' ';
            }
            CursorY = Rows - 1;
        }

        private void ExecuteCsi()
        {
            var parts = _ansiParams.Split(';');
            int p1 = parts.Length > 0 && int.TryParse(parts[0], out int v) ? v : 0;
            int p2 = parts.Length > 1 && int.TryParse(parts[1], out int v2) ? v2 : 0;

            switch (_ansiFinal)
            {
                case 'A': CursorY = System.Math.Max(0, CursorY - (p1 == 0 ? 1 : p1)); break;
                case 'B': CursorY = System.Math.Min(Rows - 1, CursorY + (p1 == 0 ? 1 : p1)); break;
                case 'C': CursorX = System.Math.Min(Cols - 1, CursorX + (p1 == 0 ? 1 : p1)); break;
                case 'D': CursorX = System.Math.Max(0, CursorX - (p1 == 0 ? 1 : p1)); break;
                case 'H':
                case 'f':
                    CursorY = System.Math.Min(Rows - 1, System.Math.Max(0, (p1 == 0 ? 1 : p1) - 1));
                    CursorX = System.Math.Min(Cols - 1, System.Math.Max(0, (p2 == 0 ? 1 : p2) - 1));
                    break;
                case 'J':
                    if (p1 == 0) // Clear from cursor to end of screen
                    {
                        for (int x = CursorX; x < Cols; x++) _buffer[CursorY, x] = ' ';
                        for (int y = CursorY + 1; y < Rows; y++)
                            for (int x = 0; x < Cols; x++) _buffer[y, x] = ' ';
                    }
                    else if (p1 == 1) // Clear from beginning to cursor
                    {
                        for (int y = 0; y < CursorY; y++)
                            for (int x = 0; x < Cols; x++) _buffer[y, x] = ' ';
                        for (int x = 0; x <= CursorX; x++) _buffer[CursorY, x] = ' ';
                    }
                    else if (p1 == 2 || p1 == 3) // Clear entire screen
                    {
                        Clear();
                    }
                    break;
                case 'K':
                    if (p1 == 0) for (int x = CursorX; x < Cols; x++) _buffer[CursorY, x] = ' ';
                    else if (p1 == 1) for (int x = 0; x <= CursorX; x++) _buffer[CursorY, x] = ' ';
                    else if (p1 == 2) for (int x = 0; x < Cols; x++) _buffer[CursorY, x] = ' ';
                    break;
            }
        }

        public string Render()
        {
            var sb = new System.Text.StringBuilder(Rows * (Cols + 1));
            for (int y = 0; y < Rows; y++)
            {
                for (int x = 0; x < Cols; x++)
                {
                    // Render cursor as a block character
                    if (y == CursorY && x == CursorX)
                    {
                        sb.Append('█');
                    }
                    else
                    {
                        sb.Append(_buffer[y, x] == '\0' ? ' ' : _buffer[y, x]);
                    }
                }
                sb.Append('\n');
            }
            return sb.ToString();
        }
    }
}


