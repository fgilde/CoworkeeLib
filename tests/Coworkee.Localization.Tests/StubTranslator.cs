using System.Net;
using System.Text;
using System.Text.Json.Nodes;

namespace Coworkee.Localization.Tests;

/// <summary>Answers like the Azure AI Translator or LibreTranslate: every text comes back with the target language and the source language in front.</summary>
internal sealed class StubTranslator : HttpMessageHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = JsonNode.Parse(await request.Content!.ReadAsStringAsync(cancellationToken))!;
        JsonNode answer;
        if (body is JsonObject libre)
        {
            var (from, to) = (libre["source"]!.GetValue<string>(), libre["target"]!.GetValue<string>());
            answer = new JsonObject { ["translatedText"] = new JsonArray([.. libre["q"]!.AsArray().Select(t => (JsonNode)$"{to}:{from}:{t}")]) };
        }
        else
        {
            var to = System.Web.HttpUtility.ParseQueryString(request.RequestUri!.Query)["to"];
            answer = new JsonArray([.. body.AsArray().Select(t => (JsonNode)new JsonObject
            {
                ["translations"] = new JsonArray(new JsonObject { ["text"] = $"{to}:{t!["Text"]}", ["to"] = to }),
            })]);
        }

        return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(answer.ToJsonString(), Encoding.UTF8, "application/json") };
    }
}
