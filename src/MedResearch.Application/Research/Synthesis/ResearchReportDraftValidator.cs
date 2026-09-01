using MedResearch.Domain;

namespace MedResearch.Application.Research.Synthesis;

public sealed class ResearchReportDraftValidator
{
    private const int MaxSectionLength = 2_500;
    private const int MaxClaimTextLength = 800;
    private const int MaxEvidencePerClaim = 12;

    private readonly SynthesisOptions _options;

    public ResearchReportDraftValidator(SynthesisOptions options)
    {
        _options = options;
    }

    public ResearchSynthesisResult Validate(
        SynthesisContext context,
        ResearchReportDraft draft,
        string provider,
        string model,
        DateTimeOffset generatedAt)
    {
        ValidateContextEvidenceScope(context);

        var status = ParseEnum<ResearchReportStatus>(draft.ReportStatus, nameof(draft.ReportStatus));
        var reason = ParseNullableEnum<ResearchReportInsufficientEvidenceReason>(draft.InsufficientEvidenceReason, nameof(draft.InsufficientEvidenceReason));
        var confidence = ParseEnum<SynthesisConfidence>(draft.SynthesisConfidence, nameof(draft.SynthesisConfidence));

        if (status == ResearchReportStatus.Completed && reason is not null)
        {
            throw new ResearchSynthesisValidationException("Completed research reports cannot include an insufficient-evidence reason.");
        }

        if (status == ResearchReportStatus.InsufficientEvidence && reason is null)
        {
            throw new ResearchSynthesisValidationException("Insufficient-evidence research reports require a reason.");
        }

        var executiveSummary = NormalizeRequired(draft.ExecutiveSummary, "Executive summary is required.", MaxSectionLength);
        var evidenceSummary = NormalizeRequired(draft.EvidenceSummary, "Evidence summary is required.", MaxSectionLength);
        var conflictSummary = NormalizeRequired(draft.ConflictSummary, "Conflict summary is required.", MaxSectionLength);
        var limitationsSummary = NormalizeRequired(draft.LimitationsSummary, "Limitations summary is required.", MaxSectionLength);
        var conclusion = NormalizeRequired(draft.Conclusion, "Conclusion is required.", MaxSectionLength);
        var claims = ValidateClaims(context, draft.Claims, status);

        if (status == ResearchReportStatus.Completed && confidence == SynthesisConfidence.InsufficientEvidence)
        {
            throw new ResearchSynthesisValidationException("Completed research reports cannot use InsufficientEvidence synthesis confidence.");
        }

        if (status == ResearchReportStatus.InsufficientEvidence)
        {
            confidence = SynthesisConfidence.InsufficientEvidence;
        }

        return new ResearchSynthesisResult(
            context.ResearchRunId,
            status,
            reason,
            executiveSummary,
            evidenceSummary,
            conflictSummary,
            limitationsSummary,
            conclusion,
            confidence,
            provider,
            model,
            ResearchSynthesisPrompt.Version,
            generatedAt,
            context.Statistics,
            context.SourceCoverage,
            context.DeterministicLimitations,
            claims);
    }

    public ResearchSynthesisResult CreateInsufficientEvidenceResult(SynthesisContext context)
    {
        const string summary = "MedResearch found no validated source-grounded evidence findings for this research run, so it did not ask the synthesis model to infer an answer from prior knowledge.";
        var limitations = context.DeterministicLimitations.Count == 0
            ? "No validated evidence findings are available for synthesis."
            : string.Join(" ", context.DeterministicLimitations);

        return new ResearchSynthesisResult(
            context.ResearchRunId,
            ResearchReportStatus.InsufficientEvidence,
            ResearchReportInsufficientEvidenceReason.NoValidatedEvidence,
            summary,
            "No validated persisted Evidence records are available for the current research run.",
            "No evidence conflict can be assessed because no validated evidence findings are available.",
            limitations,
            "No evidence-supported conclusion can be drawn from the persisted MedResearch corpus for this run.",
            SynthesisConfidence.InsufficientEvidence,
            null,
            null,
            ResearchSynthesisPrompt.Version,
            DateTimeOffset.UtcNow,
            context.Statistics,
            context.SourceCoverage,
            context.DeterministicLimitations,
            []);
    }

    private IReadOnlyCollection<AcceptedResearchReportClaim> ValidateClaims(
        SynthesisContext context,
        IReadOnlyCollection<ResearchReportClaimDraft>? draftClaims,
        ResearchReportStatus status)
    {
        var drafts = draftClaims?.ToArray() ?? [];
        if (drafts.Length > _options.BoundedMaxClaims)
        {
            throw new ResearchSynthesisValidationException($"Research report claim count exceeds {_options.BoundedMaxClaims}.");
        }

        if (status == ResearchReportStatus.Completed && drafts.Length == 0)
        {
            throw new ResearchSynthesisValidationException("Completed research reports require at least one evidence-supported claim.");
        }

        var evidenceById = context.Studies
            .SelectMany(study => study.Evidence)
            .ToDictionary(evidence => evidence.EvidenceId);
        var claims = new List<AcceptedResearchReportClaim>();

        for (var index = 0; index < drafts.Length; index++)
        {
            var draft = drafts[index];
            if (!string.IsNullOrWhiteSpace(draft.Pmid) || !string.IsNullOrWhiteSpace(draft.Doi) || !string.IsNullOrWhiteSpace(draft.StudyId))
            {
                throw new ResearchSynthesisValidationException("Model-supplied PMID, DOI, or StudyId values are not accepted as report citation authority.");
            }

            var type = ParseEnum<ResearchReportClaimType>(draft.Type, nameof(draft.Type));
            var direction = ParseEnum<ResearchReportClaimDirection>(draft.Direction, nameof(draft.Direction));
            var text = NormalizeRequired(draft.Text, "Report claim text is required.", MaxClaimTextLength);
            var evidenceIds = ParseEvidenceIds(draft.EvidenceIds);

            if (evidenceIds.Length == 0)
            {
                throw new ResearchSynthesisValidationException("Every persisted report claim must cite at least one supplied EvidenceId.");
            }

            if (evidenceIds.Length > MaxEvidencePerClaim)
            {
                throw new ResearchSynthesisValidationException($"Report claim cites more than {MaxEvidencePerClaim} evidence findings.");
            }

            var supportingEvidence = evidenceIds.Select(evidenceId =>
            {
                if (!evidenceById.TryGetValue(evidenceId, out var evidence))
                {
                    throw new ResearchSynthesisValidationException("Report claim references evidence outside the supplied synthesis context.");
                }

                return evidence;
            }).ToArray();

            ValidateClaimDirection(type, direction, supportingEvidence);
            claims.Add(new AcceptedResearchReportClaim(type, direction, text, evidenceIds, index));
        }

        if (status == ResearchReportStatus.Completed && claims.All(claim => claim.ClaimType != ResearchReportClaimType.Conclusion))
        {
            throw new ResearchSynthesisValidationException("Completed research reports require a conclusion claim with Evidence references.");
        }

        return claims;
    }

    private static void ValidateContextEvidenceScope(SynthesisContext context)
    {
        if (context.Studies.SelectMany(study => study.Evidence).Any(evidence => evidence.ResearchRunId != context.ResearchRunId))
        {
            throw new ResearchSynthesisValidationException("Synthesis context contains evidence from another research run.");
        }
    }

    private static Guid[] ParseEvidenceIds(IReadOnlyCollection<string>? values)
    {
        if (values is null)
        {
            return [];
        }

        return values
            .Select(value => Guid.TryParse(value, out var parsed) ? parsed : Guid.Empty)
            .Select(id => id == Guid.Empty ? throw new ResearchSynthesisValidationException("Report claim contains an invalid EvidenceId.") : id)
            .Distinct()
            .ToArray();
    }

    private static void ValidateClaimDirection(
        ResearchReportClaimType type,
        ResearchReportClaimDirection direction,
        IReadOnlyCollection<SynthesisEvidenceContext> evidence)
    {
        var directions = evidence.Select(item => item.Direction).Distinct().ToArray();

        if (type == ResearchReportClaimType.Conflict || direction == ResearchReportClaimDirection.Mixed)
        {
            if (!directions.Contains(EvidenceDirection.Mixed)
                && !(directions.Contains(EvidenceDirection.Positive) && directions.Contains(EvidenceDirection.Negative)))
            {
                throw new ResearchSynthesisValidationException("Mixed or conflict claims require mixed evidence or opposing positive and negative evidence directions.");
            }

            return;
        }

        if (direction == ResearchReportClaimDirection.NotApplicable)
        {
            return;
        }

        if (direction == ResearchReportClaimDirection.Positive && !directions.Contains(EvidenceDirection.Positive))
        {
            throw new ResearchSynthesisValidationException("Positive report claims require at least one positive supporting evidence direction.");
        }

        if (direction == ResearchReportClaimDirection.Negative && !directions.Contains(EvidenceDirection.Negative))
        {
            throw new ResearchSynthesisValidationException("Negative report claims require at least one negative supporting evidence direction.");
        }

        if (direction == ResearchReportClaimDirection.NoClearEffect && !directions.Contains(EvidenceDirection.NoClearEffect))
        {
            throw new ResearchSynthesisValidationException("NoClearEffect report claims require at least one no-clear-effect supporting evidence direction.");
        }

        if (direction == ResearchReportClaimDirection.NotReported && directions.Any(item => item != EvidenceDirection.NotReported))
        {
            throw new ResearchSynthesisValidationException("NotReported report claims cannot cite evidence with reported effect directions.");
        }
    }

    private static TEnum ParseEnum<TEnum>(string? value, string propertyName)
        where TEnum : struct, Enum
    {
        if (string.IsNullOrWhiteSpace(value) || !Enum.TryParse<TEnum>(value.Trim(), ignoreCase: false, out var parsed))
        {
            throw new ResearchSynthesisValidationException($"Unsupported or missing report category for {propertyName}.");
        }

        return parsed;
    }

    private static TEnum? ParseNullableEnum<TEnum>(string? value, string propertyName)
        where TEnum : struct, Enum
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return ParseEnum<TEnum>(value, propertyName);
    }

    private static string NormalizeRequired(string? value, string message, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ResearchSynthesisValidationException(message);
        }

        var normalized = string.Join(' ', value.Split(null as char[], StringSplitOptions.RemoveEmptyEntries));
        if (normalized.Length > maxLength)
        {
            throw new ResearchSynthesisValidationException($"Research report text exceeds {maxLength} characters.");
        }

        return normalized;
    }
}