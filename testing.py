import json
from pathlib import Path
from urllib import error, request

workflow = json.loads(Path("z-image.json").read_text(encoding="utf-8"))
payload = {
    "prompt": workflow,
    "client_id": "my_client",
}

req = request.Request(
    "http://localhost:8189/prompt",
    data=json.dumps(payload).encode("utf-8"),
    headers={"Content-Type": "application/json"},
    method="POST",
)

try:
    with request.urlopen(req, timeout=60) as resp:
        print(resp.read().decode("utf-8"))
except error.HTTPError as ex:
    print(f"HTTP {ex.code}: {ex.reason}")
    print(ex.read().decode("utf-8", errors="replace"))
