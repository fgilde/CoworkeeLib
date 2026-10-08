using System.Globalization;

namespace Coworkee.Client.Blazor.Localization;

/// <summary>Sends the language chosen in the app, not the browser's, so server messages come in the same language.</summary>
internal sealed class CultureHeaderHandler : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (CultureInfo.CurrentUICulture.Name is { Length: > 0 } culture)
        {
            request.Headers.AcceptLanguage.Clear();
            request.Headers.AcceptLanguage.ParseAdd(culture);
        }

        return base.SendAsync(request, cancellationToken);
    }
}
