using MedResearch.Api.Research;
using MedResearch.Application.DependencyInjection;
using MedResearch.Application.Research;
using MedResearch.Application.Research.Quantitative;
using MedResearch.Application.Research.Synthesis;
using MedResearch.Application.Research.Provenance;
using MedResearch.Infrastructure.DependencyInjection;
using MedResearch.Application.Security;
using MedResearch.Api.Security;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Mvc;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentActor, HttpCurrentActor>();
builder.Services.AddMedResearchAuthentication(builder.Configuration, builder.Environment);
var allowedCorsOrigins = builder.Configuration
    .GetSection("Cors:AllowedOrigins")
    .Get<string[]>() ?? [];
if (allowedCorsOrigins.Length > 0)
{
    builder.Services.AddCors(options =>
    {
        options.AddPolicy("Frontend", policy =>
        {
            policy
                .WithOrigins(allowedCorsOrigins)
                .AllowAnyHeader()
                .AllowAnyMethod();
        });
    });
}

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration, builder.Environment);

var app = builder.Build();

app.UseExceptionHandler(errorApp =>
{
    errorApp.Run(async context =>
    {
        var exceptionHandler = context.Features.Get<IExceptionHandlerFeature>();
        var exception = exceptionHandler?.Error;
        var logger = context.RequestServices.GetRequiredService<ILoggerFactory>()
            .CreateLogger("MedResearch.Api.ErrorHandling");

        var (statusCode, title) = exception switch
        {
            ArgumentException or InvalidOperationException => (StatusCodes.Status400BadRequest, "Invalid request"),
            _ => (StatusCodes.Status500InternalServerError, "An unexpected error occurred")
        };

        if (statusCode == StatusCodes.Status500InternalServerError)
        {
            logger.LogError(exception, "Unhandled exception while processing HTTP request.");
        }
        else
        {
            logger.LogInformation(exception, "Client request failed validation.");
        }

        context.Response.StatusCode = statusCode;
        await Results.Problem(
            title: title,
            statusCode: statusCode,
            extensions: statusCode == StatusCodes.Status400BadRequest
                ? new Dictionary<string, object?> { ["error"] = exception?.Message }
                : null)
            .ExecuteAsync(context);
        });
});

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}
if (allowedCorsOrigins.Length > 0)
{
    app.UseCors("Frontend");
}
app.UseAuthentication();
app.UseAuthorization();

app.MapHealthChecks("/health");
app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = _ => false
});
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready") || check.Tags.Contains("database")
});

var research = app.MapGroup("/api/research")
    .WithTags("Research")
    .RequireAuthorization(AuthenticationConfiguration.PolicyName);

research.MapPost("/", async (
        CreateResearchRequest request,
        CreateResearchUseCase useCase,
        CancellationToken cancellationToken) =>
    {
        var result = await useCase.ExecuteAsync(new CreateResearchCommand(request.Question), cancellationToken);
        var response = new CreateResearchResponse(result.ResearchRunId, result.Status);

        return Results.Created($"/api/research/{result.ResearchRunId}", response);
    })
    .WithName("CreateResearch")
    .Accepts<CreateResearchRequest>("application/json")
    .Produces<CreateResearchResponse>(StatusCodes.Status201Created)
    .Produces<ProblemDetails>(StatusCodes.Status400BadRequest)
    .Produces<ProblemDetails>(StatusCodes.Status500InternalServerError);

research.MapGet("/", async (
        int? page,
        int? pageSize,
        string? status,
        ListResearchRunsUseCase useCase,
        CancellationToken cancellationToken) =>
    {
        var result = await useCase.ExecuteAsync(
            new ListResearchRunsQuery(page, pageSize, status),
            cancellationToken);

        return Results.Ok(new ResearchRunListResponse(
            result.Items.Select(item => new ResearchRunSummaryResponse(
                item.ResearchRunId,
                item.ResearchQuestionId,
                item.Question,
                item.Status,
                item.CreatedAt,
                item.StartedAt,
                item.CompletedAt,
                item.FailureReason)).ToArray(),
            result.Page,
            result.PageSize,
            result.TotalCount,
            result.TotalPages));
    })
    .WithName("ListResearchRuns")
    .Produces<ResearchRunListResponse>(StatusCodes.Status200OK)
    .Produces<ProblemDetails>(StatusCodes.Status400BadRequest)
    .Produces<ProblemDetails>(StatusCodes.Status500InternalServerError);

research.MapGet("/{researchRunId:guid}", async (
        Guid researchRunId,
        GetResearchUseCase useCase,
        CancellationToken cancellationToken) =>
    {
        var result = await useCase.ExecuteAsync(researchRunId, cancellationToken);

        if (result is null)
        {
            return Results.NotFound(new ProblemDetails
            {
                Status = StatusCodes.Status404NotFound,
                Title = "Research run not found"
            });
        }

        return Results.Ok(new ResearchRunResponse(
            result.ResearchRunId,
            result.Question,
            result.Status,
            result.CreatedAt,
            result.StartedAt,
            result.CompletedAt,
            result.FailureReason));
    })
    .WithName("GetResearch")
    .Produces<ResearchRunResponse>(StatusCodes.Status200OK)
    .Produces<ProblemDetails>(StatusCodes.Status404NotFound)
    .Produces<ProblemDetails>(StatusCodes.Status500InternalServerError);

research.MapGet("/{researchRunId:guid}/progress", async (
        Guid researchRunId,
        GetResearchProgressUseCase useCase,
        CancellationToken cancellationToken) =>
    {
        var result = await useCase.ExecuteAsync(researchRunId, cancellationToken);

        if (result is null)
        {
            return Results.NotFound(new ProblemDetails
            {
                Status = StatusCodes.Status404NotFound,
                Title = "Research run not found"
            });
        }

        return Results.Ok(ToProgressResponse(result));
    })
    .WithName("GetResearchProgress")
    .Produces<ResearchRunProgressResponse>(StatusCodes.Status200OK)
    .Produces<ProblemDetails>(StatusCodes.Status404NotFound)
    .Produces<ProblemDetails>(StatusCodes.Status500InternalServerError);


research.MapGet("/{researchRunId:guid}/report", async (
        Guid researchRunId,
        GetResearchUseCase getResearchUseCase,
        GetResearchReportUseCase getReportUseCase,
        CancellationToken cancellationToken) =>
    {
        var run = await getResearchUseCase.ExecuteAsync(researchRunId, cancellationToken);
        if (run is null)
        {
            return Results.NotFound(new ProblemDetails
            {
                Status = StatusCodes.Status404NotFound,
                Title = "Research run not found"
            });
        }

        var report = await getReportUseCase.ExecuteAsync(researchRunId, cancellationToken);
        if (report is null)
        {
            return Results.Conflict(new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "Research report is not ready",
                Extensions =
                {
                    ["researchRunId"] = researchRunId,
                    ["researchRunStatus"] = run.Status
                }
            });
        }

        return Results.Ok(ToReportResponse(report));
    })
    .WithName("GetResearchReport")
    .Produces<ResearchReportResponse>(StatusCodes.Status200OK)
    .Produces<ProblemDetails>(StatusCodes.Status404NotFound)
    .Produces<ProblemDetails>(StatusCodes.Status409Conflict)
    .Produces<ProblemDetails>(StatusCodes.Status500InternalServerError);

research.MapGet("/{researchRunId:guid}/quantitative", async (
        Guid researchRunId,
        GetResearchUseCase getResearchUseCase,
        GetQuantitativeSynthesisArtifactsUseCase useCase,
        CancellationToken cancellationToken) =>
    {
        var run = await getResearchUseCase.ExecuteAsync(researchRunId, cancellationToken);
        if (run is null)
        {
            return Results.NotFound(new ProblemDetails
            {
                Status = StatusCodes.Status404NotFound,
                Title = "Research run not found"
            });
        }

        var artifacts = await useCase.ExecuteAsync(researchRunId, cancellationToken);
        return Results.Ok(artifacts.Select(artifact => new QuantitativeSynthesisArtifactResponse(
            artifact.ArtifactId,
            artifact.PersistedAt,
            artifact.SnapshotFingerprint,
            artifact.Result)).ToArray());
    })
    .WithName("GetQuantitativeSynthesisArtifacts")
    .Produces<IReadOnlyCollection<QuantitativeSynthesisArtifactResponse>>(StatusCodes.Status200OK)
    .Produces<ProblemDetails>(StatusCodes.Status404NotFound)
    .Produces<ProblemDetails>(StatusCodes.Status500InternalServerError);

research.MapGet("/{researchRunId:guid}/provenance", async (
        Guid researchRunId,
        GetResearchProvenanceUseCase useCase,
        CancellationToken cancellationToken) =>
    {
        var result = await useCase.ExecuteAsync(researchRunId, cancellationToken);

        if (result is null)
        {
            return Results.NotFound(new ProblemDetails
            {
                Status = StatusCodes.Status404NotFound,
                Title = "Research run not found"
            });
        }

        return Results.Ok(ToProvenanceResponse(result));
    })
    .WithName("GetResearchProvenance")
    .Produces<ResearchProvenanceResponse>(StatusCodes.Status200OK)
    .Produces<ProblemDetails>(StatusCodes.Status404NotFound)
    .Produces<ProblemDetails>(StatusCodes.Status500InternalServerError);
app.Run();

static ResearchReportResponse ToReportResponse(ResearchReportReadModel report)
{
    return new ResearchReportResponse(
        report.ResearchRunId,
        report.ResearchReportId,
        report.Status.ToString(),
        report.InsufficientEvidenceReason?.ToString(),
        report.Question,
        report.ExecutiveSummary,
        report.EvidenceSummary,
        report.ConflictSummary,
        report.LimitationsSummary,
        report.Conclusion,
        report.SynthesisConfidence.ToString(),
        report.PromptVersion,
        report.GeneratedAt,
        new ResearchReportCoverageResponse(
            report.Coverage.DiscoveredStudyCount,
            report.Coverage.ExtractedStudyCount,
            report.Coverage.EvaluatedStudyCount,
            report.Coverage.EvidenceFindingCount,
            report.Coverage.IncludedStudyCount,
            report.Coverage.IncludedEvidenceFindingCount,
            report.Coverage.SearchQueryCount,
            report.Coverage.StudiesWithNoExtractableEvidence,
            report.Coverage.StudiesWithInsufficientEvaluationSource,
            report.Coverage.PotentialConflictDetected,
            report.Coverage.EvidenceTruncated,
            report.Coverage.UsesAbstractLevelEvidenceOnly,
            report.Coverage.SearchedSources),
        report.DeterministicLimitations,
        report.Claims.Select(claim => new ResearchReportClaimResponse(
            claim.ClaimId,
            claim.ClaimType.ToString(),
            claim.Direction.ToString(),
            claim.Text,
            claim.Ordinal,
            claim.Citations.Select(citation => new ResearchReportCitationResponse(
                citation.EvidenceId,
                citation.StudyId,
                citation.Pmid,
                citation.Pmcid,
                citation.Doi,
                citation.Title,
                citation.Journal,
                citation.PublicationYear,
                citation.PublicationMonth,
                citation.PublicationDay,
                citation.PublicationTypes,
                citation.Authors,
                citation.StudySource,
                citation.Outcome,
                citation.ResultSummary,
                citation.SupportingText,
                citation.EvidenceDirection.ToString(),
                citation.SourceScope.ToString(),
                citation.GroundingValidated,
                citation.Population,
                citation.ExposureOrIntervention,
                citation.Comparator,
                citation.StudyDesign,
                citation.SampleSize,
                citation.EffectMeasure,
                citation.EffectValue,
                citation.ConfidenceIntervalLower,
                citation.ConfidenceIntervalUpper,
                citation.ConfidenceLevel,
                citation.ReportedStandardError,
                citation.PValue,
                citation.ExtractedAt,
                citation.SourceMaterial is null
                    ? null
                    : new ResearchReportSourceMaterialResponse(
                        citation.SourceMaterial.SourceMaterialId,
                        citation.SourceMaterial.Type,
                        citation.SourceMaterial.Provider,
                        citation.SourceMaterial.RetrievalMethod,
                        citation.SourceMaterial.ContentVersion,
                        citation.SourceMaterial.RetrievedAt,
                        citation.SourceMaterial.AccessStatus,
                        citation.SourceMaterial.WasTruncated,
                        citation.SourceMaterial.SectionNames),
                citation.Ordinal)).ToArray())).ToArray());
}

static ResearchRunProgressResponse ToProgressResponse(ResearchRunProgress progress)
{
    return new ResearchRunProgressResponse(
        progress.ResearchRunId,
        progress.Question,
        progress.Status,
        progress.CreatedAt,
        progress.StartedAt,
        progress.CompletedAt,
        progress.FailureReason,
        progress.RefreshedAt,
        new ResearchRunProcessingProgressResponse(
            progress.Processing.LeaseState,
            progress.Processing.LeaseExpiresAt,
            progress.Processing.LastHeartbeatAt,
            progress.Processing.LeaseVersion),
        new ResearchRunProgressMetricsResponse(
            progress.Metrics.ResearchPlanCount,
            progress.Metrics.PlannedSearchQueryCount,
            progress.Metrics.LiteratureSearchCount,
            progress.Metrics.LiteratureSearchSourceCount,
            progress.Metrics.LiteratureSearchResultCount,
            progress.Metrics.DiscoveryPathCount,
            progress.Metrics.DistinctDiscoveredStudyCount,
            progress.Metrics.CurrentSourceMaterialCount,
            progress.Metrics.StructuredFullTextMaterialCount,
            progress.Metrics.AbstractMaterialCount,
            progress.Metrics.EvidenceExtractionCount,
            progress.Metrics.CompletedEvidenceExtractionCount,
            progress.Metrics.SkippedEvidenceExtractionCount,
            progress.Metrics.EvidenceFindingCount,
            progress.Metrics.EvidenceEvaluationCount,
            progress.Metrics.CompletedEvidenceEvaluationCount,
            progress.Metrics.SkippedEvidenceEvaluationCount,
            progress.Metrics.ResearchReportCount,
            progress.Metrics.ResearchReportClaimCount),
        progress.Stages.Select(stage => new ResearchRunStageProgressResponse(
            stage.Stage,
            stage.State,
            stage.Metrics.Select(metric => new ResearchRunProgressMetricResponse(
                metric.Label,
                metric.Value)).ToArray())).ToArray());
}

static ResearchProvenanceResponse ToProvenanceResponse(ResearchProvenanceReadModel provenance)
{
    return new ResearchProvenanceResponse(
        provenance.ResearchRunId,
        provenance.Question,
        provenance.Status.ToString(),
        provenance.CreatedAt,
        provenance.StartedAt,
        provenance.CompletedAt,
        new ResearchProvenanceCoverageResponse(
            provenance.Coverage.ResearchPlanCount,
            provenance.Coverage.LiteratureSearchCount,
            provenance.Coverage.DiscoveryPathCount,
            provenance.Coverage.DistinctStudyCount,
            provenance.Coverage.SourceMaterialCount,
            provenance.Coverage.EvidenceExtractionCount,
            provenance.Coverage.EvidenceFindingCount,
            provenance.Coverage.EvidenceEvaluationCount,
            provenance.Coverage.ResearchReportClaimCount,
            provenance.Coverage.HasPersistedProviderFailureProvenance),
        provenance.Plans.Select(plan => new ResearchPlanProvenanceResponse(
            plan.ResearchPlanId,
            plan.OriginalQuestion,
            plan.SearchQueries,
            plan.Provider,
            plan.Model,
            plan.PromptVersion,
            plan.GeneratedAt)).ToArray(),
        provenance.Searches.Select(search => new LiteratureSearchProvenanceResponse(
            search.LiteratureSearchId,
            search.ResearchPlanId,
            search.Source,
            search.Query,
            search.SearchedAt,
            search.ResultCount,
            search.PersistedStudyCount,
            search.DuplicateStudyCount,
            search.ResultStatus)).ToArray(),
        provenance.Studies.Select(study => new StudyProvenanceResponse(
            study.StudyId,
            study.Title,
            study.Pmid,
            study.Pmcid,
            study.Doi,
            study.Journal,
            study.PublicationYear,
            study.PublicationMonth,
            study.PublicationDay,
            study.PublicationTypes,
            study.Authors,
            study.Source,
            study.DiscoveryPaths.Select(discovery => new StudyDiscoveryProvenanceResponse(
                discovery.ResearchStudyDiscoveryId,
                discovery.LiteratureSearchId,
                discovery.Source,
                discovery.SourceStudyIdentifier,
                discovery.Query,
                discovery.SearchedAt,
                discovery.DiscoveredAt)).ToArray(),
            study.SourceMaterials.Select(material => new SourceMaterialProvenanceResponse(
                material.SourceMaterialId,
                material.StudyId,
                material.Type.ToString(),
                material.Provider,
                material.ProviderSourceId,
                material.RetrievalMethod,
                material.ContentHash,
                material.ContentVersion,
                material.RetrievedAt,
                material.SourceUpdatedAt,
                material.AccessStatus.ToString(),
                material.CharacterCount,
                material.WasTruncated,
                material.IsCurrent,
                material.SectionNames)).ToArray(),
            study.Extractions.Select(extraction => new EvidenceExtractionProvenanceResponse(
                extraction.EvidenceExtractionId,
                extraction.StudyId,
                extraction.SourceMaterialId,
                extraction.Status.ToString(),
                extraction.SkipReason?.ToString(),
                extraction.SourceScope.ToString(),
                extraction.Provider,
                extraction.Model,
                extraction.PromptVersion,
                extraction.ExtractedAt,
                extraction.EvidenceCount,
                extraction.GroundingValidated)).ToArray(),
            study.Evidence.Select(item => new EvidenceProvenanceResponse(
                item.EvidenceId,
                item.EvidenceExtractionId,
                item.Outcome,
                item.ResultSummary,
                item.SupportingText,
                item.Direction.ToString(),
                item.SourceScope.ToString(),
                item.ExtractedAt,
                item.GroundingValidated,
                item.Population,
                item.ExposureOrIntervention,
                item.Comparator,
                item.StudyDesign,
                item.SampleSize,
                item.EffectMeasure,
                item.EffectValue,
                item.ConfidenceIntervalLower,
                item.ConfidenceIntervalUpper,
                item.ConfidenceLevel,
                item.ReportedStandardError,
                item.PValue)).ToArray(),
            study.Evaluations.Select(evaluation => new EvidenceEvaluationProvenanceResponse(
                evaluation.EvidenceEvaluationId,
                evaluation.StudyId,
                evaluation.Status.ToString(),
                evaluation.SkipReason?.ToString(),
                evaluation.SourceScope.ToString(),
                evaluation.EvidenceIds,
                evaluation.EvaluatorProvider,
                evaluation.EvaluatorModel,
                evaluation.PromptVersion,
                evaluation.EvaluatedAt,
                evaluation.StudyDesign.ToString(),
                evaluation.SampleInformation.ToString(),
                evaluation.ComparatorPresence.ToString(),
                evaluation.ComparatorDescription,
                evaluation.Randomization.ToString(),
                evaluation.Blinding.ToString(),
                evaluation.AllocationConcealment.ToString(),
                evaluation.AttritionMissingData.ToString(),
                evaluation.Precision.ToString(),
                evaluation.Directness.ToString(),
                evaluation.OverallConfidence.ToString(),
                evaluation.Rationale,
                evaluation.ReportingLimitations,
                evaluation.AuthorReportedLimitations,
                evaluation.HasSampleSize,
                evaluation.HasEffectEstimate,
                evaluation.HasConfidenceInterval,
                evaluation.HasPValue,
                evaluation.HasComparator,
                evaluation.UnknownDomainCount,
                evaluation.InsufficientSourceDomainCount)).ToArray())).ToArray(),
        provenance.ReportClaims.Select(claim => new ResearchReportClaimProvenanceResponse(
            claim.ResearchReportId,
            claim.ResearchReportClaimId,
            claim.ClaimType.ToString(),
            claim.Direction.ToString(),
            claim.Text,
            claim.Ordinal,
            claim.EvidenceIds)).ToArray(),
        provenance.QuantitativeContributions.Select(contribution => new QuantitativeContributionProvenanceResponse(
            contribution.ArtifactId,
            contribution.GroupKey,
            contribution.AnalysisMethod,
            contribution.Ordinal,
            contribution.EvidenceId,
            contribution.StudyId,
            contribution.EvidenceExtractionId,
            contribution.SourceMaterialId)).ToArray());
}
public partial class Program
{
}
