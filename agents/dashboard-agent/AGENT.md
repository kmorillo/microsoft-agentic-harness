---
id: dashboard-agent
name: Dashboard Agent
description: Conversational agent embedded in the observability dashboard that reads the current view and acts on it (time range, navigation, refresh) and can list available metrics.
domain: observability
category: observability
version: 1.0.0
author: Microsoft Agentic Harness
tags: ["dashboard", "observability", "agui", "interactive"]
skill: dashboard-agent
---

# Dashboard Agent

The agent behind the embedded dashboard chat panel. It answers questions about the observability
dashboard and acts on it for the user — changing the time range, navigating between pages, and
refreshing data — using the `dashboard_control` tool, and can enumerate the available metrics with
`list_metrics`.

## When to use

- As the agent bound to conversations created by the dashboard's agent panel.
- Anywhere a chat surface needs to both read and manipulate the live dashboard view via AG-UI
  client round-trip tool calls.

The companion skill (`skills/dashboard-agent/SKILL.md`) defines the tool contracts and usage patterns.
