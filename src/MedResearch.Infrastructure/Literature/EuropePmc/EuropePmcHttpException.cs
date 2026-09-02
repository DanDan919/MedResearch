using System.Net;

namespace MedResearch.Infrastructure.Literature.EuropePmc;

public sealed class EuropePmcHttpException : Exception
{
    public EuropePmcHttpException(HttpStatusCode statusCode, string? diagnosticBody, TimeSpan? retryAfter)
        : base($"Europe PMC request failed with HTTP status {(int)statusCode}.")
    {
        StatusCode = statusCode;
        DiagnosticBody = diagnosticBody;
        RetryAfter = retryAfter;
    }

    public HttpStatusCode StatusCode { get; }

    public string? DiagnosticBody { get; }

    public TimeSpan? RetryAfter { get; }
}