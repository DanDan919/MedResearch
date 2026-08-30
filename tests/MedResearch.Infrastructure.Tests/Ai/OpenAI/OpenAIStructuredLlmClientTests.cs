using System.Net;
using System.Text.Json;
using MedResearch.Application.Research.Ai;
using MedResearch.Application.Research.Planning;
using MedResearch.Infrastructure.Ai.OpenAI;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace MedResearch.Infrastructure.Tests.Ai.OpenAI;

public sealed class OpenAIStructuredLlmClientTests
{
    [Fact]
    public async Task GenerateStructuredAsync_ConstructsStrictResponsesApiRequestAndMapsOutput()
    {
        var handler = new RecordingHandler(OpenAIResponse("resp_test", PlannerJson()));
        var client = CreateClient(handler);
        var request = CreateRequest();

        var result = await client.GenerateStructuredAsync<ResearchPlanDraft>(request, CancellationToken.None);

        Assert.Equal("OpenAI", result.Metadata.Provider);
        Assert.Equal("configured-model", result.Metadata.Model);
        Assert.Equal("resp_test", result.Metadata.ResponseId);
        Assert.Equal("Does sleep deprivation impair memory?", result.Value.OriginalQuestion);
        Assert.Equal(["sleep AND memory"], result.Value.SearchQueries);

        Assert.NotNull(handler.RequestBody);
        using var document = JsonDocument.Parse(handler.RequestBody);
        var root = document.RootElement;
        Assert.Equal("configured-model", root.GetProperty("model").GetString());
        Assert.Equal(2000, root.GetProperty("max_output_tokens").GetInt32());
        var format = root.GetProperty("text").GetProperty("format");
        Assert.Equal("json_schema", format.GetProperty("type").GetString());
        Assert.Equal("research_plan", format.GetProperty("name").GetString());
        Assert.True(format.GetProperty("strict").GetBoolean());
        Assert.True(format.GetProperty("schema").GetProperty("additionalProperties").GetBoolean() is false);
        Assert.Equal("Bearer", handler.AuthorizationScheme);
        Assert.Equal("test-key", handler.AuthorizationParameter);
        Assert.EndsWith("/responses", handler.RequestUri?.AbsoluteUri, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GenerateStructuredAsync_ConvertsMalformedStructuredOutputToStructuredLlmException()
    {
        var client = CreateClient(new RecordingHandler(OpenAIResponse("resp_bad", "not-json")));

        await Assert.ThrowsAsync<StructuredLlmException>(() =>
            client.GenerateStructuredAsync<ResearchPlanDraft>(CreateRequest(), CancellationToken.None));
    }

    [Fact]
    public async Task GenerateStructuredAsync_ConvertsRateLimitResponseToStructuredLlmException()
    {
        var client = CreateClient(new RecordingHandler("{}", HttpStatusCode.TooManyRequests));

        var exception = await Assert.ThrowsAsync<StructuredLlmException>(() =>
            client.GenerateStructuredAsync<ResearchPlanDraft>(CreateRequest(), CancellationToken.None));

        Assert.Equal("OpenAI rate limit was reached.", exception.Message);
    }

    [Fact]
    public async Task GenerateStructuredAsync_RequiresApiKeyAndModelConfiguration()
    {
        var client = CreateClient(new RecordingHandler("{}"), new OpenAIOptions());

        var exception = await Assert.ThrowsAsync<StructuredLlmException>(() =>
            client.GenerateStructuredAsync<ResearchPlanDraft>(CreateRequest(), CancellationToken.None));

        Assert.Equal("OpenAI model is required when AI:Provider is OpenAI.", exception.Message);
    }

    [Fact]
    public async Task GenerateStructuredAsync_CancellationPropagates()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var client = CreateClient(new RecordingHandler("{}"));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            client.GenerateStructuredAsync<ResearchPlanDraft>(CreateRequest(), cancellation.Token));
    }

    private static OpenAIStructuredLlmClient CreateClient(
        HttpMessageHandler handler,
        OpenAIOptions? options = null)
    {
        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://api.openai.com/v1/"),
            Timeout = TimeSpan.FromSeconds(10)
        };

        return new OpenAIStructuredLlmClient(
            httpClient,
            Options.Create(options ?? new OpenAIOptions
            {
                Model = "configured-model",
                ApiKey = "test-key"
            }),
            NullLogger<OpenAIStructuredLlmClient>.Instance);
    }

    private static StructuredLlmRequest CreateRequest()
    {
        return new StructuredLlmRequest(
            ResearchPlannerPrompt.Version,
            "system prompt",
            "user prompt",
            ResearchPlannerPrompt.OutputSchema);
    }

    private static string OpenAIResponse(string responseId, string outputText)
    {
        return JsonSerializer.Serialize(new
        {
            id = responseId,
            output = new[]
            {
                new
                {
                    content = new[]
                    {
                        new
                        {
                            type = "output_text",
                            text = outputText
                        }
                    }
                }
            }
        });
    }

    private static string PlannerJson()
    {
        return JsonSerializer.Serialize(new
        {
            originalQuestion = "Does sleep deprivation impair memory?",
            population = "adults",
            exposureOrIntervention = "sleep deprivation",
            comparator = (string?)null,
            outcomes = new[] { "memory" },
            preferredStudyTypes = new[] { "observational study" },
            searchQueries = new[] { "sleep AND memory" },
            exclusionHints = Array.Empty<string>()
        });
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly string _responseBody;
        private readonly HttpStatusCode _statusCode;

        public RecordingHandler(string responseBody, HttpStatusCode statusCode = HttpStatusCode.OK)
        {
            _responseBody = responseBody;
            _statusCode = statusCode;
        }

        public string? RequestBody { get; private set; }

        public Uri? RequestUri { get; private set; }

        public string? AuthorizationScheme { get; private set; }

        public string? AuthorizationParameter { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RequestUri = request.RequestUri;
            AuthorizationScheme = request.Headers.Authorization?.Scheme;
            AuthorizationParameter = request.Headers.Authorization?.Parameter;
            RequestBody = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);

            return new HttpResponseMessage(_statusCode)
            {
                Content = new StringContent(_responseBody)
            };
        }
    }
}

