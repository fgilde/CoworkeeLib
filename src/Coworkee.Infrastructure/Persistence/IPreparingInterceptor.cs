using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Coworkee.Infrastructure.Persistence;

/// <summary>
/// An interceptor that runs before every other one, built-in or registered as <see cref="IInterceptor"/>: rows it adds while saving get their
/// tenant and audit fields, an audit entry, history, outbox, realtime and webhook messages like any other change. Register it as this type, not as <see cref="IInterceptor"/>.
/// </summary>
public interface IPreparingInterceptor : IInterceptor;
