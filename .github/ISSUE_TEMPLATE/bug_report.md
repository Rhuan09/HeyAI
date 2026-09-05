---
name: Bug report
about: Something behaves differently from what is documented
labels: bug
---

## What happened

## What you expected

## To reproduce

Prefer `heyai test <tool> '<json>'` over a client transcript — it runs the same pipeline
without a model in the loop, so the output is reproducible.

```
heyai test ...
```

## Environment

- Windows build: <!-- winver -->
- `heyai doctor` output:

```

```

<!-- doctor reports whether the process is packaged and whether the STA dispatcher came
     up, which are the two things that fail confusingly. -->

## Relevant audit entries

`%LOCALAPPDATA%\HeyAI\logs\audit.jsonl`. Redact anything sensitive — argument payloads are
recorded, and window titles or track names may be in there.
