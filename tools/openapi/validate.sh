#!/usr/bin/env bash
set -euo pipefail

generator_image='openapitools/openapi-generator-cli@sha256:e56372add5e038753fb91aa1bbb470724ef58382fdfc35082bf1b3e079ce353c' # v7.16.0
docker run --rm \
  --user "$(id -u):$(id -g)" \
  --volume "$PWD:/workspace" \
  "$generator_image" validate \
  -i /workspace/docs/api/openapi.yaml
