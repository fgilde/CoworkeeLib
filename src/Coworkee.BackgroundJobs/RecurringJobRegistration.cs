using Hangfire;

namespace Coworkee.BackgroundJobs;

public sealed record RecurringJobRegistration(string Id, string Cron, Action<IRecurringJobManager, string, string> Schedule);
