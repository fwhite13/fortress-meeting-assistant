# FIRM Nav Clickability Fix

## Issue
Nav elements (hamburger, waffle menu, sidebar links) not responding to clicks

## Root Cause
`<Routes />` in `App.razor` had **no `@rendermode` attribute**, causing `MainLayout.razor` to render in **static SSR mode**.

- In static SSR, Blazor renders HTML on the server but does NOT establish a SignalR connection
- All `OnClick` event handlers (hamburger `ToggleDrawer`, waffle `MudMenu`, user avatar menu) are inert — they exist in markup but have no JavaScript interop
- Individual pages (`Home.razor`, `Settings.razor`, `MeetingDetails.razor`) had `@rendermode InteractiveServer`, so their content was interactive, but the **layout wrapper** was not
- FAIT (working reference) uses `@rendermode="new InteractiveServerRenderMode(prerender: false)"` on Routes
- FORMS uses `@rendermode="RenderMode.InteractiveServer"` on Routes

## Fix
1. Added `@rendermode="RenderMode.InteractiveServer"` to `<Routes />` in `App.razor`
2. Added `@rendermode="RenderMode.InteractiveServer"` to `<HeadOutlet />` for consistency
3. Added `@using Microsoft.AspNetCore.Components.Web` for RenderMode type access
4. Corrected `--color-header-bg` from `#1E293B` to `#1a2332` (Fortress Navy) in `fip-theme.css`

### Files Changed
- `src/RefugeMeetingAssistant.Web/Components/App.razor` — added @rendermode to Routes and HeadOutlet
- `src/RefugeMeetingAssistant.Web/wwwroot/fip-theme.css` — corrected header color to #1a2332

## Build: succeeded ✅
## Commit: dfdf5d78

## Additional Fix: Icon Spacing
- Hamburger and avatar were too inset (36px total: 20px AppBar padding + 16px inner div padding)
- Reduced AppBar padding to 8px, removed inner div padding-left/padding-right
- Now matches tighter edge spacing
- Commit: ec47b239
