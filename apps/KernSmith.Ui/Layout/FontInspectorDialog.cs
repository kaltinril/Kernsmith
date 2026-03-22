using Gum.DataTypes;
using Gum.Forms;
using Gum.Forms.Controls;
using KernSmith.Ui.ViewModels;

namespace KernSmith.Ui.Layout;

public class FontInspectorDialog
{
    private readonly FontConfigViewModel _fontConfig;

    public FontInspectorDialog(FontConfigViewModel fontConfig)
    {
        _fontConfig = fontConfig;
    }

    public void Show()
    {
        if (!_fontConfig.IsFontLoaded || _fontConfig.FontData == null)
            return;

        var window = new Window();
        window.Anchor(Gum.Wireframe.Anchor.Center);
        window.Width = 460;
        window.Height = 500;
        FrameworkElement.ModalRoot.Children.Add(window.Visual);

        var scrollViewer = new ScrollViewer();
        scrollViewer.Width = 0;
        scrollViewer.Visual.WidthUnits = DimensionUnitType.RelativeToParent;
        scrollViewer.Height = 420;
        scrollViewer.Y = 28;
        window.AddChild(scrollViewer);

        // Read full font info to get table details
        Font.Models.FontInfo? fontInfo = null;
        try
        {
            fontInfo = BmFont.ReadFontInfo(_fontConfig.FontData, _fontConfig.FaceIndex);
        }
        catch
        {
            // Fall back to what's available in the view model
        }

        AddRow(scrollViewer, "Family Name", _fontConfig.FamilyName);
        AddRow(scrollViewer, "Style Name", _fontConfig.StyleName);
        AddRow(scrollViewer, "Glyphs", _fontConfig.NumGlyphs.ToString("N0"));
        AddRow(scrollViewer, "Color Glyphs", _fontConfig.HasColorGlyphs ? "Yes" : "No");

        if (fontInfo != null)
        {
            AddRow(scrollViewer, "Units per Em", fontInfo.UnitsPerEm.ToString());
            AddRow(scrollViewer, "Ascender", fontInfo.Ascender.ToString());
            AddRow(scrollViewer, "Descender", fontInfo.Descender.ToString());
            AddRow(scrollViewer, "Line Gap", fontInfo.LineGap.ToString());
            AddRow(scrollViewer, "Line Height", fontInfo.LineHeight.ToString());
            AddRow(scrollViewer, "Bold", fontInfo.IsBold ? "Yes" : "No");
            AddRow(scrollViewer, "Italic", fontInfo.IsItalic ? "Yes" : "No");
            AddRow(scrollViewer, "Fixed Pitch", fontInfo.IsFixedPitch ? "Yes" : "No");
            AddRow(scrollViewer, "Kerning Pairs", fontInfo.KerningPairs.Count.ToString("N0"));

            if (fontInfo.Head is { } head)
            {
                AddSectionHeader(scrollViewer, "Head Table");
                AddRow(scrollViewer, "  Bounding Box", $"({head.XMin}, {head.YMin}) to ({head.XMax}, {head.YMax})");
                AddRow(scrollViewer, "  Lowest Rec PPEM", head.LowestRecPPEM.ToString());
                AddRow(scrollViewer, "  Created", head.CreatedUtc.ToString("yyyy-MM-dd"));
                AddRow(scrollViewer, "  Modified", head.ModifiedUtc.ToString("yyyy-MM-dd"));
            }

            if (fontInfo.Hhea is { } hhea)
            {
                AddSectionHeader(scrollViewer, "Hhea Table");
                AddRow(scrollViewer, "  Ascender", hhea.Ascender.ToString());
                AddRow(scrollViewer, "  Descender", hhea.Descender.ToString());
                AddRow(scrollViewer, "  Line Gap", hhea.LineGap.ToString());
                AddRow(scrollViewer, "  Max Advance Width", hhea.AdvanceWidthMax.ToString());
                AddRow(scrollViewer, "  H-Metrics Count", hhea.NumberOfHMetrics.ToString());
            }

            if (fontInfo.Os2 is { } os2)
            {
                AddSectionHeader(scrollViewer, "OS/2 Table");
                AddRow(scrollViewer, "  Weight Class", os2.WeightClass.ToString());
                AddRow(scrollViewer, "  Width Class", os2.WidthClass.ToString());
                AddRow(scrollViewer, "  Typo Ascender", os2.TypoAscender.ToString());
                AddRow(scrollViewer, "  Typo Descender", os2.TypoDescender.ToString());
                AddRow(scrollViewer, "  x-Height", os2.XHeight.ToString());
                AddRow(scrollViewer, "  Cap Height", os2.CapHeight.ToString());
                AddRow(scrollViewer, "  Char Range", $"U+{os2.FirstCharIndex:X4} to U+{os2.LastCharIndex:X4}");
            }

            if (fontInfo.VariationAxes is { Count: > 0 } axes)
            {
                AddSectionHeader(scrollViewer, $"Variation Axes ({axes.Count})");
                foreach (var axis in axes)
                {
                    var name = axis.Name ?? axis.Tag;
                    AddRow(scrollViewer, $"  {name} ({axis.Tag})",
                        $"{axis.MinValue:F0} .. {axis.DefaultValue:F0} .. {axis.MaxValue:F0}");
                }
            }
        }

        var okButton = new Button();
        okButton.Text = "Close";
        okButton.Anchor(Gum.Wireframe.Anchor.Bottom);
        okButton.Y = -10;
        okButton.Width = 80;
        window.AddChild(okButton.Visual);
        okButton.Click += (_, _) => window.RemoveFromRoot();
    }

    private static void AddRow(ScrollViewer parent, string label, string value)
    {
        var row = new Label();
        row.Text = $"{label}: {value}";
        parent.AddChild(row);
    }

    private static void AddSectionHeader(ScrollViewer parent, string title)
    {
        var header = new Label();
        header.Text = title;
        parent.AddChild(header);
    }
}
