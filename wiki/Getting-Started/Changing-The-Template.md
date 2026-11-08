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
dotnet new uninstall StarterKit.Templates   # if the published pack is installed: it provides the same templates
dotnet new install .            # from the repository root (both templates)
dotnet new starterkit -n Probe -o ../probe --sample false
cd ../probe && dotnet build Probe.slnx && dotnet test src/Api.Tests && bash scripts/check-docs.sh
```

## Releasing the templates

A release is a `templates-v<version>` tag on `master`. The tag starts the `Templates` workflow, which packs
`StarterKit.Templates` with the tag's version (`templates-v1.1.0` gives `1.1.0`), generates and tests the four
products again, and only when all of them pass, publishes the pack to GitHub Packages.

1. Merge the template change through a PR as usual; `templates.yml` must be green.
2. Create the tag on `master`, either way:
   - **GitHub website:** Releases → *Draft a new release* → *Choose a tag* → type `templates-v1.1.0` → *Create new tag
     on publish*, target `master` → *Publish release*. The release also gives you a place for the notes.
   - **Terminal:**
     ```bash
     git checkout master && git pull --ff-only origin master
     git tag -a templates-v1.1.0 -m "StarterKit.Templates 1.1.0"
     git push origin templates-v1.1.0
     ```
3. Watch the `Templates` run in the Actions tab. The release is out when *Publish to GitHub Packages* is green.

- *Run workflow* in the Actions tab tests the templates but never publishes: publishing needs the tag.
- A published version can't be replaced (the push skips duplicates). A broken release is fixed by a new version
  (`templates-v1.1.1`), never by moving the tag.
- Semantic versions: a fix is a patch, a new switch or module option is a minor, and a change that breaks generated
  products or the template options (a renamed or removed switch) is a major.
- Then update your own machine with `dotnet new update`, run outside a repository ([Templates](Templates.md#install)).

## Test framework of generated modules

The module template references `xunit.v3.mtp-off` (xUnit v3 without Microsoft.Testing.Platform, so VSTest `dotnet test`,
filters and coverlet work as before). A product generated before the xUnit v3 move still lists `xunit` 2.9.3 in its
`Directory.Packages.props`, so a module generated into it with a newer pack does not restore. Move the product first:
replace `xunit` with `xunit.v3.mtp-off` there and in its test projects, add `<OutputType>Exe</OutputType>` to each test
project, and turn `IAsyncLifetime` members into `ValueTask`. `git log` of the template repository has the full change.

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
