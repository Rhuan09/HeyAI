# HeyAI

**A typed, policed MCP surface onto native Windows APIs — with a permission model, not
just a PowerShell hole.**

HeyAI is an MCP server. Point Claude Code, Cursor, or any MCP client at it and the agent
can read what is playing, control media, manage windows, and read the screen through
native WinRT — with a deny-by-default allowlist, per-invocation risk classification, and
an append-only audit log it cannot reach.

It is not an assistant. Your assistant is whatever you already use. HeyAI is the layer
underneath it, so you can build your own Jarvis without reimplementing Windows.

## Status

Phase 1. Media module working end to end; Window and Vision next. See
[the roadmap](docs/ARCHITECTURE.md#roadmap).

## Install

There is no packaged release yet (see [Distribution](docs/ARCHITECTURE.md#distribution)).
Build from source:

```bash
git clone https://github.com/Rhuan09/HeyAI
cd HeyAI
dotnet build -c Release
```

Then point your MCP client at the built executable:

```bash
claude mcp add heyai -- <repo>/src/HeyAI.Server/bin/Release/net10.0-windows10.0.26100.0/heyai.exe
```

## Tools

| Tool | Risk | What it does |
| --- | --- | --- |
| `media_get_status` | read | Active media sessions with track, artist, play state |
| `media_control` | convenience | play / pause / toggle / next / previous / stop |
| `audio_get_devices` | read | Output devices and the per-app mixer |
| `audio_set_volume` | convenience | Master or per-application volume and mute |

New tools ship **disabled**. Enable them in `%LOCALAPPDATA%\HeyAI\config.json`.

## Try it without an agent

```bash
heyai.exe doctor              # check the STA dispatcher and state directory
heyai.exe list                # registered tools and whether they are enabled
heyai.exe test media_get_status
heyai.exe test audio_set_volume '{"target":"app","app":"firefox","level":0.3}'
```

`heyai.exe test` runs the full policy and audit pipeline, so what you see is what an agent
would get.

## Security

HeyAI gives a language model tools that read attacker-chosen content *and* tools that act
on your machine. That combination is the whole risk, and it is designed against rather
than hand-waved: untrusted output is marked and fenced, `Critical` actions are refused for
a window after any untrusted read, and everything — including refusals — is audited.

Read [docs/SECURITY.md](docs/SECURITY.md) before enabling anything beyond the defaults. It
also states what HeyAI does *not* protect against.

## Contributing

Read [CONTRIBUTING.md](CONTRIBUTING.md) for the branching model, the PR checklist, and
the rules that are not negotiable. [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) explains
why things are shaped the way they are.

```bash
dotnet build
dotnet test --filter "Category!=RequiresDesktop"    # what CI runs
dotnet test --filter "Category=RequiresDesktop"     # run locally before every PR
```

CI cannot test WinRT interop — `windows-latest` has no audio endpoint and no media
session — so desktop-gated tests are on you before opening a PR.

## Licence

Apache-2.0.
