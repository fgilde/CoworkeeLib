using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Coworkee.Studio.Views;

public sealed partial class MainView : UserControl
{
    public MainView() => InitializeComponent();

    private void Minimize(object? sender, RoutedEventArgs e) => Window()?.WindowState = WindowState.Minimized;

    private void Maximize(object? sender, RoutedEventArgs e) =>
        Window()?.WindowState = Window()?.WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void Close(object? sender, RoutedEventArgs e) => Window()?.Close();

    private Window? Window() => TopLevel.GetTopLevel(this) as Window;
}
