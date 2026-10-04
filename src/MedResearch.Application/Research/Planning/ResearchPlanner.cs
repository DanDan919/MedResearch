using MedResearch.Application.Research.Ai;
using MedResearch.Domain;
using Microsoft.Extensions.Logging;

namespace MedResearch.Application.Research.Planning;

public sealed class ResearchPlanner : IResearchPlanner
{
    private readonly IStructuredLlmClient _structuredLlmClient;
    private readonly IResearchPlanStore _researchPlanStore;
    private readonly ILogger<ResearchPlanner> _logger;
    private readonly ResearchPlanningOptions _options;

    public ResearchPlanner(
        IStructuredLlmClient structuredLlmClient,
        IResearchPlanStore researchPlanStore,
        ILogger<ResearchPlanner> logger,
        ResearchPlanningOptions? options = null)
    {
        _structuredLlmClient = structuredLlmClient;
        _researchPlanStore = researchPlanStore;
        _logger = logger;
        _options = options ?? new ResearchPlanningOptions();
        _options.Validate();
    }

    public async Task<ResearchPlan> GenerateAndPersistPlanAsync(
        Guid researchRunId,
        Guid researchQuestionId,
        string researchQuestion,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(researchQuestion);

        var maxSearchQueries = _options.BoundedMaxSearchQueries;
        var existingPlan = await _researchPlanStore.FindByResearchRunIdAsync(researchRunId, cancellationToken);
        if (existingPlan is not null)
        {
            if (existingPlan.ResearchQuestionId != researchQuestionId
                || !string.Equals(existingPlan.OriginalQuestion, NormalizeQuestion(researchQuestion), StringComparison.OrdinalIgnoreCase)
                || !string.Equals(existingPlan.PromptVersion, ResearchPlannerPrompt.Version, StringComparison.Ordinal))
            {
                throw new ResearchPlanValidationException(
                    "A different research plan already exists for this research run.");
            }

            _logger.LogInformation(
                "ResearchPlanReused. ResearchRunId: {ResearchRunId}; ResearchPlanId: {ResearchPlanId}; PromptVersion: {PromptVersion}",
                researchRunId,
                existingPlan.Id,
                existingPlan.PromptVersion);
            return existingPlan;
        }

        var prompt = ResearchPlannerPrompt.Create(researchQuestion, maxSearchQueries);
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
                    ResearchPlannerPrompt.CreateOutputSchema(maxSearchQueries)),
                cancellationToken);

            var acceptedPlan = ResearchPlanValidator.CreateValidatedPlan(
                Guid.NewGuid(),
                researchRunId,
                researchQuestionId,
                researchQuestion,
                generationResult.Value,
                generationResult.Metadata,
                ResearchPlannerPrompt.Version,
                maxSearchQueries);

            var persistedPlan = await _researchPlanStore.SaveResearchPlanAsync(acceptedPlan, cancellationToken);

            _logger.LogInformation(
                "ResearchPlanPersisted. ResearchRunId: {ResearchRunId}; ResearchPlanId: {ResearchPlanId}; Provider: {Provider}; Model: {Model}; PromptVersion: {PromptVersion}; SearchQueryCount: {SearchQueryCount}",
                researchRunId,
                persistedPlan.Id,
                persistedPlan.Provider,
                persistedPlan.Model,
                persistedPlan.PromptVersion,
                persistedPlan.SearchQueries.Length);

            _logger.LogInformation(
                "ResearchPlanningCompleted. ResearchRunId: {ResearchRunId}; ResearchPlanId: {ResearchPlanId}; Provider: {Provider}; Model: {Model}; PromptVersion: {PromptVersion}; SearchQueryCount: {SearchQueryCount}; DurationMs: {DurationMs}",
                researchRunId,
                persistedPlan.Id,
                persistedPlan.Provider,
                persistedPlan.Model,
                persistedPlan.PromptVersion,
                persistedPlan.SearchQueries.Length,
                (DateTimeOffset.UtcNow - startedAt).TotalMilliseconds);

            return persistedPlan;
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

    private static string NormalizeQuestion(string question)
    {
        return string.Join(' ', question.Split(null as char[], StringSplitOptions.RemoveEmptyEntries));
    }
}

