using Gum.DataTypes;
using Gum.Forms;
using Gum.Forms.Controls;
using KernSmith.Ui.Styling;
using Microsoft.Xna.Framework;
using MonoGameGum.GueDeriving;

namespace KernSmith.Ui.Layout;

/// <summary>
/// Modal color picker dialog with hex, RGB, HSL, and HSV input modes.
/// All inputs sync bidirectionally. Includes "New" and "Previous" color preview swatches.
/// </summary>
public class ColorPickerDialog
{
    private bool _suppressSync;

    private byte _r, _g, _b;
    private readonly Color _previousColor;

    private TextBox _hexBox = null!;
    private TextBox _rBox = null!, _gBox = null!, _bBox = null!;
    private TextBox _hslHBox = null!, _hslSBox = null!, _hslLBox = null!;
    private TextBox _hsvHBox = null!, _hsvSBox = null!, _hsvVBox = null!;
    private ColoredRectangleRuntime _newSwatch = null!;

    private ColorPickerDialog(Color currentColor)
    {
        _previousColor = currentColor;
        _r = currentColor.R;
        _g = currentColor.G;
        _b = currentColor.B;
    }

    /// <summary>
    /// Opens the color picker dialog. Calls <paramref name="onColorSelected"/> with the chosen color on OK.
    /// Does nothing on Cancel.
    /// </summary>
    public static void Show(Color currentColor, Action<Color> onColorSelected)
    {
        var dialog = new ColorPickerDialog(currentColor);
        dialog.Build(onColorSelected);
    }

    private void Build(Action<Color> onColorSelected)
    {
        var window = new Window();
        window.Anchor(Gum.Wireframe.Anchor.Center);
        window.Width = 400;
        window.Height = 260;
        FrameworkElement.ModalRoot.Children.Add(window.Visual);

        var stack = new ContainerRuntime();
        stack.WidthUnits = DimensionUnitType.RelativeToParent;
        stack.HeightUnits = DimensionUnitType.RelativeToChildren;
        stack.Width = -16;
        stack.Height = 0;
        stack.X = 8;
        stack.Y = 32;
        stack.ChildrenLayout = Gum.Managers.ChildrenLayout.TopToBottomStack;
        stack.StackSpacing = 4;
        window.AddChild(stack);

        // --- Color preview swatches ---
        BuildSwatchRow(stack);

        // --- Input grid (Hex, RGB, HSL, HSV) ---
        BuildInputGrid(stack);

        // --- OK / Cancel buttons ---
        var buttonRow = new ContainerRuntime();
        buttonRow.WidthUnits = DimensionUnitType.RelativeToParent;
        buttonRow.Width = 0;
        buttonRow.HeightUnits = DimensionUnitType.RelativeToChildren;
        buttonRow.Height = 0;
        buttonRow.ChildrenLayout = Gum.Managers.ChildrenLayout.LeftToRightStack;
        buttonRow.StackSpacing = 8;
        stack.Children.Add(buttonRow);

        var okBtn = new Button();
        okBtn.Text = "OK";
        okBtn.Width = 80;
        okBtn.Click += (_, _) =>
        {
            onColorSelected(new Color(_r, _g, _b));
            window.RemoveFromRoot();
        };
        buttonRow.Children.Add(okBtn.Visual);

        var cancelBtn = new Button();
        cancelBtn.Text = "Cancel";
        cancelBtn.Width = 80;
        cancelBtn.Click += (_, _) => window.RemoveFromRoot();
        buttonRow.Children.Add(cancelBtn.Visual);

        // Initial sync to populate all fields from the current color
        SyncFromRgb();
    }

    private void BuildSwatchRow(ContainerRuntime parent)
    {
        var row = new ContainerRuntime();
        row.WidthUnits = DimensionUnitType.RelativeToParent;
        row.Width = 0;
        row.HeightUnits = DimensionUnitType.RelativeToChildren;
        row.Height = 0;
        row.ChildrenLayout = Gum.Managers.ChildrenLayout.LeftToRightStack;
        row.StackSpacing = 4;
        parent.Children.Add(row);

        // "New" label + swatch
        var newLabel = new TextRuntime();
        newLabel.Text = "New";
        newLabel.Width = 36;
        newLabel.Color = Theme.TextMuted;
        row.Children.Add(newLabel);

        var newContainer = new ContainerRuntime();
        newContainer.Width = 40;
        newContainer.Height = 24;
        row.Children.Add(newContainer);

        var newBorder = new ColoredRectangleRuntime();
        newBorder.Width = 0;
        newBorder.WidthUnits = DimensionUnitType.RelativeToParent;
        newBorder.Height = 0;
        newBorder.HeightUnits = DimensionUnitType.RelativeToParent;
        newBorder.Color = Theme.PanelBorder;
        newContainer.Children.Add(newBorder);

        _newSwatch = new ColoredRectangleRuntime();
        _newSwatch.X = 1;
        _newSwatch.Y = 1;
        _newSwatch.Width = -2;
        _newSwatch.WidthUnits = DimensionUnitType.RelativeToParent;
        _newSwatch.Height = -2;
        _newSwatch.HeightUnits = DimensionUnitType.RelativeToParent;
        _newSwatch.Color = new Color(_r, _g, _b);
        newContainer.Children.Add(_newSwatch);

        // "Previous" label + swatch
        var prevLabel = new TextRuntime();
        prevLabel.Text = "Previous";
        prevLabel.Width = 56;
        prevLabel.Color = Theme.TextMuted;
        row.Children.Add(prevLabel);

        var prevContainer = new ContainerRuntime();
        prevContainer.Width = 40;
        prevContainer.Height = 24;
        row.Children.Add(prevContainer);

        var prevBorder = new ColoredRectangleRuntime();
        prevBorder.Width = 0;
        prevBorder.WidthUnits = DimensionUnitType.RelativeToParent;
        prevBorder.Height = 0;
        prevBorder.HeightUnits = DimensionUnitType.RelativeToParent;
        prevBorder.Color = Theme.PanelBorder;
        prevContainer.Children.Add(prevBorder);

        var prevSwatch = new ColoredRectangleRuntime();
        prevSwatch.X = 1;
        prevSwatch.Y = 1;
        prevSwatch.Width = -2;
        prevSwatch.WidthUnits = DimensionUnitType.RelativeToParent;
        prevSwatch.Height = -2;
        prevSwatch.HeightUnits = DimensionUnitType.RelativeToParent;
        prevSwatch.Color = _previousColor;
        prevContainer.Children.Add(prevSwatch);
    }

    private void BuildInputGrid(ContainerRuntime parent)
    {
        // Outer horizontal stack acts as a "grid" with vertical column stacks
        var grid = new ContainerRuntime();
        grid.HeightUnits = DimensionUnitType.RelativeToChildren;
        grid.Height = 0;
        grid.WidthUnits = DimensionUnitType.RelativeToChildren;
        grid.Width = 0;
        grid.ChildrenLayout = Gum.Managers.ChildrenLayout.LeftToRightStack;
        grid.StackSpacing = 2;
        parent.Children.Add(grid);

        // Helper to create a vertical column stack
        ContainerRuntime MakeColumn()
        {
            var col = new ContainerRuntime();
            col.HeightUnits = DimensionUnitType.RelativeToChildren;
            col.Height = 0;
            col.WidthUnits = DimensionUnitType.RelativeToChildren;
            col.Width = 0;
            col.ChildrenLayout = Gum.Managers.ChildrenLayout.TopToBottomStack;
            col.StackSpacing = 4;
            grid.Children.Add(col);
            return col;
        }

        // Helper to create a label TextRuntime
        TextRuntime MakeLabel(string text, float width)
        {
            var lbl = new TextRuntime();
            lbl.Text = text;
            lbl.Width = width;
            lbl.Color = string.IsNullOrEmpty(text) ? Theme.TextMuted : Theme.Text;
            return lbl;
        }

        // Helper to create a letter TextRuntime
        TextRuntime MakeLetter(string text)
        {
            var lbl = new TextRuntime();
            lbl.Text = text;
            lbl.Width = 12;
            lbl.Color = Theme.TextMuted;
            return lbl;
        }

        // --- Column 0: Row labels ---
        var col0 = MakeColumn();
        col0.Children.Add(MakeLabel("Hex:", 36));
        col0.Children.Add(MakeLabel("RGB:", 36));
        col0.Children.Add(MakeLabel("HSL:", 36));
        col0.Children.Add(MakeLabel("HSV:", 36));

        // --- Precompute HSL/HSV initial values ---
        var (hslH, hslS, hslL) = RgbToHsl(_r, _g, _b);
        var (hsvH, hsvS, hsvV) = RgbToHsv(_r, _g, _b);

        // --- Column 1: Letter 1 ---
        var col1 = MakeColumn();
        col1.Children.Add(MakeLetter(""));   // hex has no letter
        col1.Children.Add(MakeLetter("R"));
        col1.Children.Add(MakeLetter("H"));
        col1.Children.Add(MakeLetter("H"));

        // --- Column 2: Value 1 ---
        var col2 = MakeColumn();

        _hexBox = new TextBox();
        _hexBox.Width = 80;
        _hexBox.Text = $"#{_r:X2}{_g:X2}{_b:X2}";
        _hexBox.TextChanged += (_, _) =>
        {
            if (_suppressSync) return;
            var hex = _hexBox.Text?.Trim() ?? "";
            if (hex.StartsWith('#')) hex = hex[1..];
            if (hex.Length == 6 &&
                byte.TryParse(hex[..2], System.Globalization.NumberStyles.HexNumber, null, out var r) &&
                byte.TryParse(hex[2..4], System.Globalization.NumberStyles.HexNumber, null, out var g) &&
                byte.TryParse(hex[4..6], System.Globalization.NumberStyles.HexNumber, null, out var b))
            {
                _r = r; _g = g; _b = b;
                SyncFromRgb(syncHex: false);
            }
        };
        col2.Children.Add(_hexBox.Visual);

        _rBox = new TextBox();
        _rBox.Width = 50;
        _rBox.Text = _r.ToString();
        col2.Children.Add(_rBox.Visual);

        _hslHBox = new TextBox();
        _hslHBox.Width = 50;
        _hslHBox.Text = ((int)hslH).ToString();
        col2.Children.Add(_hslHBox.Visual);

        _hsvHBox = new TextBox();
        _hsvHBox.Width = 50;
        _hsvHBox.Text = ((int)hsvH).ToString();
        col2.Children.Add(_hsvHBox.Visual);

        // --- Column 3: Letter 2 ---
        var col3 = MakeColumn();
        col3.Children.Add(MakeLetter(""));   // hex spacer
        col3.Children.Add(MakeLetter("G"));
        col3.Children.Add(MakeLetter("S"));
        col3.Children.Add(MakeLetter("S"));

        // --- Column 4: Value 2 ---
        var col4 = MakeColumn();

        var spacer4 = new ContainerRuntime();
        spacer4.Width = 50;
        spacer4.Height = 24;
        col4.Children.Add(spacer4);

        _gBox = new TextBox();
        _gBox.Width = 50;
        _gBox.Text = _g.ToString();
        col4.Children.Add(_gBox.Visual);

        _hslSBox = new TextBox();
        _hslSBox.Width = 50;
        _hslSBox.Text = ((int)hslS).ToString();
        col4.Children.Add(_hslSBox.Visual);

        _hsvSBox = new TextBox();
        _hsvSBox.Width = 50;
        _hsvSBox.Text = ((int)hsvS).ToString();
        col4.Children.Add(_hsvSBox.Visual);

        // --- Column 5: Letter 3 ---
        var col5 = MakeColumn();
        col5.Children.Add(MakeLetter(""));   // hex spacer
        col5.Children.Add(MakeLetter("B"));
        col5.Children.Add(MakeLetter("L"));
        col5.Children.Add(MakeLetter("V"));

        // --- Column 6: Value 3 ---
        var col6 = MakeColumn();

        var spacer6 = new ContainerRuntime();
        spacer6.Width = 50;
        spacer6.Height = 24;
        col6.Children.Add(spacer6);

        _bBox = new TextBox();
        _bBox.Width = 50;
        _bBox.Text = _b.ToString();
        col6.Children.Add(_bBox.Visual);

        _hslLBox = new TextBox();
        _hslLBox.Width = 50;
        _hslLBox.Text = ((int)hslL).ToString();
        col6.Children.Add(_hslLBox.Visual);

        _hsvVBox = new TextBox();
        _hsvVBox.Width = 50;
        _hsvVBox.Text = ((int)hsvV).ToString();
        col6.Children.Add(_hsvVBox.Visual);

        // --- Wire up TextChanged handlers ---

        void OnRgbChanged()
        {
            if (_suppressSync) return;
            if (byte.TryParse(_rBox.Text, out var r) &&
                byte.TryParse(_gBox.Text, out var g) &&
                byte.TryParse(_bBox.Text, out var b))
            {
                _r = r; _g = g; _b = b;
                SyncFromRgb(syncRgb: false);
            }
        }

        _rBox.TextChanged += (_, _) => OnRgbChanged();
        _gBox.TextChanged += (_, _) => OnRgbChanged();
        _bBox.TextChanged += (_, _) => OnRgbChanged();

        void OnHslChanged()
        {
            if (_suppressSync) return;
            if (int.TryParse(_hslHBox.Text, out var hv) &&
                int.TryParse(_hslSBox.Text, out var sv) &&
                int.TryParse(_hslLBox.Text, out var lv) &&
                hv is >= 0 and <= 360 && sv is >= 0 and <= 100 && lv is >= 0 and <= 100)
            {
                var (r, g, b) = HslToRgb(hv, sv, lv);
                _r = r; _g = g; _b = b;
                SyncFromRgb(syncHsl: false);
            }
        }

        _hslHBox.TextChanged += (_, _) => OnHslChanged();
        _hslSBox.TextChanged += (_, _) => OnHslChanged();
        _hslLBox.TextChanged += (_, _) => OnHslChanged();

        void OnHsvChanged()
        {
            if (_suppressSync) return;
            if (int.TryParse(_hsvHBox.Text, out var hv) &&
                int.TryParse(_hsvSBox.Text, out var sv) &&
                int.TryParse(_hsvVBox.Text, out var vv) &&
                hv is >= 0 and <= 360 && sv is >= 0 and <= 100 && vv is >= 0 and <= 100)
            {
                var (r, g, b) = HsvToRgb(hv, sv, vv);
                _r = r; _g = g; _b = b;
                SyncFromRgb(syncHsv: false);
            }
        }

        _hsvHBox.TextChanged += (_, _) => OnHsvChanged();
        _hsvSBox.TextChanged += (_, _) => OnHsvChanged();
        _hsvVBox.TextChanged += (_, _) => OnHsvChanged();
    }

    /// <summary>
    /// Syncs all input fields and preview swatch from the current _r, _g, _b values.
    /// Pass false for any mode that triggered the change to avoid redundant updates.
    /// </summary>
    private void SyncFromRgb(bool syncHex = true, bool syncRgb = true, bool syncHsl = true, bool syncHsv = true)
    {
        _suppressSync = true;
        try
        {
            _newSwatch.Color = new Color(_r, _g, _b);

            if (syncHex)
                _hexBox.Text = $"#{_r:X2}{_g:X2}{_b:X2}";

            if (syncRgb)
            {
                _rBox.Text = _r.ToString();
                _gBox.Text = _g.ToString();
                _bBox.Text = _b.ToString();
            }

            if (syncHsl)
            {
                var (h, s, l) = RgbToHsl(_r, _g, _b);
                _hslHBox.Text = ((int)h).ToString();
                _hslSBox.Text = ((int)s).ToString();
                _hslLBox.Text = ((int)l).ToString();
            }

            if (syncHsv)
            {
                var (hv, sv, vv) = RgbToHsv(_r, _g, _b);
                _hsvHBox.Text = ((int)hv).ToString();
                _hsvSBox.Text = ((int)sv).ToString();
                _hsvVBox.Text = ((int)vv).ToString();
            }
        }
        finally
        {
            _suppressSync = false;
        }
    }

    // --- Color conversion helpers ---

    /// <summary>Converts RGB (0-255) to HSL. Returns H (0-360), S (0-100), L (0-100).</summary>
    private static (float H, float S, float L) RgbToHsl(byte r, byte g, byte b)
    {
        float rf = r / 255f, gf = g / 255f, bf = b / 255f;
        float max = MathF.Max(rf, MathF.Max(gf, bf));
        float min = MathF.Min(rf, MathF.Min(gf, bf));
        float delta = max - min;
        float l = (max + min) / 2f;

        if (delta == 0)
            return (0, 0, l * 100f);

        float s = l > 0.5f ? delta / (2f - max - min) : delta / (max + min);
        float h;

        if (max == rf)
            h = ((gf - bf) / delta) % 6f;
        else if (max == gf)
            h = (bf - rf) / delta + 2f;
        else
            h = (rf - gf) / delta + 4f;

        h *= 60f;
        if (h < 0) h += 360f;

        return (h, s * 100f, l * 100f);
    }

    /// <summary>Converts HSL to RGB. H (0-360), S (0-100), L (0-100) -> RGB bytes.</summary>
    private static (byte R, byte G, byte B) HslToRgb(float h, float s, float l)
    {
        s /= 100f;
        l /= 100f;

        if (s == 0)
        {
            var v = (byte)MathF.Round(l * 255f);
            return (v, v, v);
        }

        float q = l < 0.5f ? l * (1f + s) : l + s - l * s;
        float p = 2f * l - q;
        float hNorm = h / 360f;

        float rf = HueToRgb(p, q, hNorm + 1f / 3f);
        float gf = HueToRgb(p, q, hNorm);
        float bf = HueToRgb(p, q, hNorm - 1f / 3f);

        return (
            (byte)MathF.Round(rf * 255f),
            (byte)MathF.Round(gf * 255f),
            (byte)MathF.Round(bf * 255f));
    }

    private static float HueToRgb(float p, float q, float t)
    {
        if (t < 0f) t += 1f;
        if (t > 1f) t -= 1f;
        if (t < 1f / 6f) return p + (q - p) * 6f * t;
        if (t < 1f / 2f) return q;
        if (t < 2f / 3f) return p + (q - p) * (2f / 3f - t) * 6f;
        return p;
    }

    /// <summary>Converts RGB (0-255) to HSV. Returns H (0-360), S (0-100), V (0-100).</summary>
    private static (float H, float S, float V) RgbToHsv(byte r, byte g, byte b)
    {
        float rf = r / 255f, gf = g / 255f, bf = b / 255f;
        float max = MathF.Max(rf, MathF.Max(gf, bf));
        float min = MathF.Min(rf, MathF.Min(gf, bf));
        float delta = max - min;

        float h;
        if (delta == 0)
            h = 0;
        else if (max == rf)
            h = 60f * (((gf - bf) / delta) % 6f);
        else if (max == gf)
            h = 60f * ((bf - rf) / delta + 2f);
        else
            h = 60f * ((rf - gf) / delta + 4f);

        if (h < 0) h += 360f;

        float s = max == 0 ? 0 : delta / max;

        return (h, s * 100f, max * 100f);
    }

    /// <summary>Converts HSV to RGB. H (0-360), S (0-100), V (0-100) -> RGB bytes.</summary>
    private static (byte R, byte G, byte B) HsvToRgb(float h, float s, float v)
    {
        s /= 100f;
        v /= 100f;

        float c = v * s;
        float x = c * (1f - MathF.Abs(h / 60f % 2f - 1f));
        float m = v - c;

        float rf, gf, bf;
        if (h < 60) { rf = c; gf = x; bf = 0; }
        else if (h < 120) { rf = x; gf = c; bf = 0; }
        else if (h < 180) { rf = 0; gf = c; bf = x; }
        else if (h < 240) { rf = 0; gf = x; bf = c; }
        else if (h < 300) { rf = x; gf = 0; bf = c; }
        else { rf = c; gf = 0; bf = x; }

        return (
            (byte)MathF.Round((rf + m) * 255f),
            (byte)MathF.Round((gf + m) * 255f),
            (byte)MathF.Round((bf + m) * 255f));
    }
}
