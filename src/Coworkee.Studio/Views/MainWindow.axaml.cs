using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Platform.Storage;
using Coworkee.Studio.ViewModels;

namespace Coworkee.Studio.Views;

public sealed partial class MainWindow : Window
{
    public MainWindow() => InitializeComponent();

    /// <summary>Pickers, clipboard and keyboard shortcuts for the view model; a language switch rebuilds the view.</summary>
    public void Attach(MainViewModel main)
    {
        main.Dialogs.OpenFile = async (title, patterns) =>
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = title,
                FileTypeFilter = [new FilePickerFileType(string.Join(", ", patterns)) { Patterns = patterns }, FilePickerFileTypes.All],
            });
            return files.Count > 0 ? files[0].TryGetLocalPath() : null;
        };
        main.Dialogs.PickFolder = async title =>
        {
            var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = title });
            return folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
        };
        main.Dialogs.SaveFile = async name =>
        {
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                SuggestedFileName = name,
                DefaultExtension = "json",
                FileTypeChoices = [new FilePickerFileType("App spec") { Patterns = ["*.json"] }],
            });
            return file?.TryGetLocalPath();
        };
        main.Dialogs.SavePng = async name =>
        {
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                SuggestedFileName = name,
                DefaultExtension = "png",
                FileTypeChoices = [FilePickerFileTypes.ImagePng],
            });
            return file?.TryGetLocalPath();
        };
        main.Dialogs.Copy = async text =>
        {
            if (Clipboard is { } clipboard)
            {
                await clipboard.SetTextAsync(text);
            }
        };
        main.LanguageChanged += () => Content = new MainView();

        KeyBindings.Add(new KeyBinding { Gesture = new KeyGesture(Key.Right, KeyModifiers.Alt), Command = main.NextCommand });
        KeyBindings.Add(new KeyBinding { Gesture = new KeyGesture(Key.Left, KeyModifiers.Alt), Command = main.BackCommand });
        KeyBindings.Add(new KeyBinding { Gesture = new KeyGesture(Key.F1), Command = main.ToggleHelpCommand });
        KeyBindings.Add(new KeyBinding { Gesture = new KeyGesture(Key.O, KeyModifiers.Control), Command = main.OpenSpecCommand });
        KeyBindings.Add(new KeyBinding { Gesture = new KeyGesture(Key.S, KeyModifiers.Control), Command = main.SaveSpecCommand });
        KeyBindings.Add(new KeyBinding { Gesture = new KeyGesture(Key.N, KeyModifiers.Control), Command = main.NewAppCommand });
    }
}
