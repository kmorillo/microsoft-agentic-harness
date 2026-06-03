import { useState } from 'react';
import { AlertTriangle, X } from 'lucide-react';
import { useAiProviderStatus } from '@/hooks/useAiProviderStatus';

/**
 * App-wide warning banner shown when the active AI provider is not configured. Surfaces the exact
 * settings to supply so a developer can fix it without digging through server logs. Dismissible for
 * the session; re-appears on reload while the provider remains unconfigured.
 */
export function AiProviderBanner() {
  const { data } = useAiProviderStatus();
  const [dismissed, setDismissed] = useState(false);

  if (dismissed || !data || data.configured) return null;

  return (
    <div
      role="alert"
      className="flex items-start gap-2 px-4 py-2 bg-amber-500/10 border-b border-amber-500/40 text-amber-700 dark:text-amber-300 text-xs"
    >
      <AlertTriangle size={14} className="mt-0.5 shrink-0" />
      <div className="flex-1 min-w-0">
        <p>
          AI provider <strong>{data.clientType}</strong> is not configured — agent turns will fail.
        </p>
        {data.missingSettings.length > 0 && (
          <p className="mt-0.5 text-amber-700/80 dark:text-amber-300/80">
            Set{' '}
            {data.missingSettings.map((s, i) => (
              <span key={s}>
                <code className="font-mono">{s}</code>
                {i < data.missingSettings.length - 1 ? ', ' : ''}
              </span>
            ))}{' '}
            via user-secrets, then restart AgentHub.
          </p>
        )}
      </div>
      <button
        onClick={() => { setDismissed(true); }}
        aria-label="Dismiss"
        className="shrink-0 rounded p-0.5 hover:bg-amber-500/20 transition-colors"
      >
        <X size={14} />
      </button>
    </div>
  );
}
