namespace Beeexy.Application.Identity;

public interface IIdentityVerificationTransaction : IAsyncDisposable
{
    Task BeginAsync(CancellationToken cancellationToken = default);

    Task SaveChangesAsync(CancellationToken cancellationToken = default);

    Task CommitAsync(CancellationToken cancellationToken = default);
}
