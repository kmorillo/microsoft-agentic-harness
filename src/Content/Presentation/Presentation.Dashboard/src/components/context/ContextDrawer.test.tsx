import { describe, it, expect, vi } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
import { ContextDrawer } from './ContextDrawer';

const body = ['# Heading', 'plain line', '"key": "value"'].join('\n');

describe('ContextDrawer', () => {
  it('renders nothing when open is false', () => {
    render(
      <ContextDrawer
        open={false}
        onOpenChange={() => {}}
        category="skills"
        name="rules/testing"
        path="/rules/testing.md"
        body={body}
      />,
    );
    expect(screen.queryByTestId('context-drawer')).not.toBeInTheDocument();
  });

  it('renders header, body, and line numbers when open', () => {
    render(
      <ContextDrawer
        open
        onOpenChange={() => {}}
        category="skills"
        name="rules/testing"
        path="/rules/testing.md"
        body={body}
      />,
    );
    expect(screen.getByTestId('context-drawer')).toBeInTheDocument();
    expect(screen.getByText('rules/testing')).toBeInTheDocument();
    expect(screen.getByText('/rules/testing.md')).toBeInTheDocument();
    expect(screen.getAllByTestId('context-drawer-line-number')).toHaveLength(3);
  });

  it('exposes the language as a data attribute', () => {
    render(
      <ContextDrawer
        open
        onOpenChange={() => {}}
        category="tools"
        name="schema"
        path="/schema.json"
        body={body}
        lang="json"
      />,
    );
    expect(screen.getByTestId('context-drawer-body')).toHaveAttribute('data-lang', 'json');
  });

  it('renders the role banner when role is set', () => {
    render(
      <ContextDrawer
        open
        onOpenChange={() => {}}
        category="messages"
        name="t-01"
        path="conversation/t-01"
        body="hello"
        role="user"
      />,
    );
    const banner = screen.getByTestId('context-drawer-role-banner');
    expect(banner).toBeInTheDocument();
    expect(banner).toHaveAttribute('data-role', 'user');
    expect(banner).toHaveTextContent(/User message/i);
  });

  it('omits the role banner when role is not provided', () => {
    render(
      <ContextDrawer
        open
        onOpenChange={() => {}}
        category="agents"
        name="agents.md"
        path="/.claude/agents.md"
        body="x"
      />,
    );
    expect(screen.queryByTestId('context-drawer-role-banner')).not.toBeInTheDocument();
  });

  it('calls onOpenChange(false) when the close button is clicked', () => {
    const onOpenChange = vi.fn();
    render(
      <ContextDrawer
        open
        onOpenChange={onOpenChange}
        category="system"
        name="prompt"
        path="harness/system.md"
        body="x"
      />,
    );
    fireEvent.click(screen.getByTestId('context-drawer-close'));
    expect(onOpenChange).toHaveBeenCalledWith(false);
  });

  it('fires onNext when ArrowRight is pressed inside the drawer', () => {
    const onNext = vi.fn();
    render(
      <ContextDrawer
        open
        onOpenChange={() => {}}
        category="skills"
        name="a"
        path="/a"
        body="x"
        onNext={onNext}
      />,
    );
    fireEvent.keyDown(screen.getByTestId('context-drawer'), { key: 'ArrowRight' });
    expect(onNext).toHaveBeenCalledTimes(1);
  });

  it('fires onPrev when ArrowLeft is pressed inside the drawer', () => {
    const onPrev = vi.fn();
    render(
      <ContextDrawer
        open
        onOpenChange={() => {}}
        category="skills"
        name="a"
        path="/a"
        body="x"
        onPrev={onPrev}
      />,
    );
    fireEvent.keyDown(screen.getByTestId('context-drawer'), { key: 'ArrowLeft' });
    expect(onPrev).toHaveBeenCalledTimes(1);
  });

  it('does not fire arrow nav handlers when drawer is closed', () => {
    const onNext = vi.fn();
    render(
      <ContextDrawer
        open={false}
        onOpenChange={() => {}}
        category="skills"
        name="a"
        path="/a"
        body="x"
        onNext={onNext}
      />,
    );
    // Drawer is unmounted in closed state — no element to receive the event,
    // so onNext must not fire even when arrow keys are pressed globally.
    fireEvent.keyDown(window, { key: 'ArrowRight' });
    expect(onNext).not.toHaveBeenCalled();
  });

  it('does not steal arrow keys when an input inside the drawer body has focus', () => {
    const onNext = vi.fn();
    render(
      <ContextDrawer
        open
        onOpenChange={() => {}}
        category="skills"
        name="a"
        path="/a"
        body={'before\nafter'}
        onNext={onNext}
      />,
    );
    // Synthesize a keydown originating from a child <input> — Dialog.Content's
    // onKeyDown still bubbles through, but the handler must early-return.
    const drawer = screen.getByTestId('context-drawer');
    const fakeInput = document.createElement('input');
    drawer.appendChild(fakeInput);
    fireEvent.keyDown(fakeInput, { key: 'ArrowRight', bubbles: true });
    expect(onNext).not.toHaveBeenCalled();
  });

  it('renders prev/next buttons with custom labels', () => {
    render(
      <ContextDrawer
        open
        onOpenChange={() => {}}
        category="skills"
        name="a"
        path="/a"
        body="x"
        onPrev={() => {}}
        onNext={() => {}}
        prevLabel="rules/style"
        nextLabel="rules/security"
      />,
    );
    expect(screen.getByTestId('context-drawer-prev')).toHaveTextContent('rules/style');
    expect(screen.getByTestId('context-drawer-next')).toHaveTextContent('rules/security');
  });
});
