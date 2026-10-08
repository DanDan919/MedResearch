import { test } from "node:test";
import assert from "node:assert/strict";
import { isBlocked } from "./security-audit-policy.mjs";
const report = (high = 0, critical = 0) => ({ metadata: { vulnerabilities: { info: 0, low: 0, moderate: 0, high, critical } } });
test("production high blocks without suppression", () => assert.equal(isBlocked(report(1), true), true));
test("production critical blocks", () => assert.equal(isBlocked(report(0, 1), true), true));
test("clean production passes", () => assert.equal(isBlocked(report(), true), false));
test("dev advisory remains reported but is not misclassified as production", () => assert.equal(isBlocked(report(1), false), false));
test("missing scanner report fails closed", () => assert.throws(() => isBlocked({}, true)));
