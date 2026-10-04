using System.Security.Claims;
using MedResearch.Application.Security;

namespace MedResearch.Api.Security;

public sealed class HttpCurrentActor : ICurrentActor
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public HttpCurrentActor(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public bool IsAuthenticated => SubjectId is not null;

    public string? SubjectId
    {
        get
        {
            var principal = _httpContextAccessor.HttpContext?.User;
            if (principal?.Identity?.IsAuthenticated != true)
            {
                return null;
            }

            var subject = principal.FindFirstValue("sub");
            if (string.IsNullOrWhiteSpace(subject))
            {
                return null;
            }

            try
            {
                return ActorIdentity.NormalizeSubject(subject);
            }
            catch (ArgumentException)
            {
                return null;
            }
        }
    }

    public string RequireSubjectId()
    {
        return SubjectId
            ?? throw new InvalidOperationException("An authenticated actor with a valid subject claim is required.");
    }
}
