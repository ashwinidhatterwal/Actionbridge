#!/bin/sh
set -eu
base=$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)
output=${1:-"$base/ubuntu-release"}
mkdir -p "$output/runtime"
output=$(CDPATH= cd -- "$output" && pwd)
dotnet publish "$base/ubuntu/ActionBridge.Ubuntu" -c Release -r linux-x64 --self-contained true -m:1 -p:RuntimeFrameworkVersion=10.0.12 -o "$output/runtime"
(cd "$base/remote/receiver" && CGO_ENABLED=0 GOOS=linux GOARCH=amd64 go build -buildvcs=false -trimpath -ldflags="-s -w" -o "$output/runtime/ActionBridge.Remote" .)
python3 "$base/ubuntu/packaging/build-deb.py" "$output/runtime" "$output"
