using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LifeDeck.Studio.Models;
using LifeDeck.Studio.Plugins;
using LifeDeck.Studio.Services;
using WpfBrush = System.Windows.Media.Brush;
using WpfBrushes = System.Windows.Media.Brushes;
using WpfButton = System.Windows.Controls.Button;
using WpfColor = System.Windows.Media.Color;
using WpfColorConverter = System.Windows.Media.ColorConverter;
using WpfImage = System.Windows.Controls.Image;
using WpfMessageBox = System.Windows.MessageBox;
using WpfOpenFileDialog = Microsoft.Win32.OpenFileDialog;

namespace LifeDeck.Studio;

public partial class MainWindow : Window
{
    private readonly ProfileService _profileService = new();
    private readonly PluginRegistry _plugins = new();
    private readonly TabletConnectionService _tablet = new();
    private DeckProfile _profile;
    private DeckPage? _selectedPage;
    private DeckButton? _selectedButton;
    private ButtonStateRule? _selectedRule;
    private NodeRule? _selectedNode;
    private IActionPlugin? _selectedPlugin;
    private bool _loadingProperties;
    private readonly string _profilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "LifeDeck", "profile.json");

    private sealed class PluginSettingBinding
    {
        public string Key { get; set; } = "";
        public TextBox? CustomBox { get; set; }
    }

    public MainWindow()
    {
        InitializeComponent();
        PluginBox.ItemsSource = _plugins.Plugins;
        RulePluginBox.ItemsSource = _plugins.Plugins;
        NodePluginBox.ItemsSource = _plugins.Plugins;
        PluginList.ItemsSource = _plugins.Plugins;

        _profile = File.Exists(_profilePath) ? _profileService.Load(_profilePath) : _profileService.CreateDemoProfile();

        _tablet.StatusChanged += s => Dispatcher.Invoke(() => ConnectionStatus.Text = "  · Tablet: " + s);
        _tablet.PageNextRequested += () => Dispatcher.Invoke(async () => await SelectRelativePageAsync(1));
        _tablet.PagePrevRequested += () => Dispatcher.Invoke(async () => await SelectRelativePageAsync(-1));
        _tablet.ButtonPressed += (id, index) => Dispatcher.Invoke(() => HandleTabletButton(id, index));

        PluginList.SelectedIndex = _plugins.Plugins.Count > 0 ? 0 : -1;
        ApplyPluginSettingsFromProfile();
        LoadPluginProperties(PluginList.SelectedItem as IActionPlugin);
        RefreshPages();
    }

    private void ApplyPluginSettingsFromProfile()
    {
        foreach (var plugin in _plugins.Plugins)
        {
            if (!_profile.PluginSettings.TryGetValue(plugin.Id, out var settings)) continue;

            foreach (var setting in plugin.Settings)
            {
                if (settings.TryGetValue(setting.Key, out var value))
                    plugin.SetSettingValue(setting.Key, value);
            }
        }
    }

    private void StorePluginSettingsToProfile()
    {
        foreach (var plugin in _plugins.Plugins)
        {
            if (plugin.Settings.Count == 0) continue;

            var settings = new Dictionary<string, string>();
            foreach (var setting in plugin.Settings)
                settings[setting.Key] = plugin.GetSettingValue(setting.Key);

            _profile.PluginSettings[plugin.Id] = settings;
        }
    }

    private void SaveProfileQuietly()
    {
        try
        {
            StorePluginSettingsToProfile();
            _profileService.Save(_profilePath, _profile);
        }
        catch { }
    }

    protected override void OnClosed(EventArgs e)
    {
        SaveProfileQuietly();
        _tablet.Dispose();
        base.OnClosed(e);
    }

    private void RefreshPages()
    {
        PagesList.ItemsSource = null;
        PagesList.ItemsSource = _profile.Pages;
        if (_profile.Pages.Count > 0) PagesList.SelectedIndex = 0;
    }

    private async void PagesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _selectedPage = PagesList.SelectedItem as DeckPage;
        RenderPreview();
        await SendCurrentPageToTabletAsync();
    }

    private void RenderPreview()
    {
        ButtonGrid.Children.Clear();
        _selectedButton = null;
        LoadButtonProperties(null);

        if (_selectedPage == null)
        {
            PreviewTitle.Text = "Vorschau";
            return;
        }

        var pageNumber = _profile.Pages.IndexOf(_selectedPage) + 1;
        PreviewTitle.Text = $"{_selectedPage.Title}  {pageNumber}/{_profile.Pages.Count}";

        for (int i = 0; i < 12; i++)
        {
            DeckButton? b = i < _selectedPage.Buttons.Count ? _selectedPage.Buttons[i] : null;
            ButtonGrid.Children.Add(CreatePreviewButton(b));
        }
    }

    private WpfButton CreatePreviewButton(DeckButton? b)
    {
        var content = new Grid();
        var mode = b?.DisplayMode ?? "imageText";
        var showImage = mode != "text";
        var showText = mode != "image";

        if (showImage) content.RowDefinitions.Add(new RowDefinition { Height = new GridLength(showText ? 4.2 : 1, GridUnitType.Star) });
        if (showText) content.RowDefinitions.Add(new RowDefinition { Height = showImage ? GridLength.Auto : new GridLength(1, GridUnitType.Star) });

        var row = 0;
        if (showImage)
        {
            if (b != null && File.Exists(b.IconPath))
            {
                var image = new WpfImage
                {
                    Stretch = Stretch.Uniform,
                    Margin = showText ? new Thickness(8, 8, 8, 2) : new Thickness(8),
                    Source = LoadImage(b.IconPath)
                };
                Grid.SetRow(image, row);
                content.Children.Add(image);
            }
            else
            {
                var spacer = new TextBlock { Text = "", VerticalAlignment = VerticalAlignment.Center };
                Grid.SetRow(spacer, row);
                content.Children.Add(spacer);
            }
            row++;
        }

        if (showText)
        {
            var title = b == null ? "" : string.IsNullOrWhiteSpace(b.Subtitle) ? b.Title : b.Title + "\n" + b.Subtitle;
            var label = new TextBlock
            {
                Text = title,
                Foreground = WpfBrushes.White,
                FontSize = showImage ? 13 : 17,
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                Margin = showImage ? new Thickness(4, 0, 4, 7) : new Thickness(4),
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                VerticalAlignment = System.Windows.VerticalAlignment.Center
            };
            Grid.SetRow(label, row);
            content.Children.Add(label);
        }

        var btn = new WpfButton
        {
            Content = content,
            Tag = b,
            Margin = new Thickness(6),
            Background = b != null ? TryBrush(b.Color) : new SolidColorBrush(WpfColor.FromRgb(35, 35, 35)),
            BorderBrush = new SolidColorBrush(WpfColor.FromRgb(80, 83, 90))
        };

        if (b != null) btn.Click += PreviewButton_Click;
        else btn.IsEnabled = false;
        return btn;
    }

    private static ImageSource? LoadImage(string path)
    {
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.UriSource = new Uri(path, UriKind.Absolute);
            bmp.DecodePixelWidth = 256;
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch { return null; }
    }

    private WpfBrush TryBrush(string color)
    {
        try { return (WpfBrush)new BrushConverter().ConvertFromString(color)!; }
        catch { return new SolidColorBrush(WpfColor.FromRgb(51, 51, 51)); }
    }

    private void PreviewButton_Click(object sender, RoutedEventArgs e)
    {
        _selectedButton = (sender as WpfButton)?.Tag as DeckButton;
        LoadButtonProperties(_selectedButton);
        RenderPreviewKeepSelection();
    }

    private PluginActionDefinition? FindAction(IActionPlugin? plugin, string idOrName)
    {
        if (plugin == null) return null;
        return plugin.Actions.FirstOrDefault(a => a.Id == idOrName || a.DisplayName == idOrName) ?? plugin.Actions.FirstOrDefault();
    }

    private PluginEventDefinition? FindEvent(IActionPlugin? plugin, string idOrName)
    {
        if (plugin == null) return null;
        return plugin.Events.FirstOrDefault(a => a.Id == idOrName || a.DisplayName == idOrName) ?? plugin.Events.FirstOrDefault();
    }

    private const string CustomValueLabel = "Benutzerdefiniert";

    private static List<string> WithCustom(IEnumerable<string>? values)
    {
        var list = new List<string>();
        if (values != null) list.AddRange(values.Where(v => !string.IsNullOrWhiteSpace(v)).Distinct(StringComparer.OrdinalIgnoreCase));
        if (!list.Any(v => string.Equals(v, CustomValueLabel, StringComparison.OrdinalIgnoreCase))) list.Add(CustomValueLabel);
        return list;
    }

    private static bool IsCustomSelection(ComboBox box) => string.Equals(box.SelectedItem?.ToString(), CustomValueLabel, StringComparison.OrdinalIgnoreCase);

    private static string GetValueFromComboOrCustom(ComboBox combo, TextBox customBox)
    {
        if (IsCustomSelection(combo)) return customBox.Text;
        return combo.SelectedItem?.ToString() ?? "";
    }

    private static void SetComboOrCustom(ComboBox combo, TextBox customBox, string value)
    {
        value ??= "";
        var items = combo.ItemsSource as IEnumerable<string>;
        var match = items?.FirstOrDefault(v => string.Equals(v, value, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(match) && !string.Equals(match, CustomValueLabel, StringComparison.OrdinalIgnoreCase))
        {
            combo.SelectedItem = match;
            customBox.Text = "";
            customBox.Visibility = Visibility.Collapsed;
        }
        else
        {
            combo.SelectedItem = CustomValueLabel;
            customBox.Text = value;
            customBox.Visibility = Visibility.Visible;
        }
    }

    private static void UpdateCustomBoxVisibility(ComboBox combo, TextBox customBox)
    {
        customBox.Visibility = IsCustomSelection(combo) ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateActionValueChoices()
    {
        var current = _selectedButton?.Value ?? GetValueFromComboOrCustom(ActionValueBox, ActionCustomValueBox);
        var action = ActionBox.SelectedItem as PluginActionDefinition;
        ActionValueBox.ItemsSource = WithCustom(action?.SuggestedValues);
        SetComboOrCustom(ActionValueBox, ActionCustomValueBox, current);
        if (ActionValueBrowseButton != null)
            ActionValueBrowseButton.Visibility = !string.IsNullOrWhiteSpace(action?.BrowseFilter) ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateRuleValueChoices()
    {
        var current = _selectedRule?.ExpectedValue ?? GetValueFromComboOrCustom(RuleExpectedValueBox, RuleCustomExpectedValueBox);
        var ev = RuleEventBox.SelectedItem as PluginEventDefinition;
        RuleExpectedValueBox.ItemsSource = WithCustom(ev?.SuggestedValues);
        SetComboOrCustom(RuleExpectedValueBox, RuleCustomExpectedValueBox, current);
    }

    private void UpdateNodeValueChoices()
    {
        var current = _selectedNode?.ConditionValue ?? GetValueFromComboOrCustom(NodeValueBox, NodeCustomValueBox);
        var ev = NodeEventBox.SelectedItem as PluginEventDefinition;
        NodeValueBox.ItemsSource = WithCustom(ev?.SuggestedValues);
        SetComboOrCustom(NodeValueBox, NodeCustomValueBox, current);
    }

    private void LoadButtonProperties(DeckButton? b)
    {
        _loadingProperties = true;
        ButtonTitleBox.Text = b?.Title ?? "";
        ButtonSubtitleBox.Text = b?.Subtitle ?? "";
        DisplayModeBox.SelectedValue = b?.DisplayMode ?? "imageText";
        ButtonColorBox.Text = b?.Color ?? "";
        IconPathBox.Text = b?.IconPath ?? "";

        var plugin = _plugins.Find(b?.Plugin ?? "none") ?? _plugins.Plugins[0];
        PluginBox.SelectedItem = plugin;
        ActionBox.ItemsSource = plugin.Actions;
        ActionBox.SelectedItem = FindAction(plugin, b?.Action ?? "none");
        UpdateActionValueChoices();
        SetComboOrCustom(ActionValueBox, ActionCustomValueBox, b?.Value ?? "");

        RulesList.ItemsSource = null;
        RulesList.ItemsSource = b?.StateRules;
        RulesList.SelectedIndex = b != null && b.StateRules.Count > 0 ? 0 : -1;
        LoadRuleProperties(RulesList.SelectedItem as ButtonStateRule);

        NodesList.ItemsSource = null;
        NodesList.ItemsSource = b?.Nodes;
        NodesList.SelectedIndex = b != null && b.Nodes.Count > 0 ? 0 : -1;
        LoadNodeProperties(NodesList.SelectedItem as NodeRule);

        UpdateIconPreview(b?.IconPath ?? "");
        _loadingProperties = false;
        RefreshNodeCanvas();
    }

    private async void ButtonProperty_Changed(object sender, EventArgs e)
    {
        if (_loadingProperties || _selectedButton == null) return;

        if (sender == PluginBox)
        {
            var plugin = PluginBox.SelectedItem as IActionPlugin;
            ActionBox.ItemsSource = plugin?.Actions;
            ActionBox.SelectedIndex = 0;
            UpdateActionValueChoices();
        }
        else if (sender == ActionBox)
        {
            UpdateActionValueChoices();
        }

        _selectedButton.Title = ButtonTitleBox.Text;
        _selectedButton.Subtitle = ButtonSubtitleBox.Text;
        _selectedButton.DisplayMode = DisplayModeBox.SelectedValue?.ToString() ?? "imageText";
        _selectedButton.Color = ButtonColorBox.Text;
        _selectedButton.IconPath = IconPathBox.Text;
        _selectedButton.Plugin = (PluginBox.SelectedItem as IActionPlugin)?.Id ?? "none";
        _selectedButton.Action = (ActionBox.SelectedItem as PluginActionDefinition)?.Id ?? "none";
        _selectedButton.Value = GetValueFromComboOrCustom(ActionValueBox, ActionCustomValueBox);
        UpdateCustomBoxVisibility(ActionValueBox, ActionCustomValueBox);

        UpdateIconPreview(_selectedButton.IconPath);
        RenderPreviewKeepSelection();
        await SendCurrentPageToTabletAsync();
    }

    private void UpdateIconPreview(string path)
    {
        var img = !string.IsNullOrWhiteSpace(path) && File.Exists(path) ? LoadImage(path) : null;
        IconPreview.Source = img;
        NoIconPreviewText.Visibility = img == null ? Visibility.Visible : Visibility.Collapsed;
    }

    private void RenderPreviewKeepSelection()
    {
        var keep = _selectedButton;
        ButtonGrid.Children.Clear();
        if (_selectedPage == null) return;
        for (int i = 0; i < 12; i++)
        {
            DeckButton? b = i < _selectedPage.Buttons.Count ? _selectedPage.Buttons[i] : null;
            var btn = CreatePreviewButton(b);
            if (ReferenceEquals(b, keep))
            {
                btn.BorderBrush = WpfBrushes.White;
                btn.BorderThickness = new Thickness(2);
            }
            ButtonGrid.Children.Add(btn);
        }
        _selectedButton = keep;
    }

    private async void AddPage_Click(object sender, RoutedEventArgs e)
    {
        var page = new DeckPage { Title = "Neue Seite" };
        for (int i = 1; i <= 12; i++) page.Buttons.Add(new DeckButton { Title = "Button " + i });
        _profile.Pages.Add(page);
        RefreshPages();
        PagesList.SelectedItem = page;
        await SendCurrentPageToTabletAsync();
    }

    private async void RenamePage_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedPage == null) return;
        var newTitle = ShowTextInput("Seite umbenennen", "Seitentitel", _selectedPage.Title);
        if (string.IsNullOrWhiteSpace(newTitle)) return;
        _selectedPage.Title = newTitle.Trim();
        PagesList.Items.Refresh();
        RenderPreview();
        SaveProfileQuietly();
        await SendCurrentPageToTabletAsync();
    }

    private string? ShowTextInput(string title, string label, string current)
    {
        var win = new Window
        {
            Title = title,
            Width = 390,
            Height = 170,
            Owner = this,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
            Background = new SolidColorBrush(WpfColor.FromRgb(31, 33, 38))
        };

        var panel = new StackPanel { Margin = new Thickness(16) };
        panel.Children.Add(new TextBlock { Text = label, Foreground = WpfBrushes.White, Margin = new Thickness(0, 0, 0, 6) });
        var box = new TextBox { Text = current, Margin = new Thickness(0, 0, 0, 14) };
        panel.Children.Add(box);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = System.Windows.HorizontalAlignment.Right };
        var cancel = new WpfButton { Content = "Abbrechen", IsCancel = true };
        var ok = new WpfButton { Content = "Übernehmen", IsDefault = true };
        ok.Click += (_, _) => { win.DialogResult = true; win.Close(); };
        buttons.Children.Add(cancel);
        buttons.Children.Add(ok);
        panel.Children.Add(buttons);
        win.Content = panel;
        box.SelectAll();
        box.Focus();
        return win.ShowDialog() == true ? box.Text : null;
    }

    private async void DuplicatePage_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedPage == null) return;
        var copy = new DeckPage { Title = _selectedPage.Title + " Kopie" };
        foreach (var b in _selectedPage.Buttons)
        {
            var nb = new DeckButton { Title = b.Title, Subtitle = b.Subtitle, Color = b.Color, IconPath = b.IconPath, DisplayMode = b.DisplayMode, Plugin = b.Plugin, Action = b.Action, Value = b.Value };
            foreach (var r in b.StateRules) nb.StateRules.Add(CloneRule(r));
            foreach (var n in b.Nodes) nb.Nodes.Add(CloneNode(n));
            copy.Buttons.Add(nb);
        }
        _profile.Pages.Add(copy);
        RefreshPages();
        PagesList.SelectedItem = copy;
        await SendCurrentPageToTabletAsync();
    }

    private static ButtonStateRule CloneRule(ButtonStateRule r) => new()
    {
        Name = r.Name,
        SourcePlugin = r.SourcePlugin,
        EventName = r.EventName,
        ExpectedValue = r.ExpectedValue,
        Apply = new ButtonVisualState { Title = r.Apply.Title, Subtitle = r.Apply.Subtitle, Color = r.Apply.Color, IconPath = r.Apply.IconPath }
    };

    private static NodeRule CloneNode(NodeRule n) => new()
    {
        Name = n.Name,
        TriggerPlugin = n.TriggerPlugin,
        TriggerEvent = n.TriggerEvent,
        ConditionOperator = n.ConditionOperator,
        ConditionValue = n.ConditionValue,
        Target = n.Target,
        Apply = new ButtonVisualState { Title = n.Apply.Title, Subtitle = n.Apply.Subtitle, Color = n.Apply.Color, IconPath = n.Apply.IconPath }
    };

    private async void DeletePage_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedPage == null || _profile.Pages.Count <= 1) return;
        _profile.Pages.Remove(_selectedPage);
        RefreshPages();
        await SendCurrentPageToTabletAsync();
    }

    private async void NewProfile_Click(object sender, RoutedEventArgs e)
    {
        _profile = _profileService.CreateDemoProfile();
        StorePluginSettingsToProfile();
        RefreshPages();
        await SendCurrentPageToTabletAsync();
    }

    private void SaveProfile_Click(object sender, RoutedEventArgs e)
    {
        StorePluginSettingsToProfile();
        _profileService.Save(_profilePath, _profile);
        WpfMessageBox.Show("Profil gespeichert:\n" + _profilePath, "LifeDeck");
    }

    private async void LoadProfile_Click(object sender, RoutedEventArgs e)
    {
        if (File.Exists(_profilePath))
        {
            _profile = _profileService.Load(_profilePath);
            ApplyPluginSettingsFromProfile();
            RefreshPages();
            await SendCurrentPageToTabletAsync();
        }
    }

    private void PickIcon_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new WpfOpenFileDialog { Filter = "Bilder|*.png;*.jpg;*.jpeg|Alle Dateien|*.*" };
        if (dlg.ShowDialog() == true) IconPathBox.Text = dlg.FileName;
    }

    private void PickActionValue_Click(object sender, RoutedEventArgs e)
    {
        var action = ActionBox.SelectedItem as PluginActionDefinition;
        var filter = string.IsNullOrWhiteSpace(action?.BrowseFilter) ? "Alle Dateien|*.*" : action.BrowseFilter;
        var dlg = new WpfOpenFileDialog { Filter = filter };
        if (dlg.ShowDialog() == true)
        {
            ActionValueBox.SelectedItem = CustomValueLabel;
            ActionCustomValueBox.Visibility = Visibility.Visible;
            ActionCustomValueBox.Text = dlg.FileName;
        }
    }

    private void PickRuleIcon_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new WpfOpenFileDialog { Filter = "Bilder|*.png;*.jpg;*.jpeg|Alle Dateien|*.*" };
        if (dlg.ShowDialog() == true) RuleIconBox.Text = dlg.FileName;
    }

    private void PickButtonColor_Click(object sender, RoutedEventArgs e)
    {
        var color = ShowColorPicker(ButtonColorBox.Text);
        if (!string.IsNullOrWhiteSpace(color)) ButtonColorBox.Text = color;
    }

    private void PickRuleColor_Click(object sender, RoutedEventArgs e)
    {
        var color = ShowColorPicker(RuleColorBox.Text);
        if (!string.IsNullOrWhiteSpace(color)) RuleColorBox.Text = color;
    }

    private void PickNodeColor_Click(object sender, RoutedEventArgs e)
    {
        var color = ShowColorPicker(NodeApplyColorBox.Text);
        if (!string.IsNullOrWhiteSpace(color)) NodeApplyColorBox.Text = color;
    }

    private string? ShowColorPicker(string current)
    {
        var start = ParseColorOrDefault(current, WpfColor.FromRgb(74, 144, 226));
        RgbToHsv(start, out var startHue, out var startSat, out var startVal);

        var win = new Window
        {
            Title = "Farbe wählen",
            Width = 420,
            Height = 380,
            Owner = this,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
            Background = new SolidColorBrush(WpfColor.FromRgb(31, 33, 38))
        };

        var panel = new StackPanel { Margin = new Thickness(16) };
        var preview = new Border { Height = 58, CornerRadius = new CornerRadius(6), Background = new SolidColorBrush(start), Margin = new Thickness(0, 0, 0, 12) };
        var hexText = new TextBlock { Text = ToHex(start), Foreground = WpfBrushes.White, HorizontalAlignment = System.Windows.HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 8) };

        var hue = MakeSlider(startHue, 0, 360);
        hue.Background = MakeHueBrush();
        var saturation = MakeSlider(startSat * 100.0, 0, 100);
        saturation.Background = new LinearGradientBrush(WpfColor.FromRgb(190, 190, 190), WpfColor.FromRgb(74, 144, 226), 0);
        var value = MakeSlider(startVal * 100.0, 0, 100);
        value.Background = new LinearGradientBrush(WpfColor.FromRgb(0, 0, 0), WpfColor.FromRgb(255, 255, 255), 0);

        string result = ToHex(start);
        void Update()
        {
            var c = HsvToRgb(hue.Value, saturation.Value / 100.0, value.Value / 100.0);
            preview.Background = new SolidColorBrush(c);
            result = ToHex(c);
            hexText.Text = result;
        }

        hue.ValueChanged += (_, _) => Update();
        saturation.ValueChanged += (_, _) => Update();
        value.ValueChanged += (_, _) => Update();

        panel.Children.Add(preview);
        panel.Children.Add(hexText);
        panel.Children.Add(new TextBlock { Text = "Farbton / Regenbogen", Foreground = WpfBrushes.White });
        panel.Children.Add(hue);
        panel.Children.Add(new TextBlock { Text = "Sättigung", Foreground = WpfBrushes.White, Margin = new Thickness(0, 8, 0, 0) });
        panel.Children.Add(saturation);
        panel.Children.Add(new TextBlock { Text = "Kontrast / Helligkeit", Foreground = WpfBrushes.White, Margin = new Thickness(0, 8, 0, 0) });
        panel.Children.Add(value);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = System.Windows.HorizontalAlignment.Right, Margin = new Thickness(0, 18, 0, 0) };
        var cancel = new WpfButton { Content = "Abbrechen", IsCancel = true };
        var ok = new WpfButton { Content = "Übernehmen", IsDefault = true };
        ok.Click += (_, _) => { win.DialogResult = true; win.Close(); };
        buttons.Children.Add(cancel);
        buttons.Children.Add(ok);
        panel.Children.Add(buttons);
        win.Content = panel;
        return win.ShowDialog() == true ? result : null;
    }

    private static Slider MakeSlider(double value, double min, double max) => new Slider { Minimum = min, Maximum = max, Value = value, TickFrequency = 1, IsSnapToTickEnabled = false, Margin = new Thickness(0, 3, 0, 5) };

    private static LinearGradientBrush MakeHueBrush()
    {
        var b = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
        b.GradientStops.Add(new GradientStop(WpfColor.FromRgb(255, 0, 0), 0.00));
        b.GradientStops.Add(new GradientStop(WpfColor.FromRgb(255, 255, 0), 0.17));
        b.GradientStops.Add(new GradientStop(WpfColor.FromRgb(0, 255, 0), 0.33));
        b.GradientStops.Add(new GradientStop(WpfColor.FromRgb(0, 255, 255), 0.50));
        b.GradientStops.Add(new GradientStop(WpfColor.FromRgb(0, 0, 255), 0.67));
        b.GradientStops.Add(new GradientStop(WpfColor.FromRgb(255, 0, 255), 0.83));
        b.GradientStops.Add(new GradientStop(WpfColor.FromRgb(255, 0, 0), 1.00));
        return b;
    }

    private static WpfColor HsvToRgb(double h, double s, double v)
    {
        h = ((h % 360) + 360) % 360;
        var c = v * s;
        var x = c * (1 - Math.Abs((h / 60.0) % 2 - 1));
        var m = v - c;
        double r = 0, g = 0, b = 0;
        if (h < 60) { r = c; g = x; }
        else if (h < 120) { r = x; g = c; }
        else if (h < 180) { g = c; b = x; }
        else if (h < 240) { g = x; b = c; }
        else if (h < 300) { r = x; b = c; }
        else { r = c; b = x; }
        return WpfColor.FromRgb((byte)Math.Round((r + m) * 255), (byte)Math.Round((g + m) * 255), (byte)Math.Round((b + m) * 255));
    }

    private static void RgbToHsv(WpfColor c, out double h, out double s, out double v)
    {
        double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        var d = max - min;
        if (d == 0) h = 0;
        else if (max == r) h = 60 * (((g - b) / d) % 6);
        else if (max == g) h = 60 * (((b - r) / d) + 2);
        else h = 60 * (((r - g) / d) + 4);
        if (h < 0) h += 360;
        s = max == 0 ? 0 : d / max;
        v = max;
    }

    private static WpfColor ParseColorOrDefault(string input, WpfColor fallback)
    {
        try
        {
            var c = (WpfColor)WpfColorConverter.ConvertFromString(input)!;
            return WpfColor.FromRgb(c.R, c.G, c.B);
        }
        catch { return fallback; }
    }

    private static string ToHex(WpfColor c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

    private async void ConnectTablet_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await _tablet.ConnectAsync();
            await SendCurrentPageToTabletAsync();
        }
        catch (Exception ex)
        {
            ConnectionStatus.Text = "  · Tablet: Fehler";
            WpfMessageBox.Show(ex.Message, "LifeDeck Verbindung");
        }
    }

    private async void SendToTablet_Click(object sender, RoutedEventArgs e)
    {
        await SendCurrentPageToTabletAsync();
    }

    private async Task SendCurrentPageToTabletAsync()
    {
        if (!_tablet.IsConnected || _selectedPage == null) return;
        var index = _profile.Pages.IndexOf(_selectedPage);
        await _tablet.SendLayoutAsync(_profile, index);
    }

    private async Task SelectRelativePageAsync(int delta)
    {
        if (_profile.Pages.Count == 0 || _selectedPage == null) return;
        var current = _profile.Pages.IndexOf(_selectedPage);
        var next = current + delta;
        if (next < 0) next = _profile.Pages.Count - 1;
        if (next >= _profile.Pages.Count) next = 0;
        PagesList.SelectedIndex = next;
        await SendCurrentPageToTabletAsync();
    }

    private async void HandleTabletButton(string id, int index)
    {
        if (_selectedPage == null) return;
        var button = _selectedPage.Buttons.FirstOrDefault(b => b.Id == id) ?? (index >= 0 && index < _selectedPage.Buttons.Count ? _selectedPage.Buttons[index] : null);
        if (button == null) return;

        _selectedButton = button;
        LoadButtonProperties(button);
        RenderPreviewKeepSelection();

        var plugin = _plugins.Find(button.Plugin);
        if (plugin != null)
        {
            try
            {
                await plugin.ExecuteAsync(button.Action, button.Value);
                ApplyStateEngine(button, plugin);
                LoadButtonProperties(button);
                RenderPreviewKeepSelection();
                await SendCurrentPageToTabletAsync();
            }
            catch (Exception ex) { WpfMessageBox.Show(ex.Message, "Action Fehler"); }
        }
    }

    private void ApplyStateEngine(DeckButton button, IActionPlugin plugin)
    {
        // Backwards-compatible simple plugin state.
        if (plugin is IStatefulPlugin stateful)
        {
            var state = stateful.GetState(button.Action, button.Value);
            ApplyVisualState(button, state);
        }

        // New v0.6 Normal Mode: user-defined rules decide how events modify the button.
        if (plugin is IEventStateProvider events)
        {
            var values = events.GetCurrentEvents();
            foreach (var rule in button.StateRules)
            {
                if (rule.SourcePlugin != plugin.Id) continue;
                if (!values.TryGetValue(rule.EventName, out var actual)) continue;
                if (!StringEquals(actual, rule.ExpectedValue)) continue;
                ApplyVisualState(button, rule.Apply);
            }

            foreach (var node in button.Nodes)
            {
                if (node.TriggerPlugin != plugin.Id) continue;
                if (!values.TryGetValue(node.TriggerEvent, out var actual)) continue;
                if (!ConditionMatches(actual, node.ConditionOperator, node.ConditionValue)) continue;
                ApplyVisualState(button, node.Apply);
            }
        }
    }

    private static bool StringEquals(string a, string b) => string.Equals((a ?? "").Trim(), (b ?? "").Trim(), StringComparison.OrdinalIgnoreCase);

    private static bool ConditionMatches(string actual, string op, string expected)
    {
        actual = (actual ?? "").Trim();
        expected = (expected ?? "").Trim();
        return op switch
        {
            "!=" => !StringEquals(actual, expected),
            "contains" => actual.Contains(expected, StringComparison.OrdinalIgnoreCase),
            ">" => double.TryParse(actual, out var av) && double.TryParse(expected, out var ev) && av > ev,
            "<" => double.TryParse(actual, out var av) && double.TryParse(expected, out var ev) && av < ev,
            _ => StringEquals(actual, expected)
        };
    }

    private static void ApplyVisualState(DeckButton button, PluginButtonState state)
    {
        if (!string.IsNullOrWhiteSpace(state.Title)) button.Title = state.Title;
        if (!string.IsNullOrWhiteSpace(state.Subtitle)) button.Subtitle = state.Subtitle;
        if (!string.IsNullOrWhiteSpace(state.Color)) button.Color = state.Color;
        if (!string.IsNullOrWhiteSpace(state.IconPath)) button.IconPath = state.IconPath;
    }

    private static void ApplyVisualState(DeckButton button, ButtonVisualState state)
    {
        if (!string.IsNullOrWhiteSpace(state.Title)) button.Title = state.Title;
        if (!string.IsNullOrWhiteSpace(state.Subtitle)) button.Subtitle = state.Subtitle;
        if (!string.IsNullOrWhiteSpace(state.Color)) button.Color = state.Color;
        if (!string.IsNullOrWhiteSpace(state.IconPath)) button.IconPath = state.IconPath;
    }



    private void RefreshNodeCanvas()
    {
        if (NodeCanvas == null) return;
        NodeCanvas.Children.Clear();

        if (_selectedButton == null)
        {
            AddCanvasHint("Button auswählen", "Wähle links in der Vorschau einen Button aus, um dessen Node-Graph zu sehen.", 24, 54);
            return;
        }

        var nodes = _selectedButton.Nodes;
        if (nodes.Count == 0)
        {
            AddCanvasHint("Noch keine Nodes", "Klicke auf 'Node +'. Der Normalmodus erzeugt später dieselbe Event → Condition → Action-Struktur automatisch.", 24, 54);
            return;
        }

        double y = 24;
        foreach (var node in nodes)
        {
            DrawNodeGraph(node, y, ReferenceEquals(node, _selectedNode));
            y += 112;
        }
        NodeCanvas.Height = Math.Max(230, y + 16);
    }

    private void AddCanvasHint(string title, string body, double x, double y)
    {
        var box = CreateNodeBox(title, body, "#2B2F38", false);
        Canvas.SetLeft(box, x);
        Canvas.SetTop(box, y);
        NodeCanvas.Children.Add(box);
    }

    private void DrawNodeGraph(NodeRule node, double y, bool selected)
    {
        var plugin = _plugins.Find(node.TriggerPlugin);
        var trigger = (plugin?.DisplayName ?? node.TriggerPlugin) + "." + node.TriggerEvent;
        var condition = $"{node.ConditionOperator} {node.ConditionValue}";
        var changes = new List<string>();
        if (!string.IsNullOrWhiteSpace(node.Apply.Title)) changes.Add("Text: " + node.Apply.Title);
        if (!string.IsNullOrWhiteSpace(node.Apply.Color)) changes.Add("Farbe: " + node.Apply.Color);
        if (!string.IsNullOrWhiteSpace(node.Apply.IconPath)) changes.Add("Icon");
        var action = changes.Count == 0 ? "Button ändern" : string.Join(" · ", changes);

        var eventBox = CreateNodeBox("Event", trigger, "#23415F", selected);
        var condBox = CreateNodeBox("Condition", condition, "#4A3A20", selected);
        var actionBox = CreateNodeBox("Action", action, "#2E5132", selected);

        Canvas.SetLeft(eventBox, 18); Canvas.SetTop(eventBox, y);
        Canvas.SetLeft(condBox, 250); Canvas.SetTop(condBox, y);
        Canvas.SetLeft(actionBox, 482); Canvas.SetTop(actionBox, y);
        NodeCanvas.Children.Add(eventBox);
        NodeCanvas.Children.Add(condBox);
        NodeCanvas.Children.Add(actionBox);

        DrawConnector(202, y + 38, 250, y + 38);
        DrawConnector(434, y + 38, 482, y + 38);
    }

    private Border CreateNodeBox(string header, string body, string bg, bool selected)
    {
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock { Text = header, Foreground = WpfBrushes.White, FontWeight = FontWeights.Bold, FontSize = 12 });
        panel.Children.Add(new TextBlock { Text = body, Foreground = new SolidColorBrush(WpfColor.FromRgb(210, 214, 222)), FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) });
        return new Border
        {
            Width = 184,
            MinHeight = 76,
            Padding = new Thickness(10),
            CornerRadius = new CornerRadius(8),
            Background = TryBrush(bg),
            BorderBrush = selected ? new SolidColorBrush(WpfColor.FromRgb(255, 255, 255)) : new SolidColorBrush(WpfColor.FromRgb(73, 78, 88)),
            BorderThickness = selected ? new Thickness(2) : new Thickness(1),
            Child = panel
        };
    }

    private void DrawConnector(double x1, double y1, double x2, double y2)
    {
        var line = new System.Windows.Shapes.Line
        {
            X1 = x1,
            Y1 = y1,
            X2 = x2,
            Y2 = y2,
            Stroke = new SolidColorBrush(WpfColor.FromRgb(100, 112, 130)),
            StrokeThickness = 2
        };
        NodeCanvas.Children.Add(line);

        var arrow = new System.Windows.Shapes.Polygon
        {
            Fill = new SolidColorBrush(WpfColor.FromRgb(100, 112, 130)),
            Points = new PointCollection { new Point(x2, y2), new Point(x2 - 8, y2 - 5), new Point(x2 - 8, y2 + 5) }
        };
        NodeCanvas.Children.Add(arrow);
    }

    private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T typed) return typed;
            var nested = FindVisualChild<T>(child);
            if (nested != null) return nested;
        }
        return null;
    }

    private void PluginList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingProperties) return;
        LoadPluginProperties(PluginList.SelectedItem as IActionPlugin);
    }

    private void LoadPluginProperties(IActionPlugin? plugin)
    {
        var wasLoading = _loadingProperties;
        _loadingProperties = true;
        _selectedPlugin = plugin;

        SelectedPluginNameText.Text = plugin == null ? "-" : $"{plugin.DisplayName}  ({plugin.Id})";
        PluginActionsList.ItemsSource = null;
        PluginActionsList.ItemsSource = plugin?.Actions;
        PluginEventsList.ItemsSource = null;
        PluginEventsList.ItemsSource = plugin?.Events;

        BuildPluginSettingsPanel(plugin);

        _loadingProperties = wasLoading;
    }

    private void BuildPluginSettingsPanel(IActionPlugin? plugin)
    {
        PluginSettingsPanel.Children.Clear();

        if (plugin == null || plugin.Settings.Count == 0)
        {
            PluginSettingsPanel.Children.Add(new TextBlock
            {
                Text = "Dieses Plugin hat keine konfigurierbaren Einstellungen.",
                Foreground = WpfBrushes.Gray,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 4, 0, 0)
            });
            return;
        }

        foreach (var setting in plugin.Settings)
        {
            PluginSettingsPanel.Children.Add(new TextBlock
            {
                Text = setting.DisplayName,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 8, 0, 3)
            });

            if (!string.IsNullOrWhiteSpace(setting.Hint))
            {
                PluginSettingsPanel.Children.Add(new TextBlock
                {
                    Text = setting.Hint,
                    Foreground = WpfBrushes.Gray,
                    FontSize = 11,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 0, 0, 3)
                });
            }

            var value = plugin.GetSettingValue(setting.Key);
            if (setting.SuggestedValues.Count > 0)
            {
                var custom = new TextBox
                {
                    Tag = setting.Key,
                    Margin = new Thickness(0, 4, 0, 8),
                    Visibility = Visibility.Collapsed
                };
                custom.TextChanged += PluginSettingText_Changed;

                var box = new ComboBox
                {
                    ItemsSource = WithCustom(setting.SuggestedValues),
                    IsEditable = false,
                    Tag = new PluginSettingBinding { Key = setting.Key, CustomBox = custom },
                    Margin = new Thickness(0, 0, 0, 4)
                };

                SetComboOrCustom(box, custom, value);
                box.SelectionChanged += PluginSettingCombo_Changed;

                PluginSettingsPanel.Children.Add(box);
                PluginSettingsPanel.Children.Add(custom);
            }
            else
            {
                var text = new TextBox
                {
                    Text = value,
                    Tag = setting.Key,
                    Margin = new Thickness(0, 0, 0, 4)
                };
                text.TextChanged += PluginSettingText_Changed;
                PluginSettingsPanel.Children.Add(text);
            }
        }
    }

    private void PluginSettingText_Changed(object sender, TextChangedEventArgs e)
    {
        if (_loadingProperties) return;
        if (_selectedPlugin == null) return;
        if (sender is TextBox tb && tb.Tag is string key)
        {
            _selectedPlugin.SetSettingValue(key, tb.Text);
            SaveProfileQuietly();
        }
    }

    private void PluginSettingCombo_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingProperties) return;
        if (_selectedPlugin == null) return;
        if (sender is ComboBox cb && cb.Tag is PluginSettingBinding binding && binding.CustomBox != null)
        {
            UpdateCustomBoxVisibility(cb, binding.CustomBox);
            var value = GetValueFromComboOrCustom(cb, binding.CustomBox);
            _selectedPlugin.SetSettingValue(binding.Key, value);
            SaveProfileQuietly();
        }
    }

    private void RulesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingProperties) return;
        LoadRuleProperties(RulesList.SelectedItem as ButtonStateRule);
    }

    private void LoadRuleProperties(ButtonStateRule? rule)
    {
        var wasLoading = _loadingProperties;
        _loadingProperties = true;
        _selectedRule = rule;
        var plugin = _plugins.Find(rule?.SourcePlugin ?? "none") ?? _plugins.Plugins[0];
        RulePluginBox.SelectedItem = plugin;
        RuleEventBox.ItemsSource = plugin.Events;
        RuleEventBox.SelectedItem = FindEvent(plugin, rule?.EventName ?? "");
        UpdateRuleValueChoices();
        SetComboOrCustom(RuleExpectedValueBox, RuleCustomExpectedValueBox, rule?.ExpectedValue ?? "true");
        RuleTitleBox.Text = rule?.Apply.Title ?? "";
        RuleSubtitleBox.Text = rule?.Apply.Subtitle ?? "";
        RuleColorBox.Text = rule?.Apply.Color ?? "";
        RuleIconBox.Text = rule?.Apply.IconPath ?? "";
        _loadingProperties = wasLoading;
    }

    private async void AddRule_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedButton == null) return;
        var plugin = _plugins.Plugins.FirstOrDefault(p => p.Events.Count > 0) ?? _plugins.Plugins[0];
        var ev = plugin.Events.FirstOrDefault();
        var rule = new ButtonStateRule
        {
            Name = "Status-Regel",
            SourcePlugin = plugin.Id,
            EventName = ev?.Id ?? "",
            ExpectedValue = "true",
            Apply = new ButtonVisualState { Subtitle = "Aktiv", Color = "#7A1F1F" }
        };
        _selectedButton.StateRules.Add(rule);
        RulesList.ItemsSource = null;
        RulesList.ItemsSource = _selectedButton.StateRules;
        RulesList.SelectedItem = rule;
        await SendCurrentPageToTabletAsync();
    }

    private async void DeleteRule_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedButton == null || _selectedRule == null) return;
        _selectedButton.StateRules.Remove(_selectedRule);
        RulesList.ItemsSource = null;
        RulesList.ItemsSource = _selectedButton.StateRules;
        LoadRuleProperties(null);
        await SendCurrentPageToTabletAsync();
    }

    private async void RuleProperty_Changed(object sender, EventArgs e)
    {
        if (_loadingProperties || _selectedRule == null) return;
        if (sender == RulePluginBox)
        {
            var plugin = RulePluginBox.SelectedItem as IActionPlugin;
            RuleEventBox.ItemsSource = plugin?.Events;
            RuleEventBox.SelectedIndex = plugin?.Events.Count > 0 ? 0 : -1;
            UpdateRuleValueChoices();
        }
        else if (sender == RuleEventBox)
        {
            UpdateRuleValueChoices();
        }

        _selectedRule.SourcePlugin = (RulePluginBox.SelectedItem as IActionPlugin)?.Id ?? "none";
        _selectedRule.EventName = (RuleEventBox.SelectedItem as PluginEventDefinition)?.Id ?? "";
        _selectedRule.ExpectedValue = GetValueFromComboOrCustom(RuleExpectedValueBox, RuleCustomExpectedValueBox);
        UpdateCustomBoxVisibility(RuleExpectedValueBox, RuleCustomExpectedValueBox);
        _selectedRule.Apply.Title = RuleTitleBox.Text;
        _selectedRule.Apply.Subtitle = RuleSubtitleBox.Text;
        _selectedRule.Apply.Color = RuleColorBox.Text;
        _selectedRule.Apply.IconPath = RuleIconBox.Text;
        RulesList.Items.Refresh();
        await SendCurrentPageToTabletAsync();
    }

    private void NodesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingProperties) return;
        LoadNodeProperties(NodesList.SelectedItem as NodeRule);
    }

    private void LoadNodeProperties(NodeRule? node)
    {
        var wasLoading = _loadingProperties;
        _loadingProperties = true;
        _selectedNode = node;
        var plugin = _plugins.Find(node?.TriggerPlugin ?? "none") ?? _plugins.Plugins[0];
        NodePluginBox.SelectedItem = plugin;
        NodeEventBox.ItemsSource = plugin.Events;
        NodeEventBox.SelectedItem = FindEvent(plugin, node?.TriggerEvent ?? "");
        UpdateNodeValueChoices();
        NodeNameBox.Text = node?.Name ?? "";
        NodeOperatorBox.SelectedIndex = 0;
        foreach (var item in NodeOperatorBox.Items.OfType<ComboBoxItem>())
        {
            if ((item.Content?.ToString() ?? "") == (node?.ConditionOperator ?? "==")) NodeOperatorBox.SelectedItem = item;
        }
        SetComboOrCustom(NodeValueBox, NodeCustomValueBox, node?.ConditionValue ?? "true");
        NodeApplyTextBox.Text = node?.Apply.Title ?? "";
        NodeApplyColorBox.Text = node?.Apply.Color ?? "";
        NodeApplyIconBox.Text = node?.Apply.IconPath ?? "";
        _loadingProperties = wasLoading;
        RefreshNodeCanvas();
    }

    private async void AddNode_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedButton == null) return;
        var plugin = _plugins.Plugins.FirstOrDefault(p => p.Events.Count > 0) ?? _plugins.Plugins[0];
        var ev = plugin.Events.FirstOrDefault();
        var node = new NodeRule { Name = "Neuer Node", TriggerPlugin = plugin.Id, TriggerEvent = ev?.Id ?? "", ConditionValue = "true", Apply = new ButtonVisualState { Title = "Aktiv", Color = "#4A90E2" } };
        _selectedButton.Nodes.Add(node);
        NodesList.ItemsSource = null;
        NodesList.ItemsSource = _selectedButton.Nodes;
        NodesList.SelectedItem = node;
        RefreshNodeCanvas();
        await SendCurrentPageToTabletAsync();
    }

    private async void DeleteNode_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedButton == null || _selectedNode == null) return;
        _selectedButton.Nodes.Remove(_selectedNode);
        NodesList.ItemsSource = null;
        NodesList.ItemsSource = _selectedButton.Nodes;
        LoadNodeProperties(null);
        RefreshNodeCanvas();
        await SendCurrentPageToTabletAsync();
    }

    private async void NodeProperty_Changed(object sender, EventArgs e)
    {
        if (_loadingProperties || _selectedNode == null) return;
        if (sender == NodePluginBox)
        {
            var plugin = NodePluginBox.SelectedItem as IActionPlugin;
            NodeEventBox.ItemsSource = plugin?.Events;
            NodeEventBox.SelectedIndex = plugin?.Events.Count > 0 ? 0 : -1;
            UpdateNodeValueChoices();
        }
        else if (sender == NodeEventBox)
        {
            UpdateNodeValueChoices();
        }

        _selectedNode.Name = NodeNameBox.Text;
        _selectedNode.TriggerPlugin = (NodePluginBox.SelectedItem as IActionPlugin)?.Id ?? "none";
        _selectedNode.TriggerEvent = (NodeEventBox.SelectedItem as PluginEventDefinition)?.Id ?? "";
        _selectedNode.ConditionOperator = (NodeOperatorBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "==";
        _selectedNode.ConditionValue = GetValueFromComboOrCustom(NodeValueBox, NodeCustomValueBox);
        UpdateCustomBoxVisibility(NodeValueBox, NodeCustomValueBox);
        _selectedNode.Apply.Title = NodeApplyTextBox.Text;
        _selectedNode.Apply.Color = NodeApplyColorBox.Text;
        _selectedNode.Apply.IconPath = NodeApplyIconBox.Text;
        NodesList.Items.Refresh();
        RefreshNodeCanvas();
        await SendCurrentPageToTabletAsync();
    }
}
