using Aspire.Hosting;

namespace Coworkee.Aspire.Tests;

/// <summary>Projects whose restore output names the Coworkee packages they use.</summary>
internal static class FakeProjects
{
    internal sealed class Migrations : FakeProject
    {
        public Migrations() : base("Migrations", "Coworkee.Infrastructure")
        {
        }
    }

    internal sealed class Auth : FakeProject
    {
        public Auth() : base("Auth", "Coworkee.Infrastructure", "Coworkee.AuthServer", "Coworkee.Account", "Coworkee.Mailing", "Coworkee.BackgroundJobs")
        {
        }
    }

    internal sealed class Api : FakeProject
    {
        public Api() : base("Api", "Coworkee.Infrastructure", "Coworkee.Realtime", "Coworkee.Mailing", "Coworkee.Storage", "Coworkee.Notifications")
        {
        }
    }

    internal sealed class Web : FakeProject
    {
        public Web() : base("Web", "Coworkee.Bff")
        {
        }
    }

    internal abstract class FakeProject : IProjectMetadata
    {
        protected FakeProject(string name, params string[] packages)
        {
            var directory = Path.Combine(Path.GetTempPath(), "coworkee-aspire-tests", Guid.NewGuid().ToString("N"), name);
            Directory.CreateDirectory(Path.Combine(directory, "obj"));
            var libraries = string.Join(",", packages.Select(p => $"\"{p}/1.0.0\": {{ \"type\": \"package\" }}"));
            File.WriteAllText(Path.Combine(directory, "obj", "project.assets.json"), $"{{ \"libraries\": {{ {libraries} }} }}");
            ProjectPath = Path.Combine(directory, name + ".csproj");
            File.WriteAllText(ProjectPath, "<Project Sdk=\"Microsoft.NET.Sdk.Web\" />");
            Directory.CreateDirectory(Path.Combine(directory, "Properties"));
            File.WriteAllText(Path.Combine(directory, "Properties", "launchSettings.json"),
                "{ \"profiles\": { \"https\": { \"commandName\": \"Project\", \"applicationUrl\": \"https://localhost:7001;http://localhost:5001\" } } }");
        }

        public string ProjectPath { get; }
    }
}
