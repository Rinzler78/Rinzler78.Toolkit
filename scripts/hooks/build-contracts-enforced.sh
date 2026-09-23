#!/usr/bin/env bash
# The guards in Rinzler78.Build.targets only ever run in a consuming project: a
# package does not import itself. Verifying them therefore requires a scratch
# consumer. Without one, these rules are prose that happens to be XML.
#
# The consumer also supplies the *policy* — accepted layers, forbidden
# dependencies, required analysers, required description fragment — because this
# package knows how to refuse, not what to refuse.
# shellcheck source=scripts/_common.sh
source "$(dirname "${BASH_SOURCE[0]}")/../_common.sh"

readonly BUILD_DIR="$REPO_ROOT/src/Rinzler78.Build/build"
scratch=$(mktemp -d)
trap 'rm -rf "$scratch"' EXIT

# A domain's policy, written into its own file — the way a consuming repository
# carries it in Directory.Build.props, rather than as an escaped shell string.
cat >"$scratch/Policy.props" <<'PROPS'
<Project>
  <PropertyGroup>
    <RinzlerKnownLayers>;Contracts;Services;Toolchain;</RinzlerKnownLayers>
    <RinzlerRequiredDescription>Not affiliated with Acme</RinzlerRequiredDescription>
  </PropertyGroup>
  <ItemGroup Condition="'$(RinzlerLayer)' == 'Contracts'">
    <RinzlerForbiddenDependency Include="Microsoft.Extensions.Http" />
    <RinzlerRequiredAnalyzer Include="Microsoft.CodeAnalysis.BannedApiAnalyzers"
                             Because="the dependency rule catches declared dependencies, the banned-symbol list catches the call site." />
  </ItemGroup>
</Project>
PROPS

readonly ANALYSER='<ItemGroup><PackageReference Include="Microsoft.CodeAnalysis.BannedApiAnalyzers" Version="5.6.0" PrivateAssets="all" /></ItemGroup>'
readonly HTTP='<ItemGroup><PackageReference Include="Microsoft.Extensions.Http" Version="9.0.0" /></ItemGroup>'

# $1 layer, $2 extra project content, $3 description
write_consumer() {
  local layer=$1 extra=${2:-} description=${3:-'Scratch consumer. Not affiliated with Acme.'}
  cat >"$scratch/Consumer.csproj" <<PROJ
<Project Sdk="Microsoft.NET.Sdk">
  <Import Project="$BUILD_DIR/Rinzler78.Build.props" />
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <RinzlerLayer>$layer</RinzlerLayer>
    <Description>$description</Description>
  </PropertyGroup>
  <Import Project="$scratch/Policy.props" />
  $extra
  <Import Project="$BUILD_DIR/Rinzler78.Build.targets" />
</Project>
PROJ
}

build_succeeds() {
  local label=$1
  if ! output=$(dotnet build "$scratch/Consumer.csproj" --nologo "${@:2}" 2>&1); then
    printf '%s\n' "$output" | tail -10
    fail "$label: refused a valid project"
  fi
  log "$label: accepted"
}

build_refuses() {
  local label=$1 expected=$2
  if output=$(dotnet build "$scratch/Consumer.csproj" --nologo "${@:3}" 2>&1); then
    fail "$label: the build succeeded when it should have failed"
  fi
  grep -qF "$expected" <<<"$output" || {
    printf '%s\n' "$output" | tail -5
    fail "$label: failed, but not on the expected rule"
  }
  log "$label: refused as expected"
}

pack_refuses() {
  local label=$1 expected=$2
  if output=$(dotnet pack "$scratch/Consumer.csproj" --nologo --output "$scratch/out" 2>&1); then
    fail "$label: packed when it should have failed"
  fi
  grep -qF "$expected" <<<"$output" || {
    printf '%s\n' "$output" | tail -5
    fail "$label: failed, but not on the expected rule"
  }
  log "$label: refused as expected"
}

# --- the mechanisms, each exercised rather than asserted ---

write_consumer Contracts "$ANALYSER"
build_succeeds 'a conforming project'

build_refuses 'invalid coverage contract' 'RinzlerCoverageContract must be' -p:RinzlerCoverageContract=Bogus
build_refuses 'invalid trim contract' 'RinzlerTrimContract must be' -p:RinzlerTrimContract=Bogus
build_succeeds 'declared contracts' -p:RinzlerTrimContract=Trimmable -p:RinzlerCoverageContract=Reduced

write_consumer Contracts "$ANALYSER$HTTP"
build_refuses 'outward dependency' 'may not depend on'

write_consumer Contracts
build_refuses 'missing required analyser' 'BannedApiAnalyzers'

write_consumer NotALayer "$ANALYSER"
build_refuses 'layer outside the declared vocabulary' 'is not one of'

write_consumer Contracts "$ANALYSER" 'A package that says nothing.'
pack_refuses 'missing required description fragment' 'Not affiliated with Acme'

# A packable project with no layer cannot be checked, and an unchecked package is
# how the rule quietly stops applying.
cat >"$scratch/Consumer.csproj" <<PROJ
<Project Sdk="Microsoft.NET.Sdk">
  <Import Project="$BUILD_DIR/Rinzler78.Build.props" />
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <RinzlerKnownLayers>;Contracts;Services;Toolchain;</RinzlerKnownLayers>
    <Description>Scratch consumer. Not affiliated with Acme.</Description>
  </PropertyGroup>
  <Import Project="$BUILD_DIR/Rinzler78.Build.targets" />
</Project>
PROJ
build_refuses 'packable project with no layer' 'must declare RinzlerLayer'

# The toolkit must stay usable outside any one ecosystem: a project declaring no
# policy at all builds, rather than being failed for having no architecture.
cat >"$scratch/Consumer.csproj" <<PROJ
<Project Sdk="Microsoft.NET.Sdk">
  <Import Project="$BUILD_DIR/Rinzler78.Build.props" />
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Description>A project with no declared architecture.</Description>
  </PropertyGroup>
  <Import Project="$BUILD_DIR/Rinzler78.Build.targets" />
</Project>
PROJ
build_succeeds 'a project declaring no policy'

# A package with no build output — props/targets, a template pack — must pack
# cleanly. It used to emit NU5017 for an empty symbol package while still writing the
# main one, which is the worst shape of failure: visible and ignorable.
cat >"$scratch/NoOutput.csproj" <<PROJ
<Project Sdk="Microsoft.NET.Sdk">
  <Import Project="$BUILD_DIR/Rinzler78.Build.props" />
  <PropertyGroup>
    <TargetFramework>netstandard2.0</TargetFramework>
    <IncludeBuildOutput>false</IncludeBuildOutput>
    <NoWarn>\$(NoWarn);NU5128</NoWarn>
    <RinzlerLayer>Toolchain</RinzlerLayer>
    <Description>A package that ships no assembly. Not affiliated with Acme.</Description>
  </PropertyGroup>
  <ItemGroup>
    <!-- NuGet requires a props file under build/ to be named after the package id,
         so the fixture renames it rather than tripping NU5129 on its own layout. -->
    <None Include="$BUILD_DIR/Rinzler78.Build.props" Pack="true" PackagePath="build/NoOutput.props" />
  </ItemGroup>
  <Import Project="$BUILD_DIR/Rinzler78.Build.targets" />
</Project>
PROJ
if ! output=$(dotnet pack "$scratch/NoOutput.csproj" --nologo --output "$scratch/out" 2>&1); then
  printf '%s\n' "$output" | tail -5
  fail 'a package with no build output failed to pack'
fi
grep -q 'NU5017' <<<"$output" && fail 'pack emitted NU5017 for an empty symbol package'
log 'a package with no build output packs cleanly'

log 'build mechanisms are enforced, and neutral where nothing is declared'
