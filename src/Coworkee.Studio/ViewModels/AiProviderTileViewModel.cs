using Coworkee.Contracts.Ai;
using Coworkee.Studio.Localization;

namespace Coworkee.Studio.ViewModels;

public sealed record AiProviderTileViewModel(AiProviderInfo Info)
{
    public string Name => Info.Name;

    public string Color => Info.Color;

    public string? Logo => Info.Logo;

    public bool HasLogo => Info.Logo is not null;

    public string Monogram => Info.Monogram;

    public string Tag => L.Get(Info switch
    {
        { Local: true } => "Local",
        { FreeTier: true } => "Free tier",
        { Id: AiProviderCatalog.Custom } => "Any address",
        _ => "Key",
    });
}
