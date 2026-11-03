namespace Coworkee.Client.Blazor.ClientEntities;

/// <summary>Tells the local copies that this client saved something, so they fetch the changes right away.</summary>
internal sealed class ClientEntitySignal
{
    public event Action? Mutated;

    public void RaiseMutated() => Mutated?.Invoke();
}
