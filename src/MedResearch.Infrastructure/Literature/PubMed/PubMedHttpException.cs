using System.Net;

namespace MedResearch.Infrastructure.Literature.PubMed;

public sealed class PubMedHttpException : Exception
{
    public PubMedHttpException(HttpStatusCode statusCode, string? diagnosticBody, TimeSpan? retryAfter)
        : base($"PubMed returned HTTP {(int)statusCode}.")
    {
        StatusCode = statusCode;
        DiagnosticBody = diagnosticBody;
        RetryAfter = retryAfter;
    }

    public HttpStatusCode StatusCode { get; }

    public string? DiagnosticBody { get; }

    public TimeSpan? RetryAfter { get; }
}