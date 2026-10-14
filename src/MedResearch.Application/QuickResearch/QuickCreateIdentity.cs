using System.Security.Cryptography;
using System.Text;

namespace MedResearch.Application.QuickResearch;

public static class QuickCreateIdentity
{
    // Domain trims outer whitespace; case/internal whitespace/Unicode remain significant.
    public static string Fingerprint(string canonicalQuestion) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("quick-create-v1\n" + canonicalQuestion)));
}
