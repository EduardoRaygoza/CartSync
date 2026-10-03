# CartSync API Contract

`openapi.yaml` is the accepted `/api/v1` contract. The production ASP.NET Core
application generates OpenAPI 3.1, and CI compares that output with this file.

Generate the Angular 22 client with a pinned OpenAPI Generator release:

```bash
docker run --rm \
  -v "$PWD:/workspace" \
  openapitools/openapi-generator-cli:v7.16.0 generate \
  -i /workspace/docs/api/openapi.yaml \
  -c /workspace/docs/api/openapi-generator-config.json \
  -o /workspace/generated/api-client
```

Generated code is a build artifact. Do not hand-edit it or use it as the source
of the contract.
