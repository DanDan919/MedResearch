using MedResearch.Application.Research.Ai;
using MedResearch.Domain;
using Microsoft.Extensions.Logging;

namespace MedResearch.Application.Research.Planning;

public sealed class ResearchPlanner : IResearchPlanner
{
    private readonly IStructuredLlmClient _structuredLlmClient;
    private readonly IResearchPlanStore _researchPlanStore;
    private readonly ILogger<ResearchPlanner> _logger;

    public ResearchPlanner(
        IStructuredLlmClient structuredLlmClient,
        IResearchPlanStore researchPlanStore,
        ILogger<ResearchPlanner> logger)
    {
        _structuredLlmClient = structuredLlmClient;
        _researchPlanStore = researchPlanStore;
        _logger = logger;
    }

    public async Task<ResearchPlan> GenerateAndPersistPlanAsync(
        Guid researchRunId,
        Guid researchQuestionId,
        string researchQuestion,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(researchQuestion);

        var prompt = ResearchPlannerPrompt.Create(researchQuestion);
        var startedAt = DateTimeOffset.UtcNow;

        _logger.LogInformation(
            "ResearchPlanningStarted. ResearchRunId: {ResearchRunId}; PromptVersion: {PromptVersion}",
            researchRunId,
            ResearchPlannerPrompt.Version);

        try
        {
            var generationResult = await _structuredLlmClient.GenerateStructuredAsync<ResearchPlanDraft>(
                new StructuredLlmRequest(
                    ResearchPlannerPrompt.Version,
                    prompt.SystemPrompt,
                    prompt.UserPrompt,
                    ResearchPlannerPrompt.OutputSchema),
                cancellationToken);

            var acceptedPlan = ResearchPlanValidator.CreateValidatedPlan(
                Guid.NewGuid(),
                researchRunId,
                researchQuestionId,
                researchQuestion,
                generationResult.Value,
                generationResult.Metadata,
                ResearchPlannerPrompt.Version);

            await _researchPlanStore.SaveResearchPlanAsync(acceptedPlan, cancellationToken);

            _logger.LogInformation(
                "ResearchPlanPersisted. ResearchRunId: {ResearchRunId}; ResearchPlanId: {ResearchPlanId}; Provider: {Provider}; Model: {Model}; PromptVersion: {PromptVersion}; SearchQueryCount: {SearchQueryCount}",
                researchRunId,
                acceptedPlan.Id,
                acceptedPlan.Provider,
                acceptedPlan.Model,
                acceptedPlan.PromptVersion,
                acceptedPlan.SearchQueries.Length);

            _logger.LogInformation(
                "ResearchPlanningCompleted. ResearchRunId: {ResearchRunId}; ResearchPlanId: {ResearchPlanId}; Provider: {Provider}; Model: {Model}; PromptVersion: {PromptVersion}; SearchQueryCount: {SearchQueryCount}; DurationMs: {DurationMs}",
                researchRunId,
                acceptedPlan.Id,
                acceptedPlan.Provider,
                acceptedPlan.Model,
                acceptedPlan.PromptVersion,
                acceptedPlan.SearchQueries.Length,
                (DateTimeOffset.UtcNow - startedAt).TotalMilliseconds);

            return acceptedPlan;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (ResearchPlanValidationException exception)
        {
            _logger.LogWarning(
                exception,
                "ResearchPlanningValidationFailed. ResearchRunId: {ResearchRunId}; PromptVersion: {PromptVersion}; DurationMs: {DurationMs}",
                researchRunId,
                ResearchPlannerPrompt.Version,
                (DateTimeOffset.UtcNow - startedAt).TotalMilliseconds);
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "ResearchPlanningProviderFailed. ResearchRunId: {ResearchRunId}; PromptVersion: {PromptVersion}; DurationMs: {DurationMs}",
                researchRunId,
                ResearchPlannerPrompt.Version,
                (DateTimeOffset.UtcNow - startedAt).TotalMilliseconds);
            throw;
        }
    }
}

