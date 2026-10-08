using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Coworkee.Localization.Tests;

/// <summary>Answers like the Azure AI Translator: every text comes back with the target culture in front.</summary>
internal sealed class StubTranslator : HttpMessageHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var to = System.Web.HttpUtility.ParseQueryString(request.RequestUri!.Query)["to"];
        var texts = JsonNode.Parse(await request.Content!.ReadAsStringAsync(cancellationToken))!.AsArray();
        var answer = new JsonArray([.. texts.Select(t => (JsonNode)new JsonObject
        {
            ["translations"] = new JsonArray(new JsonObject { ["text"] = $"{to}:{t!["Text"]}", ["to"] = to }),
        })]);
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(answer.ToJsonString(), Encoding.UTF8, "application/json") };
    }
}
