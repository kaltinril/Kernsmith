using Gum.Forms;
using Gum.Forms.Controls;

namespace KernSmith.Ui.Layout;

public class FileBrowserDialog
{
    private Window _window = null!;
    private TextBox _pathBox = null!;
    private ListBox _fileList = null!;
    private string _currentDir = "";
    private string? _selectedFile;
    private Action<string>? _onFileSelected;

    private static readonly string[] FontExtensions = [".ttf", ".otf", ".woff", ".ttc"];

    /// <summary>
    /// Optional custom file extensions filter. If set, overrides the default font extensions.
    /// Each entry should include the leading dot (e.g., ".fnt").
    /// </summary>
    public string[]? FileExtensionFilter { get; set; }

    public void Show(Action<string> onFileSelected)
    {
        _onFileSelected = onFileSelected;
        _currentDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        _window = new Window();
        _window.Anchor(Gum.Wireframe.Anchor.Center);
        _window.Width = 500;
        _window.Height = 400;
        FrameworkElement.ModalRoot.Children.Add(_window.Visual);

        // Title label inside the title bar
        var windowVisual = _window.Visual as Gum.Forms.DefaultVisuals.V3.WindowVisual;
        if (windowVisual?.TitleBarInstance != null)
        {
            var titleLabel = new Label();
            titleLabel.Text = "Choose Font File";
            titleLabel.X = 8;
            titleLabel.Y = 2;
            windowVisual.TitleBarInstance.AddChild(titleLabel);
        }

        // One vertical StackPanel inside the window holds everything
        var stack = new StackPanel();
        stack.Spacing = 4;
        stack.Y = 28; // below window title bar
        stack.Visual.WidthUnits = Gum.DataTypes.DimensionUnitType.RelativeToParent;
        stack.Width = -8;
        stack.Visual.X = 4;
        _window.AddChild(stack);

        // 1. Path box
        _pathBox = new TextBox();
        _pathBox.Visual.WidthUnits = Gum.DataTypes.DimensionUnitType.RelativeToParent;
        _pathBox.Width = 0;
        _pathBox.Text = _currentDir;
        _pathBox.TextChanged += (_, _) =>
        {
            var path = _pathBox.Text?.Trim();
            if (!string.IsNullOrEmpty(path) && Directory.Exists(path))
            {
                _currentDir = path;
                RefreshFileList();
            }
        };
        stack.AddChild(_pathBox);

        // 2. File list (fixed height to fill most of the dialog)
        _fileList = new ListBox();
        _fileList.Visual.WidthUnits = Gum.DataTypes.DimensionUnitType.RelativeToParent;
        _fileList.Width = 0;
        _fileList.Height = 300;
        stack.AddChild(_fileList);

        _fileList.SelectionChanged += (_, _) =>
        {
            if (_fileList.SelectedObject is string selected)
            {
                if (selected == "..")
                {
                    var parent = Directory.GetParent(_currentDir)?.FullName;
                    if (parent != null)
                    {
                        _currentDir = parent;
                        _pathBox.Text = _currentDir;
                        RefreshFileList();
                    }
                }
                else if (selected.EndsWith('/'))
                {
                    var dirName = selected.TrimEnd('/');
                    var fullPath = Path.Combine(_currentDir, dirName);
                    if (Directory.Exists(fullPath))
                    {
                        _currentDir = fullPath;
                        _pathBox.Text = _currentDir;
                        RefreshFileList();
                    }
                }
                else
                {
                    _selectedFile = Path.Combine(_currentDir, selected);
                }
            }
        };

        // 3. Button row
        var btnRow = new StackPanel();
        btnRow.Orientation = Orientation.Horizontal;
        btnRow.Spacing = 8;
        stack.AddChild(btnRow);

        var okBtn = new Button();
        okBtn.Text = "Open";
        okBtn.Width = 80;
        okBtn.Click += (_, _) =>
        {
            if (_selectedFile != null && File.Exists(_selectedFile))
            {
                _onFileSelected?.Invoke(_selectedFile);
                _window.RemoveFromRoot();
            }
        };
        btnRow.AddChild(okBtn);

        var cancelBtn = new Button();
        cancelBtn.Text = "Cancel";
        cancelBtn.Width = 80;
        cancelBtn.Click += (_, _) => _window.RemoveFromRoot();
        btnRow.AddChild(cancelBtn);

        RefreshFileList();
    }

    private void RefreshFileList()
    {
        var items = _fileList.Items;
        if (items == null) return;

        items.Clear();
        _selectedFile = null;

        try
        {
            if (Directory.GetParent(_currentDir) != null)
                items.Add("..");

            foreach (var dir in Directory.GetDirectories(_currentDir).OrderBy(d => Path.GetFileName(d)))
            {
                items.Add(Path.GetFileName(dir) + "/");
            }

            var extensions = FileExtensionFilter ?? FontExtensions;
            foreach (var file in Directory.GetFiles(_currentDir)
                .Where(f => extensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
                .OrderBy(f => Path.GetFileName(f)))
            {
                items.Add(Path.GetFileName(file));
            }
        }
        catch (UnauthorizedAccessException) { }
        catch (IOException) { }
    }
}
