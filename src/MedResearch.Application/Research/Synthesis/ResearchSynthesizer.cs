using MedResearch.Application.Research.Ai;
using MedResearch.Domain;
using Microsoft.Extensions.Logging;

namespace MedResearch.Application.Research.Synthesis;

public sealed class ResearchSynthesizer : IResearchSynthesizer
{
    private readonly IStructuredLlmClient _structuredLlmClient;
    private readonly ResearchReportDraftValidator _validator;
    private readonly ILogger<ResearchSynthesizer> _logger;

    public ResearchSynthesizer(
        IStructuredLlmClient structuredLlmClient,
        ResearchReportDraftValidator validator,
        ILogger<ResearchSynthesizer> logger)
    {
        _structuredLlmClient = structuredLlmClient;
        _validator = validator;
        _logger = logger;
    }

    public async Task<ResearchSynthesisResult> SynthesizeAsync(
        SynthesisContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

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
            var generationResult = await _structuredLlmClient.GenerateStructuredAsync<ResearchReportDraft>(
                new StructuredLlmRequest(
                    ResearchSynthesisPrompt.Version,
                    prompt.SystemPrompt,
                    prompt.UserPrompt,
                    ResearchSynthesisPrompt.OutputSchema),
                cancellationToken);

            var result = _validator.Validate(
                context,
                generationResult.Value,
                generationResult.Metadata.Provider,
                generationResult.Metadata.Model,
                generationResult.Metadata.GeneratedAt);

            _logger.LogInformation(
                "ResearchSynthesisCompleted. ResearchRunId: {ResearchRunId}; ReportStatus: {ReportStatus}; Provider: {Provider}; Model: {Model}; PromptVersion: {PromptVersion}; ClaimCount: {ClaimCount}; ConflictCount: {ConflictCount}; DurationMs: {DurationMs}",
                result.ResearchRunId,
                result.Status,
                result.SynthesizerProvider,
                result.SynthesizerModel,
                result.PromptVersion,
                result.Claims.Count,
                context.OutcomeDirectionSummaries.Count(summary => summary.ConflictStatus == SynthesisConflictStatus.Present),
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