using System.Net;
using System.Text.Json;
using MedResearch.Application.QuickResearch;
using MedResearch.Application.Research.Ai;
using MedResearch.Infrastructure.Ai.CodexCli;
using MedResearch.Infrastructure.Ai.OpenAI;
using MedResearch.Infrastructure.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace MedResearch.Infrastructure.Tests;

public sealed class QuickAnswerBoundaryTests
{
    [Fact]
    public async Task RequestLimits_NarrowTokens_AndUseExistingStructuredClient()
    {
        var draft = new QuickAnswerDraft("Insufficient material.", [], [], ["Abstract context is limited."], "Insufficient", []);
        var handler = new Handler(JsonSerializer.Serialize(new { output_text = JsonSerializer.Serialize(draft, new JsonSerializerOptions(JsonSerializerDefaults.Web)) }));
        var result = await Client(handler).GenerateStructuredAsync<QuickAnswerDraft>(Request(), default);
        Assert.Equal("Insufficient", result.Value.Uncertainty);
        using var body = JsonDocument.Parse(handler.Body!);
        Assert.Equal(1200, body.RootElement.GetProperty("max_output_tokens").GetInt32());
        Assert.Equal(1, handler.Calls);
    }

    [Theory]
    [InlineData("oversize")]
    [InlineData("malformed")]
    [InlineData("unknownField")]
    [InlineData("missingField")]
    public async Task UntrustedHttpOutput_IsRejectedWithoutRetry(string defect)
    {
        var output = defect switch
        {
            "oversize" => new string('x', 5000),
            "malformed" => JsonSerializer.Serialize(new { output_text = "not json" }),
            "unknownField" => JsonSerializer.Serialize(new { output_text = "{\"shortAnswer\":\"x\",\"findings\":[],\"contradictions\":[],\"limitations\":[],\"uncertainty\":\"Insufficient\",\"sourceReferences\":[],\"untrusted\":true}" }),
            _ => "{\"output_text\":\"{}\"}"
        };
        var handler = new Handler(output);
        if (defect == "missingField")
        {
            var result = await Client(handler).GenerateStructuredAsync<QuickAnswerDraft>(Request(), default);
            Assert.Null(result.Value.Findings);
            // Shape validation belongs to the application, never to transport success alone.
        }
        else await Assert.ThrowsAsync<StructuredLlmException>(() => Client(handler).GenerateStructuredAsync<QuickAnswerDraft>(Request(), default));
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task BoundedTimeout_IsTyped_AndCallerCancellationPropagates()
    {
        var timeout = new Handler("", wait: true);
        await Assert.ThrowsAsync<StructuredLlmTimeoutException>(() => Client(timeout)
            .GenerateStructuredAsync<QuickAnswerDraft>(Request() with { Limits = new(1200, 4096, 1) }, default));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Client(new Handler("{}"))
            .GenerateStructuredAsync<QuickAnswerDraft>(Request(), cancellation.Token));
        Assert.Equal(1, timeout.Calls);
    }

    [Fact]
    public async Task CodexCli_IsRejectedBeforeStartingAProcessForBoundedRequests()
    {
        var runner = new NeverRunner();
        var client = new CodexCliStructuredLlmClient(Options.Create(new CodexCliOptions()), runner, NullLogger<CodexCliStructuredLlmClient>.Instance);
        await Assert.ThrowsAsync<StructuredLlmException>(() => client.GenerateStructuredAsync<QuickAnswerDraft>(Request(), default));
        Assert.Equal(0, runner.Calls);
    }

    [Theory]
    [InlineData("timeout", 0)]
    [InlineData("timeout", 61)]
    [InlineData("source", 6001)]
    [InlineData("source", 0)]
    [InlineData("prompt", 40001)]
    [InlineData("tokens", 0)]
    [InlineData("tokens", 2001)]
    [InlineData("bytes", 0)]
    [InlineData("bytes", 64001)]
    public void InvalidQuickBounds_AreRejected(string field, int value)
    {
        var options = new QuickAnswerOptions();
        if (field == "timeout") options.TimeoutSeconds = value;
        if (field == "source") options.MaxSourceCharacters = value;
        if (field == "prompt") options.MaxPromptCharacters = value;
        if (field == "tokens") options.MaxOutputTokens = value;
        if (field == "bytes") options.MaxResponseBytes = value;
        Assert.Throws<InvalidOperationException>(options.Validate);
    }

    [Theory]
    [InlineData(false, 90)]
    [InlineData(true, 60)]
    public void EnabledAnswers_RequireRetrievalAndEnoughLease(bool retrieval, int lease)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:MedResearch"] = "Host=localhost;Database=unused;Username=unused",
            ["QuickResearch:Answer:Enabled"] = "true", ["QuickResearch:Retrieval:Enabled"] = retrieval.ToString(),
            ["QuickResearch:LeaseDurationSeconds"] = lease.ToString()
        }).Build();
        Assert.Throws<InvalidOperationException>(() => new ServiceCollection().AddInfrastructure(configuration));
    }

    private static StructuredLlmRequest Request() => new(QuickAnswerPrompt.Version, "system", "user", QuickAnswerPrompt.Schema, new(1200, 4096, 10));
    private static OpenAIStructuredLlmClient Client(Handler handler) => new(new HttpClient(handler) { BaseAddress = new("https://provider.invalid/") },
        Options.Create(new OpenAIOptions { Model = "fake-model", ApiKey = "fake-only-sentinel" }), NullLogger<OpenAIStructuredLlmClient>.Instance);
    private sealed class Handler(string response, bool wait = false) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        public string? Body { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Calls++;
            Body = await request.Content!.ReadAsStringAsync(token);
            if (wait) await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return new(HttpStatusCode.OK) { Content = new StringContent(response) };
        }
    }
    private sealed class NeverRunner : ICodexCliProcessRunner
    {
        public int Calls { get; private set; }
        public Task<CodexCliProcessResult> RunAsync(CodexCliProcessRequest request, CancellationToken token)
        { Calls++; throw new InvalidOperationException("No CLI may start"); }
    }
}
