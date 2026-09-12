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

log 'build contracts are enforced'
