"""Verify Codex's real MCP client without creating a chat or calling a model."""
import json
import queue
import shutil
import subprocess
import threading
import time
from pathlib import Path

root = Path(__file__).resolve().parent.parent
process = subprocess.Popen(
    [shutil.which("codex"), "app-server", "--stdio"],
    stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.DEVNULL,
    text=True, encoding="utf-8", creationflags=subprocess.CREATE_NO_WINDOW,
    cwd=root,
)
lines = queue.Queue()


def receive():
    for line in process.stdout:
        lines.put(line)
    lines.put(None)


threading.Thread(target=receive, daemon=True).start()


def send(message):
    process.stdin.write(json.dumps(message, ensure_ascii=False) + "\n")
    process.stdin.flush()


def response(request_id):
    deadline = time.monotonic() + 40
    while time.monotonic() < deadline:
        line = lines.get(timeout=max(0.01, deadline - time.monotonic()))
        if line is None:
            raise RuntimeError("Codex app-server exited before responding")
        item = json.loads(line)
        if item.get("id") == request_id:
            if "error" in item:
                raise RuntimeError("Codex RPC error: " + str(item["error"]))
            return item["result"]
    raise TimeoutError("Codex MCP discovery timed out")


try:
    send({"id": 1, "method": "initialize", "params": {
        "clientInfo": {"name": "clawdpet_verification", "version": "1.0"},
        "capabilities": {"experimentalApi": True},
    }})
    response(1)
    send({"method": "initialized"})
    send({"id": 2, "method": "mcpServerStatus/list", "params": {
        "serverName": "clawdpet", "detail": "toolsAndAuthOnly",
    }})
    result = response(2)
    pet = next(server for server in result["data"] if server["name"] == "clawdpet")
    names = list(pet["tools"])
    assert len(names) == 2, "Expected both ClawdPet tools"
    assert any(name.endswith("clawdpet_act") for name in names), names
    assert any(name.endswith("clawdpet_status") for name in names), names
    evidence = {"server": pet["name"], "tools": names,
                "serverInfo": pet.get("serverInfo"),
                "verification": "Codex app-server MCP client; no thread or model request"}
    output = root / "work" / "codex" / "codex-discovery.json"
    output.write_text(json.dumps(evidence, ensure_ascii=False, indent=2), encoding="utf-8")
    print("PASS Codex initialized the configured ClawdPet MCP and discovered both tools")
    print(json.dumps(evidence, ensure_ascii=False))
finally:
    process.stdin.close()
    try:
        process.wait(timeout=5)
    except subprocess.TimeoutExpired:
        process.terminate()
        process.wait(timeout=5)
