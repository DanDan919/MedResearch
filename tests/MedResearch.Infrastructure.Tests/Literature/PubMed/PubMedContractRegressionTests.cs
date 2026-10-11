using MedResearch.Infrastructure.Literature.PubMed;

namespace MedResearch.Infrastructure.Tests.Literature.PubMed;

public sealed class PubMedContractRegressionTests
{
    [Theory]
    [InlineData("<ERROR>Invalid uid</ERROR>")]
    [InlineData("<eFetchResult><ERROR>Temporary failure</ERROR></eFetchResult>")]
    [InlineData("<html><body>Service unavailable</body></html>")]
    public void Fetch_RejectsSuccessfulHttpErrorPayloadInsteadOfReturningZeroArticles(string xml)
    {
        Assert.Throws<PubMedResponseException>(() => new PubMedArticleMapper().MapArticles(xml));
    }

    [Fact]
    public void Search_RejectsProviderErrorEvenIfIdListExists()
    {
        Assert.Throws<PubMedResponseException>(() => new PubMedSearchResponseParser().Parse(
            """{"esearchresult":{"count":"0","idlist":[],"ERROR":"Invalid query"}}"""));
    }

    [Fact]
    public void Fetch_ReadsPmcidWithoutResolvingExternalDtd()
    {
        var result = Assert.Single(new PubMedArticleMapper().MapArticles("""
            <?xml version="1.0"?>
            <!DOCTYPE PubmedArticleSet SYSTEM "https://invalid.example.test/never-fetch.dtd">
            <PubmedArticleSet><PubmedArticle>
            <MedlineCitation><PMID>12345678</PMID><Article><ArticleTitle>Vaping research.</ArticleTitle></Article></MedlineCitation>
            <PubmedData><ArticleIdList><ArticleId IdType="pmc">pmc1234567</ArticleId></ArticleIdList></PubmedData>
            </PubmedArticle></PubmedArticleSet>
            """));
        Assert.Equal("PMC1234567", result.Pmcid);
    }
}
