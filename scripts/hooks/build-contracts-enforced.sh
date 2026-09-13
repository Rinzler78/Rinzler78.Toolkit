#!/usr/bin/env bash
# The guards in Rinzler78.Build.targets only ever run in a consuming project: a
# package does not import itself. Verifying them therefore requires a scratch
# consumer. Without this, the guards are prose that happens to be XML.
# shellcheck source=scripts/_common.sh
source "$(dirname "${BASH_SOURCE[0]}")/../_common.sh"

readonly BUILD_DIR="$REPO_ROOT/src/Rinzler78.Build/build"
scratch=$(mktemp -d)
trap 'rm -rf "$scratch"' EXIT

cat >"$scratch/Consumer.csproj" <<PROJ
<Project Sdk="Microsoft.NET.Sdk">
  <Import Project="$BUILD_DIR/Rinzler78.Build.props" />
  <PropertyGroup>
    <TargetFramework>netstandard2.0</TargetFramework>
    <HereLayer>Toolchain</HereLayer>
    <Description>Scratch consumer. Not affiliated with HERE Technologies.</Description>
  </PropertyGroup>
  <Import Project="$BUILD_DIR/Rinzler78.Build.targets" />
</Project>
PROJ

expect_failure() {
  local label=$1 expected=$2
  shift 2
  if output=$(dotnet build "$scratch/Consumer.csproj" --nologo "$@" 2>&1); then
    printf '%s\n' "$output" | tail -5
    fail "$label: the build succeeded when it should have failed"
  fi
  if ! grep -qF "$expected" <<<"$output"; then
    printf '%s\n' "$output" | tail -5
    fail "$label: failed, but not with the expected message"
  fi
  log "$label: refused as expected"
}

expect_success() {
  local label=$1
  shift
  if ! output=$(dotnet build "$scratch/Consumer.csproj" --nologo "$@" 2>&1); then
    printf '%s\n' "$output" | tail -10
    fail "$label: a valid combination was refused"
  fi
  log "$label: accepted"
}

expect_success 'defaults'
expect_failure 'invalid coverage contract' 'HereCoverageContract must be' -p:HereCoverageContract=Bogus
expect_failure 'invalid trim contract' 'HereTrimContract must be' -p:HereTrimContract=Bogus
expect_success 'declared contracts' -p:HereTrimContract=Trimmable -p:HereCoverageContract=Reduced

# The non-affiliation guard fires on pack, not on build, so it needs its own case.
cat >"$scratch/Silent.csproj" <<PROJ
<Project Sdk="Microsoft.NET.Sdk">
  <Import Project="$BUILD_DIR/Rinzler78.Build.props" />
  <PropertyGroup>
    <TargetFramework>netstandard2.0</TargetFramework>
    <IsPackable>true</IsPackable>
    <HereLayer>Toolchain</HereLayer>
    <Description>A package that says nothing about HERE.</Description>
  </PropertyGroup>
  <Import Project="$BUILD_DIR/Rinzler78.Build.targets" />
</Project>
PROJ

if output=$(dotnet pack "$scratch/Silent.csproj" --nologo --output "$scratch/out" 2>&1); then
  fail 'non-affiliation: a package packed without the statement'
fi
grep -qF 'not affiliated with HERE Technologies' <<<"$output" ||
  fail 'non-affiliation: failed, but not with the expected message'
log 'non-affiliation: refused as expected'

# The dependency rule. A layer that cannot be caught reaching outward is a drawing,
# not a constraint, so each case is exercised rather than asserted.
write_layered_consumer() {
  local layer=$1 extra=${2:-}
  cat >"$scratch/Layered.csproj" <<PROJ
<Project Sdk="Microsoft.NET.Sdk">
  <Import Project="$BUILD_DIR/Rinzler78.Build.props" />
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <HereLayer>$layer</HereLayer>
    <Description>Scratch consumer. Not affiliated with HERE Technologies.</Description>
  </PropertyGroup>
  $extra
  <Import Project="$BUILD_DIR/Rinzler78.Build.targets" />
</Project>
PROJ
}

readonly HTTP_REFERENCE='<ItemGroup><PackageReference Include="Microsoft.Extensions.Http" Version="9.0.0" /></ItemGroup>'
# An inward layer must carry the banned-symbol analyser; a clean consumer therefore
# declares it, and its absence is exercised as its own case below.
readonly BANNED_ANALYSER='<ItemGroup><PackageReference Include="Microsoft.CodeAnalysis.BannedApiAnalyzers" Version="5.6.0" PrivateAssets="all" /></ItemGroup>'
# A packable project that ships a public surface must carry the API baseline
# analyser: the surface diff is what decides the published version.
readonly API_ANALYSER='<ItemGroup><PackageReference Include="Microsoft.CodeAnalysis.PublicApiAnalyzers" Version="5.6.0" PrivateAssets="all" /></ItemGroup>'
readonly INWARD_ANALYSERS="$BANNED_ANALYSER$API_ANALYSER"

write_layered_consumer Contracts "$HTTP_REFERENCE$INWARD_ANALYSERS"
if output=$(dotnet build "$scratch/Layered.csproj" --nologo 2>&1); then
  printf '%s\n' "$output" | tail -5
  fail 'dependency rule: a contract assembly reached an HTTP client and the build passed'
fi
grep -qF 'may not depend on' <<<"$output" ||
  {
    printf '%s\n' "$output" | tail -5
    fail 'dependency rule: failed, but not on the rule'
  }
log 'dependency rule: outward reference refused'

write_layered_consumer Contracts "$INWARD_ANALYSERS"
expect_layered_success() {
  if ! output=$(dotnet build "$scratch/Layered.csproj" --nologo 2>&1); then
    printf '%s\n' "$output" | tail -10
    fail "$1: a clean layer was refused"
  fi
  log "$1: accepted"
}
expect_layered_success 'clean contract layer'

# The analyser requirements themselves.
write_layered_consumer Contracts "$API_ANALYSER"
if output=$(dotnet build "$scratch/Layered.csproj" --nologo 2>&1); then
  fail 'analyser requirement: an inward layer built without the banned-symbol analyser'
fi
grep -qF 'BannedApiAnalyzers' <<<"$output" ||
  fail 'analyser requirement: failed, but not on the analyser'
log 'analyser requirement: missing banned-symbol analyser refused'

write_layered_consumer Contracts "$BANNED_ANALYSER"
if output=$(dotnet build "$scratch/Layered.csproj" --nologo 2>&1); then
  fail 'API baseline: a package shipping a public surface built without the baseline analyser'
fi
grep -qF 'PublicApiAnalyzers' <<<"$output" ||
  fail 'API baseline: failed, but not on the analyser'
log 'API baseline: missing baseline analyser refused'

# A typo in HereLayer would silently disable the dependency rule.
write_layered_consumer NotALayer "$INWARD_ANALYSERS"
if output=$(dotnet build "$scratch/Layered.csproj" --nologo 2>&1); then
  fail 'layer validation: a misspelt layer was accepted'
fi
grep -qF 'is not one of' <<<"$output" ||
  fail 'layer validation: failed, but not on the layer name'
log 'layer validation: misspelt layer refused'

# A packable project with no layer cannot be checked, and an unchecked package is
# how the rule quietly stops applying.
cat >"$scratch/Unassigned.csproj" <<PROJ
<Project Sdk="Microsoft.NET.Sdk">
  <Import Project="$BUILD_DIR/Rinzler78.Build.props" />
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <IsPackable>true</IsPackable>
    <Description>Scratch consumer. Not affiliated with HERE Technologies.</Description>
  </PropertyGroup>
  <Import Project="$BUILD_DIR/Rinzler78.Build.targets" />
</Project>
PROJ
if output=$(dotnet build "$scratch/Unassigned.csproj" --nologo 2>&1); then
  fail 'layer requirement: a packable project built without declaring its layer'
fi
grep -qF 'must declare HereLayer' <<<"$output" ||
  fail 'layer requirement: failed, but not on the rule'
log 'layer requirement: unassigned packable project refused'

log 'build contracts are enforced'
