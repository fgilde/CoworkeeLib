using Coworkee.Studio.ViewModels;

namespace Coworkee.Studio.Tests;

public sealed class NavigationTests
{
    [Fact]
    public void Starts_on_the_welcome_page_with_only_it_reachable()
    {
        using var folder = new TempFolder();
        var main = Studio.Create(folder);

        main.Current.ShouldBe(main.Welcome);
        main.Steps.Skip(1).ShouldAllBe(s => !s.IsReachable);
        main.CanGoBack.ShouldBeFalse();
        main.Basics.ShowErrorBadge.ShouldBeFalse();
    }

    [Fact]
    public void Next_waits_for_a_valid_name()
    {
        using var folder = new TempFolder();
        var main = Studio.Create(folder);
        main.NewApp();

        main.Current.ShouldBe(main.Basics);
        main.CanGoNext.ShouldBeFalse();
        main.Basics.Name = "My Shop";
        main.Basics.NameError.ShouldNotBeNull();
        main.CanGoNext.ShouldBeFalse();

        main.Basics.Name = "Shop";
        main.CanGoNext.ShouldBeTrue();
        main.Next();
        main.Current.ShouldBe(main.Branding);
        main.Basics.State.ShouldBe(StepState.Done);
    }

    [Fact]
    public void Jumps_only_to_pages_already_reached_and_back_reverses_the_slide()
    {
        using var folder = new TempFolder();
        var main = Studio.Create(folder);
        main.NewApp();
        main.Basics.Name = "Shop";

        main.GoTo(main.Review);
        main.Current.ShouldBe(main.Basics);

        main.Next();
        main.Next();
        main.IsForward.ShouldBeTrue();
        main.GoTo(main.Basics);
        main.Current.ShouldBe(main.Basics);
        main.IsForward.ShouldBeFalse();
        main.GoTo(main.SignIn);
        main.Current.ShouldBe(main.SignIn);
    }

    [Fact]
    public void Next_on_the_review_starts_the_creation()
    {
        using var folder = new TempFolder();
        var main = Scenario.Shop(folder);
        main.GoTo(main.Review);

        main.Next();

        main.Current.ShouldBe(main.Generate);
        main.Generate.RunState.ShouldBe(RunState.CliMissing);
    }

    [Fact]
    public void Problems_show_on_the_page_they_belong_to()
    {
        using var folder = new TempFolder();
        var main = Scenario.Shop(folder);

        main.Option("title").Value = "Acme <Shop>";
        main.Entities.Modules[0].Entities[0].Properties[0].Name = "Id";

        main.Basics.Errors.ShouldHaveSingleItem().ShouldStartWith("title:");
        main.Basics.TitleError.ShouldNotBeNull();
        main.Basics.CanContinue.ShouldBeFalse();
        main.Entities.Errors.ShouldHaveSingleItem().ShouldStartWith("modules[0].entities[0].properties[0].name");
        main.Entities.Modules[0].Entities[0].Errors.ShouldHaveSingleItem().ShouldStartWith("Property 1 (Id):");
        main.ErrorSummary.ShouldBe("2 problems to fix");
    }

    [Fact]
    public void A_spec_with_problems_is_not_handed_to_the_cli()
    {
        using var folder = new TempFolder();
        var calls = 0;
        var main = Studio.Create(folder, (_, _, _) => Task.FromResult(++calls));
        main.NewApp();
        main.Basics.Name = "1Shop";

        main.Generate.StartCommand.Execute(null);

        main.Generate.RunState.ShouldBe(RunState.Invalid);
        main.Generate.Log.ShouldNotBeEmpty();
        calls.ShouldBe(0);
    }
}
