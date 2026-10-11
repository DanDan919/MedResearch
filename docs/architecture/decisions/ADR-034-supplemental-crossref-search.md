# ADR-034: Supplemental Crossref Ranked Retrieval

Status: Accepted
Date: 2026-10-11

## Context

The operator requested working PubMed and broader scientific retrieval. Actual
local configuration disabled PubMed and capped Europe PMC at three results. A
bounded public check returned ESearch and batched EFetch HTTP 200: the disabled
source was not an NCBI outage. Five negative tests independently exposed ESearch
ERROR acceptance, wrong/error XML accepted as zero, and dropped PubMed PMCID.

Crossref supplies publisher-deposited DOI metadata and some abstracts through a
public REST API without credentials. It adds coverage beyond the two biomedical
indexes but cannot reproduce PubMed Boolean/MeSH semantics or guarantee relevance.

First-party references checked:

- [NCBI parameters](https://www.ncbi.nlm.nih.gov/entrez/query/static/esearch_help.html):
  official eutils host, PubMed db, JSON ESearch, XML EFetch, encoded term/id,
  tool/email and optional api_key. [NCBI usage](https://eutilities.github.io/site/API_Key/usageandkey/)
  describes 3 requests/second without key, 10 by default with key, batching/history.
- [Crossref REST](https://www.crossref.org/documentation/retrieve-metadata/rest-api/):
  public works metadata, abstracts may retain publisher copyright.
- [Access](https://www.crossref.org/documentation/retrieve-metadata/rest-api/access-and-authentication/):
  no public credentials; optional real mailto for polite pool; quotas in headers.
- [Query/pagination guidance](https://www.crossref.org/documentation/retrieve-metadata/rest-api/tips-for-using-the-crossref-rest-api/):
  works/filter/query, rows, cursor start `*`, next-cursor and short-page stop.
- [Official Swagger](https://api.crossref.org/swagger-ui/index.html) plus a small
  live `/works?query=...&filter=type:journal-article,has-abstract:true&rows=3`
  request verified implemented parameters/shape. The old Crossref GitHub REST
  README is marked deprecated; it is not used as the current rate authority.
- [Crossref cursor changes](https://community.crossref.org/t/changes-to-cursors-filtering-and-sorting-in-the-rest-api/16246):
  the August 2026 upgrade requires all original parameters on every page,
  updates next-cursor on every page and does not guarantee a stable result set.

Actual public list-response headers were rate 1/1s, concurrency 1. Older general
documentation/forum rates are not assumed to override actual per-route headers.

## Decision

Reuse the existing source port, coordinator, attempts and fenced scientific
store. Add one Infrastructure adapter with default 20 inspected records/query,
page size 20, hard bounds 200/100, 2MB body cap and independent 15s body deadline.
Filter to journal articles with deposited abstracts. No publisher/PDF crawling,
subscription, fabricated identifiers or automatic title-based merge.

Crossref query preparation deliberately expands supported positive terms to a
ranked lexical query. Remove supported field/group/Boolean/quote syntax and
terminal wildcard marker, not negative constraints: NOT/negative/unsupported
fields are rejected. Actual expanded query is stored; original remains in plan.
This is recall expansion, not eligibility validation or identical query semantics.

Every page explicitly sends `sort=score&order=desc`, preserving query/filter
and updating next-cursor. In a real three-record A/B query, cursor retrieval
without sort returned unrelated low-score FEBS articles; explicit score sorting
returned vaping articles. Two deterministic request-contract regressions failed
before this correction. Score order is not biomedical eligibility validation;
changing cursor result sets can still produce duplicate or missing records.

One singleton BCL token bucket (1 request/second, burst one) and semaphore (one
in flight) cover headers and body reading across client instances. Limits are
process-local, not a distributed IP quota. Fixed conservative pacing avoids
assuming higher polite-pool access merely because contact was configured.

Retry only 429, 500/502/503/504, network/IO and timeout, default two replacements,
with bounded exponential jitter. Honor Retry-After <=30s; a longer cooldown
fails, never retries early. Invalid JSON, overlarge bodies, permanent HTTP and
local query/config errors do not retry. Host cancellation remains cancellation.
Discard error bodies and transport exception text; no automatic HttpClient URL
logging. Redirects are rejected. Contact is configured, never fabricated.

Canonical identity/enrichment and per-search discovery paths are unchanged. DOI
must have a usable normalized numeric-prefix/suffix shape within the current
column bound; no PMID/PMCID is inferred. Partial publication dates stay partial;
index/deposit dates are ignored. Preserve source-reported abstract markup for
immutable snapshots and exact-quote grounding.

Normal solution/CI use deterministic fake HTTP. A separate opt-in three-adapter
live smoke does four requests for <=3 articles/source, no database writes/LLM.
It is a current external contract observation, not an availability guarantee.

## Consequences

Three paths can resolve to one Study and one extraction/synthesis work item.
More records are bounded independently from downstream LLM work. Search default
breadth rises to 20 per source/query; processing defaults remain 10 Studies/run.
No schema/migration is added; old search/source/evidence snapshots stay intact.

Crossref coverage excludes records without deposited abstracts. Journal-article
type is not a study-design/peer-review/quality/relevance guarantee. Crossref
ranking, date metadata, DOI conflicts and missing source information remain
untrusted. No universal relevance gate, distributed quota or cost budget is
claimed. Other providers are deferred until a concrete coverage need is verified.
