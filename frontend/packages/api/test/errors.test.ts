import { describe, expect, it } from "vitest";
import { mapStatusToKind } from "../src";

describe("API error mapping", () => {
  it("distinguishes meaningful backend status codes", () => {
    expect(mapStatusToKind(400)).toBe("validation");
    expect(mapStatusToKind(404)).toBe("not-found");
    expect(mapStatusToKind(409)).toBe("conflict");
    expect(mapStatusToKind(500)).toBe("server");
    expect(mapStatusToKind(418)).toBe("unexpected");
  });
});
