using MedResearch.Application.Research.Ai;
using MedResearch.Domain;
using Microsoft.Extensions.Logging;

namespace MedResearch.Application.Research.Extraction;

public sealed class EvidenceExtractor : IEvidenceExtractor
{
    private readonly IStructuredLlmClient _structuredLlmClient;
    private readonly EvidenceExtractionDraftValidator _validator;
    private readonly ILogger<EvidenceExtractor> _logger;

    public EvidenceExtractor(
        IStructuredLlmClient structuredLlmClient,
        EvidenceExtractionDraftValidator validator,
        ILogger<EvidenceExtractor> logger)
    {
        _structuredLlmClient = structuredLlmClient;
        _validator = validator;
        _logger = logger;
    }

    public async Task<EvidenceExtractionResult> ExtractAsync(
        EvidenceExtractionStudyContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(context.Abstract))
        {
            _logger.LogInformation(
                "EvidenceExtractionSkipped. ResearchRunId: {ResearchRunId}; StudyId: {StudyId}; PromptVersion: {PromptVersion}; Reason: {Reason}",
                context.ResearchRunId,
                context.StudyId,
                EvidenceExtractionPrompt.Version,
                EvidenceExtractionSkipReason.NoExtractableText);

            return new EvidenceExtractionResult(
                context.ResearchRunId,
                context.StudyId,
                EvidenceExtractionStatus.Skipped,
                EvidenceExtractionSkipReason.NoExtractableText,
                EvidenceSourceScope.Abstract,
                null,
                null,
                EvidenceExtractionPrompt.Version,
                DateTimeOffset.UtcNow,
                false,
                []);
        }

        var prompt = EvidenceExtractionPrompt.Create(context);
        var startedAt = DateTimeOffset.UtcNow;

        _logger.LogInformation(
            "EvidenceExtractionStarted. ResearchRunId: {ResearchRunId}; StudyId: {StudyId}; SourceScope: {SourceScope}; PromptVersion: {PromptVersion}",
            context.ResearchRunId,
            context.StudyId,
            EvidenceSourceScope.Abstract,
            EvidenceExtractionPrompt.Version);

        try
        {
            var generationResult = await _structuredLlmClient.GenerateStructuredAsync<EvidenceExtractionDraft>(
                new StructuredLlmRequest(
                    EvidenceExtractionPrompt.Version,
                    prompt.SystemPrompt,
                    prompt.UserPrompt,
                    EvidenceExtractionPrompt.OutputSchema),
                cancellationToken);

            var acceptedFindings = _validator.Validate(context, generationResult.Value);

            _logger.LogInformation(
                "EvidenceExtractionCompleted. ResearchRunId: {ResearchRunId}; StudyId: {StudyId}; Provider: {Provider}; Model: {Model}; PromptVersion: {PromptVersion}; FindingCount: {FindingCount}; DurationMs: {DurationMs}",
                context.ResearchRunId,
                context.StudyId,
                generationResult.Metadata.Provider,
                generationResult.Metadata.Model,
                EvidenceExtractionPrompt.Version,
                acceptedFindings.Count,
                (DateTimeOffset.UtcNow - startedAt).TotalMilliseconds);

            return new EvidenceExtractionResult(
                context.ResearchRunId,
                context.StudyId,
                EvidenceExtractionStatus.Completed,
                null,
                EvidenceSourceScope.Abstract,
                generationResult.Metadata.Provider,
                generationResult.Metadata.Model,
                EvidenceExtractionPrompt.Version,
                generationResult.Metadata.GeneratedAt,
                true,
                acceptedFindings);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (EvidenceGroundingValidationException exception)
        {
            _logger.LogWarning(
                exception,
                "EvidenceGroundingValidationFailed. ResearchRunId: {ResearchRunId}; StudyId: {StudyId}; PromptVersion: {PromptVersion}; DurationMs: {DurationMs}",
                context.ResearchRunId,
                context.StudyId,
                EvidenceExtractionPrompt.Version,
                (DateTimeOffset.UtcNow - startedAt).TotalMilliseconds);
            throw;
        }
        catch (EvidenceExtractionValidationException exception)
        {
            _logger.LogWarning(
                exception,
                "EvidenceExtractionValidationFailed. ResearchRunId: {ResearchRunId}; StudyId: {StudyId}; PromptVersion: {PromptVersion}; DurationMs: {DurationMs}",
                context.ResearchRunId,
                context.StudyId,
                EvidenceExtractionPrompt.Version,
                (DateTimeOffset.UtcNow - startedAt).TotalMilliseconds);
            throw;
        }
    }
}
