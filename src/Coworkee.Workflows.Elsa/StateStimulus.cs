namespace Coworkee.Workflows.Elsa;

/// <summary>What a trigger or bookmark waits for; Elsa matches stimuli by the hash of these values.</summary>
public sealed record StateStimulus(string EntityType, string State);
