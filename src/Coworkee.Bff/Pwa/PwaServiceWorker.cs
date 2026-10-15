using System.Reflection;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Http;

namespace Coworkee.Bff.Pwa;

/// <summary>
/// The service worker of the installable app. It keeps one static file, the offline page, and shows it for navigations without network; everything
/// else goes to the network untouched (Blazor keeps its own framework files), so sign-in, the BFF, the API, antiforgery and live connections work as
/// without it. Its version follows the app's static assets, so every deployment installs a new worker that waits until the user reloads.
/// It shows pushed notifications and opens their link on a click.
/// </summary>
internal static class PwaServiceWorker
{
    public const string Path = "/service-worker.js";

    private const string Source = """
        const CACHE = 'coworkee-__VERSION__';
        const OFFLINE = '/bff/pwa/offline';
        const ICON = '/bff/pwa/icon';

        self.addEventListener('install', event => event.waitUntil(caches.open(CACHE).then(cache => cache.add(new Request(OFFLINE, { cache: 'reload' })))));

        self.addEventListener('activate', event => event.waitUntil((async () => {
            for (const key of await caches.keys()) {
                if (key.startsWith('coworkee-') && key !== CACHE) {
                    await caches.delete(key);
                }
            }
            await self.clients.claim();
        })()));

        self.addEventListener('message', event => {
            if (event.data === 'skip-waiting') {
                self.skipWaiting();
            }
        });

        // page loads only, and only when the network fails; API, BFF and file requests are left alone
        self.addEventListener('fetch', event => {
            const request = event.request;
            if (request.mode === 'navigate' && request.method === 'GET' && new URL(request.url).origin === self.location.origin) {
                event.respondWith(fetch(request).catch(async () => (await caches.match(OFFLINE)) ?? Response.error()));
            }
        });

        self.addEventListener('push', event => {
            const data = event.data ? event.data.json() : {};
            event.waitUntil(self.registration.showNotification(data.title || '', { body: data.body || undefined, tag: data.tag, icon: ICON, data: { url: data.url || '/' } }));
        });

        self.addEventListener('notificationclick', event => {
            event.notification.close();
            const target = new URL(event.notification.data?.url || '/', self.location.origin);
            const url = target.origin === self.location.origin ? target.href : self.location.origin + '/';
            event.waitUntil((async () => {
                const open = (await self.clients.matchAll({ type: 'window', includeUncontrolled: true })).find(client => new URL(client.url).origin === self.location.origin);
                if (open) {
                    await open.focus();
                    try {
                        return await open.navigate(url);
                    } catch {
                    }
                }
                return self.clients.openWindow(url);
            })());
        });
        """;

    private static readonly Lazy<string> Script = new(() => Source.Replace("__VERSION__", Version(), StringComparison.Ordinal));

    public static IResult Serve(HttpContext context)
    {
        context.Response.Headers.CacheControl = "no-cache";
        context.Response.Headers.XContentTypeOptions = "nosniff";
        return Results.Text(Script.Value, "text/javascript");
    }

    /// <summary>The hash of the app's static asset endpoints (they carry the fingerprints of every file), else of the app's assembly.</summary>
    private static string Version()
    {
        var entry = Assembly.GetEntryAssembly();
        var assets = System.IO.Path.Combine(AppContext.BaseDirectory, $"{entry?.GetName().Name}.staticwebassets.endpoints.json");
        var bytes = File.Exists(assets) ? File.ReadAllBytes(assets) : (entry?.ManifestModule.ModuleVersionId ?? Guid.Empty).ToByteArray();
        return Convert.ToHexString(SHA256.HashData(bytes))[..16].ToLowerInvariant();
    }
}
