import { describe, expect, it } from "vitest";
import { defaultApiBaseUrl, normalizeApiBaseUrl } from "../src";

describe("API configuration", () => {
  it("uses the local API default when no value is configured", () => {
    expect(normalizeApiBaseUrl(undefined)).toBe(defaultApiBaseUrl);
    expect(normalizeApiBaseUrl("  ")).toBe(defaultApiBaseUrl);
  });

  it("trims trailing slashes from configured API URLs", () => {
    expect(normalizeApiBaseUrl("http://localhost:8080///")).toBe("http://localhost:8080");
  });
});
