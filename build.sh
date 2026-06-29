#!/bin/bash
set -e
curl -sSL https://dot.net/v1/dotnet-install.sh | bash -s -- --channel 8.0 --install-dir "$HOME/.dotnet"
export PATH="$HOME/.dotnet:$PATH"
dotnet publish src/Duels.Web/Duels.Web.csproj -c Release -o release --nologo
