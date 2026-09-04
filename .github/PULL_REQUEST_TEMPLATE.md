## What and why

<!-- What changes, and what problem it solves. Link an issue if there is one. -->

## Testing

CI cannot test WinRT interop — `windows-latest` has no audio endpoint and no media
session, so `Category=RequiresDesktop` is excluded there. A green CI check is not evidence
your interop works.

- [ ] `dotnet test --filter "Category!=RequiresDesktop"` passes
- [ ] `dotnet test --filter "Category=RequiresDesktop"` passes locally
- [ ] New behaviour is covered by a test

<!-- If this touches interop, say what you actually exercised on your machine. -->

## Checklist

- [ ] No new NuGet dependency, or it is justified above
- [ ] `Microsoft.WindowsAppSDK` not added to anything under `src/`
- [ ] New tools ship disabled and mark untrusted output
- [ ] Threading follows the MTA/STA rule in CONTRIBUTING.md

## Security impact

<!-- Required if this touches Security/, Audit/, ToolInvoker, or any EvaluateRisk.
     State plainly what an agent can do after this change that it could not do before.
     Write "none" if there is genuinely no change to the security surface. -->
