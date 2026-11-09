using Coworkee.Contracts.Auditing;
using Coworkee.Contracts.Settings;
using Coworkee.Settings;

namespace Coworkee.Auditing.Requests;

internal sealed class RequestLogSettingDefinitions : ISettingDefinitionContributor
{
    public const int MaxBodyKb = 1024;

    public void Define(SettingDefinitionContext context) =>
        context.Group("RequestLog", "Request log")
            .Add(RequestLogSettings.Requests, "Log requests", SettingType.MultiChoice, [SettingScope.Global],
                $"{RequestLogSettings.Reading},{RequestLogSettings.Writing}", choices: [RequestLogSettings.Reading, RequestLogSettings.Writing],
                description: "Reading: GET and OData queries; writing: POST, PUT, PATCH, DELETE. With headers and bodies. Nothing chosen turns the request log off.")
            .Add(RequestLogSettings.MaxBodyKb, "Body size (KB)", SettingType.Int, [SettingScope.Global], "32",
                description: "Kilobytes kept of each request and response body; longer bodies are cut, 0 keeps none.")
            .Add(RequestLogSettings.RedactedNames, "Hidden names", SettingType.String, [SettingScope.Global],
                description: "Header and field names whose values are logged as ***, comma separated; passwords, secrets, tokens, keys, cookies and Authorization always are.");
}
