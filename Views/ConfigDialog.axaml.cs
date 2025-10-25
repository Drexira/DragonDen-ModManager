using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using AvaloniaEdit.Highlighting;

namespace DragonDen.ModManager.Views;

public partial class ConfigDialog : Window
{
    private ColumnDefinition? _leftCol;
    private ColumnDefinition? _spacerCol;
    private ColumnDefinition? _rightCol;

    private static readonly HashSet<string> EditableExts = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".json", ".json5", ".jsonc", ".cfg", ".ini", ".toml", ".yml", ".yaml", ".xml", ".cs", ".js"
    };

    public sealed class ConfigItem
    {
        public string DisplayPath { get; init; } = "";
        public string FullPath { get; init; } = "";
        public override string ToString() => string.IsNullOrWhiteSpace(DisplayPath) ? FullPath : DisplayPath;
    }

    private string? _currentFile;
    private string _original = "";
    private bool _suppressDirty;
    private bool _frozenAfterFirstEdit;
    private double _frozenW;
    private double _frozenH;

    public ConfigDialog()
    {
        InitializeComponent();

        SizeToContent = SizeToContent.WidthAndHeight;
        EditorText.Options.HighlightCurrentLine = true;
        EditorText.Background = new SolidColorBrush(Color.Parse("#1B1B1B"));
        EditorText.Foreground = new SolidColorBrush(Color.Parse("#1B1B1B"));
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
            if (pos.Y <= 36 && e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            {
                try
                {
                    BeginMoveDrag(e);
                }
                catch
                {
                    // good girl action
                }
            }
        };

        AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                if (EditorPanel.IsVisible) CloseEditor();
                else Close();
            }

            if (e.Key == Key.S && e.KeyModifiers.HasFlag(KeyModifiers.Control)) SaveCurrent();
            if (e.Key == Key.R && e.KeyModifiers.HasFlag(KeyModifiers.Control)) ResetEdits();
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

    private void OnCloseClicked(object? s, RoutedEventArgs e)
    {
        Close();
    }

    private void OnSelectionChanged(object? s, SelectionChangedEventArgs e)
    {
        var item = FilesList?.SelectedItem as ConfigItem;
        if (item is null || !File.Exists(item.FullPath))
        {
            CloseEditor();
            return;
        }

        try
        {
            _currentFile = item.FullPath;
            _original = File.ReadAllText(item.FullPath, new UTF8Encoding(false, true));
            _suppressDirty = true;
            EditorText.Text = _original;
            _suppressDirty = false;

            EditorFileName.Text = Path.GetFileName(item.FullPath);
            DirtyDot.IsVisible = false;
            SaveBtn.IsEnabled = false;
            ResetBtn.IsEnabled = false;

            ApplySyntaxHighlighting(item.FullPath);
            ToggleEditor(true);
            SetHints(true);
        }
        catch
        {
            App.Toasts?.Show("Could not open file.");
            CloseEditor();
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

    private void OnSaveClicked(object? s, RoutedEventArgs e) => SaveCurrent();
    private void OnResetClicked(object? s, RoutedEventArgs e) => ResetEdits();
    private void OnCloseEditorClicked(object? s, RoutedEventArgs e) => CloseEditor();

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
            App.Toasts?.Show("Saved.");
        }
        catch
        {
            App.Toasts?.Show("Save failed.");
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

    private void CloseEditor()
    {
        _currentFile = null;
        _original = "";
        _suppressDirty = true;
        EditorText.Text = "";
        _suppressDirty = false;

        ToggleEditor(false);
        SetHints(false);
        FilesList.SelectedItem = null;
    }

    private async void ToggleEditor(bool show)
    {
        var cols = MainSplit.ColumnDefinitions;
        if (cols.Count < 2) return;

        EditorPanel.IsVisible = show;

        if (show)
        {
            Grid.SetColumnSpan(LeftPanel, 1);
            cols[1].Width = new GridLength(1, GridUnitType.Star);
        }
        else
        {
            cols[1].Width = new GridLength(0);
            Grid.SetColumnSpan(LeftPanel, 2);
        }

        SizeToContent = SizeToContent.WidthAndHeight;
        await Dispatcher.UIThread.InvokeAsync(() => { });
        Width = Bounds.Width;
        Height = Bounds.Height;
        SizeToContent = SizeToContent.Manual;
    }

    private void SetHints(bool editing)
    {
        if (editing)
        {
            HintText.Text = "Editing mode";
            HintText2.Text = "Ctrl+S save • Ctrl+R reset • ESC close";
        }
        else
        {
            HintText.Text = "Select a config to edit";
            HintText2.Text = "ESC closes this window";
        }
    }

    private void ApplySyntaxHighlighting(string path)
    {
        var ext = (Path.GetExtension(path) ?? "").ToLowerInvariant();
        IHighlightingDefinition? def = null;
        
        /*if (ext is ".txt" or ".json" or ".json5" or ".jsonc" or ".cfg" or ".ini" or ".toml" or ".yml" or ".yaml" or ".xml" or ".cs" or ".js")
            def = HighlightingManager.Instance.GetDefinitionByExtension(".json");
        else */
            def = HighlightingManager.Instance.GetDefinitionByExtension(".json");

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

        HighlightingColor? F(string name) =>
            def.NamedHighlightingColors?.FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));

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
}