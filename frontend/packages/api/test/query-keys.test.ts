import { describe, expect, it } from "vitest";
import { queryKeys } from "../src/query-keys";

describe("quantitative query keys", () => {
  it("isolates quantitative artifacts by research run", () => {
    expect(queryKeys.research.quantitative("11111111-1111-4111-8111-111111111111")).not.toEqual(
      queryKeys.research.quantitative("22222222-2222-4222-8222-222222222222")
    );
    expect(queryKeys.research.quantitative("11111111-1111-4111-8111-111111111111")).toEqual([
      "research",
      "11111111-1111-4111-8111-111111111111",
      "quantitative"
    ]);
  });
});
