# ComfyBridge API

ComfyBridge API is a .NET 9 service that sits in front of ComfyUI and exposes stable endpoints for workflow-based generation.

It provides:
- Job-based image generation (`202 Accepted` + polling)
- Template discovery and dynamic run endpoints
- Workflow analyze/save APIs
- Built-in Razor Pages UI for workflow import/testing

## What Is Implemented

### API endpoints
- `POST /api/v1/generate/image`
- `POST /api/v1/generate/video` (currently returns `501 Not Implemented`)
- `GET /api/v1/jobs/{jobId}`
- `GET /api/v1/templates`
- `POST /api/v1/{category}/{name}` (dynamic run endpoint, latest version by name)
- `GET /api/v1/workflows/templates`
- `POST /api/v1/workflows/analyze`
- `POST /api/v1/workflows/save`
- `DELETE /api/v1/workflows/templates/{name}/{version}`

### Built-in web pages
- `/workflows`
- `/workflows/import`
- `/workflows/{id}`
- `/workflows/edit/{id}`
- `/workflows/test/{id}`
- `/api-explorer`

### OpenAPI
- In Development environment, OpenAPI is mapped with `MapOpenApi()`.

## Prerequisites

- .NET SDK 9.0+
- ComfyUI running and reachable
- At least one usable workflow/model setup in ComfyUI

## Configuration

Configuration files:
- `ComfyBridge.Api/appsettings.json`
- `ComfyBridge.Api/appsettings.Development.json`

Current defaults:

```json
{
  "ComfyUi": {
    "BaseUrl": "http://127.0.0.1:8189",
    "PollIntervalMs": 1000,
    "JobTimeoutSeconds": 120
  },
  "TemplateStorage": {
    "TemplatesPath": "Templates"
  },
  "WorkflowAi": {
    "Provider": "Ollama",
    "Endpoint": "http://127.0.0.1:11434",
    "Model": "llama3.1",
    "MaxTemplateValidationAttempts": 3,
    "AllowHeuristicFallback": true
  },
  "JobStore": {
    "Provider": "InMemory"
  }
}
```

Update at least:
1. `ComfyUi.BaseUrl` to match your ComfyUI instance.
2. `WorkflowAi` values if using workflow analysis features.
3. `TemplateStorage.TemplatesPath` only if you want a different template folder.

## Run The App

From repository root:

```powershell
dotnet restore .\ComfyBridgeAPI.sln
dotnet build .\ComfyBridgeAPI.sln
dotnet run --project .\ComfyBridge.Api\ComfyBridge.Api.csproj
```

Default URLs (from launch settings):
- HTTP: `http://localhost:5176`
- HTTPS: `https://localhost:7051`

## How To Use ComfyBridge.Api

### 1) Ensure ComfyUI is reachable

ComfyBridge expects ComfyUI APIs to be available (prompt submission/history/view flow). If ComfyUI is down, jobs end as failed.

### 2) Check available templates

```http
GET /api/v1/templates
```

### 3) Submit a generation job (template route)

```http
POST /api/v1/generate/image
Content-Type: application/json

{
  "template": "txt2img-basic",
  "inputs": {
    "prompt": "a cinematic dragon flying over mountains",
    "width": 512,
    "height": 512,
    "steps": 30,
    "cfg": 7.5,
    "seed": 42
  }
}
```

Expected response: `202 Accepted` with `jobId` and `status`.

### 4) Poll job status

```http
GET /api/v1/jobs/{jobId}
```

Terminal states are `Completed` or `Failed`.

### 5) Use dynamic endpoint per template category/name

```http
POST /api/v1/{category}/{name}
Content-Type: application/json
```

Important behavior:
- It resolves the latest version of `{name}`.
- Body must include exactly the declared input keys for that template.
- Unknown fields or missing fields return validation errors.

Example:

```json
{
  "prompt": "ultra detailed cyberpunk city",
  "steps": 25,
  "cfg": 7
}
```

### 6) Use the browser UI (optional)

Open:
- `http://localhost:5176/workflows` for workflow management
- `http://localhost:5176/api-explorer` to test generated dynamic endpoints from a UI

## Template Storage And Formats

Templates are loaded from `TemplateStorage:TemplatesPath`.

The app supports two layouts:

1. Legacy flat file
- `{name}.{version}.json`
- If the file is a raw ComfyUI graph, it is treated as a legacy workflow template with empty public input mapping.

2. Folder layout (used by save flow)
- `{name}.{version}/template.json`
- `{name}.{version}/raw-workflow.json`
- `{name}.{version}/mapping.json`

`mapping.json` is treated as the authoritative source for `inputs` and `mapping` in the folder layout.

## Workflow APIs

### Analyze workflow JSON

```http
POST /api/v1/workflows/analyze
Content-Type: application/json

{
  "workflowJson": "{ ... raw ComfyUI JSON as string ... }"
}
```

### Save template

```http
POST /api/v1/workflows/save
Content-Type: application/json

{
  "name": "txt2img-basic",
  "version": "1.0",
  "category": "text2image",
  "workflowJson": "{ ... raw ComfyUI JSON as string ... }",
  "inputs": {
    "prompt": "string",
    "steps": "int"
  },
  "mapping": {
    "prompt": {
      "nodeClass": "CLIPTextEncode",
      "nodeIndex": 0,
      "field": "text"
    },
    "steps": {
      "nodeClass": "KSampler",
      "field": "steps"
    }
  }
}
```

Validation notes:
- `workflowJson` is required and must be valid JSON object.
- Max workflow JSON size is 2 MB.

## Quick Local Test (curl)

```powershell
curl.exe -X POST "http://localhost:5176/api/v1/generate/image" `
  -H "Content-Type: application/json" `
  -d "{\"template\":\"txt2img-basic\",\"inputs\":{\"prompt\":\"a cinematic dragon flying over mountains\",\"steps\":30,\"cfg\":7.5,\"seed\":42}}"
```

Then:

```powershell
curl.exe "http://localhost:5176/api/v1/jobs/<jobId>"
```

## Notes

- Current job storage provider is `InMemory`.
- `POST /api/v1/generate/video` is reserved for future workflows.
- `ComfyBridge.Api/ComfyBridge.Api.http` contains ready-to-run local requests.
