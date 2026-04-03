# ComfyBridge API

ComfyBridge API is a middleware service that exposes clean, stable endpoints for image generation while hiding ComfyUI workflow complexity.

It lets client apps:
- Call simple endpoints
- Use named templates instead of raw ComfyUI JSON
- Inject runtime inputs into reusable workflow templates
- Track asynchronous generation jobs

## 1. Prerequisites

Install the following:
- .NET SDK 9.0+
- ComfyUI running and reachable (default: http://127.0.0.1:8188)
- At least one valid ComfyUI model/workflow environment in your ComfyUI instance

Optional:
- Redis (only if you later implement RedisJobStore; current default store is in-memory)

## 2. Project Structure

```text
ComfyBridgeAPI.sln
ComfyBridge.Api/                # API host, controllers, middleware, background worker
ComfyBridge.Application/        # Use cases, contracts, orchestration services
ComfyBridge.Domain/             # Domain models, enums, exceptions
ComfyBridge.Infrastructure/     # Template repository, job store, queue, ComfyUI client
```

## 3. Configure the API

Main configuration is in:
- ComfyBridge.Api/appsettings.json
- ComfyBridge.Api/appsettings.Development.json

Important settings:

```json
{
  "ComfyUi": {
    "BaseUrl": "http://127.0.0.1:8188",
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

What to change:
1. Set ComfyUi.BaseUrl to your ComfyUI server URL.
2. Keep TemplateStorage.TemplatesPath as Templates unless you want another folder.
3. Configure WorkflowAi to your AI provider endpoint (Ollama/custom).
4. Keep JobStore.Provider as InMemory unless Redis is implemented.

## 4. Run ComfyUI First

Start ComfyUI and verify these endpoints are available:
- POST /prompt
- GET /history/{prompt_id}
- GET /view

If ComfyUI is not running, generation jobs will move to Failed with upstream errors.

## 5. Build and Run the API

From repository root:

```powershell
dotnet restore .\ComfyBridgeAPI.sln
dotnet build .\ComfyBridgeAPI.sln
dotnet run --project .\ComfyBridge.Api\ComfyBridge.Api.csproj
```

Default local URL (from launch settings):
- http://localhost:5176

When startup is successful, logs show:
- Generation worker started
- Now listening on: http://localhost:5176

## 6. Public API Endpoints

- POST /api/v1/generate/image
- POST /api/v1/generate/video (reserved/future, returns 501)
- GET /api/v1/jobs/{jobId}
- GET /api/v1/templates
- GET /api/v1/workflows/templates
- POST /api/v1/workflows/analyze
- POST /api/v1/workflows/save
- DELETE /api/v1/workflows/templates/{name}/{version}

## 6.1 Workflow Manager UI

The API host now includes Razor Pages for template workflow management:

- /workflows
  - List templates
  - View template details
  - Test templates with sample inputs
  - Delete templates
- /workflows/import
  - Upload JSON file (max 1 MB) or paste workflow JSON
  - Analyze and generate draft template using AI service
- /workflows/{id}
  - Review raw workflow JSON
  - Edit input names/types and node mapping (nodeId/nodeClass/field)
  - Save validated template to configured templates folder
- /workflows/test/{id}
  - Execute a template test run from UI
  - Submit input values using the template schema
  - Get JobId and status endpoint link

End-to-end flow:
1. Import workflow JSON.
2. Analyzer detects prompt/numeric/image fields and produces mapping.
3. Review and manually override mappings.
4. Save template to disk.

## 7. How to Add New Workflows (No Code Changes)

Templates are loaded from:
- ComfyBridge.Api/Templates

### Step-by-step

1. Export or prepare a valid ComfyUI workflow JSON.
2. Create a new template file in ComfyBridge.Api/Templates, for example:
   - my-workflow.1.0.json
3. Define template metadata:
   - name
   - version
   - inputs (public input schema)
   - mapping (how inputs are injected)
   - workflow (raw ComfyUI graph)
4. Restart API (or redeploy) so templates are reloaded on next read.
5. Call GET /api/v1/templates to confirm template appears.

Alternative:
- Use /workflows/import and complete the guided import/analyze/review/save flow in UI.

### Template Contract

```json
{
  "name": "txt2img-basic",
  "version": "1.0",
  "inputs": {
    "prompt": "string",
    "checkpointName": "string",
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
    },
    "checkpointName": {
      "nodeClass": "CheckpointLoaderSimple",
      "field": "ckpt_name"
    }
  },
  "workflow": {
    "...": {}
  }
}
```

### Mapping Options Supported

For each input, mapping supports one of these patterns:

1. jsonPointer
- Direct JSON pointer path into the workflow.
- Example: /3/inputs/steps

2. nodeClass + field (+ optional nodeIndex)
- Finds node(s) by ComfyUI class_type.
- Uses nodeIndex when multiple nodes share same class.
- This is preferred to reduce coupling to static node IDs.

3. nodeId + field
- Explicit node ID targeting.
- Supported as fallback, but less resilient if IDs change.

## 8. Test the API

Use the included HTTP file:
- ComfyBridge.Api/ComfyBridge.Api.http

### Test flow

1. List templates
- GET /api/v1/templates

2. Submit image generation job
- POST /api/v1/generate/image
- Example body:

```json
{
  "template": "txt2img-basic",
  "inputs": {
    "prompt": "a cinematic dragon flying over mountains",
    "negativePrompt": "blurry, low quality",
    "steps": 30,
    "cfg": 7.5,
    "seed": 42,
    "width": 1024,
    "height": 576,
    "checkpointName": "flux1-dev-fp8.safetensors"
  }
}
```

Windows curl example:

```powershell
curl.exe -X POST "http://localhost:5176/api/v1/generate/image" -H "Content-Type: application/json" -d "{\"template\":\"txt2img-basic:1.0\",\"inputs\":{\"prompt\":\"a cinematic dragon flying over mountains\",\"steps\":30,\"cfg\":7.5,\"seed\":42}}"
```

bash curl example:

```bash
curl -X POST 'http://localhost:5176/api/v1/generate/image' \
  -H 'Content-Type: application/json' \
  -d '{"template":"txt2img-basic:1.0","inputs":{"prompt":"a cinematic dragon flying over mountains","steps":30,"cfg":7.5,"seed":42}}'
```

**Note:** `checkpointName` must be a model available in your ComfyUI instance. Check ComfyUI's model manager or API logs to find available checkpoints.

3. Read response and capture jobId
- API returns 202 Accepted with jobId and initial status.

4. Poll job status
- GET /api/v1/jobs/{jobId}
- Repeat until status is Completed or Failed.

5. Read results
- On Completed, response includes assetUrls and raw result payload.

## 9. Understanding Job Lifecycle

Job states:
- Pending: request accepted, queued
- Running: submitted to ComfyUI, waiting result
- Completed: result collected
- Failed: validation, upstream, or timeout error

Internally:
1. Request validated
2. Template loaded
3. Inputs injected into workflow
4. Job created and queued
5. Background worker submits to ComfyUI
6. Worker polls history
7. Job updated with result or failure

## 10. Common Issues and Fixes

1. Template not found
- Cause: wrong template name/version in request
- Fix: call GET /api/v1/templates and match exactly

2. Invalid input
- Cause: missing required inputs or type mismatch
- Fix: align request inputs with template inputs schema

3. Model/Checkpoint not in list (400 Bad Request)
- Cause: checkpointName doesn't exist in ComfyUI, or template hardcodes unavailable model
- Fix: 
  - Run ComfyUI and check available models in the model manager
  - Pass the correct checkpointName in your request
  - Or update the template default in workflow.json if the model is unavailable globally

4. Upstream failure (502)
- Cause: ComfyUI unavailable or rejected prompt
- Fix: verify ComfyUI URL and check ComfyUI logs

5. Timeout (504)
- Cause: generation exceeded JobTimeoutSeconds
- Fix: increase ComfyUi.JobTimeoutSeconds

6. Empty results
- Cause: workflow does not produce image outputs expected by parser
- Fix: ensure workflow history contains output images and SaveImage path is valid

## 11. Production Notes

Current implementation is production-oriented but minimal:
- Clean architecture boundaries are in place
- Interfaces and DI are used throughout
- In-memory store is active
- Redis store is scaffolded but not yet implemented

Recommended next hardening steps:
1. Implement RedisJobStore
2. Add API key authentication
3. Add rate limiting
4. Add WebSocket/SignalR push updates for job progress
5. Add workflow schema validation on template load
