using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using MedResearch.Api.Research;
using MedResearch.Api.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace MedResearch.IntegrationTests;

public sealed class JwtApiTests
{
    [Theory]
    [InlineData("missing", HttpStatusCode.Unauthorized)]
    [InlineData("malformed", HttpStatusCode.Unauthorized)]
    [InlineData("signature", HttpStatusCode.Unauthorized)]
    [InlineData("issuer", HttpStatusCode.Unauthorized)]
    [InlineData("audience", HttpStatusCode.Unauthorized)]
    [InlineData("id-token", HttpStatusCode.Unauthorized)]
    [InlineData("expired", HttpStatusCode.Unauthorized)]
    [InlineData("no-sub", HttpStatusCode.Forbidden)]
    [InlineData("long-sub", HttpStatusCode.Forbidden)]
    public async Task RealJwtHandler_RejectsInvalidIdentity(string scenario, HttpStatusCode expected)
    {
        using var factory = new JwtApiFactory();
        using var client = factory.CreateClient();
        if (scenario != "missing") client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", factory.Token(scenario));
        client.DefaultRequestHeaders.Add("X-Owner-Id", "UserA");
        var response = await client.GetAsync("/api/research");
        Assert.Equal(expected, response.StatusCode);
    }

    [Fact]
    public async Task RealJwtHandler_ValidSignatureAndPolicy_UsesSubNotEmailOrHeaders()
    {
        using var factory = new JwtApiFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", factory.Token("valid"));
        client.DefaultRequestHeaders.Add("X-Authenticated-Subject", "UserB");
        var response = await client.GetAsync("/test/auth");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("UserA", await response.Content.ReadAsStringAsync());
    }
}

[Collection(PostgreSqlCollection.Name)]
public sealed class JwtPostgreSqlOwnershipTests(PostgreSqlFixture fixture)
{
    [SkippableFact]
    [Trait("Category", "PostgreSql")]
    public async Task SignedJwtUsers_AreIsolatedByActualPostgreSqlStores()
    {
        Skip.IfNot(fixture.IsAvailable, fixture.UnavailableReason);
        using var factory = new JwtApiFactory(fixture.ConnectionString);
        using var owner = factory.CreateClient();
        using var other = factory.CreateClient();
        owner.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", factory.Token("valid", "UserA-" + Guid.NewGuid()));
        other.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", factory.Token("valid", "UserB-" + Guid.NewGuid()));
        other.DefaultRequestHeaders.Add("X-Owner-Id", "UserA");
        var created = await owner.PostAsJsonAsync("/api/research", new CreateResearchRequest("JWT owner isolation test"));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var run = (await created.Content.ReadFromJsonAsync<CreateResearchResponse>())!;
        foreach (var suffix in new[] { "", "/progress", "/report", "/quantitative", "/provenance" })
        {
            Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/api/research/{run.ResearchRunId}{suffix}")).StatusCode);
            Assert.Equal(suffix == "/report" ? HttpStatusCode.Conflict : HttpStatusCode.OK,
                (await owner.GetAsync($"/api/research/{run.ResearchRunId}{suffix}")).StatusCode);
        }
        var ownerList = (await owner.GetFromJsonAsync<ResearchRunListResponse>("/api/research"))!;
        Assert.Contains(ownerList.Items, item => item.ResearchRunId == run.ResearchRunId);
        var otherList = (await other.GetFromJsonAsync<ResearchRunListResponse>("/api/research"))!;
        Assert.Empty(otherList.Items);
    }
}

// Only the metadata source/key is synthetic; the production JWT handler and policy remain intact.
internal sealed class JwtApiFactory(string? connectionString = null) : WebApplicationFactory<Program>
{
    private const string Issuer = "https://synthetic-issuer.example.org/";
    private const string Audience = "medresearch-api";
    private readonly RSA _rsa = RSA.Create(2048);

    public string Token(string scenario, string subject = "UserA")
    {
        if (scenario == "malformed") return "not-a-jwt";
        using var wrongKey = RSA.Create(2048);
        var key = new RsaSecurityKey(scenario == "signature" ? wrongKey : _rsa) { KeyId = "test-key" };
        var claims = new List<Claim> { new("email", "not-owner@example.org") };
        if (scenario != "no-sub") claims.Add(new Claim("sub", scenario == "long-sub" ? new string('x', 201) : subject));
        var now = DateTime.UtcNow;
        var token = new JwtSecurityToken(scenario == "issuer" ? "https://other.example.org/" : Issuer,
            scenario is "audience" or "id-token" ? "web-client" : Audience, claims,
            now.AddHours(-1), scenario == "expired" ? now.AddMinutes(-10) : now.AddMinutes(5),
            new SigningCredentials(key, SecurityAlgorithms.RsaSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Production");
        builder.UseSetting("Authentication:Mode", "JwtBearer");
        builder.UseSetting("Authentication:Authority", Issuer);
        builder.UseSetting("Authentication:Audience", Audience);
        builder.UseSetting("ConnectionStrings:MedResearch", connectionString ?? "Host=localhost;Database=unused;Username=unused;Password=unused");
        builder.UseSetting("Database:ApplyMigrationsOnStartup", "false");
        builder.UseSetting("ResearchProcessing:Enabled", "false");
        builder.UseSetting("AI:Provider", "OpenAI");
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IHostedService>();
            services.PostConfigure<JwtBearerOptions>(AuthenticationConfiguration.JwtScheme, options =>
            {
                var metadata = new OpenIdConnectConfiguration { Issuer = Issuer };
                metadata.SigningKeys.Add(new RsaSecurityKey(_rsa) { KeyId = "test-key" });
                options.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(metadata);
            });
            services.AddSingleton<IStartupFilter, TestPolicyProbe>();
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing) _rsa.Dispose();
    }

    private sealed class TestPolicyProbe : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use(async (context, continuation) =>
            {
                if (context.Request.Path != "/test/auth") { await continuation(); return; }
                var identity = await context.AuthenticateAsync(AuthenticationConfiguration.JwtScheme);
                var authorization = context.RequestServices.GetRequiredService<IAuthorizationService>();
                if (!identity.Succeeded || !(await authorization.AuthorizeAsync(identity.Principal!, null, AuthenticationConfiguration.PolicyName)).Succeeded)
                { context.Response.StatusCode = 401; return; }
                await context.Response.WriteAsync(identity.Principal!.FindFirst("sub")!.Value);
            });
            next(app);
        };
    }
}
