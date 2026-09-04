# HeyAI — Architecture

## What this is

An MCP server that gives an AI agent a typed, policed surface onto native Windows APIs.
Not an assistant. The assistant is whatever the user already has — Claude Code, Cursor, a
local model. HeyAI is the layer underneath it.

## Decisions and why

### MCP server, not an application

Building a "Jarvis" means building a UI, a voice stack, and a model integration, and
competing with every other assistant. Building the OS layer means every agent becomes the
front end, and the project is useful the day the first tool works.

### No Windows App SDK in the server

A headless stdio process needs the CsWinRT projection, which the
`net10.0-windows10.0.26100.0` TFM provides on its own. `Microsoft.WindowsAppSDK` adds the
bootstrapper, self-contained deployment constraints, and a class of activation failures
(`REGDB_E_CLASSNOTREG`) that are painful to diagnose. It is reserved for the Phase 3 tray
app, as a separate project.

### .NET 10

.NET 9 was STS and left support in May 2026. 10 is the current LTS.

### Unpackaged, not MSIX

Unpackaged means `dotnet tool install -g HeyAI` and no installer. The cost is that some
capabilities are gated behind package identity:

| Capability | Unpackaged status |
| --- | --- |
| GSMTC media control | Works |
| Core Audio volume | Works |
| Win32 window management | Works |
| `Windows.Media.Ocr` | Works |
| `Windows.Graphics.Capture` | Works for HWND/monitor capture via `IGraphicsCaptureItemInterop`; the *picker* needs identity |
| Toast notifications | Needs an AUMID + Start Menu shortcut + COM activator, or `Microsoft.Windows.AppNotifications` |
| Radio toggle (Wi-Fi/Bluetooth) | `Radio.RequestAccessAsync` generally fails — **cut from the roadmap** |

Distribution as a global tool also sidesteps SmartScreen and AV heuristics, which an
unsigned .exe that enumerates windows and captures screens would trip hard.

### Hand-rolled JSON-RPC

The transport is one file. Every call must pass through `ToolInvoker` for policy and
audit, and an SDK's attribute-based tool registration would fight that. Revisit if HeyAI
needs sampling or elicitation.

### Hand-rolled Core Audio interop

GSMTC has no volume concept; master and per-app volume are MMDevice/AudioSession COM. Four
interfaces, so `[GeneratedComInterface]` beats making NAudio the project's only non-SDK
runtime dependency.

## Layout

```
src/HeyAI.Core/          contracts, policy, audit, STA dispatcher, ToolInvoker
src/HeyAI.Modules.Media/ GSMTC + Core Audio (the first vertical slice)
src/HeyAI.Server/        stdio MCP transport + CLI
tests/HeyAI.Tests/       CI-safe logic tests + desktop-gated interop tests
```

Modules depend on Core. Nothing depends on a module except the server's registration call.
Core knows nothing about GSMTC or MMDevice.

## The invocation pipeline

```
tools/call
   -> ToolInvoker
        1. tool.EvaluateRisk(args)      pure, from the arguments, never static
        2. PolicyEngine.Evaluate        allowlist -> taint check -> risk ceiling
        3. audit.Write(decision)        refusals are recorded too
        4. tool.ExecuteAsync            may fail, must not throw
        5. taint.RecordUntrustedRead    if the result is marked tainted
        6. audit.Write(outcome)         duration, error code, taint
```

Calling `ExecuteAsync` outside this path bypasses the entire security model. There is no
second entry point on purpose.

## Threading

One dedicated STA thread hosting a `DispatcherQueue` created through CoreMessaging's
`CreateDispatcherQueueController`, with a Win32 message loop that *is* the queue drain.
This is what WinUI sets up for you and what a console host must build by hand.

`Windows.Graphics.Capture` requires it. GSMTC and Core Audio must not use it — see
`IWinRtDispatcher`'s remarks and the rule in `CONTRIBUTING.md`.

`heyai doctor` verifies the thread comes up STA before you debug anything else.

## Confirmation transport

`PolicyOutcome.RequireConfirmation` has no way to reach a human in Phase 1, so
`ToolInvoker` reports it as a refusal naming the config file. Phase 3 replaces that branch
with a tray prompt. MCP's `elicitation` capability is the eventual in-protocol answer, but
client support is not broad enough to depend on yet.

## Testing

GitHub Actions `windows-latest` has no audio endpoint and no media session, so interop
tests cannot run there. Split:

- **CI** — `--filter "Category!=RequiresDesktop"`. Policy, config, schema contracts,
  invoker error handling.
- **Local** — `--filter "Category=RequiresDesktop"`. Dispatcher apartment, device
  enumeration, volume round-trip, GSMTC degradation.

Keep logic reachable without a desktop. A module only testable on a desktop is a module
nobody can review a PR for.

## Roadmap

| Phase | Scope | Status |
| --- | --- | --- |
| 1 | Core + Media slice, stdio transport, CLI | done |
| 2 | `HeyAI.Modules.Window` (User32, UI Automation) | next — demoable, low risk |
| 3 | `HeyAI.Modules.Vision` (native OCR + Graphics.Capture) | the differentiator |
| 4 | Tray app: confirmation prompts, live audit view | unblocks `RequireConfirmation` |
| 5 | `HeyAI.Modules.Shell` | last; it is the dangerous one |

Vision is the moat — `Windows.Media.Ocr` is fast, offline and already installed, against a
field of Tesseract wrappers. It ships after Window because it needs the dispatcher and the
taint plumbing proven first.

Radio toggling and arbitrary PowerShell execution are explicitly **not** on the roadmap.
