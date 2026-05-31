using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Dictio.Models;
using Dictio.Services;
using NAudio.Wave;
using Wpf.Ui.Controls;

using WpfColor      = System.Windows.Media.Color;
using WpfBrush      = System.Windows.Media.Brush;
using WpfBrushes    = System.Windows.Media.Brushes;
using WpfTextBlock  = System.Windows.Controls.TextBlock;
using WpfComboBoxItem = System.Windows.Controls.ComboBoxItem;
using WpfKeyEventArgs = System.Windows.Input.KeyEventArgs;
using WpfButton = System.Windows.Controls.Button;

namespace Dictio.Views;

public partial class WelcomeWindow : FluentWindow
{
    // ── Model catalog (simplified, Welcome-specific) ──────────────────────────

    private sealed record WelcomeModel(
        string Name, string Description, string Languages,
        bool IsPaid, bool IsAvailable, TranscriptionModelId Id);

    private static readonly WelcomeModel[] Models =
    [
        new("gpt-4o-transcribe",
            "Best accuracy. Cloud model powered by OpenAI.",
            "Multilingual",
            IsPaid: true, IsAvailable: true,
            TranscriptionModelId.GPT4oTranscribe),

        new("Whisper",
            "Balanced accuracy and speed. Runs fully locally.",
            "Multilingual",
            IsPaid: false, IsAvailable: false,
            TranscriptionModelId.WhisperTurbo),

        new("SenseVoice",
            "Very fast. Best for EN / ZH / JA / KO / Cantonese.",
            "EN / ZH / JA / KO",
            IsPaid: false, IsAvailable: false,
            TranscriptionModelId.SenseVoice),

        new("Parakeet V3",
            "Fast and accurate. Multilingual local model.",
            "Multilingual",
            IsPaid: false, IsAvailable: false,
            TranscriptionModelId.ParakeetV3),

        new("GigaAM v3",
            "Best accuracy for Russian. Fast local inference.",
            "Russian",
            IsPaid: false, IsAvailable: false,
            TranscriptionModelId.GigaAMv3),
    ];

    // ── Fields ────────────────────────────────────────────────────────────────

    private readonly AppSettings _settings;

    private int  _capturedMods;
    private int  _capturedVk;
    private bool _capturingHotkey;
    private bool _showingApiKey;

    private HotkeyMode          _selectedMode;
    private TranscriptionModelId _selectedModel;

    private readonly Dictionary<TranscriptionModelId, Border> _modelCards = new();

    // ── Constructor ───────────────────────────────────────────────────────────

    public WelcomeWindow(AppSettings settings)
    {
        InitializeComponent();
        _settings = settings;

        _capturedMods  = settings.HotkeyModifiers;
        _capturedVk    = settings.HotkeyVirtualKey;
        _selectedMode  = settings.HotkeyMode;
        _selectedModel = settings.SelectedModel;
    }

    // ── Loaded ────────────────────────────────────────────────────────────────

    private void WelcomeWindow_Loaded(object sender, RoutedEventArgs e)
    {
        ThemeService.ThemeChanged += OnThemeChanged;

        UpdateHotkeyDisplay();
        UpdateModeButtons();
        PopulateMics();
        LoadLanguage();
        BuildModelCards();
    }

    protected override void OnClosed(EventArgs e)
    {
        ThemeService.ThemeChanged -= OnThemeChanged;
        base.OnClosed(e);
    }

    private void OnThemeChanged() => RefreshModelCardStates();

    // ── Hotkey capture ────────────────────────────────────────────────────────

    private void EditHotkey_Click(object sender, RoutedEventArgs e)
    {
        _capturingHotkey = true;
        HotkeyCaptureBorder.BorderBrush = new SolidColorBrush(WpfColor.FromRgb(0x1E, 0x78, 0xE6));
        HotkeyDisplayText.Text = "Press a shortcut…";
    }

    protected override void OnPreviewKeyDown(WpfKeyEventArgs e)
    {
        if (!_capturingHotkey) { base.OnPreviewKeyDown(e); return; }

        var key = e.Key == Key.System ? e.SystemKey : e.Key;

        if (IsModifierOnly(key)) { base.OnPreviewKeyDown(e); return; }

        if (key == Key.Escape)
        {
            _capturingHotkey = false;
            UpdateHotkeyDisplay();
            e.Handled = true;
            return;
        }

        bool ctrl  = Keyboard.IsKeyDown(Key.LeftCtrl)  || Keyboard.IsKeyDown(Key.RightCtrl);
        bool shift = Keyboard.IsKeyDown(Key.LeftShift)  || Keyboard.IsKeyDown(Key.RightShift);
        bool alt   = Keyboard.IsKeyDown(Key.LeftAlt)    || Keyboard.IsKeyDown(Key.RightAlt);
        int  mods  = (ctrl ? 1 : 0) | (shift ? 2 : 0) | (alt ? 4 : 0);

        if (mods == 0) { base.OnPreviewKeyDown(e); return; }

        _capturedMods = mods;
        _capturedVk   = KeyInterop.VirtualKeyFromKey(key);
        _capturingHotkey = false;
        UpdateHotkeyDisplay();
        e.Handled = true;
    }

    private static bool IsModifierOnly(Key k) =>
        k is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift
          or Key.LeftAlt  or Key.RightAlt  or Key.LWin or Key.RWin;

    private void UpdateHotkeyDisplay()
    {
        if (HotkeyDisplayText == null) return;
        HotkeyCaptureBorder.BorderBrush = _capturingHotkey
            ? new SolidColorBrush(WpfColor.FromRgb(0x1E, 0x78, 0xE6))
            : R("ControlStrokeColorDefaultBrush");
        HotkeyDisplayText.Text = HotkeyService.FormatHotkey(_capturedMods, _capturedVk);
    }

    // ── Mode buttons (segmented) ──────────────────────────────────────────────

    private void ModeToggle_Click(object sender, RoutedEventArgs e)
    {
        _selectedMode = HotkeyMode.Toggle;
        UpdateModeButtons();
    }

    private void ModePtt_Click(object sender, RoutedEventArgs e)
    {
        _selectedMode = HotkeyMode.PushToTalk;
        UpdateModeButtons();
    }

    private void UpdateModeButtons()
    {
        if (ModeToggleBtn == null) return;
        var activeBg   = new SolidColorBrush(WpfColor.FromRgb(0x1E, 0x78, 0xE6));
        var activeFg   = WpfBrushes.White;
        var inactiveBg = R("ControlFillColorDefaultBrush");
        var inactiveFg = R("TextFillColorPrimaryBrush");

        bool toggleActive = _selectedMode == HotkeyMode.Toggle;
        SetModeButtonStyle(ModeToggleBtn, toggleActive, activeBg, activeFg, inactiveBg, inactiveFg);
        SetModeButtonStyle(ModePttBtn,   !toggleActive, activeBg, activeFg, inactiveBg, inactiveFg);
    }

    private static void SetModeButtonStyle(WpfButton btn, bool active,
        WpfBrush activeBg, WpfBrush activeFg, WpfBrush inactiveBg, WpfBrush inactiveFg)
    {
        // Override the template background via Tag hack: use a property trigger workaround.
        // Simpler: just set the control-level Background/Foreground, which templates check last.
        btn.Background = active ? activeBg : inactiveBg;
        btn.Foreground = active ? activeFg : inactiveFg;

        // Rebuild the border template override for background
        if (btn.Template?.FindName("Bg", btn) is System.Windows.Controls.Border bg)
            bg.Background = active ? activeBg : inactiveBg;
    }

    // ── Microphone ────────────────────────────────────────────────────────────

    private void PopulateMics()
    {
        MicCombo.Items.Clear();
        int count = WaveIn.DeviceCount;
        if (count == 0)
        {
            MicCombo.Items.Add(new WpfComboBoxItem { Content = "(No microphones found)", IsEnabled = false });
            MicCombo.SelectedIndex = 0;
            return;
        }
        for (int i = 0; i < count; i++)
        {
            var caps  = WaveIn.GetCapabilities(i);
            var label = i == 0 ? $"{caps.ProductName} (default)" : caps.ProductName;
            MicCombo.Items.Add(new WpfComboBoxItem { Content = label, Tag = i });
        }
        var saved = _settings.AudioDeviceIndex;
        MicCombo.SelectedIndex = saved < count ? saved : 0;
    }

    private void RefreshMic_Click(object sender, RoutedEventArgs e)
    {
        var current = (MicCombo.SelectedItem as WpfComboBoxItem)?.Tag is int idx ? idx : 0;
        PopulateMics();
        MicCombo.SelectedIndex = current < MicCombo.Items.Count ? current : 0;
    }

    // ── Language ──────────────────────────────────────────────────────────────

    private void LoadLanguage()
    {
        LanguageCombo.SelectedIndex = _settings.TranscriptionLanguage switch
        {
            TranscriptionLanguage.English   => 1,
            TranscriptionLanguage.Russian   => 2,
            TranscriptionLanguage.Ukrainian => 3,
            _                               => 0
        };
    }

    private TranscriptionLanguage SelectedLanguage() => LanguageCombo.SelectedIndex switch
    {
        1 => TranscriptionLanguage.English,
        2 => TranscriptionLanguage.Russian,
        3 => TranscriptionLanguage.Ukrainian,
        _ => TranscriptionLanguage.Auto
    };

    // ── Model cards ───────────────────────────────────────────────────────────

    private static WpfBrush R(string key) =>
        System.Windows.Application.Current.Resources[key] is WpfBrush b ? b : WpfBrushes.Transparent;

    private static WpfBrush SelectedCardBg() =>
        ThemeService.CurrentIsDark
            ? new SolidColorBrush(WpfColor.FromRgb(0x1E, 0x3A, 0x5F))
            : new SolidColorBrush(WpfColor.FromRgb(0xEF, 0xF6, 0xFF));

    private void BuildModelCards()
    {
        _modelCards.Clear();
        WelcomeModelsPanel.Children.Clear();

        foreach (var model in Models)
        {
            bool isSelected = model.Id == _selectedModel;

            var card = new Border
            {
                BorderThickness = new Thickness(1.5),
                CornerRadius    = new CornerRadius(8),
                Padding         = new Thickness(14, 10, 14, 10),
                Margin          = new Thickness(0, 0, 0, 8),
                Background      = isSelected ? SelectedCardBg() : R("CardBackgroundFillColorDefaultBrush"),
                BorderBrush     = isSelected
                    ? new SolidColorBrush(WpfColor.FromRgb(0x1E, 0x78, 0xE6))
                    : R("CardStrokeColorDefaultBrush"),
                Cursor          = model.IsAvailable ? System.Windows.Input.Cursors.Hand : System.Windows.Input.Cursors.Arrow,
            };

            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var left = new StackPanel();
            Grid.SetColumn(left, 0);

            var titleRow = new System.Windows.Controls.StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Horizontal
            };
            titleRow.Children.Add(new WpfTextBlock
            {
                Text       = model.Name,
                FontWeight = FontWeights.SemiBold,
                FontSize   = 13,
                Foreground = R("TextFillColorPrimaryBrush"),
                VerticalAlignment = VerticalAlignment.Center
            });

            // Badge
            string badgeText  = model.IsAvailable ? (model.IsPaid ? "Paid" : "Free") : "Coming soon";
            string badgeFg    = model.IsAvailable ? (model.IsPaid ? "#92400E" : "#065F46") : "#6B7280";
            string badgeBg    = model.IsAvailable ? (model.IsPaid ? "#FFF3CD" : "#D1FAE5") : "#F3F4F6";
            string badgeBdr   = model.IsAvailable ? (model.IsPaid ? "#FDE68A" : "#6EE7B7") : "#E5E7EB";
            titleRow.Children.Add(MakeBadge(badgeText, badgeFg, badgeBg, badgeBdr));
            if (isSelected) titleRow.Children.Add(MakeBadge("✓ Active", "#1E78E6", "#EFF6FF", "#BFDBFE"));
            left.Children.Add(titleRow);

            left.Children.Add(new WpfTextBlock
            {
                Text         = model.Description,
                FontSize     = 11,
                Foreground   = R("TextFillColorSecondaryBrush"),
                Margin       = new Thickness(0, 3, 0, 0),
                TextWrapping = TextWrapping.Wrap
            });
            left.Children.Add(new WpfTextBlock
            {
                Text       = "🌐 " + model.Languages,
                FontSize   = 10,
                Foreground = R("TextFillColorTertiaryBrush"),
                Margin     = new Thickness(0, 2, 0, 0)
            });

            row.Children.Add(left);
            card.Child = row;
            _modelCards[model.Id] = card;

            if (model.IsAvailable)
            {
                var captured = model;
                card.MouseLeftButtonUp += (_, _) => OnModelCardClicked(captured);
            }

            WelcomeModelsPanel.Children.Add(card);
        }
    }

    private void OnModelCardClicked(WelcomeModel model)
    {
        _selectedModel = model.Id;
        RefreshModelCardStates();
        UpdateApiKeyVisibility();
    }

    private void RefreshModelCardStates()
    {
        foreach (var model in Models)
        {
            if (!_modelCards.TryGetValue(model.Id, out var card)) continue;
            bool selected = model.Id == _selectedModel;
            card.Background  = selected ? SelectedCardBg() : R("CardBackgroundFillColorDefaultBrush");
            card.BorderBrush = selected
                ? new SolidColorBrush(WpfColor.FromRgb(0x1E, 0x78, 0xE6))
                : R("CardStrokeColorDefaultBrush");
        }
    }

    private void UpdateApiKeyVisibility()
    {
        var model = Array.Find(Models, m => m.Id == _selectedModel);
        ApiKeySection.Visibility = (model?.IsPaid == true)
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private static Border MakeBadge(string text, string fg, string bg, string border)
    {
        var conv = new BrushConverter();
        var b = new Border
        {
            CornerRadius    = new CornerRadius(10),
            Padding         = new Thickness(7, 2, 7, 2),
            Margin          = new Thickness(6, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Background      = (WpfBrush)conv.ConvertFrom(bg)!,
            BorderBrush     = (WpfBrush)conv.ConvertFrom(border)!,
            BorderThickness = new Thickness(1),
        };
        b.Child = new WpfTextBlock
        {
            Text = text, FontSize = 10,
            Foreground = (WpfBrush)conv.ConvertFrom(fg)!
        };
        return b;
    }

    // ── API key visibility toggle ─────────────────────────────────────────────

    private void ShowApiKey_Click(object sender, RoutedEventArgs e)
    {
        _showingApiKey = !_showingApiKey;
        if (_showingApiKey)
        {
            ApiKeyVisible.Text       = ApiKeyBox.Password;
            ApiKeyBox.Visibility     = Visibility.Collapsed;
            ApiKeyVisible.Visibility = Visibility.Visible;
        }
        else
        {
            ApiKeyBox.Password       = ApiKeyVisible.Text;
            ApiKeyBox.Visibility     = Visibility.Visible;
            ApiKeyVisible.Visibility = Visibility.Collapsed;
        }
    }

    // ── Navigation ────────────────────────────────────────────────────────────

    private void Next_Click(object sender, RoutedEventArgs e)
    {
        Step1Panel.Visibility = Visibility.Collapsed;
        Step2Panel.Visibility = Visibility.Visible;
        BackBtn.Visibility    = Visibility.Visible;
        NextBtn.Visibility    = Visibility.Collapsed;
        StartBtn.Visibility   = Visibility.Visible;
        UpdateApiKeyVisibility();
    }

    private void Back_Click(object sender, RoutedEventArgs e)
    {
        Step2Panel.Visibility = Visibility.Collapsed;
        Step1Panel.Visibility = Visibility.Visible;
        BackBtn.Visibility    = Visibility.Collapsed;
        NextBtn.Visibility    = Visibility.Visible;
        StartBtn.Visibility   = Visibility.Collapsed;
    }

    private void Start_Click(object sender, RoutedEventArgs e)
    {
        // Apply all choices directly to the shared settings object
        _settings.HotkeyModifiers      = _capturedMods;
        _settings.HotkeyVirtualKey     = _capturedVk;
        _settings.HotkeyMode           = _selectedMode;
        _settings.TranscriptionLanguage = SelectedLanguage();
        _settings.AudioDeviceIndex     = (MicCombo.SelectedItem as WpfComboBoxItem)?.Tag is int idx ? idx : 0;
        _settings.SelectedModel        = _selectedModel;

        var apiKey = _showingApiKey ? ApiKeyVisible.Text.Trim() : ApiKeyBox.Password.Trim();
        if (!string.IsNullOrEmpty(apiKey))
            _settings.OpenAiApiKey = apiKey;

        DialogResult = true;
    }
}
