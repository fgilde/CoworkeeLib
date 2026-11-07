namespace Coworkee.Cli.Spec;

public static partial class SpecValidator
{
    private const int MaxStateLength = 50;
    private static readonly string[] StateColors = ["primary", "secondary", "info", "success", "warning", "error"];

    private static void Workflow(List<string> errors, string path, EntitySpec entity, AppSpec spec)
    {
        if (entity.Workflow is not { } workflow)
        {
            return;
        }

        var states = workflow.AllStates.ToList();
        Check(errors, path + ".initial", workflow.Initial.Length == 0, "a workflow needs an initial state");
        Check(errors, path + ".transitions", workflow.Transitions.Count == 0 && !workflow.States.Any(s => s.Approval is not null), "a workflow needs a transition or an approval");
        foreach (var state in states.Where(s => s.Length > MaxStateLength || !IsSafeTitle(s) || s.Contains('{') || s.Contains('}')))
        {
            errors.Add($"{path}: state '{state}' must have 1 to {MaxStateLength} characters without quotes, backslashes, braces, <, >, @ or control characters.");
        }

        foreach (var (transition, index) in workflow.Transitions.Select((t, i) => (t, i)))
        {
            var at = $"{path}.transitions[{index}]";
            Check(errors, at + ".name", transition.Name.Length is 0 or > MaxStateLength || !IsSafeTitle(transition.Name) || transition.Name.Contains('{') || transition.Name.Contains('}'),
                $"a transition needs a name of 1 to {MaxStateLength} characters without quotes, backslashes, braces, <, >, @ or control characters");
            Check(errors, at + ".name", workflow.Transitions.Take(index).Any(t => t.Name.Equals(transition.Name, StringComparison.OrdinalIgnoreCase)), $"'{transition.Name}' appears twice");
            Check(errors, at + ".from", transition.From.Count == 0, "a transition starts from at least one state");
            Check(errors, at + ".to", transition.To.Length == 0, "a transition leads to a state");
            if (transition.Permission is { } permission)
            {
                Name(errors, at + ".permission", permission);
                Check(errors, at + ".permission", permission == "GroupName" || permission == entity.PluralName, $"'{permission}' is taken in the permissions class of {entity.PluralName}");
            }
        }

        var properties = entity.Properties.ToDictionary(p => p.Name, StringComparer.Ordinal);
        if (workflow.MirrorTo is { } mirror)
        {
            Check(errors, path + ".mirrorTo", !properties.TryGetValue(mirror, out var target) || target.Type != PropertyType.String || target.Required,
                $"'{mirror}' is no optional string property of {entity.Name}");
        }

        var roles = new HashSet<string>([.. PermissionCatalog.BuiltInRoles(spec), .. spec.Roles.Select(r => r.Name), "admin"], StringComparer.OrdinalIgnoreCase);
        foreach (var (state, index) in workflow.States.Select((s, i) => (s, i)))
        {
            var at = $"{path}.states[{index}]";
            Check(errors, at + ".name", workflow.States.Take(index).Any(s => s.Name.Equals(state.Name, StringComparison.OrdinalIgnoreCase)), $"'{state.Name}' appears twice");
            Check(errors, at + ".color", state.Color is { } color && !StateColors.Contains(color), $"the color is one of {string.Join(", ", StateColors)}");
            Check(errors, at + ".stamp", state.Stamp is { } stamp && (!properties.TryGetValue(stamp, out var stamped) || stamped.Type != PropertyType.DateTime || stamped.Required),
                $"'{state.Stamp}' is no optional dateTime property of {entity.Name}");
            Check(errors, at + ".notify", state.Notify is { } notify && !roles.Contains(notify), $"'{state.Notify}' is no role of the spec or the app");
            if (state.Approval is { } approval)
            {
                Check(errors, at + ".approval", approval.Approved.Length == 0 || approval.Rejected.Length == 0, "an approval names the approved and the rejected state");
                Check(errors, at + ".approval.roles", approval.Roles.Count == 0, "an approval needs the roles whose members decide");
                foreach (var role in approval.Roles.Where(r => !roles.Contains(r)))
                {
                    errors.Add($"{at}.approval.roles: '{role}' is no role of the spec or the app.");
                }

                Check(errors, at + ".approval.dueInDays", approval.DueInDays is < 1, "the deadline is at least one day");
                Check(errors, at + ".approval.remindAfterDays", approval.RemindAfterDays is < 1 || (approval.RemindAfterDays >= approval.DueInDays), "the reminder comes after at least one day and before the deadline");
            }
        }
    }
}
