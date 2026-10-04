using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace MedResearch.Api.Security;

public sealed class DevelopmentLocalAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    private readonly DevelopmentLocalAuthenticationOptions _options;

    public DevelopmentLocalAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> schemeOptions,
        ILoggerFactory logger,
        UrlEncoder encoder,
        IOptions<DevelopmentLocalAuthenticationOptions> options)
        : base(schemeOptions, logger, encoder)
    {
        _options = options.Value;
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var identity = new ClaimsIdentity(
            [new Claim("sub", _options.Subject)],
            Scheme.Name);
        var principal = new ClaimsPrincipal(identity);
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name)));
    }
}

public sealed class DevelopmentLocalAuthenticationOptions
{
    public const string SectionName = "Authentication";

    public string Subject { get; set; } = "local-development-user";
}
