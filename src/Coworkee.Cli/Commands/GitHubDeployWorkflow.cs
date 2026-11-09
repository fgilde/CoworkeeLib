using System.Text.RegularExpressions;
using Coworkee.Cli.Spec;

namespace Coworkee.Cli.Commands;

/// <summary>
/// The deploy workflow of an app (<see cref="DeployWorkflowSettings"/>): <c>.github/workflows/deploy-azure.yml</c> runs <c>coworkee deploy azure --apply</c>
/// for every push to the listed branches: one GitHub environment and one resource group (<c>prefix-branch</c>) per branch, signed in to Azure with OIDC.
/// The generated parameters of an environment live in its GitHub secret COWORKEE_DEPLOY_SECRETS, so passwords and certificates stay from run to run.
/// </summary>
internal static partial class GitHubDeployWorkflow
{
    public const string RelativePath = DeployWorkflowSettings.RelativePath;

    /// <summary>
    /// The resource group of a branch, computed in the workflow because the branch is known only there: lower case, a-z0-9 and '-', at most 90 characters.
    /// Reads RESOURCE_GROUP_PREFIX and BRANCH; writes NAME (the deployment name) and GROUP to stdout as KEY=value lines.
    /// </summary>
    public const string NamesScript = """
        name=$(printf '%s' "$BRANCH" | tr '[:upper:]' '[:lower:]' | sed -E 's/[^a-z0-9-]+/-/g; s/^-+//; s/-+$//')
        group=$(printf '%s' "$RESOURCE_GROUP_PREFIX-$name" | cut -c1-90 | sed -E 's/-+$//')
        echo "NAME=$name"
        echo "GROUP=$group"
        """;

    /// <summary>Writes the workflow; a file changed by hand since coworkee wrote it is overwritten only with <paramref name="force"/>.</summary>
    public static async Task<string> WriteAsync(Solution solution, DeployWorkflowSettings settings, bool force, CancellationToken cancellationToken)
    {
        var path = Path.GetFullPath(Path.Combine(solution.Root, RelativePath));
        var yaml = DeployWorkflowSettings.Stamp(Yaml(solution, settings));
        if (File.Exists(path) && !force && DeployWorkflowSettings.IsChangedByHand(await File.ReadAllTextAsync(path, cancellationToken)))
        {
            throw new CliException($"{RelativePath} was changed by hand since coworkee wrote it; --force overwrites it and your changes are lost.");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, yaml, cancellationToken);
        return path;
    }

    /// <summary>Removes the workflow when the option is switched off, unless it was changed by hand.</summary>
    public static string? Remove(Solution solution)
    {
        var path = Path.GetFullPath(Path.Combine(solution.Root, RelativePath));
        if (!File.Exists(path))
        {
            return null;
        }

        if (DeployWorkflowSettings.IsChangedByHand(File.ReadAllText(path)))
        {
            return $"{RelativePath} was changed by hand, so it stays; delete it yourself.";
        }

        File.Delete(path);
        return $"{RelativePath} removed.";
    }

    public static string Yaml(Solution solution, DeployWorkflowSettings settings)
    {
        var prefix = settings.ResourceGroupPrefix ?? Sanitized(solution.Name);
        if (!Prefix().IsMatch(prefix))
        {
            throw new CliException($"Resource group prefix {prefix}: lower case letters, digits and '-', starting with a letter or digit, at most 60 characters.");
        }

        var branches = settings.Branches.Count > 0 ? settings.Branches : ["main"];
        var key = solution.Name.ToUpperInvariant().Replace('-', '_').Replace('.', '_');
        var setupDotnet = File.Exists(Path.Combine(solution.Root, "global.json")) ? "global-json-file: global.json" : "dotnet-version: 10.0.x";
        var names = string.Join("\n", NamesScript.Replace("\r\n", "\n").Split('\n').Select(l => $"          {l}"))
            .Replace("echo \"NAME=$name\"", "echo \"NAME=$name\" >> \"$GITHUB_ENV\"").Replace("echo \"GROUP=$group\"", "echo \"GROUP=$group\" >> \"$GITHUB_ENV\"");
        // the AirWorks client is built with Node.js 22 (as in the CI workflow of the template)
        var node = Directory.Exists(Path.Combine(solution.Src, $"{solution.Name}.AirWorks"))
            ? "\n      - uses: actions/setup-node@v4\n        with:\n          node-version: 22"
            : string.Empty;
        var delete = settings.TeardownOnDelete ? "\n  delete:" : string.Empty;
        var teardown = settings.TeardownOnDelete ? $$$"""

              # DELETES the branch's resource group with everything in it (database, files) when the branch is deleted; never for the default branch.
              # The delete event has no branch filter: the condition compares the deleted branch with the list above.
              teardown:
                if: >-
                  github.event_name == 'delete' && github.event.ref_type == 'branch' && github.event.ref != github.event.repository.default_branch
                  && ({{{string.Join(" || ", branches.Select(Matches))}}})
                runs-on: ubuntu-latest
                environment: ${{ github.event.ref }}
                steps:
                  - name: Resource group of the branch
                    run: |
            {{{names}}}
                  - uses: azure/login@v2
                    with:
                      client-id: ${{ secrets.AZURE_CLIENT_ID }}
                      tenant-id: ${{ secrets.AZURE_TENANT_ID }}
                      subscription-id: ${{ secrets.AZURE_SUBSCRIPTION_ID }}
                  - name: Delete the resource group
                    run: |
                      if [ "$(az group exists -n "$GROUP")" = true ]; then az group delete -n "$GROUP" --yes --no-wait; fi
            """ : string.Empty;
        return $$$"""
            # Written by coworkee (option deployWorkflow); change its settings with coworkee deploy azure --github or in the Studio. coworkee notices
            # changes made here by hand and overwrites them only when told to. Every push to a branch below deploys it to Azure Container Apps:
            # GitHub environment <branch>, resource group {{{prefix}}}-<branch>. One-time setup: Hosting > Deployment in the Coworkee docs.
            name: Deploy to Azure

            on:
              push:
                branches: [{{{string.Join(", ", branches.Select(Quoted))}}}]
              workflow_dispatch:{{{delete}}}

            permissions:
              id-token: write # OIDC sign-in to Azure, no client secret
              contents: read

            env:
              RESOURCE_GROUP_PREFIX: {{{prefix}}}
              LOCATION: {{{settings.Location}}}
              BRANCH: ${{ github.event_name == 'delete' && github.event.ref || github.ref_name }}

            # one run per branch at a time: two deployments into the same resource group would trip over each other
            concurrency:
              group: deploy-azure-${{ github.event_name == 'delete' && github.event.ref || github.ref_name }}
              cancel-in-progress: false

            jobs:
              deploy:
                if: github.event_name != 'delete'
                runs-on: ubuntu-latest
                environment: ${{ github.ref_name }}
                steps:
                  - uses: actions/checkout@v4
                  - uses: actions/setup-dotnet@v4
                    with:
                      {{{setupDotnet}}}{{{node}}}
                  - name: Resource group and deployment name of the branch
                    run: |
            {{{names}}}
                  - name: wasm-tools when the web client links native code (SQLite in the browser, AOT)
                    run: |
                      if grep -rqsE --include='*.csproj' 'WasmBuildNative|RunAOTCompilation|ClientEntities\.Sqlite' src; then dotnet workload install wasm-tools; fi
                  - name: Coworkee CLI in the version of the app
                    run: |
                      version=$(sed -n 's:.*<CoworkeeVersion>\(.*\)</CoworkeeVersion>.*:\1:p' Directory.Packages.props)
                      dotnet tool install -g Coworkee.Cli --version "$version"
                      echo "$HOME/.dotnet/tools" >> "$GITHUB_PATH"
                  - uses: azure/login@v2
                    with:
                      client-id: ${{ secrets.AZURE_CLIENT_ID }}
                      tenant-id: ${{ secrets.AZURE_TENANT_ID }}
                      subscription-id: ${{ secrets.AZURE_SUBSCRIPTION_ID }}
                  # the parameters generated on the first run (administrator, passwords, certificates, push keys) come back from the environment's secret;
                  # values set here are taken on the first run of an environment only
                  - name: Deploy
                    env:
                      COWORKEE_DEPLOY_SECRETS: ${{ secrets.COWORKEE_DEPLOY_SECRETS }}
                      GH_TOKEN: ${{ secrets.COWORKEE_SECRETS_TOKEN }}
                      {{{key}}}_ADMIN_EMAIL: ${{ vars.{{{key}}}_ADMIN_EMAIL }}
                      {{{key}}}_ADMIN_PASSWORD: ${{ secrets.{{{key}}}_ADMIN_PASSWORD }}
                      {{{key}}}_SMTP_HOST: ${{ vars.{{{key}}}_SMTP_HOST }}
                      {{{key}}}_SMTP_PORT: ${{ vars.{{{key}}}_SMTP_PORT }}
                      {{{key}}}_SMTP_USERNAME: ${{ vars.{{{key}}}_SMTP_USERNAME }}
                      {{{key}}}_SMTP_FROM: ${{ vars.{{{key}}}_SMTP_FROM }}
                      {{{key}}}_SMTP_PASSWORD: ${{ secrets.{{{key}}}_SMTP_PASSWORD }}
                    run: |
                      if [ -z "$COWORKEE_DEPLOY_SECRETS" ] && [ -z "$GH_TOKEN" ]; then
                        echo "::error::The generated parameters could not be kept: set the repository secret COWORKEE_SECRETS_TOKEN (see the Coworkee docs)."
                        exit 1
                      fi
                      mkdir -p .coworkee/deployments
                      if [ -n "$COWORKEE_DEPLOY_SECRETS" ]; then printf '%s' "$COWORKEE_DEPLOY_SECRETS" > ".coworkee/deployments/$NAME.secrets.json"; fi
                      coworkee deploy azure --resource-group "$GROUP" --location "$LOCATION" --name "$NAME" --apply --yes
                  # also after a failed run: what the first attempt created already uses these values
                  - name: Keep the generated parameters in the environment
                    if: always()
                    env:
                      COWORKEE_DEPLOY_SECRETS: ${{ secrets.COWORKEE_DEPLOY_SECRETS }}
                      GH_TOKEN: ${{ secrets.COWORKEE_SECRETS_TOKEN }}
                    run: |
                      file=".coworkee/deployments/$NAME.secrets.json"
                      if [ -f "$file" ] && [ "$(cat "$file")" != "$COWORKEE_DEPLOY_SECRETS" ]; then
                        gh secret set COWORKEE_DEPLOY_SECRETS --env "$BRANCH" --repo "$GITHUB_REPOSITORY" < "$file"
                      fi{{{teardown}}}

            """.Replace("\r\n", "\n");
    }

    /// <summary>The condition of the teardown job for one branch of the list: a pattern with * compares by its prefix.</summary>
    private static string Matches(string branch) =>
        branch.IndexOf('*') is var star and >= 0 ? $"startsWith(github.event.ref, '{branch[..star]}')" : $"github.event.ref == '{branch}'";

    private static string Quoted(string branch) => branch.Contains('*') ? $"'{branch}'" : branch;

    private static string Sanitized(string name) => SanitizedChars().Replace(name.ToLowerInvariant(), "-").Trim('-') is { Length: > 0 } s ? s[..Math.Min(s.Length, 60)] : "app";

    [GeneratedRegex("^[a-z0-9][a-z0-9-]{0,59}$")]
    private static partial Regex Prefix();

    [GeneratedRegex("[^a-z0-9-]+")]
    private static partial Regex SanitizedChars();
}
