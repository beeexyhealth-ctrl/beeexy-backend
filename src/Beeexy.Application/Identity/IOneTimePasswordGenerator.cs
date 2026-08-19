namespace Beeexy.Application.Identity;

public interface IOneTimePasswordGenerator
{
    string Generate(int length);
}
