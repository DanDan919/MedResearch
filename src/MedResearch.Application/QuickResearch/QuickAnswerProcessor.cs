using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MedResearch.Application.Research.Ai;
using MedResearch.Domain.QuickResearch;
using Microsoft.Extensions.Logging;

namespace MedResearch.Application.QuickResearch;

public sealed class QuickAnswerProcessor(IQuickAnswerStore store, IStructuredLlmClient client, QuickAnswerOptions options,
    ILogger<QuickAnswerProcessor> logger)
{
    public bool Enabled => options.Enabled;

    public async Task ProcessAsync(QuickResearchClaim claim, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var retrieval = claim.Retrieval ?? throw new ArgumentException("Quick answer requires saved retrieval.");
        retrieval.Validate(claim.RunId);
        if (!retrieval.IsFinal) throw new ArgumentException("Quick answer requires final retrieval.");
        if (claim.AnswerGeneration is { } reserved)
        {
            reserved.Validate(claim.RunId);
            if (reserved.Outcome != QuickAnswerOutcome.Started) throw new QuickResearchLeaseLostException();
            await Settle(reserved with { Outcome = QuickAnswerOutcome.OutcomeUnknown, FailureCategory = "OutcomeUnknown", FinishedAt = DateTimeOffset.UtcNow });
            return;
        }
        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(retrieval)))).ToLowerInvariant();
        var state = new QuickAnswerGeneration(Guid.NewGuid(), claim.RunId, QuickAnswerPrompt.Version, fingerprint,
            0, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, QuickAnswerOutcome.Failed, "GenerationDisabled", null);
        if (retrieval.Outcome == QuickRetrievalOutcome.Failed)
        {
            await Settle(state with { FailureCategory = "RetrievalFailed" });
            return;
        }
        if (!options.Enabled) { await Settle(state); return; }
        QuickAnswerInput input;
        try { input = QuickAnswerPrompt.Build(claim, options); }
        catch (QuickAnswerInputTooLargeException) { await Settle(state with { FailureCategory = "InputTooLarge" }); return; }
        state = state with { InputFingerprint = input.Fingerprint };
        if (input.SourceIds.Length == 0)
        {
            await Settle(state with { Outcome = QuickAnswerOutcome.InsufficientEvidence, FailureCategory = null,
                Answer = QuickAnswerValidator.Insufficient(retrieval) });
            return;
        }
        state = state with { AttemptCount = 1, FinishedAt = null, Outcome = QuickAnswerOutcome.Started, FailureCategory = null };
        // The reservation commits before dispatch and is never refunded, including pre-dispatch crashes.
        if (!await store.ReserveAnswerAsync(claim, state, token)) throw new QuickResearchLeaseLostException();
        logger.LogInformation("QuickAnswerReserved. QuickRunId: {QuickRunId}; GenerationId: {GenerationId}; LeaseVersion: {LeaseVersion}",
            claim.RunId, state.Id, claim.LeaseVersion);
        token.ThrowIfCancellationRequested();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(options.TimeoutSeconds));
        try
        {
            var result = await client.GenerateStructuredAsync<QuickAnswerDraft>(input.Request, deadline.Token).WaitAsync(deadline.Token);
            if (Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(result.Value)) > options.MaxResponseBytes)
                throw new ArgumentException("Quick answer exceeded the output limit.");
            var answer = QuickAnswerValidator.ValidateDraft(result.Value, retrieval, input.SourceIds);
            state = state with { Outcome = answer.Uncertainty == "Insufficient" ? QuickAnswerOutcome.InsufficientEvidence : QuickAnswerOutcome.Succeeded,
                Answer = answer, FinishedAt = DateTimeOffset.UtcNow };
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (OperationCanceledException)
        { state = state with { Outcome = QuickAnswerOutcome.OutcomeUnknown, FailureCategory = "OutcomeUnknown", FinishedAt = DateTimeOffset.UtcNow }; }
        catch (StructuredLlmTimeoutException)
        { state = state with { Outcome = QuickAnswerOutcome.OutcomeUnknown, FailureCategory = "OutcomeUnknown", FinishedAt = DateTimeOffset.UtcNow }; }
        catch (ArgumentException)
        { state = state with { Outcome = QuickAnswerOutcome.Failed, FailureCategory = "InvalidAnswer", FinishedAt = DateTimeOffset.UtcNow }; }
        catch (Exception)
        { state = state with { Outcome = QuickAnswerOutcome.Failed, FailureCategory = "ProviderFailure", FinishedAt = DateTimeOffset.UtcNow }; }
        // Storage errors are not provider failures. A lost response cannot cause another generation.
        await Settle(state);

        async Task Settle(QuickAnswerGeneration value)
        {
            value.Validate(claim.RunId);
            if (!await store.SettleAnswerAsync(claim, value, token)) throw new QuickResearchLeaseLostException();
            logger.LogInformation("QuickAnswerSettled. QuickRunId: {QuickRunId}; Outcome: {Outcome}; LeaseVersion: {LeaseVersion}",
                claim.RunId, value.Outcome, claim.LeaseVersion);
        }
    }
}
