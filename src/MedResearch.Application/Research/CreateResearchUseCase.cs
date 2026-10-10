using MedResearch.Domain;
using MedResearch.Application.Security;
using MedResearch.Application.Research.Admission;
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
        if (string.IsNullOrWhiteSpace(command.Question))
        {
            throw new ArgumentException("Question is required.", nameof(command));
        }

        var ownerSubjectId = _currentActor.RequireSubjectId();
        var key = ResearchCreateIdentity.ParseKey(command.IdempotencyKey);
        if (command.Question.Trim().Length > 1000)
            throw new ArgumentException("Question must not exceed 1000 characters.", nameof(command));
        var now = DateTimeOffset.UtcNow;
        var question = new ResearchQuestion(command.Question, now, ownerSubjectId);
        var run = new ResearchRun(question.Id, now);

        var result = await _researchStore.PersistInitialResearchAsync(question, run, ownerSubjectId, key, cancellationToken);

        _logger.LogInformation(
            "Research admission {AdmissionOutcome}. ResearchRunId: {ResearchRunId}",
            result.Replayed ? "Replayed" : "Accepted",
            result.ResearchRunId);

        return result;
    }
}
