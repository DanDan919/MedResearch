using MedResearch.Application.Research.Ai;
using MedResearch.Domain;
using Microsoft.Extensions.Logging;

namespace MedResearch.Application.Research.Extraction;

public sealed class EvidenceExtractor : IEvidenceExtractor
{
    private readonly IStructuredLlmClient _structuredLlmClient;
    private readonly EvidenceExtractionDraftValidator _validator;
    private readonly ILogger<EvidenceExtractor> _logger;
    private readonly ValidationGuidedLlmRepairService _repairService;

    public EvidenceExtractor(
        IStructuredLlmClient structuredLlmClient,
        EvidenceExtractionDraftValidator validator,
        ILogger<EvidenceExtractor> logger,
        ValidationGuidedLlmRepairService? repairService = null)
    {
        _structuredLlmClient = structuredLlmClient;
        _validator = validator;
        _logger = logger;
        _repairService = repairService ?? new ValidationGuidedLlmRepairService(structuredLlmClient);
    }

    public async Task<EvidenceExtractionResult> ExtractAsync(
        EvidenceExtractionStudyContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (context.SourceMaterialId is null || string.IsNullOrWhiteSpace(context.SourceContent))
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
                null,
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
            "EvidenceExtractionStarted. ResearchRunId: {ResearchRunId}; StudyId: {StudyId}; SourceMaterialId: {SourceMaterialId}; SourceScope: {SourceScope}; PromptVersion: {PromptVersion}",
            context.ResearchRunId,
            context.StudyId,
            context.SourceMaterialId,
            context.SourceScope,
            EvidenceExtractionPrompt.Version);

        try
        {
            var validatedGeneration = await _repairService.GenerateAndValidateAsync<EvidenceExtractionDraft, IReadOnlyCollection<AcceptedEvidenceFinding>>(
                new StructuredLlmRequest(
                    EvidenceExtractionPrompt.Version,
                    prompt.SystemPrompt,
                    prompt.UserPrompt,
                    EvidenceExtractionPrompt.OutputSchema),
                (draft, _) => _validator.Validate(context, draft),
                "evidence extraction",
                cancellationToken);
            var acceptedFindings = validatedGeneration.Value;

            _logger.LogInformation(
                "EvidenceExtractionCompleted. ResearchRunId: {ResearchRunId}; StudyId: {StudyId}; SourceMaterialId: {SourceMaterialId}; Provider: {Provider}; Model: {Model}; PromptVersion: {PromptVersion}; FindingCount: {FindingCount}; LlmAttemptCount: {LlmAttemptCount}; RepairedIssueCodes: {RepairedIssueCodes}; DurationMs: {DurationMs}",
                context.ResearchRunId,
                context.StudyId,
                context.SourceMaterialId,
                validatedGeneration.Metadata.Provider,
                validatedGeneration.Metadata.Model,
                EvidenceExtractionPrompt.Version,
                acceptedFindings.Count,
                validatedGeneration.AttemptCount,
                string.Join(',', validatedGeneration.RepairedIssueCodes),
                (DateTimeOffset.UtcNow - startedAt).TotalMilliseconds);

            return new EvidenceExtractionResult(
                context.ResearchRunId,
                context.StudyId,
                context.SourceMaterialId,
                EvidenceExtractionStatus.Completed,
                null,
                context.SourceScope,
                validatedGeneration.Metadata.Provider,
                validatedGeneration.Metadata.Model,
                EvidenceExtractionPrompt.Version,
                validatedGeneration.Metadata.GeneratedAt,
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
                "EvidenceGroundingValidationFailed. ResearchRunId: {ResearchRunId}; StudyId: {StudyId}; SourceMaterialId: {SourceMaterialId}; PromptVersion: {PromptVersion}; DurationMs: {DurationMs}",
                context.ResearchRunId,
                context.StudyId,
                context.SourceMaterialId,
                EvidenceExtractionPrompt.Version,
                (DateTimeOffset.UtcNow - startedAt).TotalMilliseconds);
            throw;
        }
        catch (EvidenceExtractionValidationException exception)
        {
            _logger.LogWarning(
                exception,
                "EvidenceExtractionValidationFailed. ResearchRunId: {ResearchRunId}; StudyId: {StudyId}; SourceMaterialId: {SourceMaterialId}; PromptVersion: {PromptVersion}; DurationMs: {DurationMs}",
                context.ResearchRunId,
                context.StudyId,
                context.SourceMaterialId,
                EvidenceExtractionPrompt.Version,
                (DateTimeOffset.UtcNow - startedAt).TotalMilliseconds);
            throw;
        }
    }
}
