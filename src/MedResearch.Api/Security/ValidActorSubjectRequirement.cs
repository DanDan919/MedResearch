using MedResearch.Application.Security;
using Microsoft.AspNetCore.Authorization;

namespace MedResearch.Api.Security;

public sealed class ValidActorSubjectRequirement : IAuthorizationRequirement
{
}

public sealed class ValidActorSubjectHandler : AuthorizationHandler<ValidActorSubjectRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        ValidActorSubjectRequirement requirement)
    {
        var subject = context.User.FindFirst("sub")?.Value;
        if (!string.IsNullOrWhiteSpace(subject))
        {
            try
            {
                _ = ActorIdentity.NormalizeSubject(subject);
                context.Succeed(requirement);
            }
            catch (ArgumentException)
            {
                // An authenticated token without a bounded stable subject is not an application identity.
            }
        }

        return Task.CompletedTask;
    }
}
