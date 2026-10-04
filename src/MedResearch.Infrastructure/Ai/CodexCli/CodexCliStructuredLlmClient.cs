using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using MedResearch.Application.Research.Ai;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MedResearch.Infrastructure.Ai.CodexCli;

public sealed class CodexCliStructuredLlmClient : IStructuredLlmClient
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly CodexCliOptions _options;
    private readonly ICodexCliProcessRunner _processRunner;
    private readonly ILogger<CodexCliStructuredLlmClient> _logger;

    public CodexCliStructuredLlmClient(
        IOptions<CodexCliOptions> options,
        ICodexCliProcessRunner processRunner,
        ILogger<CodexCliStructuredLlmClient> logger)
    {
        _options = options.Value;
        _processRunner = processRunner;
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
        _options.Validate();

        var prompt = BuildPrompt(request);
        if (prompt.Length > _options.MaxPromptCharacters)
        {
            throw new StructuredLlmException(
                $"Codex CLI prompt exceeds AI:CodexCli:MaxPromptCharacters ({_options.MaxPromptCharacters}); input was not truncated.");
        }

        var startedAt = Stopwatch.GetTimestamp();
        var requestDirectory = Path.Combine(
            Path.GetTempPath(),
            "medresearch-codex-cli",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(requestDirectory);

        var schemaPath = Path.Combine(requestDirectory, "output-schema.json");
        var outputPath = Path.Combine(requestDirectory, "output.json");

        try
        {
            ValidateSchema(request.OutputSchema.JsonSchema);
            await File.WriteAllTextAsync(schemaPath, request.OutputSchema.JsonSchema, cancellationToken);

            var arguments = BuildArguments(requestDirectory, schemaPath, outputPath);
            var processResult = await _processRunner.RunAsync(
                new CodexCliProcessRequest(
                    _options.ExecutablePath,
                    arguments,
                    requestDirectory,
                    prompt,
                    _options.Timeout),
                cancellationToken);

            if (processResult.ExitCode != 0)
            {
                var category = ClassifyFailure(processResult.StandardError);
                _logger.LogWarning(
                    "CodexCliStructuredGenerationFailed. Provider: {Provider}; PromptVersion: {PromptVersion}; Category: {Category}; ExitCode: {ExitCode}; DurationMs: {DurationMs}",
                    "codex-cli",
                    request.PromptVersion,
                    category,
                    processResult.ExitCode,
                    ElapsedMilliseconds(startedAt));
                throw new StructuredLlmException(
                    $"Codex CLI process failed ({category}) with exit code {processResult.ExitCode}.");
            }

            if (!File.Exists(outputPath))
            {
                throw new StructuredLlmException("Codex CLI did not produce its structured output file.");
            }

            var output = await File.ReadAllTextAsync(outputPath, cancellationToken);
            T? value;
            try
            {
                value = JsonSerializer.Deserialize<T>(output, SerializerOptions);
            }
            catch (JsonException exception)
            {
                throw new StructuredLlmException("Codex CLI returned malformed structured output.", exception);
            }

            if (value is null)
            {
                throw new StructuredLlmException("Codex CLI structured output could not be deserialized.");
            }

            _logger.LogInformation(
                "CodexCliStructuredGenerationCompleted. Provider: {Provider}; Model: {Model}; PromptVersion: {PromptVersion}; SchemaName: {SchemaName}; DurationMs: {DurationMs}",
                "codex-cli",
                _options.EffectiveModel,
                request.PromptVersion,
                request.OutputSchema.Name,
                ElapsedMilliseconds(startedAt));

            return new StructuredGenerationResult<T>(
                value,
                new StructuredLlmProviderMetadata(
                    "codex-cli",
                    _options.EffectiveModel,
                    null,
                    DateTimeOffset.UtcNow));
        }
        catch (CodexCliProcessException exception) when (exception.Kind == CodexCliProcessFailureKind.NotInstalled)
        {
            _logger.LogWarning(
                "CodexCliStructuredGenerationFailed. Provider: {Provider}; PromptVersion: {PromptVersion}; Category: {Category}; DurationMs: {DurationMs}",
                "codex-cli",
                request.PromptVersion,
                "cli-not-installed",
                ElapsedMilliseconds(startedAt));
            throw new StructuredLlmException("Codex CLI executable was not found.", exception);
        }
        catch (CodexCliProcessException exception) when (exception.Kind == CodexCliProcessFailureKind.TimedOut)
        {
            _logger.LogWarning(
                "CodexCliStructuredGenerationFailed. Provider: {Provider}; PromptVersion: {PromptVersion}; Category: {Category}; DurationMs: {DurationMs}",
                "codex-cli",
                request.PromptVersion,
                "timeout",
                ElapsedMilliseconds(startedAt));
            throw new StructuredLlmException("Codex CLI request timed out.", exception);
        }
        catch (IOException exception)
        {
            _logger.LogWarning(
                "CodexCliStructuredGenerationFailed. Provider: {Provider}; PromptVersion: {PromptVersion}; Category: {Category}; DurationMs: {DurationMs}",
                "codex-cli",
                request.PromptVersion,
                "process-io-failure",
                ElapsedMilliseconds(startedAt));
            throw new StructuredLlmException("Codex CLI process I/O failed.", exception);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation(
                "CodexCliStructuredGenerationCancelled. Provider: {Provider}; PromptVersion: {PromptVersion}; DurationMs: {DurationMs}",
                "codex-cli",
                request.PromptVersion,
                ElapsedMilliseconds(startedAt));
            throw;
        }
        finally
        {
            TryDeleteDirectory(requestDirectory);
        }
    }

    private static string BuildPrompt(StructuredLlmRequest request)
    {
        return $"System instructions:\n{request.SystemPrompt}\n\nUser task:\n{request.UserPrompt}";
    }

    private IReadOnlyCollection<string> BuildArguments(
        string requestDirectory,
        string schemaPath,
        string outputPath)
    {
        var arguments = new List<string>
        {
            "exec",
            "--ephemeral",
            "--skip-git-repo-check",
            "--sandbox",
            "read-only",
            "--color",
            "never",
            "--cd",
            requestDirectory,
            "--output-schema",
            schemaPath,
            "--output-last-message",
            outputPath
        };

        if (!string.IsNullOrWhiteSpace(_options.Model))
        {
            arguments.Insert(1, _options.Model.Trim());
            arguments.Insert(1, "--model");
        }

        return arguments;
    }

    private static void ValidateSchema(string schema)
    {
        try
        {
            _ = JsonNode.Parse(schema)
                ?? throw new StructuredLlmException("Structured output schema is empty.");
        }
        catch (JsonException exception)
        {
            throw new StructuredLlmException("Structured output schema is not valid JSON.", exception);
        }
    }

    private static string ClassifyFailure(string stderr)
    {
        if (stderr.Contains("rate", StringComparison.OrdinalIgnoreCase)
            || stderr.Contains("usage", StringComparison.OrdinalIgnoreCase)
            || stderr.Contains("limit", StringComparison.OrdinalIgnoreCase))
        {
            return "usage-or-rate-limit";
        }

        if (stderr.Contains("auth", StringComparison.OrdinalIgnoreCase)
            || stderr.Contains("login", StringComparison.OrdinalIgnoreCase))
        {
            return "authentication";
        }

        return "process-failure";
    }

    private static double ElapsedMilliseconds(long startedAt)
    {
        return Math.Round(Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds, 1);
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
