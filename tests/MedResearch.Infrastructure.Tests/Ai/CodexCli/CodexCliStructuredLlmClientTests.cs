using MedResearch.Application.Research.Ai;
using MedResearch.Infrastructure.Ai.CodexCli;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace MedResearch.Infrastructure.Tests.Ai.CodexCli;

public sealed class CodexCliStructuredLlmClientTests
{
    [Fact]
    public async Task GenerateStructuredAsync_UsesStrictSchemaStdinAndUniqueOutputFile()
    {
        var observation = new Observation();
        var runner = new RecordingRunner((request, _) =>
        {
            var schemaPath = ArgumentAfter(request.Arguments, "--output-schema");
            var outputPath = ArgumentAfter(request.Arguments, "--output-last-message");
            observation.SchemaPath = schemaPath;
            observation.OutputPath = outputPath;
            observation.Input = request.StandardInput;
            observation.Schema = File.ReadAllText(schemaPath);
            File.WriteAllText(outputPath, "{\"status\":\"ok\"}");
            return Task.FromResult(new CodexCliProcessResult(0, "progress", string.Empty));
        });
        var client = CreateClient(runner);

        var result = await client.GenerateStructuredAsync<Probe>(CreateRequest("hostile ; text && do-not-run"), CancellationToken.None);

        Assert.Equal("ok", result.Value.Status);
        Assert.Equal("codex-cli", result.Metadata.Provider);
        Assert.Equal("codex-cli-default", result.Metadata.Model);
        Assert.NotNull(observation.SchemaPath);
        Assert.NotNull(observation.OutputPath);
        Assert.NotEqual(observation.SchemaPath, observation.OutputPath);
        Assert.Contains("additionalProperties", observation.Schema!);
        Assert.Contains("hostile ; text && do-not-run", observation.Input!);
        Assert.False(Directory.Exists(Path.GetDirectoryName(observation.SchemaPath!)!));
    }

    [Fact]
    public async Task GenerateStructuredAsync_ConvertsNonZeroExitWithoutExposingStderr()
    {
        var client = CreateClient(new RecordingRunner((_, _) =>
            Task.FromResult(new CodexCliProcessResult(7, string.Empty, "authentication token=do-not-log"))));

        var exception = await Assert.ThrowsAsync<StructuredLlmException>(() =>
            client.GenerateStructuredAsync<Probe>(CreateRequest("question"), CancellationToken.None));

        Assert.Contains("authentication", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("token=do-not-log", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GenerateStructuredAsync_RejectsMalformedOutput()
    {
        var client = CreateClient(new RecordingRunner((request, _) =>
        {
            File.WriteAllText(ArgumentAfter(request.Arguments, "--output-last-message"), "not-json");
            return Task.FromResult(new CodexCliProcessResult(0, string.Empty, string.Empty));
        }));

        await Assert.ThrowsAsync<StructuredLlmException>(() =>
            client.GenerateStructuredAsync<Probe>(CreateRequest("question"), CancellationToken.None));
    }

    [Fact]
    public async Task GenerateStructuredAsync_MapsTimeoutCancellationAndMissingExecutable()
    {
        var timeoutClient = CreateClient(new RecordingRunner((_, _) => throw new CodexCliProcessException(
            CodexCliProcessFailureKind.TimedOut,
            "internal timeout")));
        var timeout = await Assert.ThrowsAsync<StructuredLlmException>(() =>
            timeoutClient.GenerateStructuredAsync<Probe>(CreateRequest("question"), CancellationToken.None));
        Assert.Contains("timed out", timeout.Message, StringComparison.OrdinalIgnoreCase);

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var cancellationClient = CreateClient(new RecordingRunner((_, token) =>
        {
            token.ThrowIfCancellationRequested();
            return Task.FromResult(new CodexCliProcessResult(0, string.Empty, string.Empty));
        }));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            cancellationClient.GenerateStructuredAsync<Probe>(CreateRequest("question"), cancellation.Token));

        var missingClient = CreateClient(new RecordingRunner((_, _) => throw new CodexCliProcessException(
            CodexCliProcessFailureKind.NotInstalled,
            "internal missing executable")));
        var missing = await Assert.ThrowsAsync<StructuredLlmException>(() =>
            missingClient.GenerateStructuredAsync<Probe>(CreateRequest("question"), CancellationToken.None));
        Assert.Contains("executable", missing.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GenerateStructuredAsync_ParallelCallsUseDifferentTemporaryDirectories()
    {
        var requests = new System.Collections.Concurrent.ConcurrentBag<CodexCliProcessRequest>();
        var runner = new RecordingRunner(async (request, _) =>
        {
            requests.Add(request);
            await Task.Delay(20);
            File.WriteAllText(ArgumentAfter(request.Arguments, "--output-last-message"), "{\"status\":\"ok\"}");
            return new CodexCliProcessResult(0, string.Empty, string.Empty);
        });
        var client = CreateClient(runner);

        await Task.WhenAll(
            client.GenerateStructuredAsync<Probe>(CreateRequest("one"), CancellationToken.None),
            client.GenerateStructuredAsync<Probe>(CreateRequest("two"), CancellationToken.None));

        Assert.Equal(2, requests.Count);
        Assert.Equal(2, requests.Select(request => request.WorkingDirectory).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public async Task GenerateStructuredAsync_RejectsPromptWithoutSilentTruncation()
    {
        var client = CreateClient(
            new RecordingRunner((_, _) => Task.FromResult(new CodexCliProcessResult(0, string.Empty, string.Empty))),
            new CodexCliOptions { MaxPromptCharacters = 1_000 });

        var exception = await Assert.ThrowsAsync<StructuredLlmException>(() =>
            client.GenerateStructuredAsync<Probe>(CreateRequest(new string('x', 2_000)), CancellationToken.None));

        Assert.Contains("not truncated", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static CodexCliStructuredLlmClient CreateClient(
        ICodexCliProcessRunner runner,
        CodexCliOptions? options = null)
    {
        return new CodexCliStructuredLlmClient(
            Options.Create(options ?? new CodexCliOptions()),
            runner,
            NullLogger<CodexCliStructuredLlmClient>.Instance);
    }

    private static StructuredLlmRequest CreateRequest(string userPrompt)
    {
        return new StructuredLlmRequest(
            "test-prompt-v1",
            "Return only the requested structured result.",
            userPrompt,
            new StructuredOutputSchema(
                "probe",
                "{\"type\":\"object\",\"properties\":{\"status\":{\"type\":\"string\"}},\"required\":[\"status\"],\"additionalProperties\":false}"));
    }

    private static string ArgumentAfter(IReadOnlyCollection<string> arguments, string name)
    {
        var values = arguments.ToArray();
        var index = Array.IndexOf(values, name);
        Assert.True(index >= 0 && index + 1 < values.Length);
        return values[index + 1];
    }

    private sealed record Probe(string Status);

    private sealed class Observation
    {
        public string? SchemaPath { get; set; }

        public string? OutputPath { get; set; }

        public string? Schema { get; set; }

        public string? Input { get; set; }
    }

    private sealed class RecordingRunner : ICodexCliProcessRunner
    {
        private readonly Func<CodexCliProcessRequest, CancellationToken, Task<CodexCliProcessResult>> _handler;

        public RecordingRunner(Func<CodexCliProcessRequest, CancellationToken, Task<CodexCliProcessResult>> handler)
        {
            _handler = handler;
        }

        public Task<CodexCliProcessResult> RunAsync(
            CodexCliProcessRequest request,
            CancellationToken cancellationToken)
        {
            return _handler(request, cancellationToken);
        }
    }
}
