using Avalonia;
using Avalonia.Controls;

namespace Coworkee.Studio.Views;

public sealed partial class BasicsView : UserControl
{
    public BasicsView() => InitializeComponent();

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        NameBox.Focus();
    }
}
