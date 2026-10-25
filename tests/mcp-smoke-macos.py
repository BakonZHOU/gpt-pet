import json
import os
import pathlib
import subprocess

root = pathlib.Path(__file__).resolve().parents[1]
binary = root / "macos-native" / "CodexPetPlugin"
env = dict(os.environ, CODEX_PET_NO_GUI="1")
proc = subprocess.Popen([str(binary), "--mcp", "--plugin-root", str(root)], stdin=subprocess.PIPE, stdout=subprocess.PIPE, text=True, env=env)
requests = [
    {"jsonrpc": "2.0", "id": 1, "method": "initialize", "params": {"protocolVersion": "2025-06-18"}},
    {"jsonrpc": "2.0", "id": 2, "method": "tools/list", "params": {}},
]
for request in requests:
    proc.stdin.write(json.dumps(request) + "\n")
proc.stdin.flush()
responses = [json.loads(proc.stdout.readline()), json.loads(proc.stdout.readline())]
proc.terminate()
assert responses[0]["result"]["serverInfo"]["version"] == "0.4.0"
assert [tool["name"] for tool in responses[1]["result"]["tools"]] == ["pet_show", "pet_status"]
print("PASS macOS MCP initialize and tools")
