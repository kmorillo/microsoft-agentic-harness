import { ResponsiveContainer, LineChart, Line } from 'recharts';
import type { MetricDataPoint } from '@/api/types';

interface SparklineProps {
  dataPoints: MetricDataPoint[];
  color?: string;
  height?: number;
}

/**
 * Inline line chart for trends. Default stroke is `currentColor` so callers
 * can drive the colour from the surrounding text container (e.g. wrap with
 * `<span className="text-cat-tools">` for a tools-tinted sparkline). Pass
 * an explicit `color` value to override.
 */
export function Sparkline({ dataPoints, color = 'currentColor', height = 32 }: SparklineProps) {
  const data = dataPoints.map((dp) => ({
    v: parseFloat(dp.value) || 0,
  }));

  return (
    <ResponsiveContainer width="100%" height={height}>
      <LineChart data={data}>
        <Line type="monotone" dataKey="v" stroke={color} dot={false} strokeWidth={1.5} />
      </LineChart>
    </ResponsiveContainer>
  );
}
