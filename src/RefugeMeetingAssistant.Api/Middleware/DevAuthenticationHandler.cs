using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace RefugeMeetingAssistant.Api.Middleware;

/// <summary>
/// Development authentication handler that accepts any request.
/// Sets up a stub user identity for local development.
/// 
/// In production, this is replaced by real Entra ID (Azure AD) JWT validation.
/// </summary>
public class DevAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "DevAuth";

    public DevAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder) : base(options, logger, encoder)
    {
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        // Check for X-User-Id header (for testing different users)
        var userId = Request.Headers["X-User-Id"].FirstOrDefault()
            ?? "00000000-0000-0000-0000-000000000001";

        var claims = new[]
        {
            new Claim("sub", userId),
            new Claim("user_id", userId),
            new Claim("email", "dev@refugems.com"),
            new Claim("name", "Dev User"),
            new Claim(ClaimTypes.Name, "Dev User"),
            new Claim(ClaimTypes.Email, "dev@refugems.com"),
            new Claim("oid", "dev-entra-object-id") // Entra Object ID
        };

        var identity = new ClaimsIdentity(claims, SchemeName);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, SchemeName);

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
