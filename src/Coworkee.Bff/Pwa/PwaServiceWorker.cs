using System.Reflection;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Http;

namespace Coworkee.Bff.Pwa;

/// <summary>
/// The service worker of the installable app. It caches static files only (network first, the copy serves while offline), shows the offline page
/// for navigations without network and leaves sign-in, the BFF, the API and live connections alone. Its version follows the app's static assets,
/// so every deployment installs a new worker that waits until the user reloads. It shows pushed notifications and opens their link on a click.
/// </summary>
internal static class PwaServiceWorker
{
    public const string Path = "/service-worker.js";

    private const string Source = """
        const CACHE = 'coworkee-__VERSION__';
        const OFFLINE = '/bff/pwa/offline';
        const ICON = '/bff/pwa/icon';
        const PASS = /^\/(bff|api|odata|hubs|signin-oidc|signout-callback-oidc|swagger|openapi|mcp|admin\/jobs|_framework\/debug|manifest\.webmanifest)(\/|$)/i;
        const STATIC = /^\/(_framework|_content)\/|\.(css|js|mjs|wasm|dll|dat|blat|woff2?|ttf|otf|svg|png|jpe?g|webp|gif|ico)$/i;

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

        self.addEventListener('fetch', event => {
            const request = event.request;
            const url = new URL(request.url);
            if (request.method !== 'GET' || url.origin !== self.location.origin || PASS.test(url.pathname)) {
                return;
            }
            if (request.mode === 'navigate') {
                event.respondWith(fetch(request).catch(async () => (await caches.match(OFFLINE)) ?? Response.error()));
            } else if (STATIC.test(url.pathname)) {
                event.respondWith(fetch(request).then(response => {
                    if (response.ok && response.type === 'basic') {
                        const copy = response.clone();
                        event.waitUntil(caches.open(CACHE).then(cache => cache.put(request, copy)));
                    }
                    return response;
                }).catch(async () => (await caches.match(request)) ?? Response.error()));
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
