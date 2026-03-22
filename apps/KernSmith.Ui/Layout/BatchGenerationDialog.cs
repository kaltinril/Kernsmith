using Gum.DataTypes;
using Gum.Forms;
using Gum.Forms.Controls;
using KernSmith.Ui.ViewModels;

namespace KernSmith.Ui.Layout;

public class BatchGenerationDialog
{
    private Window _window = null!;
    private ListBox _fileList = null!;
    private Label _progressLabel = null!;
    private Button _generateBtn = null!;
    private readonly MainViewModel _viewModel;
    private readonly List<string> _queuedFiles = new();

    private static readonly string[] FontExtensions = [".ttf", ".otf", ".woff", ".ttc"];

    public BatchGenerationDialog(MainViewModel viewModel)
    {
        _viewModel = viewModel;
    }

    public void Show()
    {
        _queuedFiles.Clear();

        _window = new Window();
        _window.Anchor(Gum.Wireframe.Anchor.Center);
        _window.Width = 500;
        _window.Height = 420;
        FrameworkElement.ModalRoot.Children.Add(_window.Visual);

        var stack = new StackPanel();
        stack.Spacing = 6;
        stack.Y = 28;
        stack.Dock(Gum.Wireframe.Dock.Fill);
        _window.AddChild(stack);

        var headerLabel = new Label();
        headerLabel.Text = "Batch Font Generation";
        stack.AddChild(headerLabel);

        _fileList = new ListBox();
        _fileList.Width = 0;
        _fileList.Visual.WidthUnits = DimensionUnitType.RelativeToParent;
        _fileList.Height = 260;
        stack.AddChild(_fileList);

        var btnRow = new StackPanel();
        btnRow.Orientation = Orientation.Horizontal;
        btnRow.Spacing = 8;
        stack.AddChild(btnRow);

        var addBtn = new Button();
        addBtn.Text = "Add Font...";
        addBtn.Width = 100;
        addBtn.Click += (_, _) => OnAddFont();
        btnRow.AddChild(addBtn);

        var removeBtn = new Button();
        removeBtn.Text = "Remove";
        removeBtn.Width = 80;
        removeBtn.Click += (_, _) => OnRemoveSelected();
        btnRow.AddChild(removeBtn);

        _generateBtn = new Button();
        _generateBtn.Text = "Generate All";
        _generateBtn.Width = 100;
        _generateBtn.Click += (_, _) => Task.Run(GenerateAllAsync);
        btnRow.AddChild(_generateBtn);

        var cancelBtn = new Button();
        cancelBtn.Text = "Close";
        cancelBtn.Width = 80;
        cancelBtn.Click += (_, _) => _window.RemoveFromRoot();
        btnRow.AddChild(cancelBtn);

        _progressLabel = new Label();
        _progressLabel.Text = "";
        stack.AddChild(_progressLabel);
    }

    private void OnAddFont()
    {
        var browser = new FileBrowserDialog();
        browser.Show(path =>
        {
            var ext = Path.GetExtension(path).ToLowerInvariant();
            if (FontExtensions.Contains(ext) && !_queuedFiles.Contains(path))
            {
                _queuedFiles.Add(path);
                _fileList.Items?.Add(Path.GetFileName(path));
            }
        });
    }

    private void OnRemoveSelected()
    {
        var index = _fileList.SelectedIndex;
        if (index >= 0 && index < _queuedFiles.Count)
        {
            _queuedFiles.RemoveAt(index);
            _fileList.Items?.RemoveAt(index);
        }
    }

    private async Task GenerateAllAsync()
    {
        if (_queuedFiles.Count == 0) return;

        _generateBtn.IsEnabled = false;

        for (int i = 0; i < _queuedFiles.Count; i++)
        {
            var fontPath = _queuedFiles[i];
            var current = i + 1;
            var total = _queuedFiles.Count;

            _progressLabel.Text = $"Processing {current} of {total}: {Path.GetFileName(fontPath)}";

            try
            {
                _viewModel.LoadFontFromPath(fontPath);
                await _viewModel.GenerateAsync();
            }
            catch (Exception ex)
            {
                _progressLabel.Text = $"Error on {Path.GetFileName(fontPath)}: {ex.Message}";
            }
        }

        _progressLabel.Text = $"Batch complete: {_queuedFiles.Count} font(s) processed.";
        _generateBtn.IsEnabled = true;
    }
}
