using Coworkee.Cli.Generation;
using Coworkee.Cli.Spec;

namespace Coworkee.Cli.Tests;

// a spec workflow becomes a declaration for Coworkee.Workflows, a registration, own permissions and a state column
public sealed class WorkflowSpecTests
{
    private static AppSpec Spec() => new()
    {
        Name = "Shop",
        Modules =
        [
            new ModuleSpec
            {
                Name = "Desk",
                Entities =
                [
                    new EntitySpec
                    {
                        Name = "Request",
                        ClientEntity = true,
                        Properties =
                        [
                            new PropertySpec { Name = "Title", Required = true, MaxLength = 200, Display = true },
                            new PropertySpec { Name = "DecidedOn", Type = PropertyType.DateTime },
                            new PropertySpec { Name = "Status", MaxLength = 50 },
                        ],
                        Workflow = new WorkflowSpec
                        {
                            Initial = "Draft",
                            MirrorTo = "Status",
                            Transitions =
                            [
                                new WorkflowTransitionSpec { Name = "Submit", From = ["Draft"], To = "In review" },
                                new WorkflowTransitionSpec { Name = "Reopen", From = ["Approved", "Rejected"], To = "Draft", Permission = "Reopen", Comment = true },
                            ],
                            States =
                            [
                                new WorkflowStateSpec
                                {
                                    Name = "In review",
                                    Color = "warning",
                                    Approval = new ApprovalSpec { Approved = "Approved", Rejected = "Rejected", Roles = ["Approver"], DueInDays = 3, RemindAfterDays = 1, NotByRequester = true },
                                },
                                new WorkflowStateSpec { Name = "Approved", Color = "success", Stamp = "DecidedOn", Notify = "Approver" },
                            ],
                        },
                    },
                ],
            },
        ],
        Roles = [new RoleSpec { Name = "Approver", Permissions = ["Desk.Requests.View", "Desk.Requests.Reopen"] }],
    };

    private static List<GeneratedFile> Files(AppSpec spec) =>
        [.. FeatureWriter.Files(new EntityModel("Shop", "Desk", spec.Modules[0].Entities[0], MappingStrategy.Manual, new SpecIndex(spec)))];

    private static string Content(IEnumerable<GeneratedFile> files, string suffix) => files.Single(f => f.Path.EndsWith(suffix, StringComparison.Ordinal)).Content;

    [Fact]
    public void A_workflow_is_declared_registered_permitted_and_shown()
    {
        var spec = Spec();
        var files = Files(spec);

        var declaration = Content(files, "Features/Requests/RequestWorkflow.cs");
        declaration.ShouldContain("workflows.Entity<Request>(EntityType, flow =>");
        declaration.ShouldContain("flow.Initial(\"Draft\").Describe(e => e.Title).Link(_ => \"/desk/requests\").MirrorTo(e => e.Status);");
        declaration.ShouldContain("flow.Transition(\"Submit\", [\"Draft\"], \"In review\").RequiresPermission(DeskPermissions.Requests.Edit);");
        declaration.ShouldContain("flow.Transition(\"Reopen\", [\"Approved\", \"Rejected\"], \"Draft\").RequiresPermission(DeskPermissions.Requests.Reopen).RequiresComment();");
        declaration.ShouldContain("flow.State(\"In review\").WithColor(\"var(--mud-palette-warning)\").Approval(\"Approved\", \"Rejected\", approval => approval.Role(\"Approver\").DueIn(TimeSpan.FromDays(3)).RemindAfter(TimeSpan.FromDays(1)).NotByRequester());");
        declaration.ShouldContain("flow.State(\"Approved\").WithColor(\"var(--mud-palette-success)\").Set(e => e.DecidedOn = DateTimeOffset.UtcNow).NotifyRole(\"Approver\");");
        ServerFiles.Services(new EntityModel("Shop", "Desk", spec.Modules[0].Entities[0], MappingStrategy.Manual, new SpecIndex(spec)))
            .ShouldContain("Features.Requests.RequestWorkflow.AddRequestWorkflow(context.Services);");
        Content(files, "DeskPermissions.Requests.cs").ShouldContain("public const string Reopen = \"Desk.Requests.Reopen\";");
        Content(files, "RequestPermissionDefinitions.cs").ShouldContain(".Add(DeskPermissions.Requests.Reopen, \"Reopen requests\", DeskPermissions.Requests.View)");
        Content(files, "RequestsPage.razor").ShouldContain("<CoworkeeWorkflowColumn T=\"RequestDto\" EntityType=\"Requests\" Title=\"@L[\"Status\"]\" MirrorProperty=\"Status\" />");
        ServerFiles.Module("Shop", "Desk", [new EntityModel("Shop", "Desk", spec.Modules[0].Entities[0], MappingStrategy.Manual, new SpecIndex(spec))])
            .First().Content.ShouldContain("<CoworkeePackage Include=\"Coworkee.Workflows\" />");
    }

    [Fact]
    public void What_the_workflow_sets_stays_out_of_the_form()
    {
        var files = Files(Spec());

        var request = Content(files, "AddEditRequestRequest.cs");
        request.ShouldContain("Title");
        request.ShouldNotContain("Status");
        request.ShouldNotContain("DecidedOn");
        Content(files, "Features/Requests/AddEditRequest.cs").ShouldNotContain("entity.Status =");
        Content(files, "RequestsPage.razor.cs").ShouldNotContain("p.DecidedOn");
        Content(files, "RequestDto.cs").ShouldContain("DecidedOn");
    }

    [Fact]
    public void A_workflow_switches_the_option_on_and_its_permissions_are_known_to_roles()
    {
        var spec = Spec();

        SpecOptions.Resolve(spec)["workflows"].ShouldBe("true");
        PermissionCatalog.For(spec).ShouldContain("Desk.Requests.Reopen");
        SpecValidator.Validate(spec).ShouldBeEmpty();
        spec.Options["workflows"] = false;
        SpecValidator.Validate(spec).ShouldContain(e => e.StartsWith("options.workflows:"));
    }

    [Fact]
    public void Broken_workflows_are_reported_with_their_path()
    {
        var spec = Spec();
        var workflow = spec.Modules[0].Entities[0].Workflow!;
        workflow.Initial = "";
        workflow.MirrorTo = "Title";
        workflow.Transitions.Add(new WorkflowTransitionSpec { Name = "Submit", From = [], To = "Done", Permission = "may approve" });
        workflow.Transitions.Add(new WorkflowTransitionSpec { Name = "Group", From = ["Draft"], To = "Done", Permission = "GroupName" });
        workflow.States[0].Approval!.Roles = ["Nobody"];
        workflow.States[0].Approval!.RemindAfterDays = 5;
        workflow.States[1].Stamp = "Title";
        workflow.States[1].Color = "pink";

        var errors = SpecValidator.Validate(spec);

        const string at = "modules[0].entities[0].workflow";
        errors.ShouldContain(e => e.StartsWith($"{at}.initial:"));
        errors.ShouldContain(e => e.StartsWith($"{at}.mirrorTo:"));
        errors.ShouldContain(e => e.StartsWith($"{at}.transitions[2].name:") && e.Contains("twice"));
        errors.ShouldContain(e => e.StartsWith($"{at}.transitions[2].from:"));
        errors.ShouldContain(e => e.StartsWith($"{at}.transitions[2].permission:"));
        errors.ShouldContain(e => e.StartsWith($"{at}.transitions[3].permission:") && e.Contains("taken"));
        errors.ShouldContain(e => e.StartsWith($"{at}.states[0].approval.roles:") && e.Contains("Nobody"));
        errors.ShouldContain(e => e.StartsWith($"{at}.states[0].approval.remindAfterDays:"));
        errors.ShouldContain(e => e.StartsWith($"{at}.states[1].stamp:"));
        errors.ShouldContain(e => e.StartsWith($"{at}.states[1].color:"));
    }

    [Fact]
    public void A_workflow_survives_the_json_of_the_spec()
    {
        var read = SpecJson.Read(SpecJson.Write(Spec()));

        var workflow = read.Modules[0].Entities[0].Workflow.ShouldNotBeNull();
        workflow.Transitions[1].Permission.ShouldBe("Reopen");
        workflow.States[0].Approval!.Roles.ShouldBe(["Approver"]);
    }
}
