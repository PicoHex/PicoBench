#!/usr/bin/env bash
# Validates the release trigger before anything else runs (review item B5-H2).
#
# git allows tag names like `v1.0.0$(id)` or `v1.0.0";id;"`, and the release
# workflow used to strip the `refs/tags/v` prefix and inline the remainder into
# `run:` scripts. This script never interpolates the trigger text into a shell:
# every value arrives through the environment, is checked against a strict
# pattern, and only the validated version leaves the script (as a step output).
#
# Inputs (environment):
#   EVENT_NAME     github.event_name
#   REF_NAME       github.ref_name
#   REF_TYPE       github.ref_type
#   INPUT_VERSION  inputs.version (workflow_dispatch only)
#   GITHUB_OUTPUT  step output file (optional, written when set)
#
# Outputs: `version=<year>.<x>.<y>` and `tag=v<year>.<x>.<y>`.
set -euo pipefail

fail() {
    echo "::error::$1" >&2
    exit 1
}

event="${EVENT_NAME:-}"
ref_name="${REF_NAME:-}"
ref_type="${REF_TYPE:-}"
input_version="${INPUT_VERSION:-}"

case "$event" in
    push)
        [ "$ref_type" = "tag" ] ||
            fail "the release workflow must run on a tag (ref_type='$ref_type')"
        tag="$ref_name"
        version="${tag#v}"
        ;;
    workflow_dispatch)
        [ -n "$input_version" ] ||
            fail "workflow_dispatch requires the 'version' input (e.g. 2026.4.0); the release is cut from the existing v<version> tag"
        version="$input_version"
        tag="v$version"
        ;;
    *)
        fail "unsupported release trigger '$event'"
        ;;
esac

# <four-digit year>.<x>.<y>, exactly the shape scripts/release.ps1 produces.
if ! printf '%s' "$version" | grep -Eq '^[0-9]{4}\.[0-9]+\.[0-9]+$'; then
    fail "malformed release version '$version': expected <year>.<x>.<y>, e.g. 2026.4.0"
fi

if [ "$tag" != "v$version" ]; then
    fail "tag '$tag' does not match version '$version'"
fi

echo "release version: $version (tag $tag)"

if [ -n "${GITHUB_OUTPUT:-}" ]; then
    {
        printf 'version=%s\n' "$version"
        printf 'tag=%s\n' "$tag"
    } >> "$GITHUB_OUTPUT"
fi
