# What HeyAI will not do

A project like this dies by accretion. Every tool looks cheap when someone asks for it and
is maintenance forever, and each one widens what an agent can do on someone's machine.
Saying no in writing is easier than saying no in a thread.

None of these are closed to discussion. They are closed to being added casually.

## Arbitrary command execution

No `run_powershell`, no `execute_shell`, no `eval`.

This is the single most requested feature such a project gets, and adding it collapses the
entire security model into one tool. Risk cannot be evaluated from the arguments when the
argument is a program — `Get-Process` and a base64-encoded downloader are the same shape.
Every other tool's tier becomes theatre the moment this one exists.

If you want an agent that can run shell commands, use a shell MCP server. That is a
coherent choice; it is just not this project.

## Input injection

No synthetic keystrokes, no mouse control, no `send_keys`.

Typing into whatever happens to have focus is arbitrary command execution wearing a
costume: it can reach a terminal, a password field, or a confirmation dialog belonging to
something else entirely. It also makes the read-then-execute chain unstoppable, since the
model could dismiss its own confirmation prompt.

`window_focus` moves focus and stops there, deliberately.

## Reading anything a person did not put on screen

No process memory, no keystroke capture, no clipboard history, no browser profile or
saved credentials.

The Vision module reads pixels a user is already looking at. That line is worth keeping:
it is the difference between a tool that helps with what is in front of you and one that
surveils you.

## Radio and network toggles

`Radio.RequestAccessAsync` needs per-user consent and fails outright unpackaged, so a
`system_toggle_setting` tool would be unreliable half the time. A tool that fails
unpredictably is worse than no tool, because the model cannot plan around it.

## Anything requiring elevation

HeyAI runs as the user and gives an agent no privilege the user lacks. A tool that prompts
for UAC would break that property and turn a compromised session into a compromised
machine.

## Cloud anything

No telemetry, no remote logging, no model calls of its own. HeyAI is a local process
speaking stdio to a client that is already talking to a model. Adding a second network
path would make the audit log a partial record, which is worse than no claim of one.

## Its own assistant

No wake word, no voice, no chat UI. The whole premise is that your assistant already
exists and needs hands. Building a competing front end would mean maintaining one instead
of the layer everyone can share.

## How to argue with this list

Open an issue that answers three things:

1. What can a user do afterwards that they cannot do today?
2. How does `EvaluateRisk` classify the dangerous case *from the arguments alone*?
3. What does an attacker get if they inject the request through OCR text?

A feature that survives all three is worth discussing. Most do not get past the second.
