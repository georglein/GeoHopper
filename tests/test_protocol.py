"""Development test only. The delivered Grasshopper component does not run Python.

Uses the installed vendor encryption module as an independent protocol oracle.
Only a temporary loopback test server is contacted, never the real PLAXIS server.
"""
import json
import os
import secrets
import socket
import subprocess
import threading
import time
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path

import encryption

GUID = "{ab20d865-0f90-4789-b876-850bda684280}"
PROBE = Path(os.environ.get("GEOHOPPER_TEST_PROBE", str(Path(__file__).parents[1] / "artifacts" / "staging" / "ReadProbe.exe")))
state = {"password": secrets.token_hex(12), "title": "GeoHopper - Wall", "mode": "ok"}
observed = []


class Handler(BaseHTTPRequestHandler):
    """Serve only the two read endpoints and use vendor code for all cryptography."""
    def log_message(self, *_):
        """Suppress HTTP logs so generated credentials and test traffic remain private."""
        pass

    def do_POST(self):
        """Validate the request structure and produce a controlled PLAXIS-like response."""
        try:
            raw = json.loads(self.rfile.read(int(self.headers["Content-Length"])))
            # Official PLAXIS code decrypts the C# request.
            request = json.loads(encryption.decrypt(raw["RequestData"], raw["Code"], state["password"]))
        except Exception:
            self.send_error(401, "Invalid test password")
            return
        observed.append((self.path, request["action"]))
        if state["mode"] == "slow":
            time.sleep(3.8)
        if self.path == "/namedobjects":
            assert request["action"] == {"namedobjects": ["Project"]}
            response = {"namedobjects": {"Project": {
                "success": state["mode"] != "missing_project", "extrainfo": "",
                "returnedobject": {"guid": GUID, "type": "Project", "islistable": False}
            }}}
        elif self.path == "/propertyvalues":
            assert request["action"] == {"propertyvalues": {
                "owner": GUID, "propertyname": "Title", "phaseguid": ""}}
            title = state["title"]
            if state["mode"] == "typed_title":
                title = {"type": "String", "value": title}
            properties = {} if state["mode"] == "missing_title" else {"Title": title}
            response = {"queries": {GUID: {"properties": properties}}}
        else:
            raise AssertionError("Unexpected endpoint: " + self.path)
        response["ReplyCode"] = "invalid" if state["mode"] == "bad_reply" else request["ReplyCode"]
        encrypted, iv = encryption.encrypt(json.dumps(response), state["password"])
        body = json.dumps({"Response": encrypted, "Code": iv}).encode("utf-8")
        self.send_response(200)
        self.send_header("Content-Type", "application/json")
        self.send_header("Content-Length", str(len(body)))
        self.end_headers()
        try:
            self.wfile.write(body)
        except (BrokenPipeError, ConnectionResetError, ConnectionAbortedError):
            pass


def probe(port, password=None):
    """Execute the actual compiled C# client with a per-test deadline."""
    completed = subprocess.run(
        [str(PROBE), "127.0.0.1", str(port)],
        input=((state["password"] if password is None else password) + "\n").encode("utf-8"),
        stdout=subprocess.PIPE, stderr=subprocess.PIPE, timeout=9,
    )
    return json.loads(completed.stdout.decode("utf-8-sig"))


def check(name, condition):
    """Fail immediately with a named assertion or emit a readable success record."""
    assert condition, name
    print("PASS: " + name, flush=True)


if __name__ == "__main__":
    server = ThreadingHTTPServer(("127.0.0.1", 0), Handler)
    threading.Thread(target=server.serve_forever, daemon=True).start()
    port = server.server_port
    try:
        result = probe(port)
        check("Encrypted title read with official PLAXIS encryption", result["Connected"] and result["Title"] == state["title"])
        state["title"] = "Updated wall: \u00e9 / \u65e5\u672c\u8a9e"
        result = probe(port)
        check("Fresh title and Unicode round trip", result["Connected"] and result["Title"] == state["title"])
        state["title"] = ""
        result = probe(port)
        check("Empty title is valid", result["Connected"] and result["Title"] == "")
        state["mode"] = "typed_title"
        check("Typed property value", probe(port)["Connected"])
        state["mode"] = "ok"
        result = probe(port, "incorrect-test-password")
        check("Wrong password fails and clears title", not result["Connected"] and result["Title"] == "" and "HTTP 401" in result["Error"])
        for mode, expected in [("bad_reply", "Reply code"), ("missing_project", "Project object"), ("missing_title", "Project.Title")]:
            state["mode"] = mode
            result = probe(port)
            check(mode, not result["Connected"] and expected in result["Error"])
        state["mode"] = "slow"
        started = time.monotonic()
        result = probe(port)
        check("Timeout is bounded", not result["Connected"] and "timed out" in result["Error"] and time.monotonic() - started < 6)
        state["mode"] = "ok"
        for password in ("abcd", "x" * 56, "Test-\u00e9\u5bc6\u78bc"):
            state["password"] = password
            check("Password compatibility: " + str(len(password.encode('utf-8'))) + " UTF-8 bytes", probe(port)["Connected"])
        for password in ("a", "ab", "abc", "x" * 57):
            check("Unsupported key length rejected: " + str(len(password)), not probe(port, password)["Connected"])
        check("Empty password rejected", not probe(port, "")["Connected"])
        with socket.socket() as unused:
            unused.bind(("127.0.0.1", 0))
            unused_port = unused.getsockname()[1]
        result = probe(unused_port)
        check("Unavailable server", not result["Connected"] and "unreachable" in result["Error"])
        check("Only read endpoints used", all(path in ("/namedobjects", "/propertyvalues") for path, _ in observed))
    finally:
        server.shutdown()
        server.server_close()
