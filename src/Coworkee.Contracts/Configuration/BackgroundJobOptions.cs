namespace Coworkee.Contracts.Configuration;

public sealed class BackgroundJobOptions
{
    public const string Section = "Coworkee:Jobs";

    public string ConnectionStringName { get; set; } = "default";

    public bool RunServer { get; set; } = true;

    public string[] Queues { get; set; } = ["default", "mail"];

    public int WorkerCount { get; set; } = 5;

    public int Attempts { get; set; } = 3;

    public int[] RetryDelaysInSeconds { get; set; } = [10, 60, 300];

    public TimeSpan PollingInterval { get; set; } = TimeSpan.FromSeconds(15);

    public string DashboardPath { get; set; } = "/admin/jobs";
}
