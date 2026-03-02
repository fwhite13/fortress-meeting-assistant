using MudBlazor;

namespace RefugeMeetingAssistant.Web.Theme;

/// <summary>
/// Fortress Intelligence Platform unified theme — MudBlazor v9 compatible.
/// This is the single source of truth for FIP design tokens in FIRM.
/// Reference spec: /workspace/fip-shared-theme/FipTheme.cs
/// </summary>
public static class FipTheme
{
    public static MudTheme Create() => new MudTheme
    {
        PaletteLight = new PaletteLight
        {
            Primary = "#1a2332",
            PrimaryContrastText = "#ffffff",
            Secondary = "#d4af37",
            SecondaryContrastText = "#1a2332",
            Background = "#f8f9fa",
            Surface = "#ffffff",
            AppbarBackground = "#1a2332",
            AppbarText = "#ffffff",
            DrawerBackground = "#1a2332",
            DrawerText = "#f0f0f0",
            DrawerIcon = "#d4af37",
            TextPrimary = "#1a2332",
            TextSecondary = "#6b7280",
            TextDisabled = "rgba(0,0,0,0.38)",
            ActionDefault = "#6b7280",
            Success = "#059669",
            Warning = "#d97706",
            Error = "#dc2626",
            Info = "#2563eb",
            TableLines = "#e5e7eb",
            TableHover = "#f3f4f6",
        },
        Typography = new Typography
        {
            Default = new DefaultTypography
            {
                FontFamily = new[] { "Inter", "system-ui", "-apple-system", "sans-serif" },
                FontSize = "0.9375rem",
                LineHeight = "1.6",
            },
            H4 = new H4Typography { FontWeight = "700" },
            H5 = new H5Typography { FontWeight = "600" },
            H6 = new H6Typography { FontWeight = "600" },
            Button = new ButtonTypography
            {
                FontFamily = new[] { "Inter", "sans-serif" },
                FontWeight = "500",
                TextTransform = "none",
                FontSize = "0.9rem",
            },
            Caption = new CaptionTypography { FontSize = "0.75rem" }
        },
        LayoutProperties = new LayoutProperties
        {
            AppbarHeight = "48px",
            DrawerWidthLeft = "264px",
        }
    };
}
