using Coworkee.Studio.Localization;

namespace Coworkee.Studio.ViewModels;

public sealed record ChoiceViewModel(string Value, string Description, string? InfoUrl = null)
{
    private static readonly Dictionary<string, (string Label, string Icon)> Looks = new()
    {
        ["postgres"] = ("PostgreSQL", "Icon.Database"),
        ["sqlServer"] = ("SQL Server", "Icon.Server"),
        ["sqlite"] = ("SQLite", "Icon.File"),
        ["anthropic"] = ("Anthropic", "Icon.Sparkle"),
        ["openAI"] = ("OpenAI", "Icon.Robot"),
        ["azureOpenAI"] = ("Azure OpenAI", "Icon.Cloud"),
        ["ollama"] = ("Ollama", "Icon.Monitor"),
        ["openAICompatible"] = ("OpenAI compatible", "Icon.Link"),
        ["libreTranslate"] = ("LibreTranslate", "Icon.Container"),
        ["myMemory"] = ("MyMemory", "Icon.Cloud"),
        ["deepL"] = ("DeepL", "Icon.Translate"),
        ["azure"] = ("Azure AI Translator", "Icon.Cloud"),
        ["google"] = ("Google Cloud Translation", "Icon.Cloud"),
        ["ai"] = ("AI assistant", "Icon.Sparkle"),
        ["none"] = ("None", "Icon.Close"),
        ["rabbitMq"] = ("RabbitMQ", "Icon.Broadcast"),
        ["azureBlob"] = ("Azure Blob Storage", "Icon.Cloud"),
        ["mapperly"] = ("Mapperly", "Icon.SwapHorizontal"),
        ["npm"] = ("npm", "Icon.Package"),
    };

    public string Text => L.Get(Description);

    public string Label => Looks.TryGetValue(Value, out var look) ? L.Get(look.Label) : Value;

    public string Icon => Looks.TryGetValue(Value, out var look) ? look.Icon : "Icon.Cube";
}
