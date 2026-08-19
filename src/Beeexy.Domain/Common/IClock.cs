namespace Beeexy.Domain.Common;

public interface IClock
{
    DateTimeOffset UtcNow { get; }
}
