using Gum.Forms.Controls;

namespace KernSmith.Ui.Layout;

public class EffectsPanel : Panel
{
    public EffectsPanel()
    {
        var label = new Label();
        label.Text = "Effects (coming soon)";
        label.Anchor(Gum.Wireframe.Anchor.Center);
        this.AddChild(label);
    }
}
