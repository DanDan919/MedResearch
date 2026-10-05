using MedResearch.Application.Research.Extraction;
using MedResearch.Domain;

namespace MedResearch.Application.Tests;

public sealed class SourceAnchoredNumericGroundingTests
{
    [Fact]
    public void Resolver_RejectsRepeatedIdenticalAnchorsAsAmbiguous()
    {
        var resolver = new SourceAnchorResolver();

        var result = resolver.Resolve(
            Guid.NewGuid(),
            "Primary OR 0.73. Secondary OR 0.73.",
            "OR 0.73");

        Assert.Equal(NumericGroundingStatus.Ambiguous, result.Status);
        Assert.Null(result.Anchor);
    }

    [Fact]
    public void Validator_VerifiesOneCompleteStatisticalContext()
    {
        const string source = "247 participants were randomized. Primary outcome: OR 0.73 (95% CI 0.55–0.96, p = 0.03).";
        var validator = new EvidenceExtractionDraftValidator();
        var context = CreateContext(source);

        var accepted = validator.Validate(
            context,
            new EvidenceExtractionDraft([
                new EvidenceFindingDraft(
                    "Primary outcome",
                    "OR 0.73 (95% CI 0.55–0.96, p = 0.03).",
                    source,
                    "Positive",
                    "participants",
                    null,
                    null,
                    "randomized controlled trial",
                    247,
                    "OR",
                    0.73m,
                    0.55m,
                    0.96m,
                    0.03m,
                    0.95m,
                    null,
                    "=")
            ]));

        var finding = Assert.Single(accepted);
        Assert.Equal(247, finding.SampleSize);
        Assert.Equal(0.73m, finding.EffectValue);
        Assert.Equal(0.55m, finding.ConfidenceIntervalLower);
        Assert.Equal(0.96m, finding.ConfidenceIntervalUpper);
        Assert.Equal(0.03m, finding.PValue);
        Assert.Equal("=", finding.PValueOperator);
        var facts = finding.NumericGrounding ?? [];
        Assert.All(facts, fact => Assert.Equal(NumericGroundingStatus.Verified, fact.Status));
        var anchor = Assert.Single(facts, fact => fact.Field == NumericGroundingField.EffectEstimate).Anchor;
        Assert.NotNull(anchor);
        Assert.Equal(SourceAnchorResolver.NormalizationVersion, anchor.NormalizationVersion);
        Assert.Equal(context.SourceMaterialId, anchor.SourceMaterialId);
        Assert.Equal(anchor.StartOffset + anchor.Text.Length, anchor.EndOffset);
    }

    [Fact]
    public void SourceAnchorIntegrity_RejectsEmptySourceIdentity()
    {
        var anchor = new SourceAnchor(Guid.Empty, SourceAnchorResolver.NormalizationVersion, 0, 3, "900150983cd24fb0d6963f7d28e17f72", "abc");

        Assert.False(SourceAnchorIntegrity.IsValid(anchor));
    }

    [Fact]
    public void Validator_RejectsFrankensteinEffectCiAndPValueAssociation()
    {
        const string source = "247 participants were randomized. Primary outcome: OR 0.73 (95% CI 0.55–0.96). Secondary outcome: OR 1.42 (95% CI 1.10–1.84). Adverse events differed (p = 0.03).";
        var validator = new EvidenceExtractionDraftValidator();

        var accepted = validator.Validate(
            CreateContext(source),
            new EvidenceExtractionDraft([
                new EvidenceFindingDraft(
                    "Secondary outcome",
                    "OR 1.42 with CI 0.55–0.96 and p = 0.03.",
                    source,
                    "Positive",
                    null,
                    null,
                    null,
                    "randomized controlled trial",
                    null,
                    "OR",
                    1.42m,
                    0.55m,
                    0.96m,
                    0.03m,
                    0.95m,
                    null,
                    "=")
            ]));

        var finding = Assert.Single(accepted);
        Assert.Equal(1.42m, finding.EffectValue);
        Assert.Null(finding.ConfidenceIntervalLower);
        Assert.Null(finding.ConfidenceIntervalUpper);
        Assert.Null(finding.PValue);
        Assert.Equal(NumericGroundingStatus.Unsupported, StatusOf(finding, NumericGroundingField.ConfidenceInterval));
        Assert.Equal(NumericGroundingStatus.Unsupported, StatusOf(finding, NumericGroundingField.PValue));
    }

    [Fact]
    public void Validator_RejectsWrongEffectMeasureAndScopedSampleSize()
    {
        const string source = "247 participants were randomized. The intervention arm had 119 participants with RR 0.73 (95% CI 0.55–0.96).";
        var validator = new EvidenceExtractionDraftValidator();

        var accepted = validator.Validate(
            CreateContext(source),
            new EvidenceExtractionDraft([
                new EvidenceFindingDraft(
                    "Primary outcome",
                    "OR 0.73 in the overall population.",
                    source,
                    "Positive",
                    null,
                    null,
                    null,
                    "randomized controlled trial",
                    119,
                    "OR",
                    0.73m,
                    0.55m,
                    0.96m,
                    null,
                    0.95m,
                    null,
                    null)
            ]));

        var finding = Assert.Single(accepted);
        Assert.Null(finding.SampleSize);
        Assert.Null(finding.EffectValue);
        Assert.Equal(NumericGroundingStatus.Unsupported, StatusOf(finding, NumericGroundingField.SampleSize));
        Assert.Equal(NumericGroundingStatus.Unsupported, StatusOf(finding, NumericGroundingField.EffectMeasure));
        Assert.Equal(NumericGroundingStatus.Unsupported, StatusOf(finding, NumericGroundingField.EffectEstimate));
    }

    [Fact]
    public void Validator_PreservesStrictPValueOperatorSemantics()
    {
        const string source = "The odds ratio was 0.73 (95% CI 0.55 to 0.96, p < 0.03).";
        var accepted = new EvidenceExtractionDraftValidator().Validate(
            CreateContext(source),
            new EvidenceExtractionDraft([
                new EvidenceFindingDraft(
                    "odds ratio",
                    "The odds ratio was 0.73.",
                    source,
                    "Positive",
                    null,
                    null,
                    null,
                    null,
                    null,
                    "odds ratio",
                    0.73m,
                    0.55m,
                    0.96m,
                    0.03m,
                    0.95m,
                    null,
                    "<")
            ]));

        var finding = Assert.Single(accepted);
        Assert.Equal(NumericGroundingStatus.Verified, StatusOf(finding, NumericGroundingField.PValue));
        Assert.Equal("<", finding.PValueOperator);
    }

    [Fact]
    public void Validator_DoesNotTrustConflictingPValueOperatorFromLlm()
    {
        const string source = "The odds ratio was 0.73 (95% CI 0.55 to 0.96, p < 0.03).";
        var accepted = new EvidenceExtractionDraftValidator().Validate(
            CreateContext(source),
            new EvidenceExtractionDraft([
                new EvidenceFindingDraft(
                    "odds ratio",
                    "The odds ratio was 0.73.",
                    source,
                    "Positive",
                    null,
                    null,
                    null,
                    null,
                    null,
                    "odds ratio",
                    0.73m,
                    0.55m,
                    0.96m,
                    0.03m,
                    0.95m,
                    null,
                    "=" )
            ]));

        var finding = Assert.Single(accepted);
        Assert.Null(finding.PValueOperator);
        Assert.Equal(NumericGroundingStatus.Unsupported, StatusOf(finding, NumericGroundingField.PValue));
        Assert.Null(finding.PValue);
    }

    private static NumericGroundingStatus StatusOf(AcceptedEvidenceFinding finding, NumericGroundingField field)
    {
        return Assert.Single(finding.NumericGrounding ?? [], fact => fact.Field == field).Status;
    }

    private static EvidenceExtractionStudyContext CreateContext(string source)
    {
        var sourceMaterialId = Guid.NewGuid();
        return new EvidenceExtractionStudyContext(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "Does treatment affect the primary outcome?",
            null,
            Guid.NewGuid(),
            sourceMaterialId,
            EvidenceSourceScope.Abstract,
            "Fixture",
            source,
            SourceMaterial.ComputeContentHash(source),
            false,
            ["Abstract"],
            "Fixture study",
            source,
            "12345678",
            null,
            "10.1000/example",
            "Journal",
            new DateOnly(2026, 1, 1),
            ["Journal Article"],
            ["Author"],
            "Fixture");
    }
}
