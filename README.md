# ComfyBridge API

ComfyBridge API is a .NET 9 service that sits in front of ComfyUI and exposes stable endpoints for workflow-based generation.

It provides:
- Job-based image generation (`202 Accepted` + polling)
- Template discovery and dynamic run endpoints
- Image upload bridge endpoint for image-to-video/image-to-image workflows
- Workflow analyze/save APIs
- Built-in Razor Pages UI for workflow import/testing

## What Is Implemented

### API endpoints
- `GET /api/v1/jobs/{jobId}`
- `GET /api/v1/templates`
- `POST /api/v1/uploads/image` (multipart image upload bridge to ComfyUI)
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
    "JobTimeoutSeconds": 600,
    "UploadMaxFileSizeBytes": 10485760,
    "AllowedUploadExtensions": [".png", ".jpg", ".jpeg", ".webp", ".bmp"]
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
2. `ComfyUi.UploadMaxFileSizeBytes` and `ComfyUi.AllowedUploadExtensions` for client upload rules.
3. `WorkflowAi` values if using workflow analysis features.
4. `TemplateStorage.TemplatesPath` only if you want a different template folder.

## Client Implementation: Upload Image Workflows

For workflows where template inputs include `image` / `file` (for example `LoadImage.image`), clients should use a two-step flow.

### Step 1) Upload image to ComfyBridge

`ComfyBridge` forwards the image to ComfyUI and returns the stored filename that must be used in the run payload.

```http
POST /api/v1/uploads/image
Content-Type: multipart/form-data
```

Form fields:
1. `file` (required): image binary
2. `type` (optional): defaults to `input`
3. `subfolder` (optional)
4. `overwrite` (optional): defaults to `true`

Response example:

```json
{
  "comfyFileName": "z-image-turbo_01372_.png",
  "subfolder": "",
  "type": "input",
  "viewUrl": "http://127.0.0.1:8189/view?filename=z-image-turbo_01372_.png&type=input"
}
```

### Step 2) Execute dynamic workflow endpoint

Use the returned `comfyFileName` in the mapped input field (usually `image`).

```http
POST /api/v1/image2video/<template-name>
Content-Type: application/json

{
  "image": "z-image-turbo_01372_.png",
  "prompt": "cinematic energy build-up",
  "negativePrompt": "blurry, low quality",
  "width": 720,
  "height": 720,
  "frameCount": 81,
  "fps": 16
}
```

### Step 3) Poll job and download assets

```http
GET /api/v1/jobs/{jobId}
```

`assetUrls` may contain image and video/gif assets depending on workflow output nodes.
Use:

```http
GET /api/v1/jobs/{jobId}/outputs/{assetIndex}
```

to download each output through the bridge.

### Client-side best practices

1. Validate file extension and size before upload, aligned with server config.
2. Cache `comfyFileName` only for short-lived workflow execution sessions.
3. Always poll job status instead of assuming immediate completion.
4. Treat run endpoint inputs as strict: unknown or missing fields return validation errors.
5. If your client already has an existing Comfy filename, you can skip upload and call run directly.

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

Port defaults by execution mode:
- F5 / `dotnet run`: `5176` (HTTP), `7051` (HTTPS)
- Docker Compose: `5180` (HTTP host port mapped to container `8080`)

## Run With Docker

The repository includes:
- `Dockerfile`
- `docker-compose.yml`
- `.env.example`

By default, Docker mode maps API port `5180 -> 8080` and uses host endpoints so the container can call services running on your host machine:
- `ComfyUi__BaseUrl = http://host.docker.internal:8189`
- `WorkflowAi__Endpoint = http://host.docker.internal:11434`

Start with Docker Compose:

```powershell
# Optional: create local overrides file from sample (first time only)
if (-not (Test-Path .env)) { Copy-Item .env.example .env }

docker compose up --build
```

API URL:
- `http://localhost:5180`

To avoid host port conflicts, override Docker published port:

```powershell
# Option A: one-off for current shell
$env:COMFYBRIDGE_API_PORT = "5190"
docker compose up --build
```

```powershell
# Option B: set COMFYBRIDGE_API_PORT in .env for persistent local default
docker compose up --build
```

Override endpoints if ComfyUI / AI are hosted elsewhere:

```powershell
# Option A: one-off overrides for current shell
$env:COMFYUI_BASE_URL = "http://<your-comfy-host>:8189"
$env:WORKFLOW_AI_ENDPOINT = "http://<your-ai-host>:11434"
$env:COMFYBRIDGE_API_PORT = "5190"
docker compose up --build
```

```powershell
# Option B: set COMFYUI_BASE_URL / WORKFLOW_AI_ENDPOINT in .env
docker compose up --build
```

Build/run image manually:

```powershell
docker build -t comfybridge-api .
docker run --rm -p 5180:8080 `
  -e ComfyUi__BaseUrl=http://host.docker.internal:8189 `
  -e WorkflowAi__Endpoint=http://host.docker.internal:11434 `
  comfybridge-api
```

If `5180` is already in use, change the first number in `-p <hostPort>:8080`.

## Run With F5 (VS Code / Visual Studio)

Use the existing launch profiles in `ComfyBridge.Api/Properties/launchSettings.json`:
- `http`
- `https`

F5 runs with `ASPNETCORE_ENVIRONMENT=Development` and keeps local defaults from appsettings unless you override with environment variables.

## Run Directly (CLI)

This remains unchanged and works without Docker:

```powershell
dotnet run --project .\ComfyBridge.Api\ComfyBridge.Api.csproj
```

## How To Use ComfyBridge.Api

### 1) Ensure ComfyUI is reachable

ComfyBridge expects ComfyUI APIs to be available (prompt submission/history/view flow). If ComfyUI is down, jobs end as failed.

### 2) Check available templates

```http
GET /api/v1/templates
```

### 3) Submit a generation job (dynamic run route)

```http
POST /api/v1/text2image/txt2img-basic
Content-Type: application/json

{
  "prompt": "a cinematic dragon flying over mountains",
  "width": 512,
  "height": 512,
  "steps": 30,
  "cfg": 7.5,
  "seed": 42
}
```

Expected response: `202 Accepted` with `jobId` and `status`.

For image-to-video templates that require file upload first, follow the two-step flow in "Client Implementation: Upload Image Workflows".

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
- `http://localhost:<api-port>/workflows` for workflow management
- `http://localhost:<api-port>/api-explorer` to test generated dynamic endpoints from a UI

Where `<api-port>` is:
- `5176` for F5 / `dotnet run`
- `5180` for Docker default (or your `COMFYBRIDGE_API_PORT` override)

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
curl.exe -X POST "http://localhost:5176/api/v1/text2image/txt2img-basic" `
  -H "Content-Type: application/json" `
  -d "{\"prompt\":\"a cinematic dragon flying over mountains\",\"steps\":30,\"cfg\":7.5,\"seed\":42}"
```

Then:

```powershell
curl.exe "http://localhost:5176/api/v1/jobs/<jobId>"
```

Upload + image2video quick example:

```powershell
# 1) Upload image
curl.exe -X POST "http://localhost:5176/api/v1/uploads/image" `
  -F "file=@C:/path/to/your-start-frame.png" `
  -F "type=input" `
  -F "overwrite=true"

# 2) Run template using comfyFileName from upload response
curl.exe -X POST "http://localhost:5176/api/v1/image2video/03-video-wan2-2-14b-i2v-subgraphed" `
  -H "Content-Type: application/json" `
  -d "{\"image\":\"z-image-turbo_01372_.png\",\"prompt\":\"energy build-up\",\"negativePrompt\":\"blurry\",\"width\":720,\"height\":720,\"frameCount\":81,\"fps\":16}"
```

## Notes

- Current job storage provider is `InMemory`.
- `ComfyBridge.Api/ComfyBridge.Api.http` contains ready-to-run local requests.
