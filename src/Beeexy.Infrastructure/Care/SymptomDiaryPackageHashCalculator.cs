using System.Security.Cryptography;
using System.Text;
using Beeexy.Application.Care;
using Beeexy.Domain.Care;

namespace Beeexy.Infrastructure.Care;

public sealed class SymptomDiaryPackageHashCalculator(
    SymptomDiaryPackageCanonicalSerializer serializer)
{
    public SymptomDiarySha256 Calculate(SymptomDiaryPackageDefinition package)
    {
        var canonical = serializer.Serialize(package);
        return SymptomDiarySha256.FromHash(
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)))
                .ToLowerInvariant());
    }
}
