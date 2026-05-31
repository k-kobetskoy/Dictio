using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Dictio.Models;
using Dictio.Services;
using Microsoft.Win32;
using NAudio.Wave;
using Wpf.Ui.Controls;
// Disambiguate WPF types from WinForms / System.Drawing (UseWindowsForms=true in csproj)
using WpfColor       = System.Windows.Media.Color;
using WpfBrush       = System.Windows.Media.Brush;
using WpfBrushes     = System.Windows.Media.Brushes;
using WpfCursors     = System.Windows.Input.Cursors;
using WpfOrientation       = System.Windows.Controls.Orientation;
using WpfMsgBox            = System.Windows.MessageBox;
using WpfTextBlock         = System.Windows.Controls.TextBlock;
using WpfMessageBoxButton  = System.Windows.MessageBoxButton;
using WpfMessageBoxResult  = System.Windows.MessageBoxResult;
using WpfMessageBoxImage   = System.Windows.MessageBoxImage;
using WpfComboBox          = System.Windows.Controls.ComboBox;
using WpfComboBoxItem = System.Windows.Controls.ComboBoxItem;
using WpfHAlign      = System.Windows.HorizontalAlignment;
using WpfKeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace Dictio.Views;

public partial class SettingsWindow : FluentWindow
{
    public AppSettings Result { get; private set; }

    private readonly AppSettings _current;
    private int     _historyCount;
    private int     _vOffset, _hOffset;
    private bool    _showingKey;
    private TranscriptionModelId _selectedModel;
    private int     _hotkeyMods;
    private int     _hotkeyVk;
    private bool    _capturingHotkey;

    // ── Model catalog ─────────────────────────────────────────────────────────

    private sealed record ModelFamily(
        string Name,
        string Description,
        string Languages,
        bool   IsPaid,
        (TranscriptionModelId Id, string Label)[] Models,
        int    AccuracyBars,
        int    SpeedBars,
        string? Size = null);

    private static readonly ModelFamily[] Catalog =
    [
        new("OpenAI gpt-4o-transcribe",
            "Highest-accuracy cloud model. Powered by OpenAI.",
            "Multilingual", true,
            [(TranscriptionModelId.GPT4oTranscribe, "gpt-4o-transcribe")],
            4, 3),

        new("SenseVoice",
            "Very fast. EN, ZH, JA, KO, Cantonese.",
            "EN / ZH / JA / KO", false,
            [(TranscriptionModelId.SenseVoice, "SenseVoice")],
            3, 4, "152 MB"),

        new("Canary 1B v2",
            "Accurate multilingual model. 25 European languages. Translation support.",
            "25 European languages", false,
            [(TranscriptionModelId.CanaryV2, "Canary 1B v2")],
            3, 2, "691 MB"),

        new("Whisper",
            "Balanced accuracy and speed. Multiple size variants.",
            "Multilingual", false,
            [
                (TranscriptionModelId.WhisperTurbo,  "Turbo"),
                (TranscriptionModelId.WhisperLarge,  "Large"),
                (TranscriptionModelId.WhisperMedium, "Medium"),
                (TranscriptionModelId.WhisperSmall,  "Small"),
                (TranscriptionModelId.WhisperBase,   "Base"),
                (TranscriptionModelId.WhisperTiny,   "Tiny"),
            ],
            3, 2, "74 MB – 3 GB"),

        new("Parakeet",
            "Fast and accurate. V3 is multilingual, V2 is English-only.",
            "EN / Multilingual (V3)", false,
            [
                (TranscriptionModelId.ParakeetV3, "V3 (Multilingual)"),
                (TranscriptionModelId.ParakeetV2, "V2 (English only)"),
            ],
            4, 4, "451 MB"),

        new("GigaAM v3",
            "Best accuracy for Russian. Fast local inference.",
            "Russian", false,
            [(TranscriptionModelId.GigaAMv3, "GigaAM v3")],
            4, 4, "~500 MB"),
    ];

    private readonly Dictionary<int, (Border Card, WpfComboBox? Variants)> _cardMap = new();

    // ── Constructor ───────────────────────────────────────────────────────────

    public SettingsWindow(AppSettings current)
    {
        InitializeComponent();
        _current = current;
        Result   = current;

        // Restore saved window size
        if (current.SettingsWindowWidth  >= 640) Width  = current.SettingsWindowWidth;
        if (current.SettingsWindowHeight >= 500) Height = current.SettingsWindowHeight;

        Load(current);
    }

    // ── Loaded / theme ────────────────────────────────────────────────────────

    private void SettingsWindow_Loaded(object sender, RoutedEventArgs e)
    {
        // FluentWindow handles Mica and title-bar chrome automatically.
        ThemeService.ThemeChanged += OnThemeChanged;
    }

    protected override void OnClosed(EventArgs e)
    {
        ThemeService.ThemeChanged -= OnThemeChanged;
        base.OnClosed(e);
    }

    private void OnThemeChanged()
    {
        // Keep ThemeCombo in sync if theme was changed externally (e.g. from tray)
        ThemeCombo.SelectedIndex = ThemeService.CurrentTheme switch
        {
            AppColorTheme.Light => 1,
            AppColorTheme.Dark  => 2,
            _                   => 0
        };

        // Rebuild dynamic card visuals
        RefreshModelCardStates();
    }

    // ── Load ──────────────────────────────────────────────────────────────────

    private void Load(AppSettings s)
    {
        // API Keys
        ApiKeyBox.Password = s.OpenAiApiKey;
        ApiKeyVisible.Text = s.OpenAiApiKey;
        KeyStoragePath.Text = "Stored at: " + System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Dictio", "settings.json");

        // Main
        HotkeyModeCombo.SelectedIndex = s.HotkeyMode == HotkeyMode.PushToTalk ? 1 : 0;
        PopulateAudioDevices(s.AudioDeviceIndex);

        LanguageCombo.SelectedIndex = s.TranscriptionLanguage switch
        {
            TranscriptionLanguage.English   => 1,
            TranscriptionLanguage.Russian   => 2,
            TranscriptionLanguage.Ukrainian => 3,
            _                               => 0
        };

        ThemeCombo.SelectedIndex = s.Theme switch
        {
            AppColorTheme.Light => 1,
            AppColorTheme.Dark  => 2,
            _                   => 0
        };

        StartWithWindowsToggle.IsChecked  = s.StartWithWindows;
        HideOnStartToggle.IsChecked       = s.HideOnStart;
        ShowTrayIconToggle.IsChecked      = s.ShowTrayIcon;
        RestoreClipboardToggle.IsChecked  = s.RestoreClipboard;
        CompressAudioToggle.IsChecked     = !s.ForceWavDebug;
        SkipSilentToggle.IsChecked        = s.SkipSilentRecordings;

        // Models
        _selectedModel = s.SelectedModel;
        BuildModelCards();

        // History
        EnableHistoryToggle.IsChecked  = s.EnableHistory;
        SaveTranscriptToggle.IsChecked = s.SaveTranscriptionText;
        _historyCount = s.HistoryMaxRecords;
        UpdateHistoryCountDisplay();
        LoadHistoryList();

        // Overlay
        OverlayVisibleToggle.IsChecked = s.OverlayVisible;
        OpacitySlider.Value = s.OverlayOpacity;

        EqSpeedCombo.SelectedIndex = s.EqScrollIntervalMs switch
        {
            <= 75  => 0,
            <= 150 => 1,
            <= 300 => 2,
            _      => 3
        };

        OverlayPositionCombo.SelectedIndex = s.OverlayPosition switch
        {
            OverlayPosition.BottomLeft  => 1,
            OverlayPosition.BottomRight => 2,
            OverlayPosition.TopCenter   => 3,
            OverlayPosition.TopLeft     => 4,
            OverlayPosition.TopRight    => 5,
            _                           => 0
        };

        _vOffset = s.OverlayVerticalOffsetPx;
        _hOffset = s.OverlayHorizontalOffsetPx;
        UpdateVOffDisplay();
        UpdateHOffDisplay();

        // Hotkey
        _hotkeyMods = s.HotkeyModifiers;
        _hotkeyVk   = s.HotkeyVirtualKey;
        UpdateHotkeyDisplay();

        NavList.SelectedIndex = 0;
    }

    // ── Navigation ────────────────────────────────────────────────────────────

    private void NavList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        ShowPage(NavList.SelectedIndex);
    }

    private void ShowPage(int idx)
    {
        PageMain.Visibility    = idx == 0 ? Visibility.Visible : Visibility.Collapsed;
        PageModels.Visibility  = idx == 1 ? Visibility.Visible : Visibility.Collapsed;
        PageApiKeys.Visibility = idx == 2 ? Visibility.Visible : Visibility.Collapsed;
        PageHistory.Visibility = idx == 3 ? Visibility.Visible : Visibility.Collapsed;
        PageOverlay.Visibility = idx == 4 ? Visibility.Visible : Visibility.Collapsed;

        if (idx == 3) LoadHistoryList();
    }

    // ── Main: microphone ──────────────────────────────────────────────────────

    private void PopulateAudioDevices(int selectedIndex)
    {
        AudioDeviceCombo.Items.Clear();
        int count = WaveIn.DeviceCount;
        if (count == 0)
        {
            AudioDeviceCombo.Items.Add(new WpfComboBoxItem
                { Content = "(No microphones found)", IsEnabled = false });
            AudioDeviceCombo.SelectedIndex = 0;
            return;
        }
        for (int i = 0; i < count; i++)
        {
            var caps  = WaveIn.GetCapabilities(i);
            var label = i == 0 ? $"{caps.ProductName} (default)" : caps.ProductName;
            AudioDeviceCombo.Items.Add(new WpfComboBoxItem { Content = label, Tag = i });
        }
        AudioDeviceCombo.SelectedIndex = selectedIndex < count ? selectedIndex : 0;
    }

    private void RefreshMic_Click(object sender, RoutedEventArgs e)
    {
        var current = (AudioDeviceCombo.SelectedItem as WpfComboBoxItem)?.Tag is int idx ? idx : 0;
        PopulateAudioDevices(current);
    }

    private void LanguageCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (LangHintText == null) return;
        var lang = SelectedLanguage();
        LangHintText.Text = lang switch
        {
            TranscriptionLanguage.Auto => "Language auto-detected from audio",
            _                          => $"Model transcribes in {lang}"
        };
    }

    // ── Models section ────────────────────────────────────────────────────────

    private bool HasOpenAiKey() =>
        !string.IsNullOrWhiteSpace(_showingKey ? ApiKeyVisible.Text : ApiKeyBox.Password);

    private static WpfBrush R(string key) =>
        System.Windows.Application.Current.Resources[key] is WpfBrush b ? b : WpfBrushes.Transparent;

    private static WpfBrush SelectedCardBg() =>
        ThemeService.CurrentIsDark
            ? new SolidColorBrush(WpfColor.FromRgb(0x1E, 0x3A, 0x5F))
            : new SolidColorBrush(WpfColor.FromRgb(0xEF, 0xF6, 0xFF));

    private void BuildModelCards()
    {
        _cardMap.Clear();
        ModelsPanel.Children.Clear();

        for (int i = 0; i < Catalog.Length; i++)
        {
            var family       = Catalog[i];
            bool isPaidNoKey  = family.IsPaid && !HasOpenAiKey();
            bool isComingSoon = !family.IsPaid;
            bool isSelected   = family.Models.Any(m => m.Id == _selectedModel);

            var card = BuildModelCard(i, family, isSelected, isPaidNoKey, isComingSoon,
                                      out var variantCombo);
            _cardMap[i] = (card, variantCombo);
            ModelsPanel.Children.Add(card);
            ModelsPanel.Children.Add(new Border { Height = 8 });
        }
    }

    private Border BuildModelCard(int index, ModelFamily family,
        bool isSelected, bool isPaidNoKey, bool isComingSoon,
        out WpfComboBox? variantCombo)
    {
        variantCombo = null as WpfComboBox;

        var card = new Border
        {
            BorderThickness = new Thickness(1.5),
            CornerRadius    = new CornerRadius(8),
            Padding         = new Thickness(14),
            Background      = isSelected ? SelectedCardBg() : R("CardBackgroundFillColorDefaultBrush"),
            BorderBrush     = isSelected
                ? new SolidColorBrush(WpfColor.FromRgb(0x1E, 0x78, 0xE6))
                : R("CardStrokeColorDefaultBrush"),
            Cursor = (isPaidNoKey || isComingSoon) ? WpfCursors.Arrow : WpfCursors.Hand,
        };

        var outerGrid = new Grid();
        outerGrid.ColumnDefinitions.Add(new ColumnDefinition
            { Width = new GridLength(1, GridUnitType.Star) });
        outerGrid.ColumnDefinitions.Add(new ColumnDefinition
            { Width = GridLength.Auto });

        // ── Left: name, badges, description, language, variant picker ─────────
        var infoPanel = new StackPanel();
        Grid.SetColumn(infoPanel, 0);

        var titleRow = new StackPanel { Orientation = WpfOrientation.Horizontal };
        titleRow.Children.Add(new WpfTextBlock
        {
            Text       = family.Name,
            FontWeight = FontWeights.SemiBold,
            FontSize   = 13,
            Foreground = R("TextFillColorPrimaryBrush"),
            VerticalAlignment = VerticalAlignment.Center
        });

        if (isSelected)
            titleRow.Children.Add(MakeBadge("✓ Active", "#1E78E6", "#EFF6FF", "#BFDBFE"));
        else if (isComingSoon)
            titleRow.Children.Add(MakeBadge("Coming soon", "#6B7280", "#F3F4F6", "#E5E7EB"));
        else if (isPaidNoKey)
            titleRow.Children.Add(MakeBadge("Requires API key", "#92400E", "#FFF3CD", "#FDE68A"));

        infoPanel.Children.Add(titleRow);

        infoPanel.Children.Add(new WpfTextBlock
        {
            Text         = family.Description,
            FontSize     = 11,
            Foreground   = R("TextFillColorSecondaryBrush"),
            Margin       = new Thickness(0, 3, 0, 0),
            TextWrapping = TextWrapping.Wrap
        });

        infoPanel.Children.Add(new WpfTextBlock
        {
            Text       = "🌐 " + family.Languages,
            FontSize   = 11,
            Foreground = R("TextFillColorTertiaryBrush"),
            Margin     = new Thickness(0, 4, 0, 0)
        });

        if (family.Models.Length > 1)
        {
            variantCombo = new WpfComboBox
            {
                FontSize   = 12,
                Height     = 28,
                Width      = 200,
                Margin     = new Thickness(0, 8, 0, 0),
                Visibility = isSelected ? Visibility.Visible : Visibility.Collapsed
            };
            foreach (var (id, label) in family.Models)
            {
                var item = new WpfComboBoxItem { Content = label, Tag = id };
                variantCombo.Items.Add(item);
                if (id == _selectedModel) variantCombo.SelectedItem = item;
            }
            if (variantCombo.SelectedIndex < 0) variantCombo.SelectedIndex = 0;

            var capturedVariant = variantCombo;
            variantCombo.SelectionChanged += (_, _) =>
            {
                if (capturedVariant.SelectedItem is WpfComboBoxItem cbi
                    && cbi.Tag is TranscriptionModelId id)
                    _selectedModel = id;
            };
            infoPanel.Children.Add(variantCombo);
        }

        outerGrid.Children.Add(infoPanel);

        // ── Right: accuracy / speed bars + size ───────────────────────────────
        var rightPanel = new StackPanel
        {
            Margin = new Thickness(12, 0, 0, 0),
            VerticalAlignment   = VerticalAlignment.Top,
            HorizontalAlignment = WpfHAlign.Right,
            MinWidth = 80
        };
        rightPanel.Children.Add(BuildBarsRow("accuracy", family.AccuracyBars));
        rightPanel.Children.Add(BuildBarsRow("speed", family.SpeedBars));
        if (family.Size != null)
            rightPanel.Children.Add(new WpfTextBlock
            {
                Text = family.Size,
                FontSize = 10,
                Foreground = R("TextFillColorTertiaryBrush"),
                HorizontalAlignment = WpfHAlign.Right,
                Margin = new Thickness(0, 5, 0, 0)
            });
        Grid.SetColumn(rightPanel, 1);
        outerGrid.Children.Add(rightPanel);

        card.Child = outerGrid;

        if (isPaidNoKey)
        {
            card.ToolTip = "Add an API key to use this model";
            card.Cursor  = WpfCursors.Hand;
            card.MouseLeftButtonUp += (_, _) => NavList.SelectedIndex = 2;
        }
        else if (!isComingSoon)
        {
            var capturedIndex  = index;
            var capturedFamily = family;
            WpfComboBox? capturedVariant = variantCombo;
            card.MouseLeftButtonUp += (_, _) =>
                OnModelCardClicked(capturedIndex, capturedFamily, capturedVariant);
        }

        return card;
    }

    private void OnModelCardClicked(int cardIndex, ModelFamily family, WpfComboBox? variantCombo)
    {
        var targetId = variantCombo?.SelectedItem is WpfComboBoxItem cbi
                       && cbi.Tag is TranscriptionModelId vid
            ? vid
            : family.Models[0].Id;

        var currentLang = SelectedLanguage();
        if (IsModelLangIncompatible(targetId, currentLang))
        {
            var modelLabel = variantCombo != null ? $"{family.Name} {family.Models[0].Label}" : family.Name;
            var msg = $"The model \"{modelLabel}\" does not support the currently selected language ({currentLang}).\n\n" +
                      "Switch to this model anyway? The Language setting will be reset to Auto.";
            if (WpfMsgBox.Show(msg, "Language mismatch", WpfMessageBoxButton.YesNo,
                               WpfMessageBoxImage.Warning) != WpfMessageBoxResult.Yes)
                return;
            LanguageCombo.SelectedIndex = 0;
        }

        _selectedModel = targetId;
        RefreshModelCardStates();
    }

    private void RefreshModelCardStates()
    {
        for (int i = 0; i < Catalog.Length; i++)
        {
            if (!_cardMap.TryGetValue(i, out var entry)) continue;
            var (card, variantCombo) = entry;
            var family   = Catalog[i];
            bool selected = family.Models.Any(m => m.Id == _selectedModel);

            card.Background  = selected ? SelectedCardBg() : R("CardBackgroundFillColorDefaultBrush");
            card.BorderBrush = selected
                ? new SolidColorBrush(WpfColor.FromRgb(0x1E, 0x78, 0xE6))
                : R("CardStrokeColorDefaultBrush");

            if (variantCombo != null)
                variantCombo.Visibility = selected ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private static bool IsModelLangIncompatible(TranscriptionModelId model,
                                                 TranscriptionLanguage lang)
    {
        if (lang is TranscriptionLanguage.Auto) return false;
        return model switch
        {
            TranscriptionModelId.ParakeetV2 => lang is not TranscriptionLanguage.English,
            TranscriptionModelId.SenseVoice => lang is not TranscriptionLanguage.English,
            TranscriptionModelId.GigaAMv3   => lang is not TranscriptionLanguage.Russian,
            _ => false
        };
    }

    private static Border MakeBadge(string text, string fg, string bg, string border)
    {
        var conv = new BrushConverter();
        var b = new Border
        {
            CornerRadius    = new CornerRadius(10),
            Padding         = new Thickness(7, 2, 7, 2),
            Margin          = new Thickness(8, 0, 0, 0),
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

    private StackPanel BuildBarsRow(string label, int filled)
    {
        var row = new StackPanel
        {
            Orientation         = WpfOrientation.Horizontal,
            HorizontalAlignment = WpfHAlign.Right,
            Margin              = new Thickness(0, 2, 0, 0)
        };
        row.Children.Add(new WpfTextBlock
        {
            Text = label, FontSize = 9, Width = 48,
            Foreground        = R("TextFillColorTertiaryBrush"),
            VerticalAlignment = VerticalAlignment.Center
        });
        for (int i = 0; i < 4; i++)
            row.Children.Add(new Border
            {
                Width = 6, Height = 6,
                CornerRadius = new CornerRadius(1),
                Margin     = new Thickness(2, 0, 0, 0),
                Background = i < filled
                    ? new SolidColorBrush(WpfColor.FromRgb(0xE7, 0x54, 0x80))
                    : R("CardStrokeColorDefaultBrush")
            });
        return row;
    }

    // ── API Keys ──────────────────────────────────────────────────────────────

    private void ShowKey_Click(object sender, RoutedEventArgs e)
    {
        _showingKey = !_showingKey;
        if (_showingKey)
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
        BuildModelCards();
    }

    // ── History ───────────────────────────────────────────────────────────────

    private void LoadHistoryList()
    {
        HistoryList.Children.Clear();
        var files = AudioArchive.GetAll();
        HistoryEmpty.Visibility = files.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        foreach (var file in files)
        {
            var txtPath    = System.IO.Path.ChangeExtension(file.FullName, ".txt");
            var transcript = File.Exists(txtPath) ? File.ReadAllText(txtPath).Trim() : null;

            var row = new Border
            {
                BorderBrush     = R("CardStrokeColorDefaultBrush"),
                BorderThickness = new Thickness(1),
                CornerRadius    = new CornerRadius(6),
                Padding         = new Thickness(12, 9, 12, 9),
                Margin          = new Thickness(0, 0, 0, 6),
                Background      = R("CardBackgroundFillColorDefaultBrush")
            };

            var textPanel = new StackPanel();
            textPanel.Children.Add(new WpfTextBlock
            {
                Text       = file.Name,
                FontSize   = 12,
                FontWeight = FontWeights.Medium,
                Foreground = R("TextFillColorPrimaryBrush")
            });

            if (transcript != null)
            {
                var preview = transcript.Length > 120 ? transcript[..120] + "…" : transcript;
                textPanel.Children.Add(new WpfTextBlock
                {
                    Text         = preview,
                    FontSize     = 11,
                    Foreground   = R("TextFillColorSecondaryBrush"),
                    Margin       = new Thickness(0, 3, 0, 0),
                    TextWrapping = TextWrapping.Wrap
                });
            }
            else
            {
                textPanel.Children.Add(new WpfTextBlock
                {
                    Text       = $"{file.Length / 1024} KB  ·  {file.LastWriteTime:yyyy-MM-dd HH:mm}",
                    FontSize   = 11,
                    Foreground = R("TextFillColorTertiaryBrush"),
                    Margin     = new Thickness(0, 2, 0, 0)
                });
            }

            row.Child = textPanel;
            HistoryList.Children.Add(row);
        }
    }

    private void UpdateHistoryCountDisplay()
    {
        if (HistoryCountDisplay != null)
            HistoryCountDisplay.Text = _historyCount.ToString();
    }

    private void HistoryCountDown_Click(object sender, RoutedEventArgs e)
    {
        if (_historyCount > 1) { _historyCount--; UpdateHistoryCountDisplay(); }
    }

    private void HistoryCountUp_Click(object sender, RoutedEventArgs e)
    {
        if (_historyCount < 50) { _historyCount++; UpdateHistoryCountDisplay(); }
    }

    private void OpenHistoryFolder_Click(object sender, RoutedEventArgs e)
    {
        var dir = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Dictio", "audio");
        Directory.CreateDirectory(dir);
        Process.Start(new ProcessStartInfo("explorer.exe", dir) { UseShellExecute = true });
    }

    // ── Overlay ───────────────────────────────────────────────────────────────

    private void OpacitySlider_Changed(object sender,
                                        RoutedPropertyChangedEventArgs<double> e)
    {
        if (OpacityDisplay == null) return;
        OpacityDisplay.Text = $"{(int)(OpacitySlider.Value * 100)}%";
    }

    private void UpdateVOffDisplay()
    {
        if (VOffDisplay != null) VOffDisplay.Text = _vOffset.ToString();
    }

    private void UpdateHOffDisplay()
    {
        if (HOffDisplay != null) HOffDisplay.Text = _hOffset.ToString();
    }

    private void VOffDown_Click(object sender, RoutedEventArgs e)
    {
        if (_vOffset > 0) { _vOffset--; UpdateVOffDisplay(); }
    }

    private void VOffUp_Click(object sender, RoutedEventArgs e)
    {
        if (_vOffset < 500) { _vOffset++; UpdateVOffDisplay(); }
    }

    private void HOffDown_Click(object sender, RoutedEventArgs e)
    {
        if (_hOffset > 0) { _hOffset--; UpdateHOffDisplay(); }
    }

    private void HOffUp_Click(object sender, RoutedEventArgs e)
    {
        if (_hOffset < 500) { _hOffset++; UpdateHOffDisplay(); }
    }

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

        _hotkeyMods = mods;
        _hotkeyVk   = KeyInterop.VirtualKeyFromKey(key);
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
        HotkeyDisplayText.Text = HotkeyService.FormatHotkey(_hotkeyMods, _hotkeyVk);
    }

    // ── Save / Cancel ─────────────────────────────────────────────────────────

    private TranscriptionLanguage SelectedLanguage() => LanguageCombo.SelectedIndex switch
    {
        1 => TranscriptionLanguage.English,
        2 => TranscriptionLanguage.Russian,
        3 => TranscriptionLanguage.Ukrainian,
        _ => TranscriptionLanguage.Auto
    };

    private AppColorTheme SelectedTheme() => ThemeCombo.SelectedIndex switch
    {
        1 => AppColorTheme.Light,
        2 => AppColorTheme.Dark,
        _ => AppColorTheme.System
    };

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var apiKey = _showingKey ? ApiKeyVisible.Text : ApiKeyBox.Password;

        int eqMs = (EqSpeedCombo.SelectedItem as WpfComboBoxItem)?.Tag is string t
                   && int.TryParse(t, out var v) ? v : 150;

        var pos = OverlayPositionCombo.SelectedIndex switch
        {
            1 => OverlayPosition.BottomLeft,
            2 => OverlayPosition.BottomRight,
            3 => OverlayPosition.TopCenter,
            4 => OverlayPosition.TopLeft,
            5 => OverlayPosition.TopRight,
            _ => OverlayPosition.BottomCenter
        };

        bool startWithWindows = StartWithWindowsToggle.IsChecked == true;
        ApplyStartWithWindows(startWithWindows);

        Result = new AppSettings
        {
            OpenAiApiKey              = apiKey,
            AudioDeviceIndex          = (AudioDeviceCombo.SelectedItem as WpfComboBoxItem)
                                        ?.Tag is int idx ? idx : 0,
            HotkeyMode                = HotkeyModeCombo.SelectedIndex == 1
                                        ? HotkeyMode.PushToTalk : HotkeyMode.Toggle,
            HotkeyModifiers           = _hotkeyMods,
            HotkeyVirtualKey          = _hotkeyVk,
            TranscriptionLanguage     = SelectedLanguage(),
            Theme                     = SelectedTheme(),
            FirstLaunchDone           = _current.FirstLaunchDone,
            FontFamily                = _current.FontFamily,
            RestoreClipboard          = RestoreClipboardToggle.IsChecked == true,
            SkipSilentRecordings      = SkipSilentToggle.IsChecked == true,
            ForceWavDebug             = CompressAudioToggle.IsChecked != true,
            EqScrollIntervalMs        = eqMs,
            OverlayPosition           = pos,
            OverlayVerticalOffsetPx   = _vOffset,
            OverlayHorizontalOffsetPx = _hOffset,
            SelectedModel             = _selectedModel,
            ShowTrayIcon              = ShowTrayIconToggle.IsChecked == true,
            HideOnStart               = HideOnStartToggle.IsChecked == true,
            StartWithWindows          = startWithWindows,
            EnableHistory             = EnableHistoryToggle.IsChecked == true,
            HistoryMaxRecords         = _historyCount,
            SaveTranscriptionText     = SaveTranscriptToggle.IsChecked == true,
            OverlayVisible            = OverlayVisibleToggle.IsChecked == true,
            OverlayOpacity            = OpacitySlider.Value,
            // size saved by App.xaml.cs after dialog closes
            SettingsWindowWidth       = _current.SettingsWindowWidth,
            SettingsWindowHeight      = _current.SettingsWindowHeight,
        };

        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    // ── Start with Windows (registry) ─────────────────────────────────────────

    private static void ApplyStartWithWindows(bool enable)
    {
        const string AppName = "Dictio";
        const string RunKey  = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
            if (key == null) return;
            if (enable)
            {
                var exePath = Process.GetCurrentProcess().MainModule?.FileName;
                if (!string.IsNullOrEmpty(exePath))
                    key.SetValue(AppName, $"\"{exePath}\"");
            }
            else
            {
                key.DeleteValue(AppName, throwOnMissingValue: false);
            }
        }
        catch { }
    }
}
