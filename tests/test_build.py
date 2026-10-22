"""Exercise production C# build logic against an encrypted, isolated API double.

Vendor encryption verifies the wire codec, not PLAXIS modeling semantics.
Never connects to an existing Input server or opens a user project.
"""
import json
import os
import secrets
import subprocess
import tempfile
import threading
import uuid
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path

import encryption

PROBE = Path(os.environ["GEOHOPPER_MODEL_PROBE"])
PASSWORD = secrets.token_hex(12)
PROJECT = str(uuid.uuid4())
state = {}
observed = []


class Handler(BaseHTTPRequestHandler):
    """Return controlled project, creation and failure responses."""
    def log_message(self, *_):
        """Keep credentials and requests out of HTTP logs."""
        pass

    def do_POST(self):
        """Decrypt with vendor code and emulate one native API operation."""
        raw = json.loads(self.rfile.read(int(self.headers["Content-Length"])))
        request = json.loads(encryption.decrypt(raw["RequestData"], raw["Code"], PASSWORD))
        action = request["action"]
        observed.append((self.path, action))
        if self.path == "/namedobjects":
            response = {"namedobjects": {"Project": {"success": True, "returnedobject": {"guid": PROJECT, "type": "Project"}}}}
        elif self.path == "/propertyvalues":
            response = {"queries": {PROJECT: {"properties": {"Title": state["title"]}}}}
        elif self.path == "/environment":
            assert action == {"name": "new", "filename": ""}
            state["materials"] = []
            state["regions"] = []
            mode = state.get("environment_mode", "empty")
            if mode != "encrypted":
                body = {"empty": b"", "text": b"OK", "bad_json": b"not-json"}[mode]
                self.send_response(200)
                self.send_header("Content-Type", "application/json" if mode == "bad_json" else "text/plain")
                self.send_header("Content-Length", str(len(body)))
                self.end_headers()
                self.wfile.write(body)
                return
            response = {}
        elif self.path == "/commands":
            command, = action["commands"]
            verb = command.split(" ", 1)[0]
            ok = verb != state.get("fail")
            # Reproduce the material-dependent read-only property reported by PLAXIS.
            invalid_saturated_weight = (verb == "soilmat" and '"Non-porous"' in command
                                        and '"gammaSat"' in command)
            ok = ok and not invalid_saturated_weight
            objects = []
            if verb == "soilmat":
                guid = str(uuid.uuid4())
                state["materials"].append(guid)
                objects = [{"guid": guid, "type": "SoilMat"}]
            elif verb == "polygon":
                soil = str(uuid.uuid4())
                state["regions"].append(soil)
                # Soil deliberately precedes Polygon; index-based assignment would fail.
                objects = [{"guid": soil, "type": "Soil"}, {"guid": str(uuid.uuid4()), "type": "Polygon"}]
                if state.get("missing_soil"):
                    objects = objects[1:]
            # Server.call_and_handle_commands in the installed vendor wrapper reads
            # each command envelope's feedback before interpreting success/objects.
            feedback = {"success": ok, "returnedobjects": objects,
                        "extrainfo": "" if ok else "Controlled test failure"}
            if invalid_saturated_weight:
                feedback["extrainfo"] = "Cannot set read-only property gammaSat"
            envelope = {"feedback": feedback}
            if state.get("missing_feedback"):
                envelope = {"success": True}
            response = {"commands": [envelope]}
        else:
            raise AssertionError(self.path)
        response["ReplyCode"] = request["ReplyCode"]
        encrypted, iv = encryption.encrypt(json.dumps(response), PASSWORD)
        body = json.dumps({"Code": iv, "Response": encrypted}).encode()
        self.send_response(200)
        self.send_header("Content-Type", "application/json")
        self.send_header("Content-Length", str(len(body)))
        self.end_headers()
        self.wfile.write(body)


def run(port, folder, **options):
    """Reset mock state and invoke the actual compiled production builder."""
    state.clear()
    state.update(title="GeoHopper:Demo", materials=[], regions=[])
    state.update(options)
    observed.clear()
    completed = subprocess.run([str(PROBE), str(port), folder],
                               input=(PASSWORD + "\n").encode(), capture_output=True, timeout=20)
    return json.loads(completed.stdout.decode("utf-8-sig"))


def check(name, condition):
    """Record a named invariant or abort the build."""
    assert condition, name
    print("PASS: " + name, flush=True)


def commands():
    """Return native command text from the last simulated build."""
    return [action["commands"][0] for path, action in observed if path == "/commands"]


if __name__ == "__main__":
    server = ThreadingHTTPServer(("127.0.0.1", 0), Handler)
    threading.Thread(target=server.serve_forever, daemon=True).start()
    try:
        with tempfile.TemporaryDirectory(prefix="GeoHopper-test-") as folder:
            r = run(server.server_port, folder, title="User project")
            check("Foreign project refused without any write", not r["Success"] and not commands() and all(p != "/environment" for p, _ in observed))
            r = run(server.server_port, folder)
            first_path = r["ProjectFile"]
            cmd = commands()
            check("Encrypted full build completes", r["Success"] and r["BackupFile"] and first_path)
            check("Backup precedes new project", observed[2][0] == "/commands" and cmd[0].startswith("save ") and observed[3][0] == "/environment")
            check("Explicit SI units and plane strain before geometry", '"UnitForce" "N"' in cmd[1] and '"UnitLength" "m"' in cmd[1] and '"Plane strain"' in cmd[1])
            check("Distinct materials created", len(state["materials"]) == 2)
            assignments = [c.split()[1:] for c in cmd if c.startswith("setmaterial ")]
            check("Assignments use returned Soil GUIDs and matching materials",
                  assignments == [[state["regions"][i], state["materials"][i]] for i in range(2)])
            check("Stable semantic names assigned", any('"GH_R_Wall"' in c for c in cmd) and any('"GH_R_Ground"' in c for c in cmd))
            check("Successful build ends with final save", cmd[-1] == 'save "' + first_path + '"')
            r = run(server.server_port, folder)
            check("Repeat build uses fresh project and unique output", r["Success"] and r["ProjectFile"] != first_path and sum(p == "/environment" for p, _ in observed) == 1)
            r = run(server.server_port, folder, fail="save")
            check("Failed backup prevents reset and reports no backup file", not r["Success"] and not r["BackupFile"] and all(p != "/environment" for p, _ in observed))
            check("Native feedback diagnostic retained", "Controlled test failure" in r["Error"])
            r = run(server.server_port, folder, missing_feedback=True)
            check("Missing feedback is a protocol error and prevents reset",
                  not r["Success"] and "feedback" in r["Error"] and all(p != "/environment" for p, _ in observed))
            r = run(server.server_port, folder, fail="setmaterial")
            check("Rejected command stops transfer and preserves backup reference",
                  not r["Success"] and not r["ProjectFile"] and r["BackupFile"] and commands()[-1].startswith("setmaterial "))
            r = run(server.server_port, folder, missing_soil=True)
            check("Ambiguous creation cannot silently assign wrong object", not r["Success"] and not any(c.startswith("setmaterial ") for c in commands()))
            r = run(server.server_port, folder, environment_mode="text")
            check("Non-JSON environment acknowledgement accepted", r["Success"])
            r = run(server.server_port, folder, environment_mode="encrypted")
            check("Encrypted environment acknowledgement authenticated", r["Success"])
            r = run(server.server_port, folder, environment_mode="bad_json")
            check("Malformed JSON environment response stops build with stage diagnostic",
                  not r["Success"] and "environment" in r["Error"] and
                  "Creating new project" in r["Error"] and len(commands()) == 1)
            r = run(server.server_port, "relative-folder")
            check("Relative output folder rejected before HTTP", not r["Success"] and not observed)
    finally:
        server.shutdown()
        server.server_close()
