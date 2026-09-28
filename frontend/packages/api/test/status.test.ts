import { describe, expect, it } from "vitest";
import {
  hasActiveResearchRuns,
  queryKeys,
  researchRunStatusPresentation
} from "../src";

describe("research history status helpers", () => {
  it("reports active histories when any run is non-terminal", () => {
    expect(hasActiveResearchRuns(["Completed", "Searching"])).toBe(true);
    expect(hasActiveResearchRuns(["Completed", "Failed", "Cancelled"])).toBe(false);
  });

  it("keeps stable query keys for default and filtered history pages", () => {
    expect(queryKeys.research.list()).toEqual(["research", "list", 1, 20, "All"]);
    expect(queryKeys.research.list({ page: 2, pageSize: 10, status: "Failed" })).toEqual([
      "research",
      "list",
      2,
      10,
      "Failed"
    ]);
  });

  it("publishes presentation metadata for every terminal state", () => {
    expect(researchRunStatusPresentation.Completed.terminal).toBe(true);
    expect(researchRunStatusPresentation.Failed.tone).toBe("danger");
    expect(researchRunStatusPresentation.Cancelled.tone).toBe("warning");
  });
});
