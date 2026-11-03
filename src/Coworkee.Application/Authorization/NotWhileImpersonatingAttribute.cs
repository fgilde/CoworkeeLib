namespace Coworkee.Application.Authorization;

/// <summary>On a command or query: refused while the caller is signed in as another user ("Log in as"), e.g. changes of the user's own credentials.</summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class NotWhileImpersonatingAttribute : Attribute;
