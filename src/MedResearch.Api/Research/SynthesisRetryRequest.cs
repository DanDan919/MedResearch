using System.Text.Json.Serialization;

namespace MedResearch.Api.Research;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SynthesisRetryRequest;
