import { render, screen, fireEvent } from '@testing-library/react';
import { vi, describe, it, expect, beforeEach } from 'vitest';
import { AiProviderBanner } from '../AiProviderBanner';
import type { AiProviderStatus } from '@/hooks/useAiProviderStatus';

const mockUse = vi.fn();
vi.mock('@/hooks/useAiProviderStatus', () => ({
  useAiProviderStatus: () => mockUse() as { data: AiProviderStatus | undefined },
}));

function withData(data: AiProviderStatus | undefined): void {
  mockUse.mockReturnValue({ data });
}

describe('AiProviderBanner', () => {
  beforeEach(() => { vi.clearAllMocks(); });

  it('renders nothing while the status is still loading', () => {
    withData(undefined);
    const { container } = render(<AiProviderBanner />);
    expect(container).toBeEmptyDOMElement();
  });

  it('renders nothing when the provider is configured', () => {
    withData({ configured: true, clientType: 'Anthropic', defaultDeployment: 'claude', missingSettings: [] });
    const { container } = render(<AiProviderBanner />);
    expect(container).toBeEmptyDOMElement();
  });

  it('warns with the missing settings when the provider is not configured', () => {
    withData({
      configured: false,
      clientType: 'Anthropic',
      defaultDeployment: 'claude-sonnet-4-6',
      missingSettings: ['AppConfig:AI:AgentFramework:Endpoint', 'AppConfig:AI:AgentFramework:ApiKey'],
    });

    render(<AiProviderBanner />);

    expect(screen.getByRole('alert')).toBeInTheDocument();
    expect(screen.getByText(/not configured/i)).toBeInTheDocument();
    expect(screen.getByText('AppConfig:AI:AgentFramework:Endpoint')).toBeInTheDocument();
    expect(screen.getByText('AppConfig:AI:AgentFramework:ApiKey')).toBeInTheDocument();
  });

  it('can be dismissed for the session', () => {
    withData({ configured: false, clientType: 'Anthropic', defaultDeployment: 'claude', missingSettings: [] });

    render(<AiProviderBanner />);
    expect(screen.getByRole('alert')).toBeInTheDocument();

    fireEvent.click(screen.getByLabelText('Dismiss'));

    expect(screen.queryByRole('alert')).toBeNull();
  });
});
