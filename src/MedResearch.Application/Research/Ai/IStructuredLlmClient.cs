namespace MedResearch.Application.Research.Ai;

public interface IStructuredLlmClient
{
    Task<StructuredGenerationResult<T>> GenerateStructuredAsync<T>(
        StructuredLlmRequest request,
        CancellationToken cancellationToken);
}
