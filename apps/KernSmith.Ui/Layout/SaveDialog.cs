using Gum.DataTypes;
using Gum.Forms;
using Gum.Forms.Controls;

namespace KernSmith.Ui.Layout;

/// <summary>
/// Modal save dialog with directory browser, file name input, and Save/Cancel buttons.
/// Used for both .fnt export and .bmfc project saving.
/// </summary>
public class SaveDialog
{
    private Window _window = null!;
    private TextBox _pathBox = null!;
    private TextBox _fileNameBox = null!;
    private ListBox _dirList = null!;
    private string _currentDir = "";
    private Action<string>? _onSave;

    private readonly string _defaultFileName;
    private readonly string _defaultExtension;

    public SaveDialog(string defaultFileName = "myfont", string defaultExtension = "fnt")
    {
        _defaultFileName = defaultFileName;
        _defaultExtension = defaultExtension;
    }

    /// <summary>
    /// Opens the save dialog. Calls <paramref name="onSave"/> with the full output path on confirmation.
    /// </summary>
    public void Show(Action<string> onSave, string? initialDir = null)
    {
        _onSave = onSave;
        _currentDir = initialDir
            ?? Environment.GetFolderPath(Environment.SpecialFolder.Desktop);

        _window = new Window();
        _window.Anchor(Gum.Wireframe.Anchor.Center);
        _window.Width = 500;
        _window.Height = 420;
        FrameworkElement.ModalRoot.Children.Add(_window.Visual);

        var windowVisual = _window.Visual as Gum.Forms.DefaultVisuals.V3.WindowVisual;
        if (windowVisual?.TitleBarInstance != null)
        {
            var titleLabel = new Label();
            titleLabel.Text = "Save As";
            titleLabel.X = 8;
            titleLabel.Y = 2;
            windowVisual.TitleBarInstance.AddChild(titleLabel);
        }

        var stack = new StackPanel();
        stack.Spacing = 4;
        stack.Y = 28;
        stack.Visual.WidthUnits = DimensionUnitType.RelativeToParent;
        stack.Width = -8;
        stack.Visual.X = 4;
        _window.AddChild(stack);

        // 1. Current directory path
        var dirLabel = new Label();
        dirLabel.Text = "Location:";
        stack.AddChild(dirLabel);

        _pathBox = new TextBox();
        _pathBox.Visual.WidthUnits = DimensionUnitType.RelativeToParent;
        _pathBox.Width = 0;
        _pathBox.Text = _currentDir;
        _pathBox.TextChanged += (_, _) =>
        {
            var path = _pathBox.Text?.Trim();
            if (!string.IsNullOrEmpty(path) && Directory.Exists(path))
            {
                _currentDir = path;
                RefreshDirList();
            }
        };
        stack.AddChild(_pathBox);

        // 2. Directory listing (for navigation)
        _dirList = new ListBox();
        _dirList.Visual.WidthUnits = DimensionUnitType.RelativeToParent;
        _dirList.Width = 0;
        _dirList.Height = 220;
        stack.AddChild(_dirList);

        _dirList.SelectionChanged += (_, _) =>
        {
            if (_dirList.SelectedObject is string selected)
            {
                if (selected == "..")
                {
                    var parent = Directory.GetParent(_currentDir)?.FullName;
                    if (parent != null)
                    {
                        _currentDir = parent;
                        _pathBox.Text = _currentDir;
                        RefreshDirList();
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
                        RefreshDirList();
                    }
                }
            }
        };

        // 3. File name input
        var nameLabel = new Label();
        nameLabel.Text = "File name:";
        stack.AddChild(nameLabel);

        _fileNameBox = new TextBox();
        _fileNameBox.Visual.WidthUnits = DimensionUnitType.RelativeToParent;
        _fileNameBox.Width = 0;
        _fileNameBox.Text = $"{_defaultFileName}.{_defaultExtension}";
        stack.AddChild(_fileNameBox);

        // 4. Button row
        var btnRow = new StackPanel();
        btnRow.Orientation = Orientation.Horizontal;
        btnRow.Spacing = 8;
        stack.AddChild(btnRow);

        var saveBtn = new Button();
        saveBtn.Text = "Save";
        saveBtn.Width = 80;
        saveBtn.Click += (_, _) =>
        {
            var fileName = _fileNameBox.Text?.Trim();
            if (string.IsNullOrEmpty(fileName)) return;

            // Ensure extension is present
            if (!fileName.Contains('.'))
                fileName = $"{fileName}.{_defaultExtension}";

            var fullPath = Path.Combine(_currentDir, fileName);
            _onSave?.Invoke(fullPath);
            _window.RemoveFromRoot();
        };
        btnRow.AddChild(saveBtn);

        var cancelBtn = new Button();
        cancelBtn.Text = "Cancel";
        cancelBtn.Width = 80;
        cancelBtn.Click += (_, _) => _window.RemoveFromRoot();
        btnRow.AddChild(cancelBtn);

        RefreshDirList();
    }

    private void RefreshDirList()
    {
        var items = _dirList.Items;
        if (items == null) return;

        items.Clear();

        try
        {
            if (Directory.GetParent(_currentDir) != null)
                items.Add("..");

            foreach (var dir in Directory.GetDirectories(_currentDir).OrderBy(d => Path.GetFileName(d)))
            {
                items.Add(Path.GetFileName(dir) + "/");
            }
        }
        catch (UnauthorizedAccessException) { }
        catch (IOException) { }
    }
}
