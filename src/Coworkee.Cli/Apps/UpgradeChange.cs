namespace Coworkee.Cli.Apps;

/// <summary>A change coworkee update made (or would make), or a place a manual step concerns.</summary>
internal sealed record UpgradeChange(string File, string Text, int Line = 0)
{
    public override string ToString() => Line > 0 ? $"{File}:{Line}: {Text}" : $"{File}: {Text}";
}
