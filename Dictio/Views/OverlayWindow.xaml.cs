using Dictio.Models;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using WpfRect = System.Windows.Shapes.Rectangle;

namespace Dictio.Views;

public enum OverlayState { Collapsed, Idle, RecordingSilent, Processing }

public partial class OverlayWindow : Window
{
    // Wpf.Ui's ApplicationThemeManager walks all open windows and sets Background on theme change.
    // Coerce Background to always stay Transparent so the overlay never gets a solid rectangle.
    static OverlayWindow()
    {
        BackgroundProperty.OverrideMetadata(
            typeof(OverlayWindow),
            new FrameworkPropertyMetadata(
                defaultValue: System.Windows.Media.Brushes.Transparent,
                propertyChangedCallback: null,
                coerceValueCallback: (_, _) => System.Windows.Media.Brushes.Transparent));
    }

    private const int GWL_EXSTYLE      = -20;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WM_NCHITTEST     = 0x0084;
    private const int HTTRANSPARENT    = -1;

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [DllImport("user32.dll")] private static extern int  GetWindowLong(IntPtr hwnd, int index);
    [DllImport("user32.dll")] private static extern int  SetWindowLong(IntPtr hwnd, int index, int value);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out RECT lpRect);

    public event Action? RecordRequested;
    public event Action? SendRequested;

    private OverlayState _state    = OverlayState.Collapsed;
    private double       _dpiScale = 1.0;

    // Visual gain applied to raw RMS before mapping to bar height.
    // Raw RMS for normal speech is ~0.05–0.15; ×8 maps that to 0.4–1.0 (fills the bar).
    private const float EqGain  = 8f;
    private const int   EqSlots = 4;

    // How often the FIFO scrolls one slot left (ms). Settable from AppSettings.
    public int EqScrollIntervalMs { get; set; } = 150;

    private readonly float[]   _eqBuffer = new float[EqSlots]; // [0]=oldest, [3]=newest
    private readonly WpfRect[] _eqBars;
    private DateTime           _lastEqShift = DateTime.MinValue;

    public OverlayWindow()
    {
        InitializeComponent();
        _eqBars = [EqBar0, EqBar1, EqBar2, EqBar3];
        ApplyPosition(OverlayPosition.BottomCenter, verticalOffsetPx: 20, horizontalOffsetPx: 20);
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd   = new WindowInteropHelper(this).Handle;
        var source = HwndSource.FromHwnd(hwnd);

        _dpiScale = source?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;

        var style = GetWindowLong(hwnd, GWL_EXSTYLE);
        SetWindowLong(hwnd, GWL_EXSTYLE, style | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW);

        source?.AddHook(WndProc);
    }

    // Returns HTTRANSPARENT for points outside the center hit region when collapsed,
    // so clicks on the transparent window edges pass through to the app underneath.
    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_NCHITTEST && _state == OverlayState.Collapsed)
        {
            int raw = unchecked((int)lParam.ToInt64());
            int cx  = unchecked((short)(raw & 0xFFFF));
            int cy  = unchecked((short)((raw >> 16) & 0xFFFF));

            GetWindowRect(hwnd, out RECT wr);

            // Hit region: 51×28 centered in the window — same footprint as the Idle pill,
            // giving comfortable hover-activation while making the outer edges click-through.
            int hitW      = (int)(51 * _dpiScale);
            int hitH      = (int)(28 * _dpiScale);
            int hitLeft   = wr.Left + (wr.Right  - wr.Left - hitW) / 2;
            int hitTop    = wr.Top  + (wr.Bottom - wr.Top  - hitH) / 2;

            if (cx < hitLeft || cx >= hitLeft + hitW || cy < hitTop || cy >= hitTop + hitH)
            {
                handled = true;
                return new IntPtr(HTTRANSPARENT);
            }
        }
        return IntPtr.Zero;
    }

    // ── Public API ────────────────────────────────────────────────────────────

    public void SetState(OverlayState state)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(() => SetState(state));
            return;
        }

        _state = state;
        StopDotsAnimation();

        // Pre-zero opacity so the container is invisible before Visibility is set to Visible.
        if (state == OverlayState.Processing)
            StateProcessing.Opacity = 0;

        // Reset equalizer to silence when entering recording state.
        if (state == OverlayState.RecordingSilent)
        {
            Array.Clear(_eqBuffer, 0, EqSlots);
            _lastEqShift = DateTime.MinValue;
            foreach (var bar in _eqBars) UpdateEqBar(bar, 0f);
        }

        StateCollapsed.Visibility       = state == OverlayState.Collapsed      ? Visibility.Visible : Visibility.Collapsed;
        StateIdle.Visibility            = state == OverlayState.Idle            ? Visibility.Visible : Visibility.Collapsed;
        StateRecordingSilent.Visibility = state == OverlayState.RecordingSilent ? Visibility.Visible : Visibility.Collapsed;
        StateProcessing.Visibility      = state == OverlayState.Processing      ? Visibility.Visible : Visibility.Collapsed;

        if (state == OverlayState.Processing)
            StartDotsAnimation();
    }

    public void ShowRecording()  => SetState(OverlayState.RecordingSilent);
    public void ShowProcessing() => SetState(OverlayState.Processing);
    public void Collapse()       => SetState(OverlayState.Collapsed);

    // Called ~10 Hz from AudioRecorderService.LevelChanged during recording.
    // The rightmost slot always shows live RMS; the FIFO scrolls left only when
    // EqScrollIntervalMs has elapsed — controlling the visible "speed" of the waveform.
    public void SetLevel(float rms)
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.Invoke(() => SetLevel(rms)); return; }
        if (_state != OverlayState.RecordingSilent) return;

        var now = DateTime.UtcNow;
        if ((now - _lastEqShift).TotalMilliseconds >= EqScrollIntervalMs)
        {
            Array.Copy(_eqBuffer, 1, _eqBuffer, 0, EqSlots - 1);
            _eqBuffer[EqSlots - 1] = rms;
            _lastEqShift = now;
        }
        else
        {
            _eqBuffer[EqSlots - 1] = rms;
        }

        for (int i = 0; i < EqSlots; i++)
            UpdateEqBar(_eqBars[i], _eqBuffer[i]);
    }

    // Maps raw RMS → bar height [4, 18] px and opacity [0.43, 1.0] continuously.
    // EqGain amplifies quiet speech so typical voice (rms ≈ 0.05) fills ~50 % of the bar.
    private static void UpdateEqBar(WpfRect bar, float rms)
    {
        float display = MathF.Min(1f, rms * EqGain);
        double h = 4.0 + display * 14.0;
        bar.Height  = h;
        bar.Opacity = 0.43 + display * 0.57;
        Canvas.SetTop(bar, (28.0 - h) / 2.0);
    }

    // ── Mouse hover: S0 ↔ S1 ─────────────────────────────────────────────────

    private void OnWindowMouseEnter(object sender, MouseEventArgs e)
    {
        if (_state == OverlayState.Collapsed)
            SetState(OverlayState.Idle);
    }

    private void OnWindowMouseLeave(object sender, MouseEventArgs e)
    {
        if (_state == OverlayState.Idle)
            SetState(OverlayState.Collapsed);
    }

    // ── Button handlers ───────────────────────────────────────────────────────

    private void OnRecordButtonClicked(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        RecordRequested?.Invoke();
    }

    private void OnSendButtonClicked(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        SendRequested?.Invoke();
    }

    // ── Processing dot animation ──────────────────────────────────────────────

    private void StartDotsAnimation()
    {
        // Fade the whole container in over 200 ms; dot wave starts after fade completes.
        var fadeIn = new DoubleAnimation(0, 1, new Duration(TimeSpan.FromMilliseconds(200)))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        StateProcessing.BeginAnimation(OpacityProperty, fadeIn);

        var dotDelay = TimeSpan.FromMilliseconds(200);
        var dur      = new Duration(TimeSpan.FromMilliseconds(500));
        AnimateDot(ProcDot1, dotDelay,                                    dur);
        AnimateDot(ProcDot2, dotDelay + TimeSpan.FromMilliseconds(160),   dur);
        AnimateDot(ProcDot3, dotDelay + TimeSpan.FromMilliseconds(320),   dur);
    }

    private static void AnimateDot(UIElement el, TimeSpan beginTime, Duration dur)
    {
        var anim = new DoubleAnimation(1.0, 0.25, dur)
        {
            AutoReverse    = true,
            RepeatBehavior = RepeatBehavior.Forever,
            BeginTime      = beginTime,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
        };
        el.BeginAnimation(OpacityProperty, anim);
    }

    private void StopDotsAnimation()
    {
        ProcDot1.BeginAnimation(OpacityProperty, null);
        ProcDot2.BeginAnimation(OpacityProperty, null);
        ProcDot3.BeginAnimation(OpacityProperty, null);
        StateProcessing.BeginAnimation(OpacityProperty, null);
    }

    // ── Positioning ───────────────────────────────────────────────────────────

    public void ApplyPosition(OverlayPosition position, int verticalOffsetPx, int horizontalOffsetPx)
    {
        var area = SystemParameters.WorkArea;
        Left = position switch
        {
            OverlayPosition.BottomLeft  or OverlayPosition.TopLeft  => area.Left  + horizontalOffsetPx,
            OverlayPosition.BottomRight or OverlayPosition.TopRight => area.Right - Width - horizontalOffsetPx,
            _                                                        => area.Left  + (area.Width - Width) / 2,
        };
        Top = position switch
        {
            OverlayPosition.TopCenter or OverlayPosition.TopLeft or OverlayPosition.TopRight => area.Top    + verticalOffsetPx,
            _                                                                                  => area.Bottom - Height - verticalOffsetPx,
        };
    }
}
