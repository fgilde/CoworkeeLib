# Plan: loading screen and media from files (spec 2026-10-15, sections 1 + 2)

## Loading screen
1. Contracts: `SignInDesign.LoadingScreen` (`LoadingScreenBackground { Animated, SignInMedia, Plain }`), `LoadingScreenDto(ThemeDto? Theme, LoadingScreenBackground Background, string? MediaUrl)`.
2. API (Theming): anonymous `GET /api/v1/themes/loading-screen` (current default theme of the caller's organisation, else the system one, + background + media), allowed by the setup gate.
3. BFF: `LoadingScreenSource` fetches it server-side (with the session's access token, cached per organisation for 30 s, 2 s timeout, defaults on failure); `CoworkeeLoadingScreen` static component renders `LoadingScreenMarkup` (inline CSS + small inline script, no assets): theme colours light/dark from the `coworkee-theme-mode` cookie or `prefers-color-scheme`, logo, progress ring from `--blazor-load-percentage(-text)`, indeterminate otherwise, animated blobs / sign-in media with overlay / plain, reduced motion, fade out.
4. Client: `CoworkeeLayout`/`SetupLayout` hide it once the theme is applied (`coworkee.js hideLoadingScreen`); the layout stores the colour mode in the cookie. Fallback: the screen hides itself after 30 s.
5. Template `App.razor` places `<CoworkeeLoadingScreen />`; Aspire forwards `SignIn.LoadingScreen`.
6. Admin: Loading screen select on the sign-in design card.

## Media from files
1. Files: `StoredFile.IsPublic` (+ DTO), `PUT /api/v1/files/{id}/public` (Manage), anonymous `GET /api/v1/files/public/{id}/{name}` (short public cache). Template migration.
2. Theme logo dialog: "From files" picks an image, reads it and runs the existing checks (resize, 512 KB, SVG sanitising on the server).
3. Sign-in media: "From files" picks an image/video, offers to make it public, stores the absolute public URL.

## Tests
Bff markup + source, Theming endpoint, Files public flag/anonymous read, bUnit for the card, logo dialog and layouts; CLI template build; Playwright with throttled network.
