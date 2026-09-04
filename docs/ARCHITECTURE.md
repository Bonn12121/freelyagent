# Architecture

Freely follows the specification's fundamental boundary: a model proposes decisions; it never receives unrestricted Windows access.

## Projects

| Project | Responsibility |
| --- | --- |
| `Freely.App` | WinUI 3 presentation, interaction, settings, and composition root |
| `Freely.Agent` | Provider/tool contracts, bounded agent loop, state, progress, cancellation |
| `Freely.AI` | Demo and OpenAI-compatible streaming provider adapters |
| `Freely.Security` | Permission policy and interactive action gate |
| `Freely.Tools` | Small, typed Windows capability adapters |
| `Freely.Storage` | SQLite initialization and repositories |
| `Freely.Perception` | Normalized UI Automation and file observations for any text-capable model |
| `Freely.Voice` | Free Windows speech synthesis, voice selection, playback, and speech-safe formatting |
| `Freely.Computer` | Exclusive desktop-control lease, Win32 input injection, and global emergency-stop hook |

Dependencies point inward toward `Freely.Agent`; the core does not reference WinUI, HTTP provider details, SQLite, or concrete Windows tools.

## Trust boundary

Every tool invocation follows this sequence:

1. The provider returns a typed tool call.
2. `AgentRuntime` resolves only registered tools.
3. `PermissionGate` evaluates the tool's declared risk and policy.
4. `Ask` decisions block on a native action-preview dialog.
5. Only an allowed call reaches `ExecuteAsync`.
6. The structured result is recorded as an observation and returned to the provider.
7. The loop stops after 12 tool turns even if the model does not conclude.

Computer control is serialized through `ComputerControlCoordinator`. Mouse and keyboard tools use `SendInput`; Windows UIPI therefore prevents Freely from controlling an application running at a higher integrity level. A low-level keyboard hook observes physical Left Shift events only and cancels the current desktop-control token after a one-second hold.

Browser control stays inside the WinUI WebView2. The host reads WebView2's Chromium accessibility and layout protocol data to identify visible interactive nodes, capture their rectangles, and redact protected values without executing page JavaScript. The host converts a selected rectangle to screen coordinates, visibly moves and clicks the Windows pointer, types with `SendInput`, and returns a fresh observation. Page text is always treated as untrusted data.

The optional persistent `permissions.browser.alwaysAllow` setting installs per-tool `Allow` overrides for browser navigation, snapshots, clicks, typing, scrolling, and key presses. Disabling it removes those overrides and restores the general permission policy. It never changes native computer, filesystem, shell, administrator, or destructive permissions.

The persistent `permissions.appControl.alwaysAllow` setting defaults to enabled and installs scoped `Allow` overrides for app launch/focus, semantic UI actions, and fallback mouse/keyboard control. Disabling it removes those overrides and restores the general permission policy. Filesystem, shell, administrator, and destructive tools are never included in this profile.

Tool arguments are passed as data. PowerShell uses `ProcessStartInfo.ArgumentList`, avoiding a second command-string interpolation layer in Freely itself. Administrator elevation is not implemented in-process.

## Local data

The database is created under `%LOCALAPPDATA%\FreelyAgent`. WAL and foreign-key enforcement are enabled. This milestone creates the conversation, message, task, setting, permission, and tool-call tables needed by the first vertical slice. Secrets are not persisted.

## Provider compatibility

`OpenAiCompatibleProvider` uses the streaming Chat Completions wire format and function tools. The base URL must include the provider's version prefix, usually `/v1/`. Tool observations are currently normalized into system messages for compatibility across local servers that do not fully implement assistant tool-call history.

Models without native function calling can use protocol mode. Freely supplies the same tool catalog as text and parses a constrained `<action>` envelope back into the normal permission pipeline.

## Perception

`PerceptionManager` routes a target to the richest available provider. The current priority is Windows UI Automation for an active application and format-aware text extraction for supported files. Both produce the shared `Observation` and `SemanticElement` schema, which is serialized into model-readable text. UI Automation password values are replaced before serialization.

Screenshot/OCR is not silently attempted by the unpackaged build: Microsoft documents that `Windows.Media.Ocr` requires package identity for desktop apps. It belongs with the planned MSIX distribution and visible capture indicator.

## Voice

`WindowsVoiceService` uses installed `Windows.Media.SpeechSynthesis` voices and local media playback, so normal answers do not require a paid speech provider. Automatic selection prefers a male voice in the current UI language. Speech text is separately formatted so code, URLs, and large machine-oriented blocks are not read aloud.
