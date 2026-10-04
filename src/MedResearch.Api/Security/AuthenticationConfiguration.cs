using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.IdentityModel.Tokens;

namespace MedResearch.Api.Security;

public static class AuthenticationConfiguration
{
    public const string PolicyName = "AuthenticatedUser";
    public const string JwtScheme = "Bearer";
    public const string DevelopmentScheme = "DevelopmentLocal";

    public static void AddMedResearchAuthentication(
        this IServiceCollection services,
        IConfiguration configuration,
        IWebHostEnvironment environment)
    {
        var section = configuration.GetSection(DevelopmentLocalAuthenticationOptions.SectionName);
        var mode = section["Mode"] ?? "JwtBearer";

        if (string.Equals(mode, "DevelopmentLocal", StringComparison.OrdinalIgnoreCase))
        {
            if (!environment.IsDevelopment())
            {
                throw new InvalidOperationException(
                    "Authentication:Mode=DevelopmentLocal is allowed only when ASPNETCORE_ENVIRONMENT=Development.");
            }

            var subject = section["DevelopmentSubject"] ?? "local-development-user";
            _ = MedResearch.Application.Security.ActorIdentity.NormalizeSubject(subject);

            services.Configure<DevelopmentLocalAuthenticationOptions>(options => options.Subject = subject);
            services.AddAuthentication(options =>
                {
                    options.DefaultAuthenticateScheme = DevelopmentScheme;
                    options.DefaultChallengeScheme = DevelopmentScheme;
                })
                .AddScheme<AuthenticationSchemeOptions, DevelopmentLocalAuthenticationHandler>(
                    DevelopmentScheme,
                    _ => { });
        }
        else if (string.Equals(mode, "JwtBearer", StringComparison.OrdinalIgnoreCase))
        {
            var authority = section["Authority"];
            var audience = section["Audience"];
            if (string.IsNullOrWhiteSpace(authority) || string.IsNullOrWhiteSpace(audience))
            {
                throw new InvalidOperationException(
                    "Authentication:Authority and Authentication:Audience are required when Authentication:Mode=JwtBearer.");
            }

            services.AddAuthentication(options =>
                {
                    options.DefaultAuthenticateScheme = JwtScheme;
                    options.DefaultChallengeScheme = JwtScheme;
                })
                .AddJwtBearer(options =>
                {
                    options.Authority = authority;
                    options.Audience = audience;
                    options.MapInboundClaims = false;
                    options.RequireHttpsMetadata = !environment.IsDevelopment();
                    options.TokenValidationParameters = new TokenValidationParameters
                    {
                        ValidateIssuer = true,
                        ValidateAudience = true,
                        ValidateIssuerSigningKey = true,
                        ValidateLifetime = true,
                        NameClaimType = "sub"
                    };
                });
        }
        else
        {
            throw new InvalidOperationException(
                $"Authentication:Mode '{mode}' is not supported. Use JwtBearer or DevelopmentLocal.");
        }

        services.AddAuthorization(options =>
            options.AddPolicy(PolicyName, policy =>
                policy.RequireAuthenticatedUser().AddRequirements(new ValidActorSubjectRequirement())));
        services.AddSingleton<IAuthorizationHandler, ValidActorSubjectHandler>();
    }
}
