#!/usr/bin/env bash
# Builds the Function App container image used by the integration tests.
set -euo pipefail
IMAGE="${1:-sample-functions:test}"
REPOSITORY="${IMAGE%%:*}"
TAG="${IMAGE#*:}"
[[ "$TAG" == "$IMAGE" ]] && TAG=latest
ROOT="$(cd "$(dirname "$0")/.." && pwd)"

dotnet publish "$ROOT/src/Sample.Functions" \
  -c Release -r linux-x64 -t:PublishContainer --nologo \
  "-p:ContainerRepository=$REPOSITORY" "-p:ContainerImageTag=$TAG"
echo "Built $IMAGE"