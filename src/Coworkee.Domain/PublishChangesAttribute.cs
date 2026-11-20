namespace Coworkee.Domain;

/// <summary>
/// Saved changes of this entity type (and of types derived from it) go to the outbox as <c>EntityChange&lt;T&gt;</c> for its <c>IEntityChangeHandler&lt;T&gt;</c>;
/// the same as <c>AddCoworkeeEvents(e => e.PublishChanges&lt;T&gt;())</c>.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class PublishChangesAttribute : Attribute;
