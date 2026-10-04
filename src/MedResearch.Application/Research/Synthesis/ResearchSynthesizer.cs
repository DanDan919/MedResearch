using MedResearch.Application.Research.Ai;
using MedResearch.Domain;
using Microsoft.Extensions.Logging;

namespace MedResearch.Application.Research.Synthesis;

public sealed class ResearchSynthesizer : IResearchSynthesizer
{
    private readonly IStructuredLlmClient _structuredLlmClient;
    private readonly ResearchReportDraftValidator _validator;
    private readonly ILogger<ResearchSynthesizer> _logger;
    private readonly ValidationGuidedLlmRepairService _repairService;

    public ResearchSynthesizer(
        IStructuredLlmClient structuredLlmClient,
        ResearchReportDraftValidator validator,
        ILogger<ResearchSynthesizer> logger,
        ValidationGuidedLlmRepairService? repairService = null)
    {
        _structuredLlmClient = structuredLlmClient;
        _validator = validator;
        _logger = logger;
        _repairService = repairService ?? new ValidationGuidedLlmRepairService(structuredLlmClient);
    }

    public async Task<ResearchSynthesisResult> SynthesizeAsync(
        SynthesisContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _validator.ValidateContext(context);

        if (context.Statistics.IncludedEvidenceFindingCount == 0)
        {
            _logger.LogInformation(
                "ResearchSynthesisSkippedForNoEvidence. ResearchRunId: {ResearchRunId}; PromptVersion: {PromptVersion}; DiscoveredStudyCount: {DiscoveredStudyCount}; EvidenceCount: {EvidenceCount}",
                context.ResearchRunId,
                ResearchSynthesisPrompt.Version,
                context.Statistics.DiscoveredStudyCount,
                context.Statistics.EvidenceFindingCount);

            return _validator.CreateInsufficientEvidenceResult(context);
        }

        var prompt = ResearchSynthesisPrompt.Create(context);
        var startedAt = DateTimeOffset.UtcNow;

        _logger.LogInformation(
            "ResearchSynthesisStarted. ResearchRunId: {ResearchRunId}; PromptVersion: {PromptVersion}; StudyCount: {StudyCount}; EvidenceCount: {EvidenceCount}; EvaluationCount: {EvaluationCount}",
            context.ResearchRunId,
            ResearchSynthesisPrompt.Version,
            context.Statistics.IncludedStudyCount,
            context.Statistics.IncludedEvidenceFindingCount,
            context.Statistics.EvaluatedStudyCount);

        try
        {
            var validatedGeneration = await _repairService.GenerateAndValidateAsync<ResearchReportDraft, ResearchSynthesisResult>(
                new StructuredLlmRequest(
                    ResearchSynthesisPrompt.Version,
                    prompt.SystemPrompt,
                    prompt.UserPrompt,
                    ResearchSynthesisPrompt.OutputSchema),
                (draft, metadata) => _validator.Validate(
                    context,
                    draft,
                    metadata.Provider,
                    metadata.Model,
                    metadata.GeneratedAt),
                "research synthesis",
                cancellationToken);

            var result = validatedGeneration.Value;

            _logger.LogInformation(
                "ResearchSynthesisCompleted. ResearchRunId: {ResearchRunId}; ReportStatus: {ReportStatus}; Provider: {Provider}; Model: {Model}; PromptVersion: {PromptVersion}; ClaimCount: {ClaimCount}; ConflictCount: {ConflictCount}; LlmAttemptCount: {LlmAttemptCount}; RepairedIssueCodes: {RepairedIssueCodes}; DurationMs: {DurationMs}",
                result.ResearchRunId,
                result.Status,
                result.SynthesizerProvider,
                result.SynthesizerModel,
                result.PromptVersion,
                result.Claims.Count,
                context.OutcomeDirectionSummaries.Count(summary => summary.ConflictStatus == SynthesisConflictStatus.Present),
                validatedGeneration.AttemptCount,
                string.Join(',', validatedGeneration.RepairedIssueCodes),
                (DateTimeOffset.UtcNow - startedAt).TotalMilliseconds);

            return result;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (ResearchSynthesisValidationException exception)
        {
            _logger.LogWarning(
                exception,
                "ResearchSynthesisValidationFailed. ResearchRunId: {ResearchRunId}; PromptVersion: {PromptVersion}; DurationMs: {DurationMs}",
                context.ResearchRunId,
                ResearchSynthesisPrompt.Version,
                (DateTimeOffset.UtcNow - startedAt).TotalMilliseconds);
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "ResearchSynthesisProviderFailed. ResearchRunId: {ResearchRunId}; PromptVersion: {PromptVersion}; DurationMs: {DurationMs}",
                context.ResearchRunId,
                ResearchSynthesisPrompt.Version,
                (DateTimeOffset.UtcNow - startedAt).TotalMilliseconds);
            throw;
        }
    }
}
