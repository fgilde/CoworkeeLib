namespace Coworkee.Contracts.Localization;

public static class TranslationProviders
{
    public const string None = "none";
    public const string LibreTranslate = "libreTranslate";
    public const string MyMemory = "myMemory";
    public const string DeepL = "deepL";
    public const string Azure = "azure";
    public const string Google = "google";
    public const string Ai = "ai";

    public static readonly string[] All = [None, LibreTranslate, MyMemory, DeepL, Azure, Google, Ai];
}
