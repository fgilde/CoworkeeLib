using Avalonia.Controls;
using Avalonia.Interactivity;
using Coworkee.Studio.Localization;
using Coworkee.Studio.ViewModels;

namespace Coworkee.Studio.Views;

public sealed partial class EntitiesView : UserControl
{
    public EntitiesView() => InitializeComponent();

    private void ZoomIn(object? sender, RoutedEventArgs e) => Diagram.ZoomBy(1.2);

    private void ZoomOut(object? sender, RoutedEventArgs e) => Diagram.ZoomBy(1 / 1.2);

    private void Fit(object? sender, RoutedEventArgs e) => Diagram.Fit();

    private async void ExportPng(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not EntitiesStep step || Diagram.Export() is not { } image)
        {
            return;
        }

        using (image)
        {
            var name = (step.Main.Basics.Name is { Length: > 0 } app ? app : "entities") + "-entities.png";
            if (await step.Main.Dialogs.SavePng(name) is { } path)
            {
                image.Save(path, new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
                step.Main.Status = L.Format("Saved {0}", Path.GetFileName(path));
            }
        }
    }
}
