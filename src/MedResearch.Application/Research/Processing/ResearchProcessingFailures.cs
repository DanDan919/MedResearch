namespace MedResearch.Application.Research.Processing;

public static class ResearchProcessingFailures
{
    public const string ReportClaimLimitExceeded = "ReportClaimLimitExceeded";
    public const string ProcessingFailed = "ProcessingFailed";
    public const string ReportClaimLimitMessage = "The report could not be generated because the response exceeded the allowed claim limit. Previously processed sources remain available.";

    public static string? Category(string status, string? reason) => status != "Failed" ? null
        : reason == ReportClaimLimitMessage ? ReportClaimLimitExceeded : ProcessingFailed;
}
