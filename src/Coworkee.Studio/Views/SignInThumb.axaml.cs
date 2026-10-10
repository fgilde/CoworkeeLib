using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Coworkee.Studio.Views;

/// <summary>A drawn sketch of the sign-in page in one of the layouts, in the colors of a preview palette.</summary>
public sealed partial class SignInThumb : UserControl
{
    public static readonly StyledProperty<string> LayoutProperty = AvaloniaProperty.Register<SignInThumb, string>(nameof(Layout), "Slide");

    public static readonly StyledProperty<bool> MediaRightProperty = AvaloniaProperty.Register<SignInThumb, bool>(nameof(MediaRight));

    public static readonly StyledProperty<ViewModels.PreviewViewModel?> PreviewProperty = AvaloniaProperty.Register<SignInThumb, ViewModels.PreviewViewModel?>(nameof(Preview));

    public static readonly StyledProperty<IImage?> MediaProperty = AvaloniaProperty.Register<SignInThumb, IImage?>(nameof(Media));

    public SignInThumb() => InitializeComponent();

    public string Layout
    {
        get => GetValue(LayoutProperty);
        set => SetValue(LayoutProperty, value);
    }

    public bool MediaRight
    {
        get => GetValue(MediaRightProperty);
        set => SetValue(MediaRightProperty, value);
    }

    public ViewModels.PreviewViewModel? Preview
    {
        get => GetValue(PreviewProperty);
        set => SetValue(PreviewProperty, value);
    }

    public IImage? Media
    {
        get => GetValue(MediaProperty);
        set => SetValue(MediaProperty, value);
    }
}
