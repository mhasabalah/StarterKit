<!-- Template repository only: excluded from generated products (.template.config/template.json). -->
# Changing the template

The repository is the template, so every change is a normal code change. Three rules keep it generatable:
- **Optional code is conditional, not deleted.**
  - C#: `#if (SAMPLE)` … `#endif`. `Directory.Build.props` defines `SAMPLE` only in the template repository, so the
    template builds and tests every optional part.
  - Project files, `.slnx` and Markdown: `<!--#if (sample)-->` … `<!--#endif-->`.
  - `appsettings*.json`: `//#if (…)` / `//#else` / `//#endif`. A line in an inactive branch is commented out (`//"Key": value,`)
    and is uncommented when its branch is chosen.
  - `.env*`: the same, with `#`.
  - Inside these JSON and `.env` blocks, every `//` or `#` is removed. Keep the blocks small and free of URLs.
- **Never put an MSBuild `Condition` attribute on an element that must survive.** `dotnet new` evaluates conditions in
  project files as template conditions and drops the element (see `src/Api/Api.csproj`). Filter items instead, for
  example with `WithMetadataValue`.
- **Whole files** that belong to one switch are listed under `sources.modifiers` in `.template.config/template.json`.
- **Never end a file on an end marker.** With CRLF line endings (every `*.cs` file, `.gitattributes`), `dotnet new`
  leaves an end marker that sits on the file's last line in the output. Keep a line of content after a conditional
  block. The templates workflow checks it.

The `templates.yml` workflow packs the templates and generates products with several switch combinations, plus
modules inside them. Each one must build, pass its tests and the docs check, and contain no leftover template name.
Run the same check locally:

```bash
dotnet new install .            # from the repository root
dotnet new starterkit -n Probe -o ../probe --sample false
cd ../probe && dotnet build Probe.slnx && dotnet test src/Api.Tests && bash scripts/check-docs.sh
```

## Module template migrations

`templates/module/_migrations/<combo>/` holds one `InitialCreate` set per option combination that has a model
(tenant/host × example/outbox; the empty skeleton without an outbox has none). They are produced by `dotnet ef`, not
written by hand, and the tenant sets get `EnableTenantRowLevelSecurity` for every table. After changing the example
module's model, run `bash scripts/regenerate-module-migrations.sh` and review the diff. The CI matrix proves every set
matches its model (`PendingModelChangesTests`).

## Why the docs avoid marker syntax

`dotnet new` processes Markdown files too, and it reads the marker syntax anywhere on a line. A page that ships in a
product must describe the markers in words, never quote them. This page and
[ADR-0020](../Decisions/0020-dotnet-new-templates.md) are excluded from generated products, so they can.

Design and rationale: [ADR-0020](../Decisions/0020-dotnet-new-templates.md).
