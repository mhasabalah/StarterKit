#!/usr/bin/env bash
# Regenerates the module template's InitialCreate migration sets (templates/module/_migrations/<combo>/) after the
# example module's model changed. ADR-0020: every option combination that has a model ships its own set, produced by
# `dotnet ef` from that exact combination; tenant-data sets get forced RLS on every table.
#
# Needs: the .NET SDK (global.json), Docker NOT required (EF only builds the model). Run from anywhere:
#   bash scripts/regenerate-module-migrations.sh
# Then review `git diff templates/module/_migrations` and let the templates workflow (or a local run of it) prove
# the result: every generated module must pass PendingModelChangesTests.
set -euo pipefail

REPO="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
WORK="$(mktemp -d)"
trap 'dotnet new uninstall "$REPO" >/dev/null 2>&1 || true; dotnet new uninstall "$REPO/templates/module" >/dev/null 2>&1 || true; rm -rf "$WORK"' EXIT

EF_VERSION="$(grep -o 'Include="Microsoft.EntityFrameworkCore.Design" Version="[^"]*"' "$REPO/Directory.Packages.props" | grep -o '[0-9][0-9.]*')"

# combo-folder | starterkit-module switches   (keep in sync with the conditional sources in templates/module)
COMBOS=(
  "tenant-example|"
  "tenant-example-outbox|--outbox"
  "tenant-outbox|--outbox --example false"
  "host-example|--data-plane host"
  "host-example-outbox|--data-plane host --outbox"
  "host-outbox|--data-plane host --outbox --example false"
)

echo "== product + EF $EF_VERSION in $WORK"
dotnet new install "$REPO" >/dev/null
dotnet new install "$REPO/templates/module" >/dev/null
(cd "$WORK" && dotnet new tool-manifest >/dev/null && dotnet tool install dotnet-ef --version "$EF_VERSION" >/dev/null)

# The migration sets must exist (even empty) for the module template's conditional sources to install.
for entry in "${COMBOS[@]}"; do mkdir -p "$REPO/templates/module/_migrations/${entry%%|*}"; done

dotnet new starterkit -n Probe -o "$WORK/Probe" >/dev/null
PRODUCT="$WORK/Probe"
MODULE_DIR="$PRODUCT/src/Modules/ModuleName"

for entry in "${COMBOS[@]}"; do
  combo="${entry%%|*}"
  switches="${entry#*|}"
  echo "== $combo ($switches)"
  rm -rf "$MODULE_DIR"
  # shellcheck disable=SC2086
  (cd "$PRODUCT" && dotnet new starterkit-module -n ModuleName $switches >/dev/null)
  dotnet build "$PRODUCT/src/Api/Api.csproj" -v q -nologo >/dev/null
  (cd "$WORK" && dotnet tool run dotnet-ef migrations add InitialCreate --context ModuleNameDbContext \
    --project "$MODULE_DIR/ModuleName" --startup-project "$PRODUCT/src/Api" --output-dir Data/Migrations --no-build >/dev/null)

  dest="$REPO/templates/module/_migrations/$combo"
  rm -rf "$dest" && mkdir -p "$dest"
  cp "$MODULE_DIR"/ModuleName/Data/Migrations/*.cs "$dest/"
  # dotnet ef writes a UTF-8 BOM; .editorconfig (charset = utf-8) and the Quality workflow reject it.
  for f in "$dest"/*.cs; do sed -i '1s/^\xEF\xBB\xBF//' "$f"; done

  if [[ "$combo" == tenant-* ]]; then
    python3 - "$dest" <<'PY'
import glob, re, sys
path = [p for p in glob.glob(sys.argv[1] + '/*_InitialCreate.cs') if not p.endswith('.Designer.cs')][0]
src = open(path, encoding='utf-8-sig').read()
up = src[src.index('protected override void Up'):src.index('protected override void Down')]
tables = re.findall(r'CreateTable\(\s*name: "([a-z_]+)",\s*schema: "modulename"', up)
calls = ''.join(f'            migrationBuilder.EnableTenantRowLevelSecurity("modulename", "{t}");\n' for t in tables)
block = ('\n            // Tenant data: forced RLS + the tenant_isolation policy on every table '
         '(TenantRowLevelSecurityCoverageTests).\n' + calls)
down_at = src.index('        /// <inheritdoc />\n        protected override void Down')
up_end = src.rindex('        }\n', 0, down_at)
src = src[:up_end] + block + src[up_end:]
src = src.replace('using Microsoft.EntityFrameworkCore.Migrations;\n',
                  'using Microsoft.EntityFrameworkCore.Migrations;\nusing Shared.Core.MultiTenancy.Data;\n', 1)
open(path, 'w', encoding='utf-8', newline='\n').write(src)
print('   RLS:', ', '.join(tables))
PY
  fi
done

echo "== done: review git diff templates/module/_migrations"
