using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.Client.Blazor.Localization;

/// <summary>
/// Translates the messages of failed API calls like the app's own texts (<see cref="CoworkeeLocalizer"/>): the title, filled from its template
/// when the server sent one with arguments, and the validation messages. A message without translation stays English.
/// </summary>
internal sealed class ProblemTextHandler(IServiceProvider services) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var response = await base.SendAsync(request, cancellationToken);
        if (response.IsSuccessStatusCode || response.Content.Headers.ContentType?.MediaType is not { } type || !type.Contains("json", StringComparison.Ordinal))
        {
            return response;
        }

        JsonNode? problem;
        try
        {
            problem = JsonNode.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        }
        catch (System.Text.Json.JsonException)
        {
            return response;
        }

        // resolved here: the localizer loads its texts through an API client that runs this handler; a client without one, or outside the browser
        // where the localizer cannot be made (it needs IJSRuntime: server-side callers, tests), passes the problem on as it came
        if (problem is JsonObject body && Localizer() is { } l)
        {
            if (body["title"]?.GetValue<string>() is { } title)
            {
                body["title"] = body["template"]?.GetValue<string>() is { } template && body["arguments"] is JsonArray arguments
                    ? l[template, [.. arguments.Select(a => a?.ToString())]]
                    : l[title];
            }

            foreach (var messages in (body["errors"] as JsonObject)?.Select(e => e.Value).OfType<JsonArray>() ?? [])
            {
                for (var i = 0; i < messages.Count; i++)
                {
                    if (messages[i]?.GetValue<string>() is { } message)
                    {
                        messages[i] = l[message];
                    }
                }
            }

            var original = response.Content;
            response.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, type);
            original.Dispose();
        }

        return response;
    }

    private CoworkeeLocalizer? Localizer()
    {
        try
        {
            return services.GetService<CoworkeeLocalizer>();
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }
}
