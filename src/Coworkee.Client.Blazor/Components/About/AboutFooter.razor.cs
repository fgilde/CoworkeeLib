using System.Runtime.InteropServices;

namespace Coworkee.Client.Blazor.Components.About;

/// <summary>The runtime the app runs on and the animated gilde.org mark.</summary>
public partial class AboutFooter
{
    private static string Runtime => RuntimeInformation.FrameworkDescription;
}
