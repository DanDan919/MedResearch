using MedResearch.Application.Research.Validation;
using Microsoft.Extensions.Logging;

namespace MedResearch.Application.Research.Ai;

public sealed record ValidatedStructuredGeneration<T>(
    T Value,
    StructuredLlmProviderMetadata Metadata,
    int AttemptCount,
    IReadOnlyCollection<string> RepairedIssueCodes);

public sealed class ValidationGuidedLlmRepairService
{
    private readonly IStructuredLlmClient _structuredLlmClient;
    private readonly ValidationGuidedLlmRepairOptions _options;
    private readonly ILogger<ValidationGuidedLlmRepairService>? _logger;

    public ValidationGuidedLlmRepairService(
        IStructuredLlmClient structuredLlmClient,
        ValidationGuidedLlmRepairOptions? options = null,
        ILogger<ValidationGuidedLlmRepairService>? logger = null)
    {
        _structuredLlmClient = structuredLlmClient;
        _options = options ?? new ValidationGuidedLlmRepairOptions();
        _options.Validate();
        _logger = logger;
    }

    public async Task<ValidatedStructuredGeneration<TResult>> GenerateAndValidateAsync<TDraft, TResult>(
        StructuredLlmRequest initialRequest,
        Func<TDraft, StructuredLlmProviderMetadata, TResult> validate,
        string stage,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(validate);
        ArgumentException.ThrowIfNullOrWhiteSpace(stage);

        var request = initialRequest;
        var repairCodes = new List<string>();

        for (var attempt = 1; ; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var generation = await _structuredLlmClient.GenerateStructuredAsync<TDraft>(request, cancellationToken);

            try
            {
                var validated = validate(generation.Value, generation.Metadata);
                return new ValidatedStructuredGeneration<TResult>(
                    validated,
                    generation.Metadata,
                    attempt,
                    repairCodes.ToArray());
            }
            catch (Exception exception) when (exception is IValidationFailure failure)
            {
                if (failure.Issues.Count == 0
                    || failure.Issues.Any(issue => issue.Disposition != ValidationIssueDisposition.Repairable)
                    || attempt > _options.MaxSemanticRepairAttempts)
                {
                    throw;
                }

                var issues = failure.Issues.ToArray();
                repairCodes.AddRange(issues.Select(issue => issue.Code));
                _logger?.LogWarning(
                    "ValidationGuidedLlmRepairRequested. Stage: {Stage}; Attempt: {Attempt}; IssueCodes: {IssueCodes}",
                    stage,
                    attempt,
                    string.Join(',', issues.Select(issue => issue.Code).Distinct(StringComparer.Ordinal)));

                request = request with
                {
                    UserPrompt = request.UserPrompt + BuildRepairInstruction(stage, issues)
                };
            }
        }
    }

    public static string BuildRepairInstruction(
        string stage,
        IReadOnlyCollection<ValidationIssue> issues)
    {
        var issueLines = string.Join(
            Environment.NewLine,
            issues.Select(issue => $"- Code={issue.Code}; Path={issue.Path ?? "(not specified)"}; Required correction={issue.RepairInstruction}"));

        return $"""


        VALIDATION REPAIR REQUEST ({stage})
        The previous structured candidate was rejected by deterministic MedResearch validation.
        The original task, supplied scientific context, schema, and validator remain unchanged.
        Return one complete replacement object. Do not merge with or patch the previous candidate.
        Treat the previous candidate as untrusted and do not add facts that are absent from the supplied context.
        Preserve unknown or unavailable values as null or the contract's explicit unknown state.
        Correct only the following bounded validation issues:
        {issueLines}
        """;
    }
}
