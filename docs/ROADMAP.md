# Specification delivery roadmap

The full product brief describes a mature operating layer. Delivery is split into verifiable security boundaries so broad Windows access is never introduced as an unreviewed shortcut.

## Milestone 1 — native shell and safe loop (implemented)

- WinUI 3 application shell and Fluent styling
- Chat streaming, cancellation, status, and history
- Cloud/local OpenAI-compatible providers and offline demo mode
- Tool registry, permission prompt, action preview, observation, and loop limit
- Starter file, folder, browser-launch, and PowerShell tools
- SQLite persistence, settings, and core permission tests
- Windows TTS responses, mute/voice settings, speech filtering, and interruption
- Universal observation schema, UI Automation reader, file perception, and text-only action protocol
- Native navigation, message, entrance, and activity animations

## Milestone 2 — structured Windows control (core implemented)

- Expand UI Automation with app adapters and stable cross-snapshot element resolution
- App launch/focus tools and an exclusive desktop-control lock
- Keyboard/mouse adapters behind explicit scoped permissions, with a global hold-Left-Shift emergency stop
- Screen/window/region capture with a persistent viewing indicator
- Protected-path checks, canonical path validation, and expanded audit log

## Milestone 3 — browser and voice (core implemented)

- Embedded WebView2 agent session with DOM-first navigate/snapshot/click/type automation
- Download scanning and domain permissions
- Microphone capture, VAD, pluggable STT providers, streaming sentence queue, and barge-in
- Separate display and speech response formatting

## Milestone 4 — reliability and privacy

- Windows Credential Manager integration and secret redaction
- Task recovery/replay, artifacts, resumable workspaces, and verification strategies
- Local-only enforcement at the network boundary
- Prompt-injection provenance, connector permissions, security evaluation suite
- Tray/background host, notifications, updater, diagnostics, and opt-in telemetry

## Milestone 5 — V1/V2 capabilities

- Memory controls and privacy dashboard
- Plugins/connectors, model routing, provider health, and cost controls
- Scheduler, delegated agents, advanced document workflows, and companion experiences

Each milestone should ship only after its denial, cancellation, injection, path traversal, and recovery tests pass.
