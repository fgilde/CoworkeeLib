using System.Diagnostics;
using Coworkee.Cli.Spec;
using Coworkee.Contracts.Ai;
using Coworkee.Studio.Model;
using Coworkee.Studio.ViewModels;

namespace Coworkee.Studio.Tests;

public sealed class AiSetupTests
{
    [Fact]
    public void Provider_model_and_endpoint_go_into_the_spec_and_the_key_does_not()
    {
        using var folder = new TempFolder();
        var main = Studio.Create(folder);
        main.NewApp();
        main.Basics.Name = "Shop";
        var ai = main.Architecture.Ai;

        ai.Providers.Select(p => p.Info.Id).ShouldBe(AiProviderCatalog.Ids);
        ai.Selected = ai.Providers.Single(p => p.Info.Id == AiProviderCatalog.OpenRouter);
        ai.Model = "openai/gpt-5";
        ai.ApiKey = "sk-or-secret";

        ai.Info.Name.ShouldBe("OpenRouter");
        ai.NeedsKey.ShouldBeTrue();
        ai.EndpointHint.ShouldBe("https://openrouter.ai/api/v1");
        var spec = main.ToSpec();
        spec.Options["aiProvider"]!.ToString().ShouldBe("openRouter");
        spec.Options["aiModel"]!.ToString().ShouldBe("openai/gpt-5");
        SpecJson.Write(spec).ShouldNotContain("sk-or-secret");
        main.Review.Refresh();
        main.Review.Items.Single(i => i.Label == "AI provider").Value.ShouldBe("OpenRouter · openai/gpt-5");

        ai.Endpoint = "ftp://nope";
        main.Architecture.Errors.ShouldContain(e => e.StartsWith("aiEndpoint:"));

        main.NewApp();
        main.AiApiKey.ShouldBeNull();
    }

    [Fact]
    public void Local_providers_need_no_key_and_the_assistant_switch_rules_the_card()
    {
        using var folder = new TempFolder();
        var main = Studio.Create(folder);
        var ai = main.Architecture.Ai;

        ai.Selected = ai.Providers.Single(p => p.Info.Id == AiProviderCatalog.LmStudio);
        ai.NeedsKey.ShouldBeFalse();
        ai.ModelHint.ShouldBe("Model name");
        ai.Note!.ShouldContain("LM Studio runs on this machine");

        main.Option("ai").IsOn = false;
        ai.IsAvailable.ShouldBeFalse();
    }

    [Fact]
    public async Task After_creation_the_key_goes_to_the_user_secrets_of_the_app_host_and_into_no_log()
    {
        using var folder = new TempFolder();
        var calls = new List<ProcessStartInfo>();
        var main = Studio.Create(folder, (start, output, _) =>
        {
            calls.Add(start);
            output(start.ArgumentList.Contains("user-secrets") ? "Successfully saved Parameters:ai-apikey = sk-secret to the secret store." : "ok", false);
            return Task.FromResult(0);
        }, new StudioSettings { Location = folder.Path, CliPath = "C:/tools/Coworkee.Cli.dll" });
        main.NewApp();
        main.Basics.Name = "Shop";
        main.Architecture.Ai.ApiKey = "sk-secret";

        await main.Generate.StartAsync();

        main.Generate.RunState.ShouldBe(RunState.Succeeded);
        calls[^1].ArgumentList.ShouldBe(["user-secrets", "set", "Parameters:ai-apikey", "sk-secret", "--project", Path.Combine(folder.Path, "Shop", "src", "Shop.AppHost")]);
        main.Generate.Log.ShouldNotContain(l => l.Text.Contains("sk-secret"));
        main.Generate.Log.ShouldContain(l => l.Text.Contains("user-secrets set Parameters:ai-apikey ********"));
    }
}
