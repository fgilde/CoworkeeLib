using Coworkee.Aspire.Tests;

namespace Projects;

// the shape of the types Aspire generates for the projects an app host references
internal sealed class Demo_Migrations() : FakeProjects.FakeProject("Migrations", "Coworkee.Infrastructure");

internal sealed class Demo_Auth() : FakeProjects.FakeProject("Auth", "Coworkee.Infrastructure", "Coworkee.AuthServer", "Coworkee.Mailing");

internal sealed class Demo_Api() : FakeProjects.FakeProject("Api", "Coworkee.Infrastructure", "Coworkee.Realtime", "Coworkee.Search.Elasticsearch", "Coworkee.BackgroundJobs", "Demo.Billing");

internal sealed class Demo_Jobs_Worker() : FakeProjects.FakeProject("Worker", "Coworkee.Infrastructure", "Coworkee.Notifications");

internal sealed class Demo_Web() : FakeProjects.FakeProject("Web", "Coworkee.Bff");

internal sealed class Demo_Tools() : FakeProjects.FakeProject("Tools");
