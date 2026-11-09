using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Coworkee.Studio.Localization;
using Coworkee.Studio.ViewModels;

namespace Coworkee.Studio.Views;

public sealed partial class MainWindow : Window
{
    private readonly SplashView? _splash;

    public MainWindow()
        : this(new MainView())
    {
    }

    /// <summary>A window that shows the splash until <see cref="StartAsync"/> has built the Studio.</summary>
    public MainWindow(SplashView splash)
        : this(new Panel { Children = { splash } }) => _splash = splash;

    private MainWindow(Control content)
    {
        InitializeComponent();
        Content = content;
    }

    /// <summary>
    /// Builds the Studio below the splash once the splash is on screen, then lets the splash reveal it.
    /// Null when <paramref name="create"/> failed: the splash shows why and offers to close.
    /// </summary>
    public async Task<MainViewModel?> StartAsync(Func<MainViewModel> create)
    {
        var splash = _splash ?? throw new InvalidOperationException("The window has no splash.");
        var layers = (Panel)Content!;
        await NextFrameAsync();
        Trace.WriteLine($"Coworkee Studio: first frame after {Program.Clock.ElapsedMilliseconds} ms");
        MainView view;
        MainViewModel main;
        try
        {
            splash.Status = L.Get("Looking for your apps…");
            await NextFrameAsync();
            main = create();
            DataContext = main;
            Attach(main);

            splash.Status = L.Get("Preparing the workspace…");
            await NextFrameAsync();
            view = new MainView();
            layers.Children.Insert(0, view);

            // laid out and rendered once below the splash, so the reveal does not stutter
            await NextFrameAsync();
        }
        catch (Exception exception)
        {
            splash.ShowError(L.Format("The Studio could not start: {0}", exception.Message));
            return null;
        }

        await splash.RevealAsync(view, view.TitleLogo);
        layers.Children.Remove(splash);
        Trace.WriteLine($"Coworkee Studio: ready after {Program.Clock.ElapsedMilliseconds} ms");
        return main;
    }

    /// <summary>Completes after the next frame was committed: the callback runs before the render, the background job after it.</summary>
    private async Task NextFrameAsync()
    {
        var frame = new TaskCompletionSource();
        RequestAnimationFrame(_ => frame.TrySetResult());
        await frame.Task;
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
    }

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
