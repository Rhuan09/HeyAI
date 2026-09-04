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

### MSIX, with an unpackaged path that still works

Shipping is MSIX. Building from source is not, and never will be, so every capability must
degrade rather than throw when `PackageIdentity.IsPackaged` is false — that is the
contributor's daily path.

What identity actually buys:

| Capability | Unpackaged status |
| --- | --- |
| GSMTC media control | Works |
| Core Audio volume | Works |
| Win32 window management | Works |
| `Windows.Media.Ocr` | Works |
| `Windows.Graphics.Capture` | Works for HWND/monitor capture via `IGraphicsCaptureItemInterop`; the *picker* needs identity |
| Toast notifications | Needs an AUMID + Start Menu shortcut + COM activator, or `Microsoft.Windows.AppNotifications` |
| Radio toggle (Wi-Fi/Bluetooth) | `Radio.RequestAccessAsync` generally fails — still **cut from the roadmap** |

## Distribution

**Decided: MSIX.**

The original plan was `dotnet tool install -g HeyAI`, which does not work. `PackAsTool`
rejects any `TargetPlatformIdentifier` (`NETSDK1146`), and the server needs
`net10.0-windows10.0.26100.0` to reference the WinRT modules. Splitting a `net10.0` tool
project off does not recover it either, because a platform-agnostic project cannot
reference a windows-TFM one. Global tools are not available to WinRT projects at all.

That left unpackaged-plus-GitHub-Releases against MSIX. MSIX wins because one mechanism
answers three problems:

- **Signing.** An unsigned executable that enumerates windows and captures screens trips
  SmartScreen and AV heuristics hard. Store distribution signs for us; the alternative is
  roughly $200/yr for a certificate.
- **Identity.** Toasts, and therefore the Phase 4 tray confirmation prompts, require it.
  Without identity that path costs an AUMID, a Start Menu shortcut and a COM activator.
- **Install.** Store or `winget`, instead of "download a zip and unblock it".

The costs are real: Store review on every release, and a packaging step contributors do
not need for local work. Hence the unpackaged path stays first-class.

### The assumption that had to be checked

MCP requires the client to spawn the server and pipe stdin/stdout. A packaged app is
launched through an app execution alias shim rather than as a plain executable. If pipes
or console handles did not survive that shim, MSIX would have been unusable regardless of
its other merits — and the failure would only have surfaced after a packaging pipeline
already existed.

Verified against a loose-registered dev-mode package before committing to the decision:

| Check | Result |
| --- | --- |
| `GetCurrentPackageFullName` via the alias | packaged — the shim genuinely confers identity |
| JSON-RPC over piped stdin/stdout | intact; `initialize` and `tools/call` both round-trip |
| STA DispatcherQueue thread | comes up STA inside the package |
| Untrusted-content fencing | preserved through the transport |

Note that a shell which resolves the alias reparse point itself (Git Bash does) will
execute the target binary directly and report *unpackaged*. Test activation from
PowerShell or cmd, or the result is meaningless.

`heyai doctor` reports identity, so this stays observable rather than becoming folklore.

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
| 4 | MSIX packaging + tray app: confirmation prompts, live audit view | packaging lands here, since identity is what toasts need |
| 5 | `HeyAI.Modules.Shell` | last; it is the dangerous one |

Vision is the moat — `Windows.Media.Ocr` is fast, offline and already installed, against a
field of Tesseract wrappers. It ships after Window because it needs the dispatcher and the
taint plumbing proven first.

Radio toggling and arbitrary PowerShell execution are explicitly **not** on the roadmap.
