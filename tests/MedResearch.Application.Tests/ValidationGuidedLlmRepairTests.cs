using MedResearch.Application.Research.Ai;
using MedResearch.Application.Research.Validation;

namespace MedResearch.Application.Tests;

public sealed class ValidationGuidedLlmRepairTests
{
    [Fact]
    public void ValidationGuidedRepairOptions_RejectsUnboundedBudget()
    {
        Assert.Throws<InvalidOperationException>(() =>
            new ValidationGuidedLlmRepairOptions { MaxSemanticRepairAttempts = 3 }.Validate());
    }

    [Fact]
    public async Task GenerateAndValidateAsync_AcceptsFirstValidCandidateWithoutRepair()
    {
        var client = new SequenceClient("valid");
        var service = new ValidationGuidedLlmRepairService(client);
        var request = CreateRequest();

        var result = await service.GenerateAndValidateAsync<string, string>(
            request,
            (value, _) => value,
            "test stage",
            CancellationToken.None);

        Assert.Equal("valid", result.Value);
        Assert.Equal(1, result.AttemptCount);
        Assert.Empty(result.RepairedIssueCodes);
        Assert.Single(client.Requests);
        Assert.Equal(request.UserPrompt, client.Requests[0].UserPrompt);
    }

    [Fact]
    public async Task GenerateAndValidateAsync_RepairsOnceAndRevalidatesReplacement()
    {
        var client = new SequenceClient("invalid", "valid");
        var service = new ValidationGuidedLlmRepairService(
            client,
            new ValidationGuidedLlmRepairOptions { MaxSemanticRepairAttempts = 1 });
        var request = CreateRequest();

        var result = await service.GenerateAndValidateAsync<string, string>(
            request,
            (value, _) => value == "invalid"
                ? throw new TestValidationException()
                : value,
            "test stage",
            CancellationToken.None);

        Assert.Equal("valid", result.Value);
        Assert.Equal(2, result.AttemptCount);
        Assert.Equal([ValidationIssueCodes.MixedClaimConflict], result.RepairedIssueCodes);
        Assert.Equal(2, client.Requests.Count);
        Assert.Contains("VALIDATION REPAIR REQUEST", client.Requests[1].UserPrompt, StringComparison.Ordinal);
        Assert.Contains(ValidationIssueCodes.MixedClaimConflict, client.Requests[1].UserPrompt, StringComparison.Ordinal);
        Assert.Contains("trusted scientific context", client.Requests[1].UserPrompt, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(request.SystemPrompt, client.Requests[1].SystemPrompt);
        Assert.Equal(request.OutputSchema, client.Requests[1].OutputSchema);
    }

    [Fact]
    public async Task GenerateAndValidateAsync_DoesNotRetryNonRepairableFailure()
    {
        var client = new SequenceClient("invalid", "valid");
        var service = new ValidationGuidedLlmRepairService(client);

        await Assert.ThrowsAsync<TestValidationException>(() =>
            service.GenerateAndValidateAsync<string, string>(
                CreateRequest(),
                (_, _) => throw new TestValidationException(nonRepairable: true),
                "test stage",
                CancellationToken.None));

        Assert.Single(client.Requests);
    }

    [Fact]
    public async Task GenerateAndValidateAsync_FailsClosedAfterOneInvalidRepair()
    {
        var client = new SequenceClient("invalid", "invalid");
        var service = new ValidationGuidedLlmRepairService(client);

        await Assert.ThrowsAsync<TestValidationException>(() =>
            service.GenerateAndValidateAsync<string, string>(
                CreateRequest(),
                (_, _) => throw new TestValidationException(),
                "test stage",
                CancellationToken.None));

        Assert.Equal(2, client.Requests.Count);
    }

    [Fact]
    public async Task GenerateAndValidateAsync_PropagatesProviderFailureWithoutRepair()
    {
        var client = new SequenceClient(new StructuredLlmException("provider failure"));
        var service = new ValidationGuidedLlmRepairService(client);

        await Assert.ThrowsAsync<StructuredLlmException>(() =>
            service.GenerateAndValidateAsync<string, string>(
                CreateRequest(),
                (value, _) => value,
                "test stage",
                CancellationToken.None));

        Assert.Single(client.Requests);
    }

    private static StructuredLlmRequest CreateRequest()
    {
        return new StructuredLlmRequest(
            "test-v1",
            "system context",
            "trusted scientific context",
            new StructuredOutputSchema("test", "{\"type\":\"object\"}"));
    }

    private sealed class SequenceClient : IStructuredLlmClient
    {
        private readonly Queue<object> _responses;

        public SequenceClient(params object[] responses)
        {
            _responses = new Queue<object>(responses);
        }

        public List<StructuredLlmRequest> Requests { get; } = [];

        public Task<StructuredGenerationResult<T>> GenerateStructuredAsync<T>(StructuredLlmRequest request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Requests.Add(request);
            var response = _responses.Dequeue();
            if (response is Exception exception)
            {
                throw exception;
            }

            return Task.FromResult(new StructuredGenerationResult<T>(
                (T)response,
                new StructuredLlmProviderMetadata("Fake", "fake", null, DateTimeOffset.UtcNow)));
        }
    }

    private sealed class TestValidationException : Exception, IValidationFailure
    {
        public TestValidationException(bool nonRepairable = false)
        {
            Issues = [new ValidationIssue(
                ValidationIssueCodes.MixedClaimConflict,
                "test.path",
                "Correct the rejected test candidate without adding unsupported facts.",
                nonRepairable ? ValidationIssueDisposition.NonRepairable : ValidationIssueDisposition.Repairable)];
        }

        public IReadOnlyCollection<ValidationIssue> Issues { get; }
    }
}
