using Gum.DataTypes;
using Gum.Forms.Controls;
using KernSmith.Ui.Models;
using KernSmith.Ui.Styling;
using KernSmith.Ui.ViewModels;
using MonoGameGum.GueDeriving;

namespace KernSmith.Ui.Layout;

/// <summary>
/// Character selection tab content. Provides preset radio buttons (ASCII/Extended/Latin/Custom),
/// a text input area for pasting characters, Unicode block checkboxes, and summary/action buttons.
/// </summary>
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
        if (scrollViewer.Visual is Gum.Forms.DefaultVisuals.V3.ScrollViewerVisual scrollVisual)
        {
            scrollVisual.Background.ApplyState(Gum.Forms.DefaultVisuals.V3.Styling.ActiveStyle.NineSlice.Solid);
            scrollVisual.BackgroundColor = Styling.Theme.Panel;
            FontConfigPanel.StripScrollViewerMargins(scrollVisual);
        }
        this.AddChild(scrollViewer);

        var stack = scrollViewer.InnerPanel;
        stack.StackSpacing = 6;

        // --- Preset RadioButtons ---
        UiFactory.AddCollapsibleHeader(stack, "CHARACTER SET PRESET", content =>
        {
            var presetRow = new StackPanel();
            presetRow.Orientation = Orientation.Horizontal;
            presetRow.Spacing = 8;
            content.Children.Add(presetRow.Visual);

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
        });

        // --- Text input area (Hiero-style) ---
        UiFactory.AddCollapsibleHeader(stack, "ADD FROM TEXT", content =>
        {
            var textInputRow = new StackPanel();
            textInputRow.Orientation = Orientation.Horizontal;
            textInputRow.Spacing = 4;
            content.Children.Add(textInputRow.Visual);

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
        });

        // --- Unicode block checkboxes ---
        UiFactory.AddCollapsibleHeader(stack, "UNICODE BLOCKS", content =>
        {
            var blockScroll = new ScrollViewer();
            blockScroll.Width = 0;
            blockScroll.WidthUnits = DimensionUnitType.RelativeToParent;
            blockScroll.Height = 300;
            blockScroll.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            content.Children.Add(blockScroll.Visual);

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
        });

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

}
