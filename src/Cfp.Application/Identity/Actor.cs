namespace Cfp.Application.Identity;

public sealed record AuthenticatedIdentity(
    string Issuer,
    string Subject,
    string? VerifiedEmail = null);

public sealed record Actor(string UserId);

public interface IUserIdentityDirectory
{
    Task<string> GetOrCreateUserIdAsync(
        AuthenticatedIdentity identity,
        CancellationToken cancellationToken);
}

public sealed class ActorResolutionService(IUserIdentityDirectory identityDirectory)
{
    public async Task<Actor> ResolveAsync(
        AuthenticatedIdentity identity,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(identity.Issuer);
        ArgumentException.ThrowIfNullOrWhiteSpace(identity.Subject);
        var userId = await identityDirectory.GetOrCreateUserIdAsync(identity, cancellationToken);
        return new Actor(userId);
    }
}
