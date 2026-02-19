using System.ComponentModel;
using System.IO;
using System.Printing;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Folding;
using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.AvalonEdit.Rendering;
using ICSharpCode.AvalonEdit.Search;
using Caret.Dialogs;
using Caret.Helpers;
using Caret.Models;

namespace Caret;

public partial class MainWindow : Window
{
    private readonly List<DocumentModel> _documents = new();
    private readonly Dictionary<DocumentModel, TextEditor> _editors = new();
    private readonly Dictionary<DocumentModel, FoldingManager?> _foldingManagers = new();
    private readonly Dictionary<DocumentModel, System.Windows.Threading.DispatcherTimer> _autoDetectTimers = new();
    private FindReplaceWindow? _findReplaceWindow;
    private bool _suppressTabChanged;
    private const double DefaultFontSize = 14.0;
    private const double ZoomStep = 2.0;
    private const long LargeFileThreshold = 10 * 1024 * 1024;
    private const int EncodingSampleSize = 64 * 1024;
    private bool _wordWrap;
    private bool _showWhiteSpace;
    private bool _showEndOfLine;
    private bool _showLineNumbers = true;
    private bool _showIndentGuide = true;

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    public MainWindow()
    {
        InitializeComponent();
        PopulateLanguageMenu();
        LoadRecentFiles();
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            int darkMode = 1;
            DwmSetWindowAttribute(hwnd, 20, ref darkMode, sizeof(int));
        }
        catch { }

        RestoreSession();

        if (_documents.Count == 0)
            CreateNewDocument();

        CheckForUpdatesSilent();
    }

    private void Window_Closing(object sender, CancelEventArgs e)
    {
        SaveSession();
        _findReplaceWindow?.Close();
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        var shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        var alt = Keyboard.Modifiers.HasFlag(ModifierKeys.Alt);

        if (ctrl && !shift && !alt)
        {
            switch (e.Key)
            {
                case Key.N: New_Click(sender, e); e.Handled = true; break;
                case Key.O: Open_Click(sender, e); e.Handled = true; break;
                case Key.S: Save_Click(sender, e); e.Handled = true; break;
                case Key.W: Close_Click(sender, e); e.Handled = true; break;
                case Key.F: Find_Click(sender, e); e.Handled = true; break;
                case Key.H: Replace_Click(sender, e); e.Handled = true; break;
                case Key.G: GoToLine_Click(sender, e); e.Handled = true; break;
                case Key.D: DuplicateLine_Click(sender, e); e.Handled = true; break;
                case Key.P: Print_Click(sender, e); e.Handled = true; break;
                case Key.OemPlus: case Key.Add: ZoomIn_Click(sender, e); e.Handled = true; break;
                case Key.OemMinus: case Key.Subtract: ZoomOut_Click(sender, e); e.Handled = true; break;
                case Key.D0: case Key.NumPad0: ResetZoom_Click(sender, e); e.Handled = true; break;
                case Key.U: ToLowerCase_Click(sender, e); e.Handled = true; break;
                case Key.OemQuestion: ToggleComment_Click(sender, e); e.Handled = true; break;
            }
        }
        else if (ctrl && shift && !alt)
        {
            switch (e.Key)
            {
                case Key.S: SaveAs_Click(sender, e); e.Handled = true; break;
                case Key.U: ToUpperCase_Click(sender, e); e.Handled = true; break;
            }
        }
        else if (!ctrl && !shift && !alt)
        {
            switch (e.Key)
            {
                case Key.F3: FindNext_Click(sender, e); e.Handled = true; break;
            }
        }
        else if (!ctrl && shift && !alt)
        {
            switch (e.Key)
            {
                case Key.F3: FindPrevious_Click(sender, e); e.Handled = true; break;
            }
        }
        else if (alt && !ctrl && !shift)
        {
            switch (e.Key)
            {
                case Key.Up: MoveLineUp_Click(sender, e); e.Handled = true; break;
                case Key.Down: MoveLineDown_Click(sender, e); e.Handled = true; break;
            }
        }

        if (ctrl && e.Key == Key.Tab)
        {
            if (TabDocuments.Items.Count > 1)
            {
                int idx = TabDocuments.SelectedIndex;
                idx = shift ? idx - 1 : idx + 1;
                if (idx < 0) idx = TabDocuments.Items.Count - 1;
                if (idx >= TabDocuments.Items.Count) idx = 0;
                TabDocuments.SelectedIndex = idx;
            }
            e.Handled = true;
        }
    }

    private DocumentModel CreateNewDocument(string? filePath = null)
    {
        var doc = new DocumentModel();
        if (filePath != null)
        {
            doc.FilePath = filePath;
            var highlighting = SyntaxHighlightingManager.GetHighlightingByExtension(filePath);
            doc.SyntaxHighlighting = highlighting;
            doc.Language = SyntaxHighlightingManager.GetLanguageNameByExtension(filePath);
            if (highlighting != null)
                doc.AutoDetectLanguage = false;
        }

        _documents.Add(doc);
        var editor = CreateEditor(doc);
        _editors[doc] = editor;

        var tabItem = CreateTabItem(doc, editor);
        _suppressTabChanged = true;
        TabDocuments.Items.Add(tabItem);
        TabDocuments.SelectedItem = tabItem;
        _suppressTabChanged = false;

        UpdateStatusBar();
        UpdateTitle();
        return doc;
    }

    private TextEditor CreateEditor(DocumentModel doc)
    {
        var editor = new TextEditor
        {
            Document = doc.Document,
            FontFamily = new FontFamily("Consolas"),
            FontSize = doc.FontSize,
            Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x1E)),
            Foreground = new SolidColorBrush(Color.FromRgb(0xD4, 0xD4, 0xD4)),
            LineNumbersForeground = new SolidColorBrush(Color.FromRgb(0x85, 0x85, 0x85)),
            ShowLineNumbers = _showLineNumbers,
            WordWrap = _wordWrap,
            HorizontalScrollBarVisibility = _wordWrap ? ScrollBarVisibility.Disabled : ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Padding = new Thickness(4, 0, 0, 0),
            SyntaxHighlighting = doc.SyntaxHighlighting,
        };

        editor.Options.EnableHyperlinks = true;
        editor.Options.EnableEmailHyperlinks = true;
        editor.Options.EnableRectangularSelection = true;
        editor.Options.HighlightCurrentLine = true;
        editor.Options.ShowTabs = _showWhiteSpace;
        editor.Options.ShowSpaces = _showWhiteSpace;
        editor.Options.ShowEndOfLine = _showEndOfLine;
        editor.Options.ConvertTabsToSpaces = false;
        editor.Options.IndentationSize = 4;
        editor.Options.EnableTextDragDrop = true;

        editor.TextArea.TextView.CurrentLineBackground = new SolidColorBrush(Color.FromArgb(40, 0xFF, 0xFF, 0xFF));
        editor.TextArea.TextView.CurrentLineBorder = new Pen(new SolidColorBrush(Color.FromArgb(25, 0xFF, 0xFF, 0xFF)), 1);
        editor.TextArea.SelectionBrush = new SolidColorBrush(Color.FromRgb(0x26, 0x4F, 0x78));
        editor.TextArea.SelectionForeground = null;
        editor.TextArea.SelectionCornerRadius = 0;

        editor.TextArea.Caret.CaretBrush = new SolidColorBrush(Colors.White);

        editor.TextArea.Caret.PositionChanged += (s, e) => UpdateStatusBar();
        editor.TextArea.SelectionChanged += (s, e) => UpdateStatusBar();
        editor.Document.TextChanged += (s, e) =>
        {
            if (!_suppressTabChanged)
            {
                doc.IsModified = true;
                UpdateTabHeader(doc);
                UpdateTitle();
            }

            if (doc.AutoDetectLanguage && !doc.IsLargeFile)
                ScheduleAutoDetect(doc, editor);
        };

        editor.PreviewMouseWheel += (s, e) =>
        {
            if (Keyboard.Modifiers == ModifierKeys.Control)
            {
                if (e.Delta > 0)
                    doc.FontSize += ZoomStep;
                else
                    doc.FontSize -= ZoomStep;
                editor.FontSize = doc.FontSize;
                UpdateStatusBar();
                e.Handled = true;
            }
        };

        SetupFolding(doc, editor);

        return editor;
    }

    private void SetupFolding(DocumentModel doc, TextEditor editor)
    {
        if (_foldingManagers.TryGetValue(doc, out var existingFm) && existingFm != null)
        {
            try { FoldingManager.Uninstall(existingFm); } catch { }
        }

        if (doc.IsLargeFile)
        {
            _foldingManagers[doc] = null;
            return;
        }

        if (doc.SyntaxHighlighting != null)
        {
            var foldingManager = FoldingManager.Install(editor.TextArea);
            _foldingManagers[doc] = foldingManager;

            var name = doc.SyntaxHighlighting.Name;
            bool isXml = name == "XML" || name == "HTML" || name == "ASP/XHTML";

            XmlFoldingStrategy? xmlStrategy = isXml ? new XmlFoldingStrategy() : null;
            BraceFoldingStrategy? braceStrategy = !isXml ? new BraceFoldingStrategy() : null;

            Action updateFoldings = () =>
            {
                if (_foldingManagers.TryGetValue(doc, out var fm) && fm != null)
                {
                    try
                    {
                        if (xmlStrategy != null)
                            xmlStrategy.UpdateFoldings(fm, editor.Document);
                        else
                            braceStrategy?.UpdateFoldings(fm, editor.Document);
                    }
                    catch { }
                }
            };

            var timer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(2)
            };
            timer.Tick += (s, e) => updateFoldings();
            timer.Start();

            editor.Dispatcher.BeginInvoke(updateFoldings,
                System.Windows.Threading.DispatcherPriority.Background);
        }
        else
        {
            _foldingManagers[doc] = null;
        }
    }

    private TabItem CreateTabItem(DocumentModel doc, TextEditor editor)
    {
        var headerPanel = new StackPanel { Orientation = Orientation.Horizontal };

        var titleText = new TextBlock
        {
            Text = doc.Title,
            Foreground = new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC)),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(4, 0, 8, 0),
            FontSize = 12,
        };

        var closeButton = new Button
        {
            Content = "✕",
            FontSize = 10,
            Width = 20,
            Height = 20,
            Padding = new Thickness(0),
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent,
            Foreground = new SolidColorBrush(Color.FromRgb(0x80, 0x80, 0x80)),
            Cursor = Cursors.Hand,
            VerticalAlignment = VerticalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            ToolTip = "Close",
        };

        closeButton.MouseEnter += (s, e) =>
        {
            closeButton.Foreground = Brushes.White;
            closeButton.Background = new SolidColorBrush(Color.FromRgb(0x60, 0x60, 0x60));
        };
        closeButton.MouseLeave += (s, e) =>
        {
            closeButton.Foreground = new SolidColorBrush(Color.FromRgb(0x80, 0x80, 0x80));
            closeButton.Background = Brushes.Transparent;
        };
        closeButton.Click += (s, e) =>
        {
            e.Handled = true;
            CloseDocument(doc);
        };

        headerPanel.Children.Add(titleText);
        headerPanel.Children.Add(closeButton);

        var tabItem = new TabItem
        {
            Header = headerPanel,
            Content = editor,
            Tag = doc,
            Background = new SolidColorBrush(Color.FromRgb(0x25, 0x25, 0x26)),
            Foreground = new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x3F, 0x3F, 0x46)),
            BorderThickness = new Thickness(0),
            Padding = new Thickness(8, 4, 4, 4),
        };

        var contextMenu = new ContextMenu();
        contextMenu.Items.Add(CreateContextMenuItem("Close", () => CloseDocument(doc)));
        contextMenu.Items.Add(CreateContextMenuItem("Close Others", () => CloseOtherDocuments(doc)));
        contextMenu.Items.Add(CreateContextMenuItem("Close All", () => CloseAllDocuments()));
        contextMenu.Items.Add(new Separator());
        contextMenu.Items.Add(CreateContextMenuItem("Close Tabs to the Left", () => CloseTabsToLeft(doc)));
        contextMenu.Items.Add(CreateContextMenuItem("Close Tabs to the Right", () => CloseTabsToRight(doc)));
        contextMenu.Items.Add(new Separator());
        contextMenu.Items.Add(CreateContextMenuItem("Save", () => SaveDocument(doc)));
        contextMenu.Items.Add(CreateContextMenuItem("Save As...", () => SaveDocumentAs(doc)));
        contextMenu.Items.Add(new Separator());
        contextMenu.Items.Add(CreateContextMenuItem("Copy File Path", () =>
        {
            if (doc.FilePath != null) Clipboard.SetText(doc.FilePath);
        }));
        contextMenu.Items.Add(CreateContextMenuItem("Copy File Name", () =>
        {
            Clipboard.SetText(doc.FileName);
        }));
        contextMenu.Items.Add(CreateContextMenuItem("Open Containing Folder", () =>
        {
            if (doc.FilePath != null)
            {
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    UseShellExecute = false,
                };
                psi.ArgumentList.Add("/select,");
                psi.ArgumentList.Add(doc.FilePath);
                System.Diagnostics.Process.Start(psi);
            }
        }));

        tabItem.ContextMenu = contextMenu;

        return tabItem;
    }

    private MenuItem CreateContextMenuItem(string header, Action action)
    {
        var item = new MenuItem { Header = header };
        item.Click += (s, e) => action();
        return item;
    }

    private void UpdateTabHeader(DocumentModel doc)
    {
        foreach (TabItem tabItem in TabDocuments.Items)
        {
            if (tabItem.Tag == doc && tabItem.Header is StackPanel panel)
            {
                var titleText = panel.Children[0] as TextBlock;
                if (titleText != null)
                    titleText.Text = doc.Title;
                break;
            }
        }
    }

    private DocumentModel? GetActiveDocument()
    {
        return (TabDocuments.SelectedItem as TabItem)?.Tag as DocumentModel;
    }

    private TextEditor? GetActiveEditor()
    {
        var doc = GetActiveDocument();
        return doc != null && _editors.TryGetValue(doc, out var editor) ? editor : null;
    }

    private void TabDocuments_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressTabChanged) return;

        UpdateStatusBar();
        UpdateTitle();
        UpdateEncodingMenu();
        UpdateLanguageMenu();

        var editor = GetActiveEditor();
        editor?.Focus();
    }

    private static readonly HashSet<string> ReservedDeviceNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    private static readonly HashSet<string> BinaryExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".dll", ".so", ".dylib", ".bin", ".obj", ".o", ".a", ".lib",
        ".pdb", ".ilk", ".exp", ".com", ".sys", ".drv", ".ocx", ".cpl", ".scr",
        ".msi", ".msp", ".msm", ".cab",
        ".iso", ".img", ".vhd", ".vhdx", ".vmdk",
        ".zip", ".7z", ".rar", ".gz", ".tar", ".bz2", ".xz", ".zst", ".lz4",
        ".jar", ".war", ".ear", ".apk", ".aab", ".ipa", ".deb", ".rpm", ".snap",
        ".class", ".pyc", ".pyo", ".wasm",
        ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx",
        ".odt", ".ods", ".odp",
        ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".ico", ".tif", ".tiff",
        ".webp", ".avif", ".heic", ".heif", ".raw", ".cr2", ".nef", ".psd",
        ".mp3", ".wav", ".flac", ".aac", ".ogg", ".wma", ".m4a", ".opus",
        ".mp4", ".mkv", ".avi", ".mov", ".wmv", ".flv", ".webm", ".m4v",
        ".ttf", ".otf", ".woff", ".woff2", ".eot",
        ".sqlite", ".db", ".mdb", ".accdb",
        ".dat", ".pak", ".asset", ".unity3d", ".unitypackage",
    };

    private static bool IsBinaryFile(string filePath)
    {
        var ext = Path.GetExtension(filePath);
        return !string.IsNullOrEmpty(ext) && BinaryExtensions.Contains(ext);
    }

    private static bool IsUnsafePath(string filePath)
    {
        try
        {
            var fileName = Path.GetFileNameWithoutExtension(filePath);
            if (ReservedDeviceNames.Contains(fileName))
                return true;

            var fullPath = Path.GetFullPath(filePath);
            if (!fullPath.StartsWith(@"\\?\") && fullPath.Contains('\0'))
                return true;

            return false;
        }
        catch
        {
            return true;
        }
    }

    public async void OpenFile(string filePath)
    {
        if (IsUnsafePath(filePath))
        {
            MessageBox.Show("Cannot open this file path.", "Error",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (IsBinaryFile(filePath))
        {
            MessageBox.Show(
                $"'{Path.GetFileName(filePath)}' appears to be a binary file and cannot be opened as text.",
                "Binary File",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var existing = _documents.FirstOrDefault(d =>
            string.Equals(d.FilePath, filePath, StringComparison.OrdinalIgnoreCase));
        if (existing != null)
        {
            SelectDocument(existing);
            return;
        }

        try
        {
            var fileInfo = new FileInfo(filePath);
            long fileSize = fileInfo.Exists ? fileInfo.Length : 0;
            bool isLargeFile = fileSize > LargeFileThreshold;

            if (isLargeFile)
                ShowLoadingOverlay($"Opening {fileInfo.Name} ({FormatFileSize(fileSize)})...");

            var (encoding, content, lineEnding) = await Task.Run(() =>
            {
                var enc = DetectEncoding(filePath);
                var text = File.ReadAllText(filePath, enc);
                var le = DetectLineEnding(text);
                return (enc, text, le);
            });

            if (_documents.Count == 1 && !_documents[0].IsModified &&
                _documents[0].FilePath == null && _documents[0].Document.TextLength == 0)
            {
                CloseDocument(_documents[0], force: true);
            }

            DocumentModel doc;
            if (isLargeFile)
            {
                doc = CreateNewDocument(null);
                doc.FilePath = filePath;
                doc.Language = SyntaxHighlightingManager.GetLanguageNameByExtension(filePath);
                doc.IsLargeFile = true;
                doc.FileSize = fileSize;
                doc.AutoDetectLanguage = false;

                if (_editors.TryGetValue(doc, out var largeEditor))
                {
                    largeEditor.SyntaxHighlighting = null;
                }
            }
            else
            {
                doc = CreateNewDocument(filePath);
                doc.FileSize = fileSize;
            }

            doc.Encoding = encoding;
            doc.LineEnding = lineEnding;

            _suppressTabChanged = true;
            doc.Document.Text = content;
            doc.IsModified = false;
            _suppressTabChanged = false;

            if (!isLargeFile && doc.AutoDetectLanguage && _editors.TryGetValue(doc, out var ed))
            {
                RunAutoDetect(doc, ed);
                if (doc.SyntaxHighlighting != null)
                    doc.AutoDetectLanguage = false;
            }

            UpdateTabHeader(doc);
            UpdateTitle();
            UpdateStatusBar();
            UpdateEncodingMenu();
            UpdateLanguageMenu();

            RecentFilesManager.AddFile(filePath);
            LoadRecentFiles();

            if (isLargeFile)
                HideLoadingOverlay();
        }
        catch (Exception ex)
        {
            HideLoadingOverlay();
            MessageBox.Show($"Error opening file:\n{ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ShowLoadingOverlay(string message)
    {
        LoadingText.Text = message;
        LoadingOverlay.Visibility = Visibility.Visible;
    }

    private void HideLoadingOverlay()
    {
        LoadingOverlay.Visibility = Visibility.Collapsed;
    }

    private static string FormatFileSize(long bytes)
    {
        if (bytes >= 1024 * 1024 * 1024)
            return $"{bytes / (1024.0 * 1024 * 1024):F1} GB";
        if (bytes >= 1024 * 1024)
            return $"{bytes / (1024.0 * 1024):F1} MB";
        if (bytes >= 1024)
            return $"{bytes / 1024.0:F1} KB";
        return $"{bytes} B";
    }

    private void SaveDocument(DocumentModel doc)
    {
        if (doc.FilePath == null)
        {
            SaveDocumentAs(doc);
            return;
        }

        try
        {
            var text = doc.Document.Text;
            File.WriteAllText(doc.FilePath, text, doc.Encoding);
            doc.IsModified = false;
            UpdateTabHeader(doc);
            UpdateTitle();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error saving file:\n{ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void SaveDocumentAs(DocumentModel doc)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Filter = BuildFileFilter(),
            FileName = doc.FileName,
            Title = "Save As",
        };

        if (doc.FilePath != null)
            dialog.InitialDirectory = Path.GetDirectoryName(doc.FilePath);

        if (dialog.ShowDialog() == true)
        {
            doc.FilePath = dialog.FileName;

            var highlighting = SyntaxHighlightingManager.GetHighlightingByExtension(dialog.FileName);
            doc.SyntaxHighlighting = highlighting;
            doc.Language = SyntaxHighlightingManager.GetLanguageNameByExtension(dialog.FileName);

            if (_editors.TryGetValue(doc, out var editor))
            {
                editor.SyntaxHighlighting = highlighting;
                SetupFolding(doc, editor);
            }

            SaveDocument(doc);
            UpdateTabHeader(doc);
            UpdateTitle();
            UpdateStatusBar();
            UpdateLanguageMenu();

            RecentFilesManager.AddFile(dialog.FileName);
            LoadRecentFiles();
        }
    }

    private void CloseDocument(DocumentModel doc, bool force = false)
    {
        if (!force && doc.IsModified)
        {
            var result = MessageBox.Show(
                $"Save changes to '{doc.FileName}'?",
                "Caret",
                MessageBoxButton.YesNoCancel,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Cancel)
                return;
            if (result == MessageBoxResult.Yes)
            {
                SaveDocument(doc);
                if (doc.IsModified) return;
            }
        }

        StopAutoDetectTimer(doc);

        if (_foldingManagers.TryGetValue(doc, out var fm))
        {
            if (fm != null && _editors.TryGetValue(doc, out var editor))
            {
                try { FoldingManager.Uninstall(fm); } catch { }
            }
            _foldingManagers.Remove(doc);
        }

        _editors.Remove(doc);
        _documents.Remove(doc);

        TabItem? tabToRemove = null;
        foreach (TabItem tab in TabDocuments.Items)
        {
            if (tab.Tag == doc)
            {
                tabToRemove = tab;
                break;
            }
        }
        if (tabToRemove != null)
        {
            _suppressTabChanged = true;
            TabDocuments.Items.Remove(tabToRemove);
            _suppressTabChanged = false;
        }

        if (_documents.Count == 0)
        {
            CreateNewDocument();
        }
        else
        {
            if (TabDocuments.SelectedIndex < 0)
                TabDocuments.SelectedIndex = 0;
        }

        UpdateStatusBar();
        UpdateTitle();
    }

    private void CloseOtherDocuments(DocumentModel keepDoc)
    {
        var docsToClose = _documents.Where(d => d != keepDoc).ToList();
        foreach (var doc in docsToClose)
            CloseDocument(doc);
    }

    private void CloseAllDocuments()
    {
        var docsToClose = _documents.ToList();
        foreach (var doc in docsToClose)
            CloseDocument(doc);
    }

    private void CloseTabsToLeft(DocumentModel targetDoc)
    {
        var idx = _documents.IndexOf(targetDoc);
        var docsToClose = _documents.Take(idx).ToList();
        foreach (var doc in docsToClose)
            CloseDocument(doc);
    }

    private void CloseTabsToRight(DocumentModel targetDoc)
    {
        var idx = _documents.IndexOf(targetDoc);
        var docsToClose = _documents.Skip(idx + 1).ToList();
        foreach (var doc in docsToClose)
            CloseDocument(doc);
    }

    private void SelectDocument(DocumentModel doc)
    {
        foreach (TabItem tab in TabDocuments.Items)
        {
            if (tab.Tag == doc)
            {
                TabDocuments.SelectedItem = tab;
                break;
            }
        }
    }

    private void New_Click(object sender, RoutedEventArgs e) => CreateNewDocument();

    private void Open_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = BuildFileFilter(),
            Multiselect = true,
            Title = "Open File",
        };

        if (dialog.ShowDialog() == true)
        {
            foreach (var file in dialog.FileNames)
                OpenFile(file);
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var doc = GetActiveDocument();
        if (doc != null) SaveDocument(doc);
    }

    private void SaveAs_Click(object sender, RoutedEventArgs e)
    {
        var doc = GetActiveDocument();
        if (doc != null) SaveDocumentAs(doc);
    }

    private void SaveAll_Click(object sender, RoutedEventArgs e)
    {
        foreach (var doc in _documents.Where(d => d.IsModified))
            SaveDocument(doc);
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        var doc = GetActiveDocument();
        if (doc != null) CloseDocument(doc);
    }

    private void CloseAll_Click(object sender, RoutedEventArgs e) => CloseAllDocuments();

    private void Print_Click(object sender, RoutedEventArgs e)
    {
        var editor = GetActiveEditor();
        if (editor == null) return;

        var printDialog = new PrintDialog();
        if (printDialog.ShowDialog() == true)
        {
            var doc = new FlowDocument(new Paragraph(new Run(editor.Text)))
            {
                FontFamily = new FontFamily("Consolas"),
                FontSize = 11,
                PagePadding = new Thickness(50),
            };

            var paginator = ((IDocumentPaginatorSource)doc).DocumentPaginator;
            paginator.PageSize = new Size(printDialog.PrintableAreaWidth, printDialog.PrintableAreaHeight);
            printDialog.PrintDocument(paginator, GetActiveDocument()?.FileName ?? "Print");
        }
    }

    private void Exit_Click(object sender, RoutedEventArgs e) => Close();

    private void Undo_Click(object sender, RoutedEventArgs e)
    {
        var editor = GetActiveEditor();
        if (editor?.CanUndo == true) editor.Undo();
    }

    private void Redo_Click(object sender, RoutedEventArgs e)
    {
        var editor = GetActiveEditor();
        if (editor?.CanRedo == true) editor.Redo();
    }

    private void Cut_Click(object sender, RoutedEventArgs e) => GetActiveEditor()?.Cut();
    private void Copy_Click(object sender, RoutedEventArgs e) => GetActiveEditor()?.Copy();
    private void Paste_Click(object sender, RoutedEventArgs e) => GetActiveEditor()?.Paste();
    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        var editor = GetActiveEditor();
        if (editor != null && editor.SelectionLength > 0)
            editor.Document.Remove(editor.SelectionStart, editor.SelectionLength);
    }

    private void SelectAll_Click(object sender, RoutedEventArgs e) => GetActiveEditor()?.SelectAll();

    private void DuplicateLine_Click(object sender, RoutedEventArgs e)
    {
        var editor = GetActiveEditor();
        if (editor == null) return;

        var line = editor.Document.GetLineByOffset(editor.CaretOffset);
        var lineText = editor.Document.GetText(line.Offset, line.TotalLength);

        if (line.DelimiterLength == 0)
            lineText = Environment.NewLine + editor.Document.GetText(line.Offset, line.Length);

        editor.Document.Insert(line.Offset + line.TotalLength, lineText);
    }

    private void MoveLineUp_Click(object sender, RoutedEventArgs e)
    {
        var editor = GetActiveEditor();
        if (editor == null) return;

        var line = editor.Document.GetLineByOffset(editor.CaretOffset);
        if (line.LineNumber <= 1) return;

        var prevLine = editor.Document.GetLineByNumber(line.LineNumber - 1);
        var currentText = editor.Document.GetText(line.Offset, line.Length);
        var prevText = editor.Document.GetText(prevLine.Offset, prevLine.Length);

        editor.Document.BeginUpdate();
        editor.Document.Replace(prevLine.Offset, prevLine.Length, currentText);
        var newCurrentLine = editor.Document.GetLineByNumber(line.LineNumber);
        editor.Document.Replace(newCurrentLine.Offset, newCurrentLine.Length, prevText);
        editor.Document.EndUpdate();

        editor.CaretOffset = prevLine.Offset;
    }

    private void MoveLineDown_Click(object sender, RoutedEventArgs e)
    {
        var editor = GetActiveEditor();
        if (editor == null) return;

        var line = editor.Document.GetLineByOffset(editor.CaretOffset);
        if (line.LineNumber >= editor.Document.LineCount) return;

        var nextLine = editor.Document.GetLineByNumber(line.LineNumber + 1);
        var currentText = editor.Document.GetText(line.Offset, line.Length);
        var nextText = editor.Document.GetText(nextLine.Offset, nextLine.Length);

        editor.Document.BeginUpdate();
        editor.Document.Replace(line.Offset, line.Length, nextText);
        var newNextLine = editor.Document.GetLineByNumber(line.LineNumber + 1);
        editor.Document.Replace(newNextLine.Offset, newNextLine.Length, currentText);
        editor.Document.EndUpdate();

        editor.CaretOffset = newNextLine.Offset;
    }

    private void ToggleComment_Click(object sender, RoutedEventArgs e)
    {
        var editor = GetActiveEditor();
        var doc = GetActiveDocument();
        if (editor == null || doc == null) return;

        var commentPrefix = GetCommentPrefix(doc.SyntaxHighlighting?.Name);
        if (commentPrefix == null) return;

        var line = editor.Document.GetLineByOffset(editor.CaretOffset);
        var lineText = editor.Document.GetText(line.Offset, line.Length);
        var trimmed = lineText.TrimStart();

        editor.Document.BeginUpdate();
        if (trimmed.StartsWith(commentPrefix))
        {
            var commentStart = line.Offset + lineText.IndexOf(commentPrefix);
            var removeLen = commentPrefix.Length;
            if (commentStart + removeLen < line.EndOffset &&
                editor.Document.GetCharAt(commentStart + removeLen) == ' ')
                removeLen++;
            editor.Document.Remove(commentStart, removeLen);
        }
        else
        {
            var indent = lineText.Length - trimmed.Length;
            editor.Document.Insert(line.Offset + indent, commentPrefix + " ");
        }
        editor.Document.EndUpdate();
    }

    private string? GetCommentPrefix(string? languageName)
    {
        return languageName switch
        {
            "C#" => "//",
            "C++" => "//",
            "Java" => "//",
            "JavaScript" => "//",
            "PHP" => "//",
            "Python" => "#",
            "PowerShell" => "#",
            "BAT" => "REM",
            "HTML" => "<!--",
            "XML" => "<!--",
            "CSS" => "/*",
            "TSQL" => "--",
            "F#" => "//",
            "Boo" => "#",
            "TeX" => "%",
            _ => "//",
        };
    }

    private void ToUpperCase_Click(object sender, RoutedEventArgs e)
    {
        var editor = GetActiveEditor();
        if (editor == null || editor.SelectionLength == 0) return;
        var selected = editor.SelectedText.ToUpper();
        editor.Document.Replace(editor.SelectionStart, editor.SelectionLength, selected);
    }

    private void ToLowerCase_Click(object sender, RoutedEventArgs e)
    {
        var editor = GetActiveEditor();
        if (editor == null || editor.SelectionLength == 0) return;
        var selected = editor.SelectedText.ToLower();
        editor.Document.Replace(editor.SelectionStart, editor.SelectionLength, selected);
    }

    private void TrimTrailingSpace_Click(object sender, RoutedEventArgs e)
    {
        var editor = GetActiveEditor();
        if (editor == null) return;

        editor.Document.BeginUpdate();
        for (int i = 1; i <= editor.Document.LineCount; i++)
        {
            var line = editor.Document.GetLineByNumber(i);
            var text = editor.Document.GetText(line.Offset, line.Length);
            var trimmed = text.TrimEnd();
            if (trimmed.Length < text.Length)
            {
                editor.Document.Replace(line.Offset + trimmed.Length,
                    text.Length - trimmed.Length, "");
            }
        }
        editor.Document.EndUpdate();
    }

    private void TabToSpace_Click(object sender, RoutedEventArgs e)
    {
        var editor = GetActiveEditor();
        if (editor == null) return;
        var spaces = new string(' ', editor.Options.IndentationSize);
        editor.Document.Text = editor.Document.Text.Replace("\t", spaces);
    }

    private void SpaceToTab_Click(object sender, RoutedEventArgs e)
    {
        var editor = GetActiveEditor();
        if (editor == null) return;
        var spaces = new string(' ', editor.Options.IndentationSize);
        editor.Document.Text = editor.Document.Text.Replace(spaces, "\t");
    }

    private void Find_Click(object sender, RoutedEventArgs e)
    {
        EnsureFindReplaceWindow();
        var editor = GetActiveEditor();
        _findReplaceWindow!.ShowFind(editor?.SelectedText);
    }

    private void FindNext_Click(object sender, RoutedEventArgs e)
    {
        if (_findReplaceWindow != null)
            _findReplaceWindow.FindNext();
    }

    private void FindPrevious_Click(object sender, RoutedEventArgs e)
    {
        if (_findReplaceWindow != null)
            _findReplaceWindow.FindPrevious();
    }

    private void Replace_Click(object sender, RoutedEventArgs e)
    {
        EnsureFindReplaceWindow();
        var editor = GetActiveEditor();
        _findReplaceWindow!.ShowReplace(editor?.SelectedText);
    }

    private void GoToLine_Click(object sender, RoutedEventArgs e)
    {
        var editor = GetActiveEditor();
        if (editor == null) return;

        var currentLine = editor.TextArea.Caret.Line;
        var maxLine = editor.Document.LineCount;

        var dialog = new GoToLineWindow(currentLine, maxLine) { Owner = this };
        if (dialog.ShowDialog() == true)
        {
            editor.ScrollToLine(dialog.SelectedLine);
            var line = editor.Document.GetLineByNumber(dialog.SelectedLine);
            editor.CaretOffset = line.Offset;
            editor.Focus();
        }
    }

    private void EnsureFindReplaceWindow()
    {
        if (_findReplaceWindow == null)
        {
            _findReplaceWindow = new FindReplaceWindow(GetActiveEditor) { Owner = this };
        }
    }

    private void AlwaysOnTop_Click(object sender, RoutedEventArgs e)
    {
        Topmost = AlwaysOnTopMenu.IsChecked;
    }

    private void WordWrap_Click(object sender, RoutedEventArgs e)
    {
        _wordWrap = WordWrapMenu.IsChecked;
        foreach (var editor in _editors.Values)
        {
            editor.WordWrap = _wordWrap;
            editor.HorizontalScrollBarVisibility = _wordWrap
                ? ScrollBarVisibility.Disabled
                : ScrollBarVisibility.Auto;
        }
    }

    private void ShowWhiteSpace_Click(object sender, RoutedEventArgs e)
    {
        _showWhiteSpace = ShowWhiteSpaceMenu.IsChecked;
        foreach (var editor in _editors.Values)
        {
            editor.Options.ShowTabs = _showWhiteSpace;
            editor.Options.ShowSpaces = _showWhiteSpace;
        }
    }

    private void ShowEndOfLine_Click(object sender, RoutedEventArgs e)
    {
        _showEndOfLine = ShowEndOfLineMenu.IsChecked;
        foreach (var editor in _editors.Values)
            editor.Options.ShowEndOfLine = _showEndOfLine;
    }

    private void ShowLineNumbers_Click(object sender, RoutedEventArgs e)
    {
        _showLineNumbers = ShowLineNumbersMenu.IsChecked;
        foreach (var editor in _editors.Values)
            editor.ShowLineNumbers = _showLineNumbers;
    }

    private void ShowIndentGuide_Click(object sender, RoutedEventArgs e)
    {
        _showIndentGuide = ShowIndentGuideMenu.IsChecked;
    }

    private void ZoomIn_Click(object sender, RoutedEventArgs e)
    {
        var doc = GetActiveDocument();
        var editor = GetActiveEditor();
        if (doc != null && editor != null)
        {
            doc.FontSize += ZoomStep;
            editor.FontSize = doc.FontSize;
            UpdateStatusBar();
        }
    }

    private void ZoomOut_Click(object sender, RoutedEventArgs e)
    {
        var doc = GetActiveDocument();
        var editor = GetActiveEditor();
        if (doc != null && editor != null)
        {
            doc.FontSize -= ZoomStep;
            editor.FontSize = doc.FontSize;
            UpdateStatusBar();
        }
    }

    private void ResetZoom_Click(object sender, RoutedEventArgs e)
    {
        var doc = GetActiveDocument();
        var editor = GetActiveEditor();
        if (doc != null && editor != null)
        {
            doc.FontSize = DefaultFontSize;
            editor.FontSize = DefaultFontSize;
            UpdateStatusBar();
        }
    }

    private void FoldAll_Click(object sender, RoutedEventArgs e)
    {
        var doc = GetActiveDocument();
        if (doc != null && _foldingManagers.TryGetValue(doc, out var fm) && fm != null)
        {
            foreach (var section in fm.AllFoldings)
                section.IsFolded = true;
        }
    }

    private void UnfoldAll_Click(object sender, RoutedEventArgs e)
    {
        var doc = GetActiveDocument();
        if (doc != null && _foldingManagers.TryGetValue(doc, out var fm) && fm != null)
        {
            foreach (var section in fm.AllFoldings)
                section.IsFolded = false;
        }
    }

    private void Encoding_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem menuItem && menuItem.Tag is string encodingTag)
        {
            var doc = GetActiveDocument();
            if (doc == null) return;

            doc.Encoding = encodingTag switch
            {
                "ANSI" => Encoding.GetEncoding(1252),
                "UTF-8" => new UTF8Encoding(false),
                "UTF-8-BOM" => new UTF8Encoding(true),
                "UTF-16LE" => new UnicodeEncoding(false, true),
                "UTF-16BE" => new UnicodeEncoding(true, true),
                _ => new UTF8Encoding(false),
            };

            doc.IsModified = true;
            UpdateTabHeader(doc);
            UpdateTitle();
            UpdateStatusBar();
            UpdateEncodingMenu();
        }
    }

    private void UpdateEncodingMenu()
    {
        var doc = GetActiveDocument();
        if (doc == null) return;

        var name = doc.EncodingName;
        EncodingAnsi.IsChecked = name == "ANSI";
        EncodingUtf8.IsChecked = name == "UTF-8";
        EncodingUtf8Bom.IsChecked = name == "UTF-8-BOM";
        EncodingUtf16Le.IsChecked = name == "UCS-2 LE BOM";
        EncodingUtf16Be.IsChecked = name == "UCS-2 BE BOM";
    }

    private void PopulateLanguageMenu()
    {
        LanguageMenu.Items.Clear();

        var normalItem = new MenuItem { Header = "Normal Text", Tag = "", IsChecked = true };
        normalItem.Click += Language_Click;
        LanguageMenu.Items.Add(normalItem);
        LanguageMenu.Items.Add(new Separator());

        var languages = SyntaxHighlightingManager.GetAllLanguages();
        foreach (var (displayName, avalonEditName) in languages)
        {
            if (avalonEditName == null) continue;

            var item = new MenuItem
            {
                Header = displayName,
                Tag = avalonEditName ?? "",
            };
            item.Click += Language_Click;
            LanguageMenu.Items.Add(item);
        }
    }

    private void Language_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem menuItem && menuItem.Tag is string langName)
        {
            var doc = GetActiveDocument();
            var editor = GetActiveEditor();
            if (doc == null || editor == null) return;

            doc.AutoDetectLanguage = false;
            StopAutoDetectTimer(doc);

            if (string.IsNullOrEmpty(langName))
            {
                doc.SyntaxHighlighting = null;
                doc.Language = "Normal Text";
                editor.SyntaxHighlighting = null;
            }
            else
            {
                var highlighting = SyntaxHighlightingManager.GetHighlightingByName(langName);
                doc.SyntaxHighlighting = highlighting;
                doc.Language = menuItem.Header?.ToString() ?? langName;
                editor.SyntaxHighlighting = highlighting;
            }

            SetupFolding(doc, editor);
            UpdateStatusBar();
            UpdateLanguageMenu();
        }
    }

    private void UpdateLanguageMenu()
    {
        var doc = GetActiveDocument();
        if (doc == null) return;

        foreach (var item in LanguageMenu.Items.OfType<MenuItem>())
        {
            if (item.Tag is string tag)
            {
                var activeTag = doc.SyntaxHighlighting?.Name ?? "";
                item.IsChecked = string.Equals(tag, activeTag, StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    private void StatusLanguage_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        LanguageMenu.IsSubmenuOpen = true;
    }

    private void LoadRecentFiles()
    {
        var recentFiles = RecentFilesManager.Load();
        RecentFilesMenu.Items.Clear();

        if (recentFiles.Count == 0)
        {
            RecentFilesMenu.Items.Add(new MenuItem { Header = "(empty)", IsEnabled = false });
            return;
        }

        int index = 1;
        foreach (var file in recentFiles)
        {
            var item = new MenuItem
            {
                Header = $"_{index}: {file}",
                Tag = file,
            };
            item.Click += (s, e) =>
            {
                if (s is MenuItem mi && mi.Tag is string path && File.Exists(path))
                    OpenFile(path);
                else
                    MessageBox.Show("File not found.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            };
            RecentFilesMenu.Items.Add(item);
            index++;
        }
    }

    private void ClearRecentFiles_Click(object sender, RoutedEventArgs e)
    {
        RecentFilesManager.ClearAll();
        LoadRecentFiles();
    }

    private async void CheckForUpdates_Click(object sender, RoutedEventArgs e)
    {
        var result = await UpdateChecker.CheckAsync();
        if (result == null)
        {
            MessageBox.Show("Could not check for updates. Please try again later.",
                "Update Check", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (result.Available)
        {
            var answer = MessageBox.Show(
                $"A new version of Caret is available!\n\n" +
                $"Current version: {result.CurrentVersion}\n" +
                $"Latest version: {result.LatestVersion}\n\n" +
                $"Would you like to open the GitHub releases page to download it?",
                "Update Available", MessageBoxButton.YesNo, MessageBoxImage.Information);
            if (answer == MessageBoxResult.Yes)
                UpdateChecker.OpenReleasesPage();
        }
        else
        {
            MessageBox.Show($"You're running the latest version ({result.CurrentVersion}).",
                "No Updates", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private async void CheckForUpdatesSilent()
    {
        var result = await UpdateChecker.CheckAsync();
        if (result is { Available: true })
        {
            var answer = MessageBox.Show(
                $"A new version of Caret is available!\n\n" +
                $"Current version: {result.CurrentVersion}\n" +
                $"Latest version: {result.LatestVersion}\n\n" +
                $"Would you like to open the GitHub releases page to download it?",
                "Update Available", MessageBoxButton.YesNo, MessageBoxImage.Information);
            if (answer == MessageBoxResult.Yes)
                UpdateChecker.OpenReleasesPage();
        }
    }

    private void About_Click(object sender, RoutedEventArgs e)
    {
        var about = new AboutWindow { Owner = this };
        about.ShowDialog();
    }

    private void Window_DragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = DragDropEffects.Copy;
            e.Handled = true;
        }
    }

    private void Window_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            var files = (string[])e.Data.GetData(DataFormats.FileDrop)!;
            foreach (var file in files)
            {
                if (File.Exists(file))
                    OpenFile(file);
            }
        }
    }

    private void UpdateStatusBar()
    {
        var doc = GetActiveDocument();
        var editor = GetActiveEditor();

        if (doc == null || editor == null)
        {
            StatusPosition.Text = "Ln: 1, Col: 1";
            StatusSelection.Text = "";
            StatusLanguage.Text = "Normal Text";
            StatusEncoding.Text = "UTF-8";
            StatusLineEnding.Text = "Windows (CR LF)";
            StatusZoom.Text = "100%";
            StatusFileInfo.Text = "";
            return;
        }

        var caret = editor.TextArea.Caret;
        StatusPosition.Text = $"Ln: {caret.Line}, Col: {caret.Column}";

        if (editor.SelectionLength > 0)
        {
            var selText = editor.SelectedText;
            var lines = selText.Split('\n').Length;
            StatusSelection.Text = $"Sel: {editor.SelectionLength} | {lines} lines";
        }
        else
        {
            StatusSelection.Text = "";
        }

        StatusLanguage.Text = doc.Language;
        StatusEncoding.Text = doc.EncodingName;
        StatusLineEnding.Text = doc.LineEnding;

        var zoomPercent = (int)Math.Round(doc.FontSize / DefaultFontSize * 100);
        StatusZoom.Text = $"{zoomPercent}%";

        var length = editor.Document.TextLength;
        var lineCount = editor.Document.LineCount;
        var sizeDisplay = doc.FileSize > 0 ? $" ({FormatFileSize(doc.FileSize)})" : "";
        var largeTag = doc.IsLargeFile ? " [Large File Mode]" : "";
        StatusFileInfo.Text = $"{length:N0} chars, {lineCount:N0} lines{sizeDisplay}{largeTag}";
    }

    private void UpdateTitle()
    {
        var doc = GetActiveDocument();
        if (doc == null)
        {
            Title = "Caret";
            return;
        }

        var prefix = doc.FilePath ?? doc.FileName;
        Title = doc.IsModified
            ? $"*{prefix} - Caret"
            : $"{prefix} - Caret";
    }

    private void SaveSession()
    {
        try
        {
            var session = new SessionData
            {
                ActiveTabIndex = TabDocuments.SelectedIndex,
                IsMaximized = WindowState == WindowState.Maximized,
                WindowLeft = RestoreBounds.Left,
                WindowTop = RestoreBounds.Top,
                WindowWidth = RestoreBounds.Width,
                WindowHeight = RestoreBounds.Height,
                WordWrap = _wordWrap,
                ShowWhiteSpace = _showWhiteSpace,
                ShowEndOfLine = _showEndOfLine,
                ShowLineNumbers = _showLineNumbers,
                ShowIndentGuide = _showIndentGuide,
                AlwaysOnTop = Topmost,
            };

            foreach (var doc in _documents)
            {
                var tab = new SessionTab
                {
                    FilePath = doc.FilePath,
                    FileName = doc.FileName,
                    IsModified = doc.IsModified,
                    FontSize = doc.FontSize,
                    SyntaxHighlightingName = doc.SyntaxHighlighting?.Name,
                    Language = doc.Language,
                    EncodingName = doc.EncodingName,
                    LineEnding = doc.LineEnding,
                    AutoDetectLanguage = doc.AutoDetectLanguage,
                };

                if (doc.FilePath == null || (doc.IsModified && !doc.IsLargeFile))
                {
                    tab.Content = doc.Document.Text;
                }

                if (_editors.TryGetValue(doc, out var editor))
                {
                    tab.CaretOffset = editor.CaretOffset;
                    tab.ScrollOffsetY = editor.TextArea.TextView.ScrollOffset.Y;
                    tab.ScrollOffsetX = editor.TextArea.TextView.ScrollOffset.X;
                }

                session.Tabs.Add(tab);
            }

            SessionManager.Save(session);
        }
        catch { }
    }

    private void RestoreSession()
    {
        var session = SessionManager.Load();
        if (session == null || session.Tabs.Count == 0)
            return;

        _wordWrap = session.WordWrap;
        _showWhiteSpace = session.ShowWhiteSpace;
        _showEndOfLine = session.ShowEndOfLine;
        _showLineNumbers = session.ShowLineNumbers;
        _showIndentGuide = session.ShowIndentGuide;
        Topmost = session.AlwaysOnTop;

        WordWrapMenu.IsChecked = _wordWrap;
        ShowWhiteSpaceMenu.IsChecked = _showWhiteSpace;
        ShowEndOfLineMenu.IsChecked = _showEndOfLine;
        ShowLineNumbersMenu.IsChecked = _showLineNumbers;
        ShowIndentGuideMenu.IsChecked = _showIndentGuide;
        AlwaysOnTopMenu.IsChecked = session.AlwaysOnTop;

        try
        {
            if (session.WindowWidth > 50 && session.WindowHeight > 50)
            {
                Left = session.WindowLeft;
                Top = session.WindowTop;
                Width = session.WindowWidth;
                Height = session.WindowHeight;
            }
            if (session.IsMaximized)
                WindowState = WindowState.Maximized;
        }
        catch { }

        foreach (var tab in session.Tabs)
        {
            try
            {
                RestoreTab(tab);
            }
            catch { }
        }

        if (session.ActiveTabIndex >= 0 && session.ActiveTabIndex < TabDocuments.Items.Count)
        {
            _suppressTabChanged = true;
            TabDocuments.SelectedIndex = session.ActiveTabIndex;
            _suppressTabChanged = false;
        }

        UpdateStatusBar();
        UpdateTitle();
        UpdateEncodingMenu();
        UpdateLanguageMenu();

        var activeEditor = GetActiveEditor();
        activeEditor?.Focus();
    }

    private void RestoreTab(SessionTab tab)
    {
        DocumentModel doc;

        if (tab.FilePath != null && File.Exists(tab.FilePath))
        {
            var fileInfo = new FileInfo(tab.FilePath);
            long fileSize = fileInfo.Length;
            bool isLargeFile = fileSize > LargeFileThreshold;

            if (isLargeFile)
            {
                doc = CreateNewDocument(null);
                doc.FilePath = tab.FilePath;
                doc.Language = SyntaxHighlightingManager.GetLanguageNameByExtension(tab.FilePath);
                doc.IsLargeFile = true;
                doc.FileSize = fileSize;
                doc.AutoDetectLanguage = false;

                if (_editors.TryGetValue(doc, out var largeEd))
                    largeEd.SyntaxHighlighting = null;
            }
            else
            {
                doc = CreateNewDocument(tab.FilePath);
                doc.FileSize = fileSize;
            }

            var encoding = DetectEncoding(tab.FilePath);
            var content = File.ReadAllText(tab.FilePath, encoding);
            doc.Encoding = encoding;
            doc.LineEnding = DetectLineEnding(content);

            _suppressTabChanged = true;

            if (tab.IsModified && tab.Content != null)
            {
                doc.Document.Text = tab.Content;
                doc.IsModified = true;
            }
            else
            {
                doc.Document.Text = content;
                doc.IsModified = false;
            }

            _suppressTabChanged = false;
        }
        else if (tab.FilePath == null && tab.Content != null)
        {
            doc = CreateNewDocument();
            doc.FileName = tab.FileName;

            _suppressTabChanged = true;
            doc.Document.Text = tab.Content;
            doc.IsModified = tab.IsModified;
            _suppressTabChanged = false;

            if (tab.SyntaxHighlightingName != null)
            {
                var highlighting = SyntaxHighlightingManager.GetHighlightingByName(tab.SyntaxHighlightingName);
                doc.SyntaxHighlighting = highlighting;
                doc.Language = tab.Language;
                if (_editors.TryGetValue(doc, out var ed))
                {
                    ed.SyntaxHighlighting = highlighting;
                    SetupFolding(doc, ed);
                }
            }
        }
        else
        {
            doc = CreateNewDocument();
            doc.FileName = tab.FileName;
            _suppressTabChanged = true;
            doc.IsModified = false;
            _suppressTabChanged = false;
            return;
        }

        doc.FontSize = tab.FontSize;
        if (!doc.IsLargeFile)
            doc.AutoDetectLanguage = tab.AutoDetectLanguage;
        doc.Language = tab.Language;

        SetEncodingFromName(doc, tab.EncodingName);
        doc.LineEnding = tab.LineEnding;

        if (_editors.TryGetValue(doc, out var editor))
        {
            editor.FontSize = doc.FontSize;

            var caretOffset = tab.CaretOffset;
            var scrollY = tab.ScrollOffsetY;
            var scrollX = tab.ScrollOffsetX;
            editor.Dispatcher.BeginInvoke(() =>
            {
                try
                {
                    if (caretOffset >= 0 && caretOffset <= editor.Document.TextLength)
                        editor.CaretOffset = caretOffset;
                    editor.ScrollToVerticalOffset(scrollY);
                    editor.ScrollToHorizontalOffset(scrollX);
                }
                catch { }
            }, System.Windows.Threading.DispatcherPriority.Loaded);
        }

        UpdateTabHeader(doc);
    }

    private static void SetEncodingFromName(DocumentModel doc, string encodingName)
    {
        doc.Encoding = encodingName switch
        {
            "ANSI" => Encoding.GetEncoding(1252),
            "UTF-8" => new UTF8Encoding(false),
            "UTF-8-BOM" => new UTF8Encoding(true),
            "UCS-2 LE BOM" => new UnicodeEncoding(false, true),
            "UCS-2 BE BOM" => new UnicodeEncoding(true, true),
            _ => new UTF8Encoding(false),
        };
    }

    private void ScheduleAutoDetect(DocumentModel doc, TextEditor editor)
    {
        if (_autoDetectTimers.TryGetValue(doc, out var existing))
        {
            existing.Stop();
            existing.Start();
            return;
        }

        var timer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(800)
        };
        timer.Tick += (s, e) =>
        {
            timer.Stop();
            RunAutoDetect(doc, editor);
        };
        _autoDetectTimers[doc] = timer;
        timer.Start();
    }

    private void RunAutoDetect(DocumentModel doc, TextEditor editor)
    {
        if (!doc.AutoDetectLanguage)
            return;

        var content = editor.Document.Text;
        if (content.Length < 5)
            return;

        var detectedLang = SyntaxHighlightingManager.DetectLanguageFromContent(content);

        var currentLang = doc.SyntaxHighlighting?.Name;
        if (detectedLang != null && !string.Equals(detectedLang, currentLang, StringComparison.OrdinalIgnoreCase))
        {
            var highlighting = SyntaxHighlightingManager.GetHighlightingByName(detectedLang);
            if (highlighting != null)
            {
                doc.SyntaxHighlighting = highlighting;
                doc.Language = SyntaxHighlightingManager.GetLanguageDisplayName(detectedLang);
                editor.SyntaxHighlighting = highlighting;
                SetupFolding(doc, editor);
                UpdateStatusBar();
                UpdateLanguageMenu();
            }
        }
    }

    private void StopAutoDetectTimer(DocumentModel doc)
    {
        if (_autoDetectTimers.TryGetValue(doc, out var timer))
        {
            timer.Stop();
            _autoDetectTimers.Remove(doc);
        }
    }

    private static Encoding DetectEncoding(string filePath)
    {
        var bom = new byte[4];
        using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            _ = fs.ReadAtLeast(bom, 4, throwOnEndOfStream: false);
        }

        if (bom[0] == 0xFF && bom[1] == 0xFE)
            return new UnicodeEncoding(false, true);
        if (bom[0] == 0xFE && bom[1] == 0xFF)
            return new UnicodeEncoding(true, true);
        if (bom[0] == 0xEF && bom[1] == 0xBB && bom[2] == 0xBF)
            return new UTF8Encoding(true);

        try
        {
            using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            var sampleSize = (int)Math.Min(fs.Length, EncodingSampleSize);
            var sample = new byte[sampleSize];
            _ = fs.ReadAtLeast(sample, sampleSize, throwOnEndOfStream: false);

            var utf8 = new UTF8Encoding(false, true);
            utf8.GetString(sample);
            return new UTF8Encoding(false);
        }
        catch
        {
            return Encoding.GetEncoding(1252);
        }
    }

    private static string DetectLineEnding(string content)
    {
        var sample = content.Length > 8192
            ? content.AsSpan(0, 8192)
            : content.AsSpan();

        if (sample.Contains("\r\n".AsSpan(), StringComparison.Ordinal))
            return "Windows (CR LF)";
        if (sample.Contains("\r".AsSpan(), StringComparison.Ordinal))
            return "Macintosh (CR)";
        if (sample.Contains("\n".AsSpan(), StringComparison.Ordinal))
            return "Unix (LF)";
        return "Windows (CR LF)";
    }

    private static string BuildFileFilter()
    {
        return "All Files (*.*)|*.*|" +
               "Text Files (*.txt)|*.txt|" +
               "C# Files (*.cs)|*.cs|" +
               "C/C++ Files (*.c;*.cpp;*.h;*.hpp)|*.c;*.cpp;*.h;*.hpp|" +
               "Java Files (*.java)|*.java|" +
               "JavaScript Files (*.js;*.jsx;*.ts;*.tsx)|*.js;*.jsx;*.ts;*.tsx|" +
               "Python Files (*.py)|*.py|" +
               "HTML Files (*.html;*.htm)|*.html;*.htm|" +
               "CSS Files (*.css;*.scss;*.less)|*.css;*.scss;*.less|" +
               "XML Files (*.xml;*.xaml)|*.xml;*.xaml|" +
               "JSON Files (*.json)|*.json|" +
               "SQL Files (*.sql)|*.sql|" +
               "Markdown Files (*.md)|*.md|" +
               "PowerShell (*.ps1)|*.ps1|" +
               "Batch Files (*.bat;*.cmd)|*.bat;*.cmd|" +
               "PHP Files (*.php)|*.php|" +
               "YAML Files (*.yml;*.yaml)|*.yml;*.yaml|" +
               "INI Files (*.ini;*.cfg)|*.ini;*.cfg|" +
               "Log Files (*.log)|*.log";
    }
}

public class BraceFoldingStrategy
{
    public void UpdateFoldings(FoldingManager manager, TextDocument document)
    {
        var foldings = CreateNewFoldings(document);
        manager.UpdateFoldings(foldings, -1);
    }

    private IEnumerable<NewFolding> CreateNewFoldings(TextDocument document)
    {
        var foldings = new List<NewFolding>();
        var stack = new Stack<int>();

        for (int i = 0; i < document.TextLength; i++)
        {
            char c = document.GetCharAt(i);
            if (c == '{')
            {
                stack.Push(i);
            }
            else if (c == '}' && stack.Count > 0)
            {
                int start = stack.Pop();
                if (i - start > 1)
                {
                    foldings.Add(new NewFolding(start, i + 1));
                }
            }
        }

        foldings.Sort((a, b) => a.StartOffset.CompareTo(b.StartOffset));
        return foldings;
    }
}
