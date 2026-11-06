namespace Coworkee.Client.Blazor.Navigation;

/// <summary>Where an item of the app bar shows; the second bar's places count only while there is a second bar, else they are the first bar's.</summary>
public enum HeaderPlacement
{
    Bar,
    Overflow,
    SecondBar,
    SecondOverflow,
    Hidden,
}
