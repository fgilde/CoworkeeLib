namespace Coworkee.Contracts.DataFilters;

public static class DataFilterTopics
{
    /// <summary>
    /// A data filter or link was saved or deleted, in any organisation (System ones touch every organisation): open tables and the admin views of the
    /// filters load again. A global topic, so it reaches every signed-in user whatever the layer's permission; it carries only ids and property names.
    /// </summary>
    public const string Changed = "global:data-filters";
}
