namespace MedResearch.Infrastructure.Literature.EuropePmc;

public sealed class EuropePmcResponseException : Exception
{
    public EuropePmcResponseException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}