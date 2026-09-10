#!/usr/bin/env bash
#
# Build the API image locally. The bash counterpart of docker.ps1.
#
#   ./docker.sh

set -euo pipefail

docker build . \
	--tag kompaz-web-backend:develop \
	--file src/Presentation/Dockerfile
