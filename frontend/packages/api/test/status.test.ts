import { describe, expect, it } from "vitest";
import { shouldPollResearchStatus, stageState } from "../src";

describe("research status helpers", () => {
  it("polls non-terminal research statuses only", () => {
    expect(shouldPollResearchStatus("Queued")).toBe(true);
    expect(shouldPollResearchStatus("Synthesizing")).toBe(true);
    expect(shouldPollResearchStatus("Completed")).toBe(false);
    expect(shouldPollResearchStatus("Failed")).toBe(false);
    expect(shouldPollResearchStatus("Cancelled")).toBe(false);
  });

  it("derives pipeline stage presentation without inventing hidden progress", () => {
    expect(stageState("Queued", "Searching")).toBe("complete");
    expect(stageState("Searching", "Searching")).toBe("current");
    expect(stageState("Completed", "Searching")).toBe("pending");
    expect(stageState("Planning", "Failed")).toBe("failed");
    expect(stageState("Planning", "Cancelled")).toBe("cancelled");
  });
});
