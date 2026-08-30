using Microsoft.Extensions.Logging;

namespace MedResearch.Application.Research;

public sealed class GetResearchUseCase
{
    private readonly IResearchStore _researchStore;
    private readonly ILogger<GetResearchUseCase> _logger;

    public GetResearchUseCase(IResearchStore researchStore, ILogger<GetResearchUseCase> logger)
    {
        _researchStore = researchStore;
        _logger = logger;
    }

    public async Task<ResearchRunDetails?> ExecuteAsync(Guid researchRunId, CancellationToken cancellationToken)
    {
        var result = await _researchStore.FindResearchRunAsync(researchRunId, cancellationToken);

        if (result is null)
        {
            _logger.LogInformation("Research run not found. ResearchRunId: {ResearchRunId}", researchRunId);
            return null;
        }

        _logger.LogInformation(
            "Research run retrieved. ResearchRunId: {ResearchRunId}; ResearchStatus: {ResearchStatus}",
            result.ResearchRunId,
            result.Status);

        return result;
    }
}
