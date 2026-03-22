using Gum.DataTypes;
using Gum.Forms.Controls;
using KernSmith.Ui.Models;
using KernSmith.Ui.ViewModels;
using MonoGameGum.GueDeriving;

namespace KernSmith.Ui.Layout;

public class CharacterSelectionPanel : Panel
{
    private readonly CharacterGridViewModel _gridVm;

    public CharacterSelectionPanel(CharacterGridViewModel gridVm)
    {
        _gridVm = gridVm;

        BuildContent();
    }

    private void BuildContent()
    {
        var scrollViewer = new ScrollViewer();
        scrollViewer.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        scrollViewer.Dock(Gum.Wireframe.Dock.Fill);
        this.AddChild(scrollViewer);

        var stack = scrollViewer.InnerPanel;
        stack.StackSpacing = 6;

        // --- Preset RadioButtons ---
        AddSectionHeader(stack, "CHARACTER SET PRESET");

        var presetRow = new StackPanel();
        presetRow.Orientation = Orientation.Horizontal;
        presetRow.Spacing = 8;
        stack.Children.Add(presetRow.Visual);

        var presetNames = new[] { "ASCII", "Extended ASCII", "Latin", "Custom" };
        var presetValues = new[]
        {
            CharacterSetPreset.Ascii,
            CharacterSetPreset.ExtendedAscii,
            CharacterSetPreset.Latin,
            CharacterSetPreset.Custom
        };

        for (int i = 0; i < presetNames.Length; i++)
        {
            var rb = new RadioButton();
            rb.Text = presetNames[i];
            if (i == 0) rb.IsChecked = true;
            var preset = presetValues[i];
            rb.Checked += (_, _) =>
            {
                if (preset != CharacterSetPreset.Custom)
                    _gridVm.ApplyPreset(preset);
                _gridVm.ActivePreset = preset;
            };
            presetRow.AddChild(rb);
        }

        AddDivider(stack);

        // --- Text input area (Hiero-style) ---
        AddSectionHeader(stack, "ADD FROM TEXT");

        var textInputRow = new StackPanel();
        textInputRow.Orientation = Orientation.Horizontal;
        textInputRow.Spacing = 4;
        stack.Children.Add(textInputRow.Visual);

        var textBox = new TextBox();
        textBox.Width = 300;
        textBox.Height = 100;
        textBox.Placeholder = "Paste or type characters here...";
        textBox.TextWrapping = Gum.Forms.TextWrapping.Wrap;
        textBox.AcceptsReturn = true;
        textInputRow.AddChild(textBox);

        var addTextBtn = new Button();
        addTextBtn.Text = "Add";
        addTextBtn.Width = 60;
        addTextBtn.Click += (_, _) =>
        {
            var text = textBox.Text?.Trim();
            if (!string.IsNullOrEmpty(text))
            {
                _gridVm.AddFromText(text);
                _gridVm.ActivePreset = CharacterSetPreset.Custom;
            }
        };
        textInputRow.AddChild(addTextBtn);

        AddDivider(stack);

        // --- Unicode block checkboxes ---
        AddSectionHeader(stack, "UNICODE BLOCKS");

        var blockScroll = new ScrollViewer();
        blockScroll.Width = 0;
        blockScroll.WidthUnits = DimensionUnitType.RelativeToParent;
        blockScroll.Height = 300;
        blockScroll.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        stack.Children.Add(blockScroll.Visual);

        var blockStack = blockScroll.InnerPanel;
        blockStack.StackSpacing = 2;

        foreach (var block in UnicodeBlock.StandardBlocks)
        {
            var cb = new CheckBox();
            cb.Text = $"{block.Name} ({block.Count})";
            cb.Width = 350;

            // Check if the block overlaps with current selection
            var blockRef = block; // capture for closure
            cb.Checked += (_, _) =>
            {
                _gridVm.SelectRange(blockRef.Start, blockRef.End);
                _gridVm.ActivePreset = CharacterSetPreset.Custom;
            };
            cb.Unchecked += (_, _) =>
            {
                _gridVm.DeselectRange(blockRef.Start, blockRef.End);
                _gridVm.ActivePreset = CharacterSetPreset.Custom;
            };
            blockScroll.AddChild(cb);
        }

        AddDivider(stack);

        // --- Summary + action buttons ---
        var bottomRow = new StackPanel();
        bottomRow.Orientation = Orientation.Horizontal;
        bottomRow.Spacing = 8;
        stack.Children.Add(bottomRow.Visual);

        var summaryLabel = new Label();
        summaryLabel.Text = _gridVm.SummaryText;
        _gridVm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(CharacterGridViewModel.SummaryText))
                summaryLabel.Text = _gridVm.SummaryText;
        };
        bottomRow.AddChild(summaryLabel);

        var selectAllBtn = new Button();
        selectAllBtn.Text = "Select All";
        selectAllBtn.Width = 80;
        selectAllBtn.Click += (_, _) =>
        {
            // Select all standard blocks
            foreach (var block in UnicodeBlock.StandardBlocks)
                _gridVm.SelectRange(block.Start, block.End);
            _gridVm.ActivePreset = CharacterSetPreset.Custom;
        };
        bottomRow.AddChild(selectAllBtn);

        var clearBtn = new Button();
        clearBtn.Text = "Clear";
        clearBtn.Width = 60;
        clearBtn.Click += (_, _) =>
        {
            _gridVm.Clear();
            _gridVm.ActivePreset = CharacterSetPreset.Custom;
        };
        bottomRow.AddChild(clearBtn);
    }

    private static void AddSectionHeader(Gum.Wireframe.GraphicalUiElement parent, string text)
    {
        var label = new Label();
        label.Text = text;
        parent.Children.Add(label.Visual);
    }

    private static void AddDivider(Gum.Wireframe.GraphicalUiElement parent)
    {
        var divider = new ColoredRectangleRuntime();
        divider.Width = 0;
        divider.WidthUnits = DimensionUnitType.RelativeToParent;
        divider.Height = 1;
        divider.Color = new Microsoft.Xna.Framework.Color(60, 60, 60);
        parent.Children.Add(divider);
    }
}
