#!/usr/bin/env bash

set -euo pipefail
umask 077

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd -P)"
RID="${1:-}"
OUTPUT_DIR="${2:-}"
PROJECT="$SCRIPT_DIR/src/Acmeshd/Acmeshd.csproj"

if [[ -z "$RID" || -z "$OUTPUT_DIR" ]]; then
  echo "Usage: $0 <rid> <output-dir>" >&2
  exit 1
fi

if [ -z "${P12_BASE64-}" ] || [ -z "${P12_BASE64// }" ]; then
  echo "P12_BASE64 is not defined" >&2
  exit 1
fi

TMP="$(mktemp -d)"
trap 'rm -rf "$TMP"' EXIT INT SIGTERM

GHP="$TMP/gh-pick.sh"
curl -fsSL "https://raw.githubusercontent.com/Itexoft/devops/refs/heads/master/gh-pick.sh" -o "$GHP"
chmod +x "$GHP"

SNK="$TMP/strongname.snk"
CCR=$("$GHP" "@master" "lib/cert-converter.sh")
"$CCR" "$P12_BASE64" snk "$SNK"

ARGS=("-c" "Release" "$PROJECT" "-o" "$OUTPUT_DIR" "-r" "$RID")
ARGS+=("-p:SignAssembly=true" "-p:PublicSign=false" "-p:AssemblyOriginatorKeyFile=$SNK" "--cert=$P12_BASE64")

if [ -n "${P12_PASSWORD-}" ]; then
  ARGS+=("--password=$P12_PASSWORD")
fi

mkdir -p "$OUTPUT_DIR"
DSP=$("$GHP" "@master" "lib/dotnet-sign-publish.sh")
"$DSP" "${ARGS[@]}"
