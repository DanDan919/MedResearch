using MedResearch.Domain;
using MedResearch.Application.Research.Validation;

namespace MedResearch.Application.Research.Synthesis;

public sealed class ResearchReportDraftValidator
{
    private const int MaxClaimTextLength = 4_000;
    private const int MaxEvidencePerClaim = 12;

    private readonly SynthesisOptions _options;

    public ResearchReportDraftValidator(SynthesisOptions options)
    {
        _options = options;
    }

    public ResearchSynthesisResult Validate(
        SynthesisContext context,
        ResearchReportDraft draft,
        string? provider,
        string? model,
        DateTimeOffset generatedAt)
    {
        ValidateContext(context);

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

        if (reason == ResearchReportInsufficientEvidenceReason.NoValidatedEvidence && context.Statistics.IncludedEvidenceFindingCount != 0)
            throw new ResearchSynthesisValidationException("NoValidatedEvidence requires an empty validated context.");

        var claims = ValidateClaims(context, draft.Claims, status);
        // No model-authored prose is promoted into another authoritative report field.
        var executiveSummary = status == ResearchReportStatus.InsufficientEvidence
            ? "Validated Evidence is insufficient for an effect conclusion; absence of evidence is not no effect."
            : $"This bounded evidence synthesis contains {claims.Count} structured claims about the cited Evidence.";
        var evidenceSummary = $"The bounded synthesis context contains {context.Statistics.IncludedEvidenceFindingCount} validated findings across {context.Statistics.IncludedStudyCount} studies.";
        var conflictSummary = claims.Any(x => x.Semantics?.Kind == ResearchClaimKind.MixedEvidence)
            ? "Cited Evidence includes differing reported directions. Mixed claims do not establish a uniform effect."
            : "No mixed claim was proposed for the cited subsets; this does not prove corpus-wide consistency.";
        var limitationsSummary = "Claims describe the supplied, bounded Evidence, not causal effects, clinical significance, or clinical recommendations. See the persisted source coverage and deterministic limitations.";
        var conclusion = status == ResearchReportStatus.InsufficientEvidence
            ? executiveSummary
            : "The scoped structured claims below are the scientific assertions. No broader effect conclusion is inferred.";

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
        return Validate(context, new ResearchReportDraft(
            nameof(ResearchReportStatus.InsufficientEvidence), nameof(ResearchReportInsufficientEvidenceReason.NoValidatedEvidence),
            null, null, null, null, null, nameof(SynthesisConfidence.InsufficientEvidence), []), null, null, DateTimeOffset.UtcNow);
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
        var semanticKeys = new HashSet<string>(StringComparer.Ordinal);

        for (var index = 0; index < drafts.Length; index++)
        {
            var draft = drafts[index];
            if (!string.IsNullOrWhiteSpace(draft.Pmid) || !string.IsNullOrWhiteSpace(draft.Doi) || !string.IsNullOrWhiteSpace(draft.StudyId))
            {
                throw new ResearchSynthesisValidationException(
                    "Model-supplied PMID, DOI, or StudyId values are not accepted as report citation authority.",
                    new ValidationIssue(
                        ValidationIssueCodes.ModelSuppliedCitationMetadata,
                        $"claims[{index}]",
                        "Remove model-supplied PMID, DOI, and StudyId values; cite only authoritative EvidenceId values from the supplied context.",
                        ValidationIssueDisposition.Repairable));
            }

            var type = ParseEnum<ResearchReportClaimType>(draft.Type, nameof(draft.Type));
            var direction = ParseEnum<ResearchReportClaimDirection>(draft.Direction, nameof(draft.Direction));
            var evidenceIds = ParseEvidenceIds(draft.EvidenceIds);

            if (evidenceIds.Length == 0 && draft.Kind != nameof(ResearchClaimKind.InsufficientEvidence))
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
                    throw new ResearchSynthesisValidationException(
                        "Report claim references evidence outside the supplied synthesis context.",
                        new ValidationIssue(
                            ValidationIssueCodes.UnknownEvidenceReference,
                            $"claims[{index}].evidenceIds",
                            "Cite only EvidenceId values present in the supplied synthesis context.",
                            ValidationIssueDisposition.Repairable));
                }

                return evidence;
            }).ToArray();

            var semantics = StructuredResearchClaimValidator.Validate(context, draft, direction, evidenceIds, status, index);
            if (type == ResearchReportClaimType.Conflict && semantics.Kind != ResearchClaimKind.MixedEvidence)
                throw new ResearchSynthesisValidationException("A conflict role requires MixedEvidence semantics.");
            if (!semanticKeys.Add(StructuredResearchClaimRenderer.SemanticKey(semantics)))
                throw new ResearchSynthesisValidationException("Duplicate structured claim semantics are not accepted.");
            var text = NormalizeRequired(StructuredResearchClaimRenderer.Render(semantics), "Rendered claim is required.", MaxClaimTextLength);
            claims.Add(new AcceptedResearchReportClaim(type, direction, text, evidenceIds, index, semantics));
        }

        if (status == ResearchReportStatus.Completed && claims.All(claim => claim.ClaimType != ResearchReportClaimType.Conclusion))
        {
            throw new ResearchSynthesisValidationException("Completed research reports require a conclusion claim with Evidence references.");
        }

        return claims;
    }

    public void ValidateContext(SynthesisContext context)
    {
        if ((context.QuantitativeArtifacts ?? []).Any(artifact => artifact.ArtifactId == Guid.Empty || artifact.Result.ResearchRunId != context.ResearchRunId ||
            artifact.SnapshotFingerprint != MedResearch.Application.Research.Quantitative.QuantitativeSynthesisArtifactSnapshot.ComputeFingerprint(artifact.Result)))
            throw new ResearchSynthesisValidationException("Synthesis context contains a foreign or corrupt quantitative artifact.",
                new ValidationIssue(ValidationIssueCodes.QuantitativeArtifactMismatch, "context.quantitativeArtifacts", "Current-run persisted artifact identity and fingerprint must be valid.", ValidationIssueDisposition.NonRepairable));
        if (context.Statistics.IncludedEvidenceFindingCount != context.Studies.Sum(x => x.Evidence.Count) ||
            context.Studies.SelectMany(x => x.Evidence).Select(x => x.EvidenceId).Distinct().Count() != context.Statistics.IncludedEvidenceFindingCount)
            throw new ResearchSynthesisValidationException("Synthesis context evidence count/identity is inconsistent.");
        if (context.Studies.SelectMany(study => study.Evidence).Any(evidence => evidence.ResearchRunId != context.ResearchRunId))
        {
            throw new ResearchSynthesisValidationException(
                "Synthesis context contains evidence from another research run.",
                new ValidationIssue(
                    ValidationIssueCodes.CrossRunEvidenceReference,
                    "context.studies[].evidence[].researchRunId",
                    "The supplied synthesis context is cross-run and cannot be repaired by the model.",
                    ValidationIssueDisposition.NonRepairable));
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

    private static TEnum ParseEnum<TEnum>(string? value, string propertyName)
        where TEnum : struct, Enum
    {
        if (string.IsNullOrWhiteSpace(value) || !Enum.TryParse<TEnum>(value.Trim(), ignoreCase: false, out var parsed) || !Enum.IsDefined(parsed) || Enum.GetName(parsed) != value.Trim())
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
