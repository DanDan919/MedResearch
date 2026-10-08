import { describe, expect, it } from "vitest";
import { z } from "zod";
import type { components } from "../src/generated/medresearch-api";
import * as schemas from "../src/schemas";
import contract from "../openapi/medresearch-api.json";

// Compiler-enforced output alignment. Runtime refinements may deliberately be narrower than the wire schema.
function aligned<T>(schema: z.ZodType<T>) { return schema; }
const responses = {
  CreateResearchResponse: aligned<components["schemas"]["CreateResearchResponse"]>(schemas.createResearchResponseSchema),
  ResearchRunResponse: aligned<components["schemas"]["ResearchRunResponse"]>(schemas.researchRunResponseSchema),
  ResearchRunListResponse: aligned<components["schemas"]["ResearchRunListResponse"]>(schemas.researchRunListResponseSchema),
  ResearchRunProgressResponse: aligned<components["schemas"]["ResearchRunProgressResponse"]>(schemas.researchRunProgressResponseSchema),
  ResearchReportResponse: aligned<components["schemas"]["ResearchReportResponse"]>(schemas.researchReportResponseSchema),
  QuantitativeSynthesisArtifactResponse: aligned<components["schemas"]["QuantitativeSynthesisArtifactResponse"]>(schemas.quantitativeSynthesisArtifactResponseSchema),
  ResearchProvenanceResponse: aligned<components["schemas"]["ResearchProvenanceResponse"]>(schemas.researchProvenanceResponseSchema)
};
describe("backend-derived wire contract and runtime validators", () => {
  for (const [name, schema] of Object.entries(responses)) {
    it(`${name} validates every emitted top-level field`, () => {
      const wire = contract.components.schemas[name as keyof typeof responses];
      const validation = z.toJSONSchema(schema, { unrepresentable: "any", io: "input" });
      expect(Object.keys(validation.properties ?? {}).sort()).toEqual(Object.keys(wire.properties).sort());
      expect([...validation.required ?? []].sort()).toEqual([...wire.required].sort());
    });
  }
  it("every research operation advertises actual Bearer authorization", () => {
    for (const path of Object.values(contract.paths))
      for (const operation of Object.values(path)) expect(operation.security).toEqual([{ Bearer: [] }]);
  });
  it("does not accept null required citation text/source or unknown run lifecycle states", () => {
    expect(schemas.researchReportCitationSchema.shape.resultSummary.safeParse(null).success).toBe(false);
    expect(schemas.researchReportCitationSchema.shape.studySource.safeParse(null).success).toBe(false);
    expect(schemas.researchProvenanceResponseSchema.shape.status.safeParse("InventedState").success).toBe(false);
  });
});
