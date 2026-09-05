# Security Policy

## Reporting a vulnerability

Please report privately rather than in a public issue: use GitHub's
**Report a vulnerability** button under the Security tab.

Include what an attacker gains and how you reached it. A proof of concept helps more than
a description.

## What is in scope

HeyAI hands a language model tools that read attacker-chosen content *and* tools that act
on the machine. The interesting reports are about that combination:

- a way to reach a tool without passing through `ToolInvoker`
- a tool whose `EvaluateRisk` under-classifies a dangerous argument
- untrusted output that reaches the model without being marked
- a way to make the audit log incomplete, or to reach it from a tool
- a way for an agent to widen its own permissions

## What is not

Read [docs/SECURITY.md](../docs/SECURITY.md) before reporting. It states plainly what the
threat model does *not* cover, including a user who enables everything, a malicious MCP
client, and a model that decides on its own to do something destructive. Those are known
and documented, not findings.

## Supported versions

Pre-1.0. Only `main` is supported.
