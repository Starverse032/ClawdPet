"""Read Codex's hook inventory. Never changes trust, permissions, or chats."""
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
    text=True, encoding="utf-8", creationflags=subprocess.CREATE_NO_WINDOW, cwd=root,
)
lines = queue.Queue()


def receive():
    for line in process.stdout:
        lines.put(line)
    lines.put(None)


threading.Thread(target=receive, daemon=True).start()


def send(value):
    process.stdin.write(json.dumps(value) + "\n")
    process.stdin.flush()


def response(request_id):
    deadline = time.monotonic() + 35
    while time.monotonic() < deadline:
        line = lines.get(timeout=max(0.01, deadline - time.monotonic()))
        if line is None:
            raise RuntimeError("Codex exited before responding")
        item = json.loads(line)
        if item.get("id") == request_id:
            if "error" in item:
                raise RuntimeError(str(item["error"]))
            return item["result"]
    raise TimeoutError("Hook discovery timed out")


try:
    send({"id": 1, "method": "initialize", "params": {
        "clientInfo": {"name": "clawdpet_hooks_verification", "version": "1.0"},
        "capabilities": {"experimentalApi": True},
    }})
    response(1)
    send({"method": "initialized"})
    send({"id": 2, "method": "hooks/list", "params": {"cwds": [str(root)]}})
    result = response(2)
    hooks = [hook for entry in result["data"] for hook in entry["hooks"]
             if "Invoke-CodexHook.ps1" in hook.get("command", "")]
    errors = [error for entry in result["data"] for error in entry["errors"]]
    assert not errors, errors
    assert len(hooks) == 10, f"Expected 10 ClawdPet hooks, got {len(hooks)}"
    assert all(hook["enabled"] for hook in hooks), "One or more hooks disabled"
    evidence = {"hookCount": len(hooks), "hooks": [
        {key: hook.get(key) for key in ("eventName", "trustStatus", "enabled", "timeoutSec", "sourcePath")}
        for hook in hooks
    ], "verification": "Codex hooks/list only; trust not changed"}
    (root / "work" / "codex-hooks" / "hooks-discovery.json").write_text(
        json.dumps(evidence, ensure_ascii=False, indent=2), encoding="utf-8")
    print("PASS Codex parsed and discovered all 10 configured ClawdPet lifecycle hooks")
    print("Trust states:", sorted({hook["trustStatus"] for hook in hooks}))
    print("Events:", ", ".join(hook["eventName"] for hook in hooks))
finally:
    process.stdin.close()
    try:
        process.wait(timeout=5)
    except subprocess.TimeoutExpired:
        process.terminate()
        process.wait(timeout=5)
