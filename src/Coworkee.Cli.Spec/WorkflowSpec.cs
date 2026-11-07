using System.ComponentModel;
using System.Text.Json.Serialization;

namespace Coworkee.Cli.Spec;

/// <summary>The process of an entity: states, transitions and approvals of Coworkee.Workflows; switches the workflows option on.</summary>
public sealed class WorkflowSpec
{
    [Description("State of a new row, e.g. Draft.")]
    public string Initial { get; set; } = string.Empty;

    [Description("The moves people make, e.g. { \"name\": \"Submit\", \"from\": [\"Draft\"], \"to\": \"In review\" }.")]
    public List<WorkflowTransitionSpec> Transitions { get; set; } = [];

    [Description("What happens in a state: color, a time stamp, a notification, an approval; states only named in transitions need no entry.")]
    public List<WorkflowStateSpec> States { get; set; } = [];

    [Description("A string property that gets the state name as well, for filters on client entities and in reports.")]
    public string? MirrorTo { get; set; }

    /// <summary>Every state: the initial one, those of the transitions, approvals and entries.</summary>
    [JsonIgnore]
    public IEnumerable<string> AllStates =>
        new[] { Initial }.Concat(Transitions.SelectMany(t => t.From.Append(t.To))).Concat(States.SelectMany(s => s.Approval is { } a ? new[] { s.Name, a.Approved, a.Rejected } : [s.Name]))
            .Where(s => s.Length > 0).Distinct();

    /// <summary>Permission names of the transitions that are not one of View, Create, Edit and Delete, e.g. Approve.</summary>
    [JsonIgnore]
    public IEnumerable<string> ExtraPermissions => Transitions.Select(t => t.Permission).OfType<string>().Where(p => !WorkflowTransitionSpec.EntityPermissions.Contains(p)).Distinct();
}

public sealed class WorkflowTransitionSpec
{
    internal static readonly string[] EntityPermissions = ["View", "Create", "Edit", "Delete"];

    [Description("What the button says, e.g. Submit.")]
    public string Name { get; set; } = string.Empty;

    [Description("States it starts from.")]
    public List<string> From { get; set; } = [];

    [Description("State it leads to.")]
    public string To { get; set; } = string.Empty;

    [Description("Permission of the entity it needs: Edit (default), View, Create, Delete or an own one like Approve (<Module>.<Plural>.Approve, for roles).")]
    public string? Permission { get; set; }

    [Description("Asks for a comment, e.g. a reason to reopen.")]
    public bool Comment { get; set; }
}

public sealed class WorkflowStateSpec
{
    [Description("State name.")]
    public string Name { get; set; } = string.Empty;

    [Description("Color of the state chip: primary, secondary, info, success, warning or error.")]
    public string? Color { get; set; }

    [Description("A dateTime property set to the moment the state is entered, e.g. ApprovedOn.")]
    public string? Stamp { get; set; }

    [Description("A role of the spec whose members get a notification when the state is entered.")]
    public string? Notify { get; set; }

    [Description("Entering the state asks for a decision; the workflow moves on to approved or rejected.")]
    public ApprovalSpec? Approval { get; set; }
}

public sealed class ApprovalSpec
{
    [Description("State after an approval.")]
    public string Approved { get; set; } = string.Empty;

    [Description("State after a rejection.")]
    public string Rejected { get; set; } = string.Empty;

    [Description("Roles whose members decide (one of them is enough); a role of the spec or admin.")]
    public List<string> Roles { get; set; } = [];

    [Description("Days until the deadline; without, no deadline.")]
    public int? DueInDays { get; set; }

    [Description("Days after which the approvers who have not decided are reminded once.")]
    public int? RemindAfterDays { get; set; }

    [Description("The user who started the approval does not decide it.")]
    public bool NotByRequester { get; set; }
}
