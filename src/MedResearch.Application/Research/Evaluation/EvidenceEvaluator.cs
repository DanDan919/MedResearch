using MedResearch.Application.Research.Ai;
using MedResearch.Domain;
using Microsoft.Extensions.Logging;

namespace MedResearch.Application.Research.Evaluation;

public sealed class EvidenceEvaluator : IEvidenceEvaluator
{
    private readonly IStructuredLlmClient _structuredLlmClient;
    private readonly EvidenceEvaluationSignalBuilder _signalBuilder;
    private readonly EvidenceEvaluationDraftValidator _validator;
    private readonly ILogger<EvidenceEvaluator> _logger;

    public EvidenceEvaluator(
        IStructuredLlmClient structuredLlmClient,
        EvidenceEvaluationSignalBuilder signalBuilder,
        EvidenceEvaluationDraftValidator validator,
        ILogger<EvidenceEvaluator> logger)
    {
        _structuredLlmClient = structuredLlmClient;
        _signalBuilder = signalBuilder;
        _validator = validator;
        _logger = logger;
    }

    public async Task<EvidenceEvaluationResult> EvaluateAsync(
        EvaluationStudyContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var signals = _signalBuilder.Build(context);
        if (context.Evidence.Count == 0)
        {
            _logger.LogInformation(
                "EvidenceEvaluationSkipped. ResearchRunId: {ResearchRunId}; StudyId: {StudyId}; PromptVersion: {PromptVersion}; Reason: {Reason}; SourceScope: {SourceScope}",
                context.ResearchRunId,
                context.StudyId,
                EvidenceEvaluationPrompt.Version,
                EvidenceEvaluationSkipReason.NoExtractedEvidence,
                context.SourceScope);

            return CreateSkippedResult(context, signals, EvidenceEvaluationSkipReason.NoExtractedEvidence);
        }

        var prompt = EvidenceEvaluationPrompt.Create(context, signals);
        var startedAt = DateTimeOffset.UtcNow;

        _logger.LogInformation(
            "EvidenceEvaluationStarted. ResearchRunId: {ResearchRunId}; StudyId: {StudyId}; PromptVersion: {PromptVersion}; SourceScope: {SourceScope}; EvidenceCount: {EvidenceCount}",
            context.ResearchRunId,
            context.StudyId,
            EvidenceEvaluationPrompt.Version,
            context.SourceScope,
            context.Evidence.Count);

        try
        {
            var generationResult = await _structuredLlmClient.GenerateStructuredAsync<EvidenceEvaluationDraft>(
                new StructuredLlmRequest(
                    EvidenceEvaluationPrompt.Version,
                    prompt.SystemPrompt,
                    prompt.UserPrompt,
                    EvidenceEvaluationPrompt.OutputSchema),
                cancellationToken);

            var result = _validator.Validate(
                context,
                generationResult.Value,
                signals,
                generationResult.Metadata.Provider,
                generationResult.Metadata.Model,
                generationResult.Metadata.GeneratedAt);

            _logger.LogInformation(
                "EvidenceEvaluationCompleted. ResearchRunId: {ResearchRunId}; StudyId: {StudyId}; Provider: {Provider}; Model: {Model}; PromptVersion: {PromptVersion}; SourceScope: {SourceScope}; OverallCategory: {OverallCategory}; UnknownDomainCount: {UnknownDomainCount}; InsufficientSourceDomainCount: {InsufficientSourceDomainCount}; DurationMs: {DurationMs}",
                result.ResearchRunId,
                result.StudyId,
                result.EvaluatorProvider,
                result.EvaluatorModel,
                result.PromptVersion,
                result.SourceScope,
                result.OverallConfidence,
                result.UnknownDomainCount,
                result.InsufficientSourceDomainCount,
                (DateTimeOffset.UtcNow - startedAt).TotalMilliseconds);

            return result;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (EvidenceEvaluationValidationException exception)
        {
            _logger.LogWarning(
                exception,
                "EvidenceEvaluationValidationFailed. ResearchRunId: {ResearchRunId}; StudyId: {StudyId}; PromptVersion: {PromptVersion}; DurationMs: {DurationMs}",
                context.ResearchRunId,
                context.StudyId,
                EvidenceEvaluationPrompt.Version,
                (DateTimeOffset.UtcNow - startedAt).TotalMilliseconds);
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "EvidenceEvaluationProviderFailed. ResearchRunId: {ResearchRunId}; StudyId: {StudyId}; PromptVersion: {PromptVersion}; DurationMs: {DurationMs}",
                context.ResearchRunId,
                context.StudyId,
                EvidenceEvaluationPrompt.Version,
                (DateTimeOffset.UtcNow - startedAt).TotalMilliseconds);
            throw;
        }
    }

    private static EvidenceEvaluationResult CreateSkippedResult(
        EvaluationStudyContext context,
        EvidenceEvaluationSignalSet signals,
        EvidenceEvaluationSkipReason skipReason)
    {
        var insufficientSourceCount = context.SourceScope == EvidenceSourceScope.Abstract ? 5 : 0;

        return new EvidenceEvaluationResult(
            context.ResearchRunId,
            context.StudyId,
            [],
            EvidenceEvaluationStatus.Skipped,
            skipReason,
            context.SourceScope,
            null,
            null,
            EvidenceEvaluationPrompt.Version,
            DateTimeOffset.UtcNow,
            signals.MetadataStudyDesignHint,
            MethodologicalAssessmentState.Unknown,
            ComparatorPresence.Unclear,
            null,
            MethodologicalAssessmentState.InsufficientSource,
            MethodologicalAssessmentState.InsufficientSource,
            MethodologicalAssessmentState.InsufficientSource,
            MethodologicalAssessmentState.InsufficientSource,
            MethodologicalAssessmentState.Unknown,
            DirectnessRating.Unclear,
            MethodologicalConfidence.InsufficientInformation,
            "No source-grounded evidence findings are available for methodological evaluation in this research run.",
            signals.ReportingLimitations,
            [],
            signals,
            4,
            insufficientSourceCount);
    }
}
