namespace MedResearch.Infrastructure.Literature.PubMed;

public sealed class PubMedResponseException : Exception
{
    public PubMedResponseException(string message)
        : base(message)
    {
    }

    public PubMedResponseException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
