using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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

/// <summary>
/// Provider, model and endpoint of the AI assistant (options aiProvider, aiModel, aiEndpoint) and the key, which stays out of the spec:
/// after creation it goes to the user secrets of the app host.
/// </summary>
public sealed partial class AiSetupViewModel(MainViewModel main) : ObservableObject
{
    public IReadOnlyList<AiProviderTileViewModel> Providers { get; } = [.. AiProviderCatalog.All.Select(p => new AiProviderTileViewModel(p))];

    public bool IsAvailable => main.Option("ai").IsOn;

    public AiProviderTileViewModel? Selected
    {
        get => Providers.FirstOrDefault(p => p.Info.Id == main.Option("aiProvider").Value);
        set
        {
            if (value is not null)
            {
                main.Option("aiProvider").Value = value.Info.Id;
            }
        }
    }

    public AiProviderInfo Info => Selected?.Info ?? AiProviderCatalog.All[0];

    public string? Model
    {
        get => main.Option("aiModel").Value;
        set => main.Option("aiModel").Value = string.IsNullOrWhiteSpace(value) ? null : value;
    }

    public string? Endpoint
    {
        get => main.Option("aiEndpoint").Value;
        set => main.Option("aiEndpoint").Value = string.IsNullOrWhiteSpace(value) ? null : value;
    }

    public string? ApiKey
    {
        get => main.AiApiKey;
        set => main.AiApiKey = string.IsNullOrWhiteSpace(value) ? null : value;
    }

    public IReadOnlyList<string> Models => Info.Models;

    public string Notes => L.Get(Info.Notes);

    public string? Note => main.Option("aiProvider").NoteText;

    public string ModelHint => Info.DefaultModel ?? L.Get(Info.Protocol == AiProtocol.AzureOpenAI ? "Deployment name" : "Model name");

    public string EndpointHint => Info.BaseUrl ?? "https://";

    public bool NeedsKey => Info.Key != AiField.None;

    public bool HasKeyUrl => Info.KeyUrl is not null;

    public string KeyLinkText => L.Get(NeedsKey ? "Get a key" : "Get models");

    [RelayCommand]
    public void OpenKeyUrl()
    {
        if (Info.KeyUrl is { } url)
        {
            main.Dialogs.Open(url);
        }
    }

    public void Refresh() => OnPropertyChanged(string.Empty);
}
