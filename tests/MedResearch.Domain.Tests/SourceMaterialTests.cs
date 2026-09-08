using MedResearch.Domain;

namespace MedResearch.Domain.Tests;

public sealed class SourceMaterialTests
{
    [Fact]
    public void Create_NormalizesContentAndComputesStableHash()
    {
        var material = SourceMaterial.Create(
            Guid.NewGuid(),
            SourceMaterialType.Abstract,
            "PubMed",
            " 12345678 ",
            "SearchMetadataAbstract",
            "  First line\r\nSecond line  ",
            1,
            DateTimeOffset.UtcNow,
            null,
            null,
            null,
            SourceMaterialAccessStatus.Unknown,
            false,
            ["Abstract"]);

        Assert.Equal("First line\nSecond line", material.Content);
        Assert.Equal(SourceMaterial.ComputeContentHash("First line\nSecond line"), material.ContentHash);
        Assert.Equal(1, material.ContentVersion);
        Assert.True(material.IsCurrent);
        Assert.Equal(22, material.CharacterCount);
        Assert.Equal(["Abstract"], material.SectionNames);
    }

    [Fact]
    public void Create_RejectsEmptyContentAndInvalidVersion()
    {
        Assert.Throws<ArgumentException>(() => SourceMaterial.Create(
            Guid.NewGuid(),
            SourceMaterialType.Abstract,
            "PubMed",
            "12345678",
            "SearchMetadataAbstract",
            " ",
            1,
            DateTimeOffset.UtcNow,
            null,
            null,
            null,
            SourceMaterialAccessStatus.Unknown,
            false,
            ["Abstract"]));

        Assert.Throws<ArgumentOutOfRangeException>(() => SourceMaterial.Create(
            Guid.NewGuid(),
            SourceMaterialType.Abstract,
            "PubMed",
            "12345678",
            "SearchMetadataAbstract",
            "Text",
            0,
            DateTimeOffset.UtcNow,
            null,
            null,
            null,
            SourceMaterialAccessStatus.Unknown,
            false,
            ["Abstract"]));
    }

    [Fact]
    public void MarkNotCurrent_PreservesHistoricalVersion()
    {
        var material = SourceMaterial.Create(
            Guid.NewGuid(),
            SourceMaterialType.StructuredFullText,
            "EuropePmc",
            "PMC123456",
            "EuropePmcFullTextXml",
            "Full text section.",
            2,
            DateTimeOffset.UtcNow,
            null,
            "CC BY",
            "https://creativecommons.org/licenses/by/4.0/",
            SourceMaterialAccessStatus.OpenAccess,
            false,
            ["Body"]);

        material.MarkNotCurrent();

        Assert.False(material.IsCurrent);
        Assert.Equal(2, material.ContentVersion);
        Assert.Equal("Full text section.", material.Content);
    }
}
