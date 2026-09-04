# Freely Agent

Freely Agent is a native Windows AI-agent shell built with C#, .NET, WinUI 3, and the Windows App SDK. This repository implements the first safety-first vertical slice from the product specification.

## What works in this milestone

- Native WinUI 3 window with Mica, custom title bar, keyboard-accessible navigation, chat, history, tasks, memory, agents, and settings surfaces.
- Streaming chat through a provider-neutral `IModelProvider` interface.
- Demo provider for running the UI without an API key, plus OpenAI-compatible cloud/local endpoints (including Ollama's `/v1` compatibility endpoint).
- A bounded agent loop with cancellation, progress, tool routing, observations, and a maximum tool-turn guard.
- Permission levels (`Allow`, `Ask`, `Deny`) with an action preview before sensitive tools run.
- Real browser use through an embedded WebView2 session: navigate, read the live DOM, move the visible mouse to semantic elements, physically click, type with Windows keyboard input, submit, and observe the changed page.
- Real Windows control through permission-gated mouse movement/click/scroll, keyboard typing/chords, and discovery, launching, and focusing of installed applications.
- A persistent control warning plus a global emergency stop: hold **Left Shift for one second** to cancel active control immediately.
- Starter tools for reading files, listing folders, opening HTTPS pages, and PowerShell. Shell and external actions ask by default.
- Local SQLite conversation history in `%LOCALAPPDATA%\FreelyAgent\freely.db`.
- Free Windows speech input and output: a composer microphone transcribes speech locally, while completed answers can use installed text-to-speech voices.
- Model-independent perception through fused UI Automation, browser accessibility, and local screenshot OCR observations, including password-field redaction.
- Stable native-app element actions that refresh named targets before clicking or typing, preventing search fields and similarly placed controls from being confused.
- Multi-model management with a persistent model list and a quick model picker in the chat composer; switching models keeps the active conversation runtime.
- Compatibility protocol mode so text-only models without native function calling can still request Freely tools.
- Native entrance, message, navigation, and working-status animations.
- Unit tests proving that denied actions do not execute and destructive actions cannot bypass confirmation.

This is a foundation, not a claim that every roadmap section is complete. Image OCR, screen capture, credential vault, arbitrary-app adapters, and background hosting are tracked as subsequent milestones.

## Prerequisites

- Windows 10 version 1809 or newer; Windows 11 recommended.
- Visual Studio with the **WinUI application development** workload, or the .NET 8 SDK plus Windows development tools.
- x64 for this first milestone.

The app currently references stable `Microsoft.WindowsAppSDK` 2.4.0 and is unpackaged/self-contained for straightforward local development.

## Build and run

```powershell
dotnet restore .\Freely.sln
dotnet build .\Freely.sln -c Debug
dotnet run --project .\src\Freely.App\Freely.App.csproj -c Debug
```

Run tests with:

```powershell
dotnet test .\Freely.sln -c Debug
```

## Try safe demo mode

The default provider does not make network requests. Just describe the task naturally:

```text
List the files in my Downloads folder
Read C:\path\to\file.txt
Open example.com
Search the web for WinUI 3 tutorials
Open Notepad
Go to Discord and change the theme to Light
Type hello from Freely
Press ctrl+s
Run Get-Date in PowerShell
```

Freely translates the request into the appropriate tool automatically. Slash commands remain available as optional shortcuts, but users never need to know them. Read-only actions follow the current permission policy. Native app launching and control are continuously allowed by default, with a glowing blue frame showing the active target. Browser actions can use the same continuous mode from Settings. File/system changes, PowerShell, administrator actions, and destructive operations continue to follow their permission rules.

## Computer and browser use

Browser tasks run in the visible **Freely browser** panel. WebView2's browser accessibility and layout protocols supply visible element roles and rectangles without executing page JavaScript. Freely converts the selected rectangle to physical screen coordinates, visibly moves the Windows pointer, clicks, turns the mouse wheel, types through `SendInput`, and presses real keys like a user. Each action returns a fresh page observation before the next decision.

Native app tasks run continuously without approval pop-ups by default. Freely discovers applications from Windows' AppsFolder inventory (including packaged Microsoft Store apps), the per-user and all-user Start menus, Windows App Paths registration, built-in Windows targets, and visible running windows. It can therefore resolve friendly requests such as “Go to Discord,” launch or focus the real application, inspect named controls and their screen bounds through UI Automation, and operate them with physical mouse and keyboard input. It re-observes the interface after changes instead of treating an app launch as task completion. The glowing blue frame identifies the controlled target, and portable programs that have no Windows app registration, Start Menu shortcut, or running window are not automatically discoverable.

For native apps, each observation now includes a region map (for example, top-left navigation versus top-center search versus bottom-center composer), parent/description context, and stable element IDs. `app.click_element` and `app.type_element` refresh the target before input and return a new observation afterward. Messaging flows are instructed to verify the exact recipient or channel and the message composer before any text is entered.

Some apps expose less accessibility information than others. In those apps, Freely can still use keyboard navigation and visible coordinates, but results depend on what that application's UI makes observable. Windows blocks input from a normal process into a higher-integrity/elevated app, so run both applications at the same integrity level.

While Freely is open, hold **Left Shift for one second** to force-stop active computer or browser control. The in-app **Stop** button remains available as a second cancellation path.

To run a multi-step browser task without approving every click, open **Settings → Action permissions**, enable **Always allow browser control**, and save. This persistent override applies only to the browser tools, whether the selected browser is embedded or installed; unrelated native apps, files, PowerShell, administrator actions, and destructive actions keep their own permission rules.

**Always allow app control** is enabled by default. It covers app launch/focus, semantic UI actions, and fallback mouse/keyboard steps so a native-app task can finish without repeated approval dialogs. Disable it in **Settings → Action permissions** to restore per-step prompts. It never changes file, shell, administrator, or destructive tool permissions.

The **Browser used by the agent** selector is populated from browsers registered with Windows, with additional checks for common Chrome, Edge, Firefox, Brave, Vivaldi, and Opera installation locations. Choose **Freely Browser** for the embedded WebView2 session or an installed browser to have Freely launch/focus its real window and control it through UI Automation plus physical mouse and keyboard input.

## Configure a model

Open **Settings → AI provider**, choose a provider, then add one or more model IDs. Select the default model there, or switch among the saved models from the compact picker beneath the chat box.

Available providers include:

- **Ollama / local compatible** with `http://localhost:11434/v1/` and a model ID; or
- **OpenRouter**, **xAI Console**, **Google AI Studio**, or **NVIDIA NIM** with the prefilled endpoint and model; or
- **Custom OpenAI-compatible** with its `/v1/` base URL, model ID, and API key.

API keys are deliberately session-only until the Windows Credential Manager vault lands. They are never written to the SQLite settings table.

For a model that accepts text but does not support native tools, enable **Compatibility mode**. Freely sends a compact text action protocol and converts the model's response back into permission-gated tool calls.

## Voice input and responses

Select the microphone beside **Send** to dictate into the prompt. Speech recognition uses Windows locally and inserts the recognized text into the composer so it can be reviewed or edited before sending. Select the microphone again to cancel listening. Windows microphone privacy permission must be enabled for Freely.

Answers are spoken by the free speech synthesizer built into Windows. In **Settings → Voice responses**, users can mute speech or choose any installed Windows voice. Automatic selection prefers a male voice matching the Windows UI language and uses a slightly brighter speaking rate. Windows does not expose a reliable voice-age property, so Freely does not falsely label an installed voice as “young.”

Code blocks, URLs, JSON, and other unsuitable output are filtered from speech while the full answer remains visible. Starting a new task or pressing Stop immediately stops playback.

## Universal perception

All providers—including text-only local models—receive the same normalized observations. The implementation supports:

- active-window controls, accessible names, values, text patterns, bounds, and state through Windows UI Automation;
- local active-window screenshot OCR, fused with accessibility data to recover visible text and coordinates from custom, canvas, and weak-accessibility interfaces;
- hybrid WebView snapshots that augment DOM/accessibility information with local OCR for visually rendered page content;
- automatic password-value redaction;
- structured reading for common text, code, markup, JSON, CSV, and log files;
- compact, normal, and full observation budgets; and
- the `perception.read` tool, selected automatically from natural-language requests such as “what is on my screen?”

This follows a structured-source-first policy: DOM, UI Automation, and file data remain primary, while a local OCR pass fills visible gaps. Screenshots stay on the computer; only normalized text, roles, and bounds are sent to the selected model.

## Architecture

```text
Model provider
      ↓
AgentRuntime (bounded loop)
      ↓
Tool registry
      ↓
PermissionGate → action preview / user decision
      ↓
Tool execution
      ↓
Observation → next model turn → completion
```

See [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) for boundaries and [docs/ROADMAP.md](docs/ROADMAP.md) for the specification-to-milestone map.
