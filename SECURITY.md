# Security Policy

## Reporting a Vulnerability

If you believe you've found a security vulnerability in pdf-inspector, please
report it privately so we can fix it before public disclosure.

**Preferred:** Email **help@firecrawl.dev** with:

- A description of the issue and its impact
- Steps to reproduce (a minimal PDF or input that triggers the bug is ideal)
- The version or commit hash of pdf-inspector you tested against

Email is the only channel we require, and a complete report sent this way needs
no further action from you.

**Alternative:** If you already use it, you may submit through Firecrawl's
Bugcrowd vulnerability disclosure program at
<https://bugcrowd.com/engagements/firecrawl-vdp-ess>. We do not require you to
create a Bugcrowd account to report a bug — email is always sufficient.

We'll acknowledge your report in a timely manner and keep you updated on
remediation progress. Please do not open a public GitHub issue for security
bugs.

## Scope

In scope:
- Memory-safety issues (panics, OOB reads, UB) reachable from a crafted PDF
- Denial-of-service vectors (unbounded allocation, infinite loops) on
  reasonably-sized inputs
- Bugs in the `pdf2md` / `detect-pdf` binaries or the `pdf-inspector` crate
  that affect downstream consumers

Out of scope:
- Bugs in upstream dependencies (`lopdf`, etc.) — please report those upstream
- Extraction quality issues (wrong text, missing tables) — open a regular
  GitHub issue instead
