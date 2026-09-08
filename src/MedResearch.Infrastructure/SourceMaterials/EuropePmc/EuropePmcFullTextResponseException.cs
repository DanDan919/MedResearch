namespace MedResearch.Infrastructure.SourceMaterials.EuropePmc;

public sealed class EuropePmcFullTextResponseException : Exception
{
    public EuropePmcFullTextResponseException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
