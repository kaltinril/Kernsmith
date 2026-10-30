using Gum.Forms;
using Gum.Forms.Controls;
using Gum.Forms.DefaultVisuals.V3;

namespace KernSmith.Ui.Tests;

/// <summary>
/// Lets tests build Gum Forms text boxes without a GumService or graphics device. Gum's template
/// table is shared static state, so the classes that use it run in one non-parallel collection
/// and register the template once.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class HeadlessGum
{
    public const string Name = "Headless Gum controls";

    static HeadlessGum()
    {
        FrameworkElement.DefaultFormsTemplates[typeof(TextBox)] =
            new VisualTemplate(() => new TextBoxVisual(tryCreateFormsObject: false));
    }

    /// <summary>Registers the text box template (once per process).</summary>
    public static void EnsureInitialized() { }
}
