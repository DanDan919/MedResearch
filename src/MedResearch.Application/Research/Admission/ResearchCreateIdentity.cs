using System.Security.Cryptography;
using System.Text;

namespace MedResearch.Application.Research.Admission;

public static class ResearchCreateIdentity
{
    public static Guid ParseKey(string? value)
    {
        if (value?.Length != 36 || !Guid.TryParseExact(value, "D", out var key) || key == Guid.Empty)
            throw new ResearchAdmissionException(ResearchAdmissionFailure.InvalidKey);
        return key;
    }

    public static string Fingerprint(string canonicalQuestion) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("research-create-v1\n" + canonicalQuestion)));
}
