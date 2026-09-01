using Microsoft.Extensions.Logging;

namespace MedResearch.Application.Research.Synthesis;

public sealed class GetResearchReportUseCase
{
    private readonly IResearchReportStore _researchReportStore;
    private readonly ILogger<GetResearchReportUseCase> _logger;

    public GetResearchReportUseCase(
        IResearchReportStore researchReportStore,
        ILogger<GetResearchReportUseCase> logger)
    {
        _researchReportStore = researchReportStore;
        _logger = logger;
    }

    public async Task<ResearchReportReadModel?> ExecuteAsync(Guid researchRunId, CancellationToken cancellationToken)
    {
        var report = await _researchReportStore.FindReportAsync(researchRunId, cancellationToken);
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