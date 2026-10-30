using System.Reflection;
using Gum.Forms;
using Gum.Forms.Controls;
using Editor = Gum.Themes.Editor;
using V3 = Gum.Forms.DefaultVisuals.V3;

namespace KernSmith.Ui.Tests;

/// <summary>
/// Lets tests build Gum Forms controls and the app's panels without a graphics device:
/// <c>GumService.InitializeForTesting</c>, the Editor theme's control visuals (what
/// <c>EditorTheme.Apply</c> registers; Apply itself needs a GraphicsDevice) and a do-nothing
/// cursor (sliders read it). Gum's template table and cursor are shared static state, so the
/// classes that use them run in one non-parallel collection and set them up once.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class HeadlessGum
{
    public const string Name = "Headless Gum controls";

    static HeadlessGum()
    {
        global::Gum.GumService.Default.InitializeForTesting();

        void Add(Type formsType, Func<object?, bool, global::Gum.Wireframe.GraphicalUiElement> visual)
            => FrameworkElement.DefaultFormsTemplates[formsType] = new VisualTemplate(visual);
        Add(typeof(TextBox), (_, c) => new Editor.TextBoxVisual(tryCreateFormsObject: c));
        Add(typeof(Button), (_, c) => new Editor.ButtonVisual(tryCreateFormsObject: c));
        Add(typeof(CheckBox), (_, c) => new Editor.CheckBoxVisual(tryCreateFormsObject: c));
        Add(typeof(ComboBox), (_, c) => new Editor.ComboBoxVisual(tryCreateFormsObject: c));
        Add(typeof(Expander), (_, c) => new Editor.ExpanderVisual(tryCreateFormsObject: c));
        Add(typeof(ListBoxItem), (_, c) => new Editor.ListBoxItemVisual(tryCreateFormsObject: c));
        Add(typeof(ListBox), (_, c) => new Editor.ListBoxVisual(tryCreateFormsObject: c));
        Add(typeof(ScrollBar), (_, c) => new Editor.ScrollBarVisual(tryCreateFormsObject: c));
        Add(typeof(ScrollViewer), (_, c) => new Editor.ScrollViewerVisual(tryCreateFormsObject: c));
        Add(typeof(Slider), (_, c) => new Editor.SliderVisual(tryCreateFormsObject: c));
        Add(typeof(RadioButton), (_, c) => new V3.RadioButtonVisual(tryCreateFormsObject: c));
        Add(typeof(Label), (_, c) => new V3.LabelVisual(tryCreateFormsObject: c));
        Add(typeof(Splitter), (_, c) => new V3.SplitterVisual(tryCreateFormsObject: c));

        FormsUtilities.SetCursor(StubCursor.Create());
    }

    /// <summary>Sets Gum up (once per process).</summary>
    public static void EnsureInitialized() { }

    /// <summary>An <see cref="global::Gum.Wireframe.ICursor"/> with no input: properties keep what is assigned, everything else is default.</summary>
    public class StubCursor : DispatchProxy
    {
        private readonly Dictionary<string, object?> _properties = [];

        public static global::Gum.Wireframe.ICursor Create() => Create<global::Gum.Wireframe.ICursor, StubCursor>();

        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            var name = method!.Name;
            if (name.StartsWith("set_", StringComparison.Ordinal))
            {
                _properties[name[4..]] = args![0];
                return null;
            }
            if (name.StartsWith("get_", StringComparison.Ordinal) && _properties.TryGetValue(name[4..], out var value))
                return value;
            return method.ReturnType.IsValueType && method.ReturnType != typeof(void)
                ? Activator.CreateInstance(method.ReturnType)
                : null;
        }
    }
}
