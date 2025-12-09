#!/usr/bin/env bash
set -euo pipefail
CONFIG=${CONFIG:-Release}

echo "Restaurando pacotes..."
dotnet restore

echo "Publicando win-x64..."
dotnet publish -c "$CONFIG" -r win-x64 -p:PublishSingleFile=false -o dist/win

echo "Publicando linux-x64..."
dotnet publish -c "$CONFIG" -r linux-x64 -p:PublishSingleFile=false -o dist/linux

echo "Concluído. Saídas em dist/win e dist/linux"
