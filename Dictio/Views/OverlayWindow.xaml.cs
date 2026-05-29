using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media.Animation;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using WpfRect = System.Windows.Shapes.Rectangle;

namespace Dictio.Views;

public enum OverlayState { Collapsed, Idle, RecordingSilent, Processing }

public partial class OverlayWindow : Window
{
    private const int GWL_EXSTYLE      = -20;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const int WS_EX_TOOLWINDOW = 0x00000080;

    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hwnd, int index);
    [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hwnd, int index, int value);

    public event Action? RecordRequested;
    public event Action? SendRequested;

    private OverlayState _state = OverlayState.Collapsed;

    // RMS threshold above which a slot is rendered as a voice bar (vs silent dot).
    // ~0.02 ≈ –34 dBFS — picks up normal speech, ignores breath/noise floor.
    private const float VoiceThreshold = 0.02f;
    private const int   EqSlots        = 4;

    private readonly Queue<float>     _rmsHistory;
    private readonly WpfRect[]        _eqBars;

    public OverlayWindow()
    {
        InitializeComponent();
        _eqBars     = [EqBar0, EqBar1, EqBar2, EqBar3];
        _rmsHistory = new Queue<float>(EqSlots);
        ResetEqHistory();
        PositionBottomCenter();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd  = new WindowInteropHelper(this).Handle;
        var style = GetWindowLong(hwnd, GWL_EXSTYLE);
        SetWindowLong(hwnd, GWL_EXSTYLE, style | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW);
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

        // Reset equalizer history to silence when entering recording state.
        if (state == OverlayState.RecordingSilent)
        {
            ResetEqHistory();
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
    // Pushes each new RMS value into a 4-slot FIFO queue; oldest slot shifts left.
    // Each slot renders as a silent dot (rms < threshold) or a voiced bar (rms ≥ threshold).
    public void SetLevel(float rms)
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.Invoke(() => SetLevel(rms)); return; }
        if (_state != OverlayState.RecordingSilent) return;

        if (_rmsHistory.Count >= EqSlots) _rmsHistory.Dequeue();
        _rmsHistory.Enqueue(rms);

        var values = _rmsHistory.ToArray();
        for (int i = 0; i < values.Length; i++)
            UpdateEqBar(_eqBars[i], values[i]);
    }

    private static void UpdateEqBar(WpfRect bar, float rms)
    {
        bool voice = rms >= VoiceThreshold;
        double h   = voice ? Math.Min(18.0, 4.0 + rms * 14.0) : 4.0;
        bar.Height  = h;
        bar.Opacity = voice ? 0.72 : 0.43;
        Canvas.SetTop(bar, (28.0 - h) / 2.0);
    }

    private void ResetEqHistory()
    {
        _rmsHistory.Clear();
        for (int i = 0; i < EqSlots; i++) _rmsHistory.Enqueue(0f);
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

    private void PositionBottomCenter()
    {
        var area = SystemParameters.WorkArea;
        Left = area.Left + (area.Width  - Width)  / 2;
        Top  = area.Bottom - Height - 20;
    }
}
