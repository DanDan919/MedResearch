using MedResearch.Domain;

namespace MedResearch.Application.Research.Processing;

public sealed record ClaimedResearchRun(ResearchRun Run, string ResearchQuestion);
