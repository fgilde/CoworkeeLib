namespace Coworkee.Cli.Generation;

/// <summary>Text edits of the files a generated app already has; each one leaves the file alone when the change is there.</summary>
internal static class AppEdits
{
    private const string Build = "var host = builder.Build();";
    private const string ConfigureApplication = "    }\n\n    public void ConfigureApplication(WebApplication app)\n    {\n";

    public static string AddToSolution(string slnx, string projectPath)
    {
        var line = $"    <Project Path=\"{projectPath}\" />\n";
        if (slnx.Contains($"\"{projectPath}\"", StringComparison.Ordinal))
        {
            return slnx;
        }

        var folder = slnx.IndexOf("<Folder Name=\"/src/\">", StringComparison.Ordinal);
        var end = folder < 0 ? slnx.IndexOf("</Solution>", StringComparison.Ordinal) : slnx.IndexOf("  </Folder>", folder, StringComparison.Ordinal);
        return end < 0 ? throw new CliException("The .slnx has no <Folder Name=\"/src/\">; add the project yourself.") : slnx.Insert(end, line);
    }

    public static string AddToItems(string project, string element)
    {
        var include = element[(element.IndexOf("Include=", StringComparison.Ordinal))..].Split(' ')[0];
        if (project.Contains(include, StringComparison.Ordinal))
        {
            return project;
        }

        var end = project.IndexOf("  </ItemGroup>", StringComparison.Ordinal);
        return end < 0 ? throw new CliException("No <ItemGroup> found; add the reference yourself: " + element.Trim()) : project.Insert(end, element);
    }

    public static string RegisterModule(string databaseModule, string moduleType)
    {
        const string Marker = "[DependsOn(";
        var index = databaseModule.IndexOf(Marker, StringComparison.Ordinal);
        return databaseModule.Contains($"typeof({moduleType})", StringComparison.Ordinal) ? databaseModule
            : index < 0 ? throw new CliException("No [DependsOn(...)] found in the database module; register the module there yourself.")
            : databaseModule.Insert(index + Marker.Length, $"typeof({moduleType}), ");
    }

    public static string AddBeforeBuild(string program, string lines)
    {
        var index = program.IndexOf(Build, StringComparison.Ordinal);
        return program.Contains(lines, StringComparison.Ordinal) ? program
            : index < 0 ? throw new CliException($"No '{Build}' in Program.cs of the web client; add these lines yourself:\n{lines}")
            : program.Insert(index, lines);
    }

    public static string EnableClientEntities(string program)
    {
        const string Using = "using Coworkee.Client.Blazor.ClientEntities;\n";
        program = AddBeforeBuild(program, "builder.Services.AddCoworkeeClientEntities();\n");
        return program.Contains(Using, StringComparison.Ordinal) ? program : Using + program;
    }

    public static string AddToModule(string moduleClass, string services, string endpoint)
    {
        var index = moduleClass.IndexOf(ConfigureApplication, StringComparison.Ordinal);
        return index < 0
            ? throw new CliException($"The module class does not look generated; register the entity yourself:\n{services}{endpoint}")
            : moduleClass.Insert(index + ConfigureApplication.Length, endpoint).Insert(index, services);
    }

    public static string Normalize(string text) => text.Replace("\r\n", "\n", StringComparison.Ordinal);
}
