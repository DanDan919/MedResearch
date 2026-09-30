"use client";

import type { QuantitativeResult } from "./quantitative-format";
import { formatNumber, shortId } from "./quantitative-format";

const width = 760;
const left = 220;
const right = 28;
const plotWidth = width - left - right;

export function QuantitativeForestPlot({ result }: { result: QuantitativeResult }) {
  const contributions = result.contributions.filter((item) => Number.isFinite(item.analysisScaleEffect));
  const random = result.randomEffects;
  const values = [
    ...contributions.map((item) => item.analysisScaleEffect),
    result.analysisScaleEffect,
    result.analysisScaleConfidenceIntervalLower,
    result.analysisScaleConfidenceIntervalUpper,
    random?.analysisScaleEffect,
    random?.analysisScaleConfidenceIntervalLower,
    random?.analysisScaleConfidenceIntervalUpper,
    random?.predictionInterval?.analysisScaleLower,
    random?.predictionInterval?.analysisScaleUpper
  ].filter((value): value is number => value !== null && value !== undefined && Number.isFinite(value));

  if (values.length === 0) {
    return <div className="rounded-md border border-dashed border-border p-6 text-sm text-muted-foreground">No finite analysis-scale values are available for a plot.</div>;
  }

  const min = Math.min(...values);
  const max = Math.max(...values);
  const span = max === min ? Math.max(Math.abs(max) * 0.2, 1) : max - min;
  const domainMin = min - span * 0.12;
  const domainMax = max + span * 0.12;
  const rowHeight = 34;
  const plotTop = 32;
  const pooledRows = [result.analysisScaleEffect, random?.analysisScaleEffect].filter((value): value is number => value !== null && value !== undefined && Number.isFinite(value));
  const height = plotTop + (contributions.length + pooledRows.length + 1) * rowHeight + 34;
  const x = (value: number) => left + ((value - domainMin) / (domainMax - domainMin)) * plotWidth;
  const pooledStart = plotTop + contributions.length * rowHeight + 10;

  return (
    <div className="space-y-3">
      <div className="overflow-x-auto rounded-md border border-border bg-background p-2" role="img" aria-label="Quantitative contribution plot">
        <svg className="min-w-[620px]" viewBox={`0 0 ${width} ${height}`} aria-hidden="true">
          <title id="forest-plot-title">Quantitative contribution plot</title>
          <desc id="forest-plot-description">Contribution point estimates and persisted pooled intervals on the analysis scale. Contribution confidence intervals are not persisted and are not calculated here.</desc>
          <line x1={left} x2={width - right} y1={plotTop - 10} y2={plotTop - 10} stroke="currentColor" opacity="0.2" />
          {contributions.map((contribution, index) => {
            const y = plotTop + index * rowHeight + 12;
            return (
              <g key={contribution.evidenceId}>
                <text x={left - 10} y={y + 4} textAnchor="end" className="fill-foreground text-[11px]">Evidence {shortId(contribution.evidenceId)}</text>
                <line x1={left} x2={width - right} y1={y} y2={y} stroke="currentColor" opacity="0.08" />
                <circle cx={x(contribution.analysisScaleEffect)} cy={y} r="5" className="fill-primary" aria-label={`Evidence ${contribution.evidenceId} point estimate ${formatNumber(contribution.analysisScaleEffect)}`} />
              </g>
            );
          })}
          <PooledRow label="Common effect" value={result.analysisScaleEffect} lower={result.analysisScaleConfidenceIntervalLower} upper={result.analysisScaleConfidenceIntervalUpper} y={pooledStart} x={x} />
          {random ? <PooledRow label="Random effects" value={random.analysisScaleEffect} lower={random.analysisScaleConfidenceIntervalLower} upper={random.analysisScaleConfidenceIntervalUpper} y={pooledStart + rowHeight} x={x} /> : null}
          {random?.predictionInterval?.analysisScaleLower !== null && random?.predictionInterval?.analysisScaleLower !== undefined && random?.predictionInterval?.analysisScaleUpper !== null && random?.predictionInterval?.analysisScaleUpper !== undefined ? (
            <line x1={x(random.predictionInterval.analysisScaleLower)} x2={x(random.predictionInterval.analysisScaleUpper)} y1={pooledStart + rowHeight * (random ? 2 : 1)} y2={pooledStart + rowHeight * (random ? 2 : 1)} stroke="currentColor" strokeWidth="3" strokeDasharray="7 5" opacity="0.7" />
          ) : null}
          <line x1={left} x2={width - right} y1={height - 26} y2={height - 26} stroke="currentColor" opacity="0.35" />
          <text x={left} y={height - 8} className="fill-muted-foreground text-[10px]">{formatNumber(domainMin)}</text>
          <text x={width - right} y={height - 8} textAnchor="end" className="fill-muted-foreground text-[10px]">{formatNumber(domainMax)}</text>
        </svg>
      </div>
      <div className="flex flex-wrap gap-x-5 gap-y-2 text-xs text-muted-foreground" aria-label="Plot legend">
        <span><span className="mr-1 inline-block h-2.5 w-2.5 rounded-full bg-primary" />Contribution point estimate</span>
        <span><span className="mr-1 inline-block h-0.5 w-5 align-middle bg-foreground" />Persisted pooled interval</span>
        <span><span className="mr-1 inline-block h-0.5 w-5 align-middle border-t-2 border-dashed border-foreground" />Prediction interval</span>
      </div>
      <p className="text-xs text-muted-foreground">The plot uses persisted analysis-scale values. Study-level confidence intervals and visual weight sizing are unavailable in the artifact and are not inferred.</p>
    </div>
  );
}

function PooledRow({ label, value, lower, upper, y, x }: { label: string; value: number | null; lower: number | null; upper: number | null; y: number; x: (value: number) => number }) {
  const hasInterval = lower !== null && upper !== null && Number.isFinite(lower) && Number.isFinite(upper);
  return (
    <g>
      <text x={left - 10} y={y + 4} textAnchor="end" className="fill-foreground text-[11px] font-medium">{label}</text>
      {hasInterval ? <line x1={x(lower)} x2={x(upper)} y1={y} y2={y} stroke="currentColor" strokeWidth="2" /> : null}
      {value !== null && Number.isFinite(value) ? <polygon points={`${x(value)},${y - 8} ${x(value) + 8},${y} ${x(value)},${y + 8} ${x(value) - 8},${y}`} className="fill-foreground" aria-label={`${label} estimate ${formatNumber(value)}`} /> : null}
    </g>
  );
}
