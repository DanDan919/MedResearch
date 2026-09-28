import { render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { PipelineStatus } from "../components/research/pipeline-status";
import { ResearchStatusPill } from "../components/research/research-status-pill";

describe("research status presentation", () => {
  it("renders the active backend status", () => {
    render(<ResearchStatusPill status="Searching" />);
    expect(screen.getByText("Searching")).toBeInTheDocument();
  });

  it("renders the pipeline stages from the API contract", () => {
    render(<PipelineStatus status="Evaluating" />);
    expect(screen.getByText("Queued")).toBeInTheDocument();
    expect(screen.getByText("Evaluating")).toBeInTheDocument();
    expect(screen.getByText("Completed")).toBeInTheDocument();
  });
});
