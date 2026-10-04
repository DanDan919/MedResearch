using MedResearch.Domain;
using MedResearch.Application.Security;
using Microsoft.Extensions.Logging;

namespace MedResearch.Application.Research;

public sealed class CreateResearchUseCase
{
    private readonly IResearchStore _researchStore;
    private readonly ICurrentActor _currentActor;
    private readonly ILogger<CreateResearchUseCase> _logger;

    public CreateResearchUseCase(
        IResearchStore researchStore,
        ICurrentActor currentActor,
        ILogger<CreateResearchUseCase> logger)
    {
        _researchStore = researchStore;
        _currentActor = currentActor;
        _logger = logger;
    }

    public async Task<CreateResearchResult> ExecuteAsync(CreateResearchCommand command, CancellationToken cancellationToken)
    {
        if (command.Question is null)
        {
            throw new ArgumentException("Question is required.", nameof(command));
        }

        var ownerSubjectId = _currentActor.RequireSubjectId();
        var now = DateTimeOffset.UtcNow;
        var question = new ResearchQuestion(command.Question, now, ownerSubjectId);
        var run = new ResearchRun(question.Id, now);

        await _researchStore.PersistInitialResearchAsync(question, run, ownerSubjectId, cancellationToken);

        _logger.LogInformation(
            "Research run created. ResearchRunId: {ResearchRunId}; ResearchQuestionId: {ResearchQuestionId}; ResearchStatus: {ResearchStatus}",
            run.Id,
            question.Id,
            run.Status);

        return new CreateResearchResult(run.Id, run.Status.ToString());
    }
}
