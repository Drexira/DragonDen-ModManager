using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using AvaloniaEdit.Highlighting;
using DragonDen.ModManager.Services;

namespace DragonDen.ModManager.Views;

public partial class ConfigDialog : Window
{
    private static readonly HashSet<string> EditableExts = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".json", ".json5", ".jsonc", ".cfg", ".ini", ".toml", ".yml", ".yaml", ".xml", ".cs", ".js"
    };

    private const double MinSplitterSize = 0.2;
    private string? _currentFile;
    private string _original = "";
    private bool _suppressDirty;
    private bool _isDraggingSplitter;

    public ConfigDialog()
    {
        InitializeComponent();

        Closing += OnWindowClosing;
        MainSplit.LayoutUpdated += (_, __) => EnforceSplitterLimits();

        EditorText.Options.HighlightCurrentLine = true;
        EditorText.Background = new SolidColorBrush(Color.Parse("#0F1317"));
        EditorText.Foreground = new SolidColorBrush(Color.Parse("#EDEDED"));
        EditorText.Options.RequireControlModifierForHyperlinkClick = true;
        EditorText.Options.EnableTextDragDrop = true;
        EditorText.Options.ShowColumnRulers = true;
        Resources["AvaloniaEdit.SelectionBrush"] = new SolidColorBrush(Color.Parse("#334455"));
        Resources["AvaloniaEdit.SelectionForeground"] = new SolidColorBrush(Colors.White);
        EditorText.TextArea.Caret.CaretBrush = new SolidColorBrush(Colors.White);

        PointerPressed += (_, e) =>
        {
            if (e.Source is Button) return;
            var pos = e.GetPosition(this);
            if (!(pos.Y <= 36) || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
            try
            {
                BeginMoveDrag(e);
            }
            catch
            {
                // good girl action
            }
        };

        AddHandler(KeyDownEvent, (_, e) =>
        {
            switch (e.Key)
            {
                case Key.Escape:
                    _ = TryCloseWindowAsync();
                    break;
                case Key.S when e.KeyModifiers.HasFlag(KeyModifiers.Control):
                    SaveCurrent();
                    break;
                case Key.R when e.KeyModifiers.HasFlag(KeyModifiers.Control):
                    ResetEdits();
                    break;
            }
        }, RoutingStrategies.Tunnel);

        FilesList.ItemTemplate = new FuncDataTemplate<ConfigItem>((item, _) =>
        {
            var tb = new TextBlock
            {
                Text = item?.DisplayPath ?? "",
                FontFamily = new FontFamily("Consolas, Menlo, Monaco, Courier New"),
                TextWrapping = TextWrapping.NoWrap
            };
            ToolTip.SetTip(tb, item?.FullPath ?? "");
            return tb;
        }, true);

        ToggleEditor(false);
        SetHints(false);
    }

    public ConfigDialog(string modName, IEnumerable<ConfigItem> items) : this()
    {
        TitleText.Text = $"Config • {modName}";
        var list = items?
            .Where(i => !string.IsNullOrWhiteSpace(i.FullPath))
            .Where(i => EditableExts.Contains(Path.GetExtension(i.FullPath)))
            .OrderBy(i => i.DisplayPath, StringComparer.OrdinalIgnoreCase)
            .ToList() ?? new List<ConfigItem>();

        FilesCountText.Text = list.Count.ToString();
        FilesList.ItemsSource = list;
    }

    private async Task<bool> ConfirmLoseChangesAsync()
    {
        if (!DirtyDot.IsVisible) return true;

        var res = await Show3ChoiceDialogAsync(
            "Unsaved changes",
            "Save your edits before closing?",
            "Save", "Discard", "Cancel");

        switch (res)
        {
            case DialogResult.Primary:
                SaveCurrent();
                return true;
            case DialogResult.Secondary:
                return true;
            default:
                return false;
        }
    }

    private void OnEditorTextChanged(object? s, EventArgs e)
    {
        if (_suppressDirty) return;
        var dirty = !string.Equals(EditorText.Text ?? "", _original, StringComparison.Ordinal);
        DirtyDot.IsVisible = dirty;
        SaveBtn.IsEnabled = dirty;
        ResetBtn.IsEnabled = dirty;
    }

    private async void OnWindowClosing(object? s, CancelEventArgs e)
    {
        if (!await ConfirmLoseChangesAsync()) e.Cancel = true;
    }

    private async void OnWindowCloseClicked(object? s, RoutedEventArgs e)
    {
        await TryCloseWindowAsync();
    }

    private async Task TryCloseWindowAsync()
    {
        if (await ConfirmLoseChangesAsync()) Close();
    }

    private async void OnSelectionChanged(object? s, SelectionChangedEventArgs e)
    {
        if (e.AddedItems?.Count > 0 && DirtyDot.IsVisible)
        {
            if (!await ConfirmLoseChangesAsync())
            {
                FilesList.SelectionChanged -= OnSelectionChanged;
                if (e.RemovedItems?.Count > 0) FilesList.SelectedItem = e.RemovedItems[0];
                else FilesList.SelectedItem = null;
                FilesList.SelectionChanged += OnSelectionChanged;
                return;
            }
        }

        var item = FilesList?.SelectedItem as ConfigItem;
        if (item is null || !File.Exists(item.FullPath))
        {
            CloseEditorPane();
            return;
        }

        try
        {
            _currentFile = item.FullPath;
            _original = File.ReadAllText(item.FullPath, new UTF8Encoding(false, true));
            _suppressDirty = true;
            EditorText.Text = _original;
            _suppressDirty = false;

            DirtyDot.IsVisible = false;
            SaveBtn.IsEnabled = false;
            ResetBtn.IsEnabled = false;

            ApplySyntaxHighlighting(item.FullPath);
            ToggleEditor(true);
            SetHints(true);
            EnforceSplitterLimits();
        }
        catch (Exception ex)
        {
            Notifications.Current.ShowError("Open Failed", $"Could not open: {item.FullPath}");
            Console.WriteLine($"[ConfigDialog] Open failed: {ex}");
            CloseEditorPane();
        }
    }

    private void OnSaveClicked(object? s, RoutedEventArgs e)
    {
        SaveCurrent();
    }

    private void OnResetClicked(object? s, RoutedEventArgs e)
    {
        ResetEdits();
    }

    private async void OnCloseEditorClicked(object? s, RoutedEventArgs e)
    {
        if (!await ConfirmLoseChangesAsync()) return;
        CloseEditorPane();
    }

    private void SaveCurrent()
    {
        if (string.IsNullOrWhiteSpace(_currentFile)) return;
        try
        {
            var text = EditorText.Text ?? "";
            File.WriteAllText(_currentFile, text, new UTF8Encoding(false, true));
            _original = text;
            DirtyDot.IsVisible = false;
            SaveBtn.IsEnabled = false;
            ResetBtn.IsEnabled = false;
            Notifications.Current.ShowSuccess("Saved", Path.GetFileName(_currentFile));
        }
        catch (Exception ex)
        {
            Notifications.Current.ShowError("Save Failed", Path.GetFileName(_currentFile));
            Console.WriteLine($"[ConfigDialog] Save failed: {ex}");
        }
    }

    private void ResetEdits()
    {
        _suppressDirty = true;
        EditorText.Text = _original;
        _suppressDirty = false;
        DirtyDot.IsVisible = false;
        SaveBtn.IsEnabled = false;
        ResetBtn.IsEnabled = false;
    }

    private void CloseEditorPane()
    {
        _currentFile = null;
        _original = "";
        _suppressDirty = true;
        EditorText.Text = "";
        _suppressDirty = false;
        DirtyDot.IsVisible = false;
        SaveBtn.IsEnabled = false;
        ResetBtn.IsEnabled = false;

        ToggleEditor(false);
        SetHints(false);
        FilesList.SelectedItem = null;
    }

    private void ToggleEditor(bool show)
    {
        var cols = MainSplit.ColumnDefinitions;
        if (cols.Count != 3) return;

        EditorPanel.IsVisible = show;
        ColSplitter.IsVisible = show;

        if (show)
        {
            cols[0].Width = new GridLength(0.5, GridUnitType.Star);
            cols[1].Width = new GridLength(6);
            cols[2].Width = new GridLength(0.5, GridUnitType.Star);
        }
        else
        {
            cols[0].Width = new GridLength(1, GridUnitType.Star);
            cols[1].Width = new GridLength(0);
            cols[2].Width = new GridLength(0);
        }
    }

    private void SetHints(bool editing)
    {
        HintText.Text = editing ? "Editing mode • Ctrl+S save • Ctrl+R reset • ESC close" : "Select a config to edit • ESC closes this window";
    }

    private void ApplySyntaxHighlighting(string path)
    {
        var def = HighlightingManager.Instance.GetDefinitionByExtension(".json");
        if (def != null)
        {
            TweakHighlighting(def);
            EditorText.SyntaxHighlighting = def;
        }
    }

    private static void TweakHighlighting(IHighlightingDefinition def)
    {
        static void SetColor(HighlightingColor? c, string? fg = null, string? bg = null, FontWeight? weight = null, FontStyle? style = null)
        {
            if (c == null) return;
            if (fg != null) c.Foreground = new SimpleHighlightingBrush(Color.Parse(fg));
            if (bg != null) c.Background = new SimpleHighlightingBrush(Color.Parse(bg));
            if (weight.HasValue) c.FontWeight = weight.Value;
            if (style.HasValue) c.FontStyle = style.Value;
        }

        HighlightingColor? F(string name)
        {
            return def.NamedHighlightingColors?.FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
        }

        SetColor(F("Text"), "#EDEDED");
        SetColor(F("Default"), "#EDEDED");
        SetColor(F("Literal"), "#E6DB74");
        SetColor(F("Number"), "#c1a0fd");
        SetColor(F("String"), "#E6DB74");
        SetColor(F("Char"), "#E6DB74");
        SetColor(F("VerbatimString"), "#E6DB74");
        SetColor(F("EscapeSequence"), "#FFA94D");
        SetColor(F("Comment"), "#6272A4");
        SetColor(F("DocComment"), "#7C8BB0");
        SetColor(F("Preprocessor"), "#6FB3D2");
        SetColor(F("Keyword"), "#FF8A00");
        SetColor(F("ControlKeyword"), "#FF8A00");
        SetColor(F("ValueKeyword"), "#FF8A00");
        SetColor(F("Operator"), "#D7DAE0");
        SetColor(F("Punctuation"), "#D7DAE0");
        SetColor(F("Delimiter"), "#D7DAE0");
        SetColor(F("Identifier"), "#EDEDED");
        SetColor(F("Namespace"), "#7AA2F7");
        SetColor(F("Class"), "#7AA2F7");
        SetColor(F("Struct"), "#7AA2F7");
        SetColor(F("Interface"), "#7AA2F7");
        SetColor(F("Enum"), "#7AA2F7");
        SetColor(F("Type"), "#7AA2F7");
        SetColor(F("KnownType"), "#7AA2F7");
        SetColor(F("ValueType"), "#7AA2F7");
        SetColor(F("Field"), "#EBDDAA");
        SetColor(F("Property"), "#EBDDAA");
        SetColor(F("Event"), "#EBDDAA");
        SetColor(F("Method"), "#A6E3A1");
        SetColor(F("MethodCall"), "#A6E3A1");
        SetColor(F("Parameter"), "#EBDDAA");
        SetColor(F("Variable"), "#EBDDAA");
        SetColor(F("Label"), "#B0BEC5");
        SetColor(F("XML Name"), "#7AA2F7");
        SetColor(F("XML Delimiter"), "#D7DAE0");
        SetColor(F("XML Comment"), "#6272A4");
        SetColor(F("XML CData"), "#E6DB74");
        SetColor(F("XML Attribute"), "#EBDDAA");
        SetColor(F("XML Attribute Value"), "#E6DB74");
        SetColor(F("XML Doc Tag"), "#7C8BB0");
        SetColor(F("XML Doc Attribute"), "#7C8BB0");
        SetColor(F("Tag"), "#7AA2F7");
        SetColor(F("Attribute"), "#EBDDAA");
        SetColor(F("AttributeValue"), "#E6DB74");
        SetColor(F("Regex"), "#C792EA");
        SetColor(F("JSON Property"), "#EBDDAA");
    }

    private enum DialogResult
    {
        Primary,
        Secondary,
        Cancel
    }

    private Task<DialogResult> Show3ChoiceDialogAsync(string title, string message, string primary, string secondary, string cancel)
    {
        var tcs = new TaskCompletionSource<DialogResult>();

        var w = new Window
        {
            Title = title,
            Width = 420,
            Height = 180,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
            SystemDecorations = SystemDecorations.BorderOnly
        };

        var txt = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) };
        var btnPrimary = new Button { Content = primary, MinWidth = 90 };
        var btnSecondary = new Button { Content = secondary, MinWidth = 90, Margin = new Thickness(8, 0, 0, 0) };
        var btnCancel = new Button { Content = cancel, MinWidth = 90, Margin = new Thickness(8, 0, 0, 0) };

        btnPrimary.Click += (_, __) =>
        {
            tcs.TrySetResult(DialogResult.Primary);
            w.Close();
        };
        btnSecondary.Click += (_, __) =>
        {
            tcs.TrySetResult(DialogResult.Secondary);
            w.Close();
        };
        btnCancel.Click += (_, __) =>
        {
            tcs.TrySetResult(DialogResult.Cancel);
            w.Close();
        };

        var grid = new Grid { RowDefinitions = new RowDefinitions("*,Auto") };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(btnPrimary);
        buttons.Children.Add(btnSecondary);
        buttons.Children.Add(btnCancel);
        Grid.SetRow(buttons, 1);
        grid.Children.Add(new ScrollViewer { Content = txt, Margin = new Thickness(0, 0, 0, 12) });
        grid.Children.Add(buttons);

        w.Content = new Border { Padding = new Thickness(16), Child = grid };
        w.Closed += (_, __) => tcs.TrySetResult(DialogResult.Cancel);

        _ = w.ShowDialog(this);
        return tcs.Task;
    }

    private void OnSplitterPointerMoved(object? sender, PointerEventArgs e)
    {
        _isDraggingSplitter = true;
        EnforceSplitterLimits();
    }

    private void OnSplitterPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        _isDraggingSplitter = false;
        EnforceSplitterLimits();
    }

    private void EnforceSplitterLimits()
    {
        if (!EditorPanel.IsVisible) return;

        var total = MainSplit.Bounds.Width;
        var split = ColSplitter.Bounds.Width;
        if (total <= 1 || total <= split + 1) return;

        var usable = total - split;
        var left = LeftPanel.Bounds.Width;
        var right = EditorPanel.Bounds.Width;

        if (left <= 0 && right <= 0) return;

        var frac = left / usable;
        var clamped = Math.Clamp(frac, MinSplitterSize, 1.0 - MinSplitterSize);

        if (Math.Abs(clamped - frac) > 0.001 || _isDraggingSplitter)
        {
            var cols = MainSplit.ColumnDefinitions;
            cols[0].Width = new GridLength(clamped, GridUnitType.Star);
            cols[1].Width = new GridLength(6);
            cols[2].Width = new GridLength(1.0 - clamped, GridUnitType.Star);
        }
    }

    public sealed class ConfigItem
    {
        public string DisplayPath { get; init; } = "";
        public string FullPath { get; init; } = "";
        public override string ToString() => string.IsNullOrWhiteSpace(DisplayPath) ? FullPath : DisplayPath;
    }
}