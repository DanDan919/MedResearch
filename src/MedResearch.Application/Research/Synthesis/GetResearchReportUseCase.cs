using Microsoft.Extensions.Logging;
using MedResearch.Application.Security;

namespace MedResearch.Application.Research.Synthesis;

public sealed class GetResearchReportUseCase
{
    private readonly IResearchReportStore _researchReportStore;
    private readonly ICurrentActor _currentActor;
    private readonly ILogger<GetResearchReportUseCase> _logger;

    public GetResearchReportUseCase(
        IResearchReportStore researchReportStore,
        ICurrentActor currentActor,
        ILogger<GetResearchReportUseCase> logger)
    {
        _researchReportStore = researchReportStore;
        _currentActor = currentActor;
        _logger = logger;
    }

    public async Task<ResearchReportReadModel?> ExecuteAsync(Guid researchRunId, CancellationToken cancellationToken)
    {
        var report = await _researchReportStore.FindReportAsync(
            researchRunId,
            _currentActor.RequireSubjectId(),
            cancellationToken);
        if (report is null)
        {
            _logger.LogInformation("ResearchReportReadNotReady. ResearchRunId: {ResearchRunId}", researchRunId);
            return null;
        }

        _logger.LogInformation(
            "ResearchReportRead. ResearchRunId: {ResearchRunId}; ResearchReportId: {ResearchReportId}; ReportStatus: {ReportStatus}; ClaimCount: {ClaimCount}",
            report.ResearchRunId,
            report.ResearchReportId,
            report.Status,
            report.Claims.Count);

        return report;
    }
}
