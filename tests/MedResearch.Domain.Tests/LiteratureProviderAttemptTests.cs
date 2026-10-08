using MedResearch.Domain;

namespace MedResearch.Domain.Tests;

public sealed class LiteratureProviderAttemptTests
{
    [Theory]
    [InlineData(0, LiteratureProviderAttemptStatus.SucceededZeroResults)]
    [InlineData(2, LiteratureProviderAttemptStatus.SucceededWithResults)]
    public void SuccessfulOutcomeIsImmutable(int count, LiteratureProviderAttemptStatus status)
    {
        var attempt = Create();
        attempt.Succeed(Guid.NewGuid(), count, attempt.StartedAt.AddSeconds(1));
        Assert.Equal(status, attempt.Status);
        Assert.Equal(count, attempt.ResultCount);
        Assert.Null(attempt.FailureCategory);
        Assert.Throws<InvalidOperationException>(() => attempt.Fail(LiteratureProviderFailureCategory.NetworkFailure, attempt.StartedAt.AddSeconds(2)));
    }

    [Theory]
    [InlineData(LiteratureProviderFailureCategory.Timeout, LiteratureProviderAttemptStatus.TimedOut)]
    [InlineData(LiteratureProviderFailureCategory.Cancelled, LiteratureProviderAttemptStatus.Cancelled)]
    [InlineData(LiteratureProviderFailureCategory.InvalidResponse, LiteratureProviderAttemptStatus.Failed)]
    public void FailureDoesNotInventScientificCount(LiteratureProviderFailureCategory category, LiteratureProviderAttemptStatus status)
    {
        var attempt = Create();
        attempt.Fail(category, attempt.StartedAt.AddSeconds(1));
        Assert.Equal(status, attempt.Status);
        Assert.Null(attempt.ResultCount);
        Assert.Null(attempt.LiteratureSearchId);
        Assert.Throws<InvalidOperationException>(() => attempt.Succeed(Guid.NewGuid(), 0, attempt.StartedAt.AddSeconds(2)));
    }

    [Fact]
    public void InvalidOutcomesAreRejected()
    {
        var attempt = Create();
        Assert.Throws<ArgumentOutOfRangeException>(() => attempt.Fail((LiteratureProviderFailureCategory)100, attempt.StartedAt));
        Assert.Throws<ArgumentException>(() => attempt.Succeed(Guid.NewGuid(), -1, attempt.StartedAt));
        Assert.Throws<ArgumentException>(() => attempt.Succeed(Guid.NewGuid(), 0, attempt.StartedAt.AddSeconds(-1)));
        Assert.Equal(LiteratureProviderAttemptStatus.Started, attempt.Status);
    }

    private static LiteratureProviderAttempt Create() => new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "PubMed", "query", DateTimeOffset.UtcNow);

    [Theory]
    [InlineData(1998, "1998-05-06", "1998-05-06")]
    [InlineData(1998, "1999-05-06", null)]
    public void DateEnrichmentCannotCombineConflictingPublicationParts(int year, string incoming, string? expected)
    {
        var study = new Study(Guid.NewGuid(), "title", null, null, "123", null, null, null, year, null, null, [], [], "PubMed");
        var date = DateOnly.Parse(incoming);
        study.EnrichMissingMetadata(null, null, "123", null, null, date, date.Year, date.Month, date.Day, [], []);
        Assert.Equal(year, study.PublicationYear);
        Assert.Equal(expected is null ? null : (DateOnly?)DateOnly.Parse(expected), study.PublicationDate);
    }
}
