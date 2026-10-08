export function isBlocked(report, production) {
  const counts = report?.metadata?.vulnerabilities;
  if (!counts || ["info", "low", "moderate", "high", "critical"].some(key => !Number.isInteger(counts[key]) || counts[key] < 0))
    throw new Error("Audit unavailable or malformed");
  return production && (counts.high > 0 || counts.critical > 0);
}
