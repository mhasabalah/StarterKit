---
name: update-docs
description: Write or update StarterKit documentation in the right place and format - decide wiki vs notes vault, follow wiki naming and structure, update wiki/INDEX.md, README and CLAUDE.md when behaviour changes, add an ADR for architectural decisions, keep placeholders safe, and run the docs link checker. Use for "document this", "update the wiki", "write docs for module X", "add an ADR", "fix broken doc links".
---

# Update documentation

## 1. Route the document

| Put it in `wiki/` (committed) when it is... | Put it in your notes vault (`<vault>/projects/<project>/`) when it is... |
|---|---|
| reference material any developer cloning the repo needs: conventions, API guides, module docs, runbooks, ADRs | a flow walkthrough, session analysis, design review, before/after comparison, investigation notes |

**Never** create markdown in the repo root. Only `README.md`, `CLAUDE.md` and `AGENTS.md` belong there.
Rule of thumb: if it would clutter `git diff` without helping a code reviewer, it goes to the vault.

## 2. Where in the wiki

| Content | Location |
|---|---|
| Cross-cutting architecture | `wiki/Architecture/` |
| A module (purpose, aggregates, endpoints, events, jobs, config) | `wiki/Modules/<Module>.md` (deep dives in a `wiki/<Module>/` folder) |
| Multi-tenancy | `wiki/MultiTenancy/` |
| Testing | `wiki/Testing/` |
| Infrastructure (Redis, Aspire) | `wiki/Infrastructure/` |
| Deployment and operations runbooks | `wiki/Deployment/` |
| Architecture decisions | `wiki/Decisions/NNNN-title.md` (copy `0000-template.md`) |
| Commands, migrations, troubleshooting, backlog | `wiki/Reference/` |
| Onboarding and new project | `wiki/Getting-Started/` |

Naming is `Title-Kebab.md` files in PascalCase folders, and each folder may have a `README.md` index. Don't create
`*-SUMMARY.md` or `*-COMPLETE.md` status reports; that is session output and belongs in the vault.

## 3. Writing rules

- Document what the code does **now**. Verify every class, route, path and command with grep or glob before
  writing it.
- Link to code by repo-relative path. Prefer linking over copying large code blocks, which go stale.
- Placeholders only: `api.example.com`, `YOUR_SERVER_IP`, `CHANGE_ME`, `your-org`. No real hosts, IPs, buckets,
  passwords or local `C:\`/`E:\` paths.
- One canonical page per topic. Extend that page; don't add a parallel one.
- Use relative links (`../Architecture/Messaging.md`), and anchors only for stable headings.

## 4. Keep the entry points in sync

When behaviour or structure changes, update in the same change:
- `wiki/INDEX.md`, which must link every wiki page.
- `README.md`: module table, quick start, config and test matrices.
- `CLAUDE.md`: rules, guardrails, skills index, docs map. Keep it rules-only; procedures go in skills or the wiki.
- `AGENTS.md` and `.github/copilot-instructions.md` are pointers. Edit them only if a hard rule changed.
- A new architectural decision (a new transport, an isolation model, a library swap) gets an ADR.

## Verify

```bash
bash scripts/check-docs.sh     # broken relative links + forbidden placeholders; must print 0 problems
```
