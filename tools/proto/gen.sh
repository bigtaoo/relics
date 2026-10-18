#!/usr/bin/env bash
# Regenerates src/Net/Generated/*.cs from src/Net/Proto/*.proto (ADR-013).
# protoc comes from the Google.Protobuf.Tools NuGet package of the same version as the runtime
# (src/Net/Plugins/Google.Protobuf.dll, Net.csproj); it is cached under artifacts/protoc.
set -euo pipefail
cd "$(dirname "$0")/../.."
VERSION=3.30.1
DIR=artifacts/protoc/$VERSION
case "$(uname -s)" in
  MINGW*|MSYS*|CYGWIN*) PROTOC=$DIR/tools/windows_x64/protoc.exe ;;
  Darwin) PROTOC=$DIR/tools/macosx_x64/protoc ;;
  *) PROTOC=$DIR/tools/linux_x64/protoc ;;
esac
if [ ! -x "$PROTOC" ]; then
  mkdir -p "$DIR"
  curl -sSL -o "$DIR/tools.nupkg" "https://api.nuget.org/v3-flatcontainer/google.protobuf.tools/$VERSION/google.protobuf.tools.$VERSION.nupkg"
  unzip -q -o "$DIR/tools.nupkg" -d "$DIR"
  chmod +x "$PROTOC" 2>/dev/null || true
fi
"$PROTOC" --proto_path=src/Net/Proto --csharp_out=src/Net/Generated src/Net/Proto/*.proto
echo "generated: $(ls src/Net/Generated/*.cs)"
