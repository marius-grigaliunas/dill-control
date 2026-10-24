#!/usr/bin/env python3
"""The editor bridge's MCP server (docs/decisions/0044).

Claude Code starts it from the project's .mcp.json and speaks MCP to it over
stdin and stdout (JSON-RPC, a message a line). It passes each tool call to
the running Godot editor's bridge plugin (addons/editor_bridge), whose port
and token are in .godot/editor_bridge.json while it listens. The editor
decides which commands there are; a PNG in a result becomes an image.

Python's standard library only. The same file is a command line, for
testing by hand:

    python tools/editor-bridge/godot_mcp.py commands
    python tools/editor-bridge/godot_mcp.py call view '{"width": 960}' --png perflogs/view.png
"""

import base64
import json
import os
import sys
import time
import urllib.error
import urllib.request

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
INFO = os.path.join(ROOT, ".godot", "editor_bridge.json")
CACHE = os.path.join(ROOT, ".godot", "editor_bridge_commands.json")

# Never through a proxy: the bridge is on this machine.
OPENER = urllib.request.build_opener(urllib.request.ProxyHandler({}))


class Unreachable(Exception):
    pass


def call(name, args=None, wait=30.0):
    """One command. Waits up to `wait` seconds for the editor: it is away
    for a few seconds every time the project's code reloads."""
    body = json.dumps(args or {}).encode("utf-8")
    deadline = time.time() + wait
    while True:
        try:
            with open(INFO, encoding="utf-8") as f:
                info = json.load(f)
            request = urllib.request.Request(
                f"http://127.0.0.1:{info['port']}/cmd/{name}", data=body, method="POST",
                headers={"X-Bridge-Token": info["token"], "Content-Type": "application/json"})
            with OPENER.open(request, timeout=300) as response:
                return json.loads(response.read().decode("utf-8"))
        except urllib.error.HTTPError as e:
            return json.loads(e.read().decode("utf-8") or '{"ok": false, "error": "HTTP %d"}' % e.code)
        except (OSError, ValueError, KeyError) as e:
            if time.time() >= deadline:
                raise Unreachable(
                    "The Godot editor's bridge is not answering: is the editor open with the "
                    f"Editor Bridge plugin on? ({e.__class__.__name__}: {e})")
            time.sleep(0.5)


def commands(wait):
    """The editor's commands; the last list it gave while it is away."""
    try:
        reply = call("commands", wait=wait)
        if reply.get("ok"):
            os.makedirs(os.path.dirname(CACHE), exist_ok=True)
            with open(CACHE, "w", encoding="utf-8") as f:
                json.dump(reply["result"], f)
            return reply["result"], True
    except Unreachable:
        pass
    try:
        with open(CACHE, encoding="utf-8") as f:
            return json.load(f), False
    except (OSError, ValueError):
        return [], False


STATUS_TOOL = {
    "name": "editor_status",
    "description": "Whether the Godot editor's bridge is answering, on which port, and the commands it has.",
    "inputSchema": {"type": "object", "properties": {}},
}


def tool_result(reply):
    if not reply.get("ok"):
        return {"content": [{"type": "text", "text": "Error: " + str(reply.get("error"))}], "isError": True}
    result = reply.get("result")
    content = []
    if isinstance(result, dict) and "image_png" in result:
        content.append({"type": "image", "data": result.pop("image_png"), "mimeType": "image/png"})
    content.append({"type": "text", "text": json.dumps(result, indent=1)})
    return {"content": content}


# ---------------- MCP ----------------

def send(message):
    sys.stdout.buffer.write((json.dumps(message) + "\n").encode("utf-8"))
    sys.stdout.buffer.flush()


def serve():
    reached = False
    for line in sys.stdin.buffer:
        line = line.strip()
        if not line:
            continue
        try:
            message = json.loads(line)
        except ValueError:
            continue
        method = message.get("method")
        mid = message.get("id")
        params = message.get("params") or {}
        if mid is None:
            continue  # a notification: nothing to answer
        try:
            if method == "initialize":
                result = {
                    "protocolVersion": params.get("protocolVersion", "2025-06-18"),
                    "capabilities": {"tools": {"listChanged": True}},
                    "serverInfo": {"name": "godot-editor-bridge", "version": "1"},
                    "instructions": (
                        "Tools that work in the user's running Godot editor, on the scene they have open "
                        "(docs/decisions/0044). Every change is one undo for the user. While a scene is open in "
                        "the editor, change it through these tools, never by editing its .tscn."),
                }
            elif method == "ping":
                result = {}
            elif method == "tools/list":
                listed, live = commands(wait=2.0)
                reached = reached or live
                result = {"tools": [STATUS_TOOL] + [
                    {"name": c["name"], "description": c["description"], "inputSchema": c["schema"]}
                    for c in listed if c["name"] != "commands"]}
            elif method == "tools/call":
                name = params.get("name")
                args = params.get("arguments") or {}
                if name == "editor_status":
                    listed, live = commands(wait=2.0)
                    port = None
                    if live:
                        with open(INFO, encoding="utf-8") as f:
                            port = json.load(f)["port"]
                    text = json.dumps({"answering": live, "port": port, "commands": [c["name"] for c in listed]}, indent=1)
                    result = {"content": [{"type": "text", "text": text}]}
                    if live and not reached:
                        reached = True
                        send({"jsonrpc": "2.0", "method": "notifications/tools/list_changed"})
                else:
                    try:
                        result = tool_result(call(name, args))
                    except Unreachable as e:
                        result = {"content": [{"type": "text", "text": str(e)}], "isError": True}
            else:
                send({"jsonrpc": "2.0", "id": mid, "error": {"code": -32601, "message": f"no method {method}"}})
                continue
            send({"jsonrpc": "2.0", "id": mid, "result": result})
        except Exception as e:  # noqa: BLE001 -- one bad call must not end the server
            send({"jsonrpc": "2.0", "id": mid, "error": {"code": -32603, "message": f"{e.__class__.__name__}: {e}"}})


# ---------------- The command line ----------------

def cli(argv):
    if argv[0] == "commands":
        listed, live = commands(wait=5.0)
        print(("" if live else "(the editor is away: its last list)\n") + "\n".join(
            f"{c['name']}: {c['description']}" for c in listed))
        return 0
    if argv[0] == "call" and len(argv) >= 2:
        args = json.loads(argv[2]) if len(argv) > 2 and not argv[2].startswith("--") else {}
        png = argv[argv.index("--png") + 1] if "--png" in argv else None
        reply = call(argv[1], args)
        result = reply.get("result")
        if isinstance(result, dict) and "image_png" in result:
            data = base64.b64decode(result.pop("image_png"))
            if png:
                with open(png, "wb") as f:
                    f.write(data)
                result["image_written"] = png
        print(json.dumps(reply, indent=1))
        return 0 if reply.get("ok") else 1
    print(__doc__)
    return 2


if __name__ == "__main__":
    if len(sys.argv) > 1:
        sys.exit(cli(sys.argv[1:]))
    serve()
