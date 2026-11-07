using Coworkee.Cli.Spec;

namespace Coworkee.Cli.Generation;

/// <summary>The workflow of an entity: its declaration for Coworkee.Workflows, the line that registers it and the state column of the list.</summary>
internal static class WorkflowFiles
{
    public static string Registration(EntityModel e) =>
        e.Spec.Workflow is null ? string.Empty : $"        Features.{e.Plural}.{e.Name}Workflow.Add{e.Name}Workflow(context.Services);\n";

    public static string Column(EntityModel e)
    {
        if (e.Spec.Workflow is not { } workflow)
        {
            return string.Empty;
        }

        // rows of a client entity have no server-side WorkflowState; the mirrored property filters, sorts and groups them
        var mirror = e.Spec.ClientEntity && workflow.MirrorTo is { } property ? $" MirrorProperty=\"{property}\"" : string.Empty;
        return $"        <CoworkeeWorkflowColumn T=\"{e.Name}Dto\" EntityType=\"{e.Plural}\" Title=\"@L[\"Status\"]\"{mirror} />\n";
    }

    public static string Declaration(EntityModel e, WorkflowSpec workflow) => $$"""
        using Coworkee.Workflows;
        using Microsoft.Extensions.DependencyInjection;
        using {{e.App}}.{{e.Module}}.Domain;
        using {{e.App}}.Contracts.{{e.Module}};

        namespace {{e.App}}.{{e.Module}}.Features.{{e.Plural}};

        /// <summary>The process of {{e.PluralWords}}, written from the workflow of the spec; guards (When), actions (Then, OnEnter) and more belong here.</summary>
        public static class {{e.Name}}Workflow
        {
            public const string EntityType = "{{e.Plural}}";

            public static IServiceCollection Add{{e.Name}}Workflow(this IServiceCollection services) =>
                services.AddCoworkeeWorkflows(workflows => workflows.Entity<{{e.Name}}>(EntityType, flow =>
                {
                    flow.Initial("{{workflow.Initial}}").Describe(e => {{e.Describe}}).Link(_ => "{{e.Route}}"){{(workflow.MirrorTo is { } mirror ? $".MirrorTo(e => e.{mirror})" : string.Empty)}};
        {{string.Concat(workflow.Transitions.Select(t => Transition(e, t)))}}{{string.Concat(workflow.States.Select(State).Where(s => s.Length > 0))}}        }));
        }

        """;

    private static string Transition(EntityModel e, WorkflowTransitionSpec t) =>
        $"            flow.Transition(\"{t.Name}\", [{string.Join(", ", t.From.Select(f => $"\"{f}\""))}], \"{t.To}\").RequiresPermission({e.Permissions}.{t.Permission ?? "Edit"}){(t.Comment ? ".RequiresComment()" : string.Empty)};\n";

    private static string State(WorkflowStateSpec s)
    {
        var calls = new List<string>();
        if (s.Color is { } color)
        {
            calls.Add($".WithColor(\"var(--mud-palette-{color})\")");
        }

        if (s.Stamp is { } stamp)
        {
            calls.Add($".Set(e => e.{stamp} = DateTimeOffset.UtcNow)");
        }

        if (s.Notify is { } role)
        {
            calls.Add($".NotifyRole(\"{role}\")");
        }

        if (s.Approval is { } a)
        {
            var step = $"approval => approval.Role({string.Join(", ", a.Roles.Select(r => $"\"{r}\""))})"
                + (a.DueInDays is { } due ? $".DueIn(TimeSpan.FromDays({due}))" : string.Empty)
                + (a.RemindAfterDays is { } remind ? $".RemindAfter(TimeSpan.FromDays({remind}))" : string.Empty)
                + (a.NotByRequester ? ".NotByRequester()" : string.Empty);
            calls.Add($".Approval(\"{a.Approved}\", \"{a.Rejected}\", {step})");
        }

        return calls.Count == 0 ? string.Empty : $"            flow.State(\"{s.Name}\"){string.Concat(calls)};\n";
    }
}
