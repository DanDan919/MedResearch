using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using MedResearch.Application.Research.Ai;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MedResearch.Infrastructure.Ai.OpenAI;

public sealed class OpenAIStructuredLlmClient : IStructuredLlmClient
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;
    private readonly OpenAIOptions _options;
    private readonly ILogger<OpenAIStructuredLlmClient> _logger;

    public OpenAIStructuredLlmClient(
        HttpClient httpClient,
        IOptions<OpenAIOptions> options,
        ILogger<OpenAIStructuredLlmClient> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<StructuredGenerationResult<T>> GenerateStructuredAsync<T>(
        StructuredLlmRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.SystemPrompt);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.UserPrompt);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.OutputSchema.Name);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.OutputSchema.JsonSchema);

        EnsureConfigured();

        try
        {
            using var httpRequest = BuildHttpRequest(request);
            using var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
            var content = await response.Content.ReadAsStringAsync(cancellationToken);

            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                throw new StructuredLlmException("OpenAI authentication failed.");
            }

            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                throw new StructuredLlmException("OpenAI rate limit was reached.");
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new StructuredLlmException($"OpenAI request failed with HTTP {(int)response.StatusCode}.");
            }

            var (responseId, outputText) = ReadStructuredOutput(content);
            var value = JsonSerializer.Deserialize<T>(outputText, SerializerOptions);

            if (value is null)
            {
                throw new StructuredLlmException("OpenAI structured output could not be deserialized.");
            }

            return new StructuredGenerationResult<T>(
                value,
                new StructuredLlmProviderMetadata(
                    "OpenAI",
                    _options.Model!,
                    responseId,
                    DateTimeOffset.UtcNow));
        }
        catch (TaskCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new StructuredLlmException("OpenAI request timed out.", exception);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (JsonException exception)
        {
            throw new StructuredLlmException("OpenAI returned malformed structured output.", exception);
        }
        catch (HttpRequestException exception)
        {
            throw new StructuredLlmException("OpenAI request failed.", exception);
        }
    }

    private HttpRequestMessage BuildHttpRequest(StructuredLlmRequest request)
    {
        var schema = JsonNode.Parse(request.OutputSchema.JsonSchema)
            ?? throw new StructuredLlmException("Structured output schema is not valid JSON.");

        var body = new JsonObject
        {
            ["model"] = _options.Model,
            ["input"] = new JsonArray
            {
                new JsonObject
                {
                    ["role"] = "system",
                    ["content"] = new JsonArray
                    {
                        new JsonObject
                        {
                            ["type"] = "input_text",
                            ["text"] = request.SystemPrompt
                        }
                    }
                },
                new JsonObject
                {
                    ["role"] = "user",
                    ["content"] = new JsonArray
                    {
                        new JsonObject
                        {
                            ["type"] = "input_text",
                            ["text"] = request.UserPrompt
                        }
                    }
                }
            },
            ["text"] = new JsonObject
            {
                ["format"] = new JsonObject
                {
                    ["type"] = "json_schema",
                    ["name"] = request.OutputSchema.Name,
                    ["strict"] = true,
                    ["schema"] = schema
                }
            },
            ["max_output_tokens"] = _options.BoundedMaxOutputTokens
        };

        var httpRequest = new HttpRequestMessage(HttpMethod.Post, "responses")
        {
            Content = new StringContent(body.ToJsonString(SerializerOptions), Encoding.UTF8, "application/json")
        };
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);

        _logger.LogInformation(
            "OpenAIStructuredGenerationStarted. Provider: {Provider}; Model: {Model}; PromptVersion: {PromptVersion}; SchemaName: {SchemaName}",
            "OpenAI",
            _options.Model,
            request.PromptVersion,
            request.OutputSchema.Name);

        return httpRequest;
    }

    private static (string? ResponseId, string OutputText) ReadStructuredOutput(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var responseId = root.TryGetProperty("id", out var idElement) && idElement.ValueKind == JsonValueKind.String
            ? idElement.GetString()
            : null;

        if (root.TryGetProperty("output_text", out var outputTextElement) && outputTextElement.ValueKind == JsonValueKind.String)
        {
            return (responseId, outputTextElement.GetString()!);
        }

        if (root.TryGetProperty("output", out var outputElement) && outputElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var outputItem in outputElement.EnumerateArray())
            {
                if (!outputItem.TryGetProperty("content", out var contentElement) || contentElement.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                foreach (var contentItem in contentElement.EnumerateArray())
                {
                    if (contentItem.TryGetProperty("text", out var textElement) && textElement.ValueKind == JsonValueKind.String)
                    {
                        return (responseId, textElement.GetString()!);
                    }
                }
            }
        }

        throw new StructuredLlmException("OpenAI response did not contain structured output text.");
    }

    private void EnsureConfigured()
    {
        if (string.IsNullOrWhiteSpace(_options.Model))
        {
            throw new StructuredLlmException("OpenAI model is required when AI:Provider is OpenAI.");
        }

        if (string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            throw new StructuredLlmException("OpenAI API key is required when AI:Provider is OpenAI.");
        }
    }
}
