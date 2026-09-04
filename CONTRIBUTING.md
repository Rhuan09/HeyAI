# Contributing to HeyAI

## Getting set up

Requires the .NET 10 SDK and Windows 10 build 19041 or later.

```bash
dotnet build
dotnet test --filter "Category!=RequiresDesktop"    # what CI runs
dotnet test --filter "Category=RequiresDesktop"     # you must run this locally
dotnet run --project src/HeyAI.Server -- doctor     # verify the STA dispatcher
```

## Branching

`main` is always releasable. Never commit to it directly — every change goes through a
pull request, including your own.

Whether GitHub *enforces* that is a repository setting, not something this file can
guarantee. To require PRs and a passing CI check before anything lands on `main`:

```bash
gh api -X POST repos/:owner/:repo/rulesets --input .github/rulesets/main.json
```

Branch names are `<type>/<short-kebab-description>`:

| Prefix | For |
| --- | --- |
| `feat/` | A new tool, module, or capability |
| `fix/` | A bug in existing behaviour |
| `docs/` | Documentation only |
| `refactor/` | Structure change with no behaviour change |
| `chore/` | Build, CI, dependencies, tooling |

```bash
git switch -c feat/window-module
```

Keep one concern per branch. A new module and a policy-engine change are two PRs — the
security-relevant one deserves review on its own.

## Commits

Present tense, imperative, explaining *why* when it isn't obvious from the diff:

```
Add window_list_open with per-monitor bounds

User32 EnumWindows returns cloaked UWP shells, so filter on
DWMWA_CLOAKED rather than IsWindowVisible alone.
```

Small, coherent commits over one squashed dump. Rebase on `main` before opening the PR.

## Pull requests

Fill in the template. It exists because of one specific gap: **CI cannot test WinRT
interop.** GitHub Actions `windows-latest` has no audio endpoint and no media session, so
`Category=RequiresDesktop` tests are excluded there. A green CI check does not mean your
interop works. Run the desktop suite locally and say so in the PR.

A PR is ready when:

- [ ] Both test suites pass locally
- [ ] New behaviour has a test — CI-safe if it can be, desktop-gated only for the interop boundary
- [ ] No new NuGet dependency without justification in the description
- [ ] `Microsoft.WindowsAppSDK` was not added to anything under `src/`
- [ ] New tools ship disabled, and untrusted output is marked

Security-relevant changes — anything touching `Security/`, `Audit/`, `ToolInvoker`, or a
tool's `EvaluateRisk` — need a second reviewer and an explicit note on what the change
lets an agent do that it could not do before.

## Rules that are not negotiable

These exist because breaking them fails in ways that are hard to diagnose later.

### stdout is the MCP wire

Diagnostics go to `Console.Error`. One stray `Console.WriteLine` in server mode corrupts
the stream and the client dies on an unrelated-looking parse error.

### Everything goes through `ToolInvoker`

Never call `IHeyAITool.ExecuteAsync` directly. `ToolInvoker` is where risk evaluation,
policy, taint tracking and audit happen; a second entry point bypasses the entire security
model. There is exactly one path on purpose.

### `ExecuteAsync` does not throw

Operational failures are `ToolResult.Error(code, message)` with a code the model can
branch on. The process is a live transport for a connected client — a throw takes down the
session, not just the call.

### Threading: MTA by default, STA only where required

A console app's main thread is MTA. Calling a DispatcherQueue-affine WinRT API from it
fails as `RPC_E_WRONG_THREAD` or a silent hang.

- **Must** use `IWinRtDispatcher`: `Windows.Graphics.Capture`, toast activation callbacks,
  anything XAML or composition.
- **Must not**: plain COM/Win32 (Core Audio, User32) and GSMTC. They are MTA-safe, and
  routing them through one pumped thread serialises every call and invites re-entrancy
  deadlocks. `HeyAI.Modules.Media` bypasses the dispatcher deliberately.

Always `.AsTask(ct)` a WinRT async operation and propagate the `CancellationToken`.

### No Windows App SDK under `src/`

The `net10.0-windows10.0.26100.0` TFM already provides the full CsWinRT `Windows.*`
projection. `Microsoft.WindowsAppSDK` adds the bootstrapper, self-contained deployment
constraints, and `REGDB_E_CLASSNOTREG` activation failures. It is permitted only in the
future tray app, as its own project.

### Interop style

Win32 P/Invoke lives in the owning module. `[LibraryImport]`, not `[DllImport]`. COM via
`[GeneratedComInterface]` with `[PreserveSig]` and explicit HRESULT checks — implicit
HRESULT-to-exception translation hides the "no device" and "no session" cases that are
normal here and must become structured tool errors.

### State location

Everything lives under `%LOCALAPPDATA%\HeyAI` via `HeyAIPaths`. Never `~/.heyai` — that is
a POSIX convention. No tool may expose a path where `HeyAIPaths.IsProtected` is true; an
audit log the agent can open is an audit log the agent can erase.

## Adding a tool

1. Derive from `HeyAITool` in the owning module's `Tools/` folder.
2. `SchemaJson` must be `"type": "object"` with `"additionalProperties": false`. A contract
   test enforces this — an unvalidated field is a way past `EvaluateRisk`.
3. Set `Annotations` (`readOnlyHint` / `destructiveHint` / `idempotentHint` /
   `openWorldHint`). These are the MCP-standard hints clients consume; `RiskLevel` is the
   internal enforcement tier and must stay consistent with them.
4. Override `EvaluateRisk` **from the arguments**, not per tool. A path under Documents and
   `powershell -enc <base64>` cannot share a tier. Keep it pure and OS-free.
5. If output contains anything a third party chooses — screen pixels, OCR text, window
   titles, track metadata, device names — return `ToolResult.UntrustedJson(value, source)`.
   That is what arms the read-then-execute block in `docs/SECURITY.md`.
6. Register it in the module's `CreateTools()`.
7. Ship it **disabled**. Only add to `HeyAIConfig.Default()` if it is read-only or
   trivially reversible.

Tool names are `snake_case`, module-prefixed, and stable — renaming one breaks every
user's config.

## Testing

Keep module logic reachable without a desktop, and let `RequiresDesktop` tests cover only
the interop boundary. A module whose logic is only testable on a live desktop is a module
nobody can review a PR for.

## Where things are explained

- [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) — why the project is shaped this way
- [docs/SECURITY.md](docs/SECURITY.md) — the threat model; read before enabling anything
  beyond the defaults
