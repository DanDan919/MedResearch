namespace MedResearch.Application.Security;

public interface ICurrentActor
{
    bool IsAuthenticated { get; }

    string? SubjectId { get; }

    string RequireSubjectId();
}
