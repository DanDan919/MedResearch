using System.Security.Cryptography;
using System.Text;

namespace MedResearch.Domain;

public sealed class SourceMaterial
{
    public SourceMaterial(
        Guid id,
        Guid studyId,
        SourceMaterialType type,
        string provider,
        string? providerSourceId,
        string retrievalMethod,
        string content,
        string contentHash,
        int contentVersion,
        DateTimeOffset retrievedAt,
        DateTimeOffset? sourceUpdatedAt,
        string? license,
        string? licenseUrl,
        SourceMaterialAccessStatus accessStatus,
        int characterCount,
        bool wasTruncated,
        bool isCurrent,
        string[]? sectionNames)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Source material id cannot be empty.", nameof(id));
        }

        if (studyId == Guid.Empty)
        {
            throw new ArgumentException("Study id cannot be empty.", nameof(studyId));
        }

        if (string.IsNullOrWhiteSpace(provider))
        {
            throw new ArgumentException("Source material provider is required.", nameof(provider));
        }

        if (string.IsNullOrWhiteSpace(retrievalMethod))
        {
            throw new ArgumentException("Source material retrieval method is required.", nameof(retrievalMethod));
        }

        if (string.IsNullOrWhiteSpace(content))
        {
            throw new ArgumentException("Available source material content is required.", nameof(content));
        }

        if (string.IsNullOrWhiteSpace(contentHash))
        {
            throw new ArgumentException("Source material content hash is required.", nameof(contentHash));
        }

        if (contentVersion <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(contentVersion), "Source material version must be positive.");
        }

        if (characterCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(characterCount), "Source material character count must be positive.");
        }

        Id = id;
        StudyId = studyId;
        Type = type;
        Provider = NormalizeRequired(provider, nameof(provider));
        ProviderSourceId = NormalizeOptional(providerSourceId);
        RetrievalMethod = NormalizeRequired(retrievalMethod, nameof(retrievalMethod));
        Content = NormalizeContent(content);
        ContentHash = NormalizeRequired(contentHash, nameof(contentHash));
        ContentVersion = contentVersion;
        RetrievedAt = retrievedAt;
        SourceUpdatedAt = sourceUpdatedAt;
        License = NormalizeOptional(license);
        LicenseUrl = NormalizeOptional(licenseUrl);
        AccessStatus = accessStatus;
        CharacterCount = characterCount;
        WasTruncated = wasTruncated;
        IsCurrent = isCurrent;
        SectionNames = NormalizeCollection(sectionNames);
    }

    public Guid Id { get; }

    public Guid StudyId { get; }

    public SourceMaterialType Type { get; }

    public string Provider { get; }

    public string? ProviderSourceId { get; }

    public string RetrievalMethod { get; }

    public string Content { get; }

    public string ContentHash { get; }

    public int ContentVersion { get; }

    public DateTimeOffset RetrievedAt { get; }

    public DateTimeOffset? SourceUpdatedAt { get; }

    public string? License { get; }

    public string? LicenseUrl { get; }

    public SourceMaterialAccessStatus AccessStatus { get; }

    public int CharacterCount { get; }

    public bool WasTruncated { get; }

    public bool IsCurrent { get; private set; }

    public string[] SectionNames { get; }

    public static SourceMaterial Create(
        Guid studyId,
        SourceMaterialType type,
        string provider,
        string? providerSourceId,
        string retrievalMethod,
        string content,
        int contentVersion,
        DateTimeOffset retrievedAt,
        DateTimeOffset? sourceUpdatedAt,
        string? license,
        string? licenseUrl,
        SourceMaterialAccessStatus accessStatus,
        bool wasTruncated,
        string[]? sectionNames)
    {
        var normalizedContent = NormalizeContent(content);
        return new SourceMaterial(
            Guid.NewGuid(),
            studyId,
            type,
            provider,
            providerSourceId,
            retrievalMethod,
            normalizedContent,
            ComputeContentHash(normalizedContent),
            contentVersion,
            retrievedAt,
            sourceUpdatedAt,
            license,
            licenseUrl,
            accessStatus,
            normalizedContent.Length,
            wasTruncated,
            true,
            sectionNames);
    }

    public void MarkNotCurrent()
    {
        IsCurrent = false;
    }

    public static string ComputeContentHash(string content)
    {
        var normalizedContent = NormalizeContent(content);
        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(normalizedContent));
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }

    public static string NormalizeContent(string content)
    {
        return content.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Trim();
    }

    private static string NormalizeRequired(string value, string parameterName)
    {
        return NormalizeOptional(value) ?? throw new ArgumentException("Value is required.", parameterName);
    }

    private static string? NormalizeOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? null
            : string.Join(' ', value.Split(null as char[], StringSplitOptions.RemoveEmptyEntries));
    }

    private static string[] NormalizeCollection(string[]? values)
    {
        if (values is null)
        {
            return [];
        }

        return values
            .Select(value => NormalizeOptional(value))
            .Where(value => value is not null)
            .Select(value => value!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}
