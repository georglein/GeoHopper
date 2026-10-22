"""Exercise staged build/solve orchestration against a temporary encrypted API double.

The fixture implements documented response envelopes. It does not validate the PLAXIS
geometry engine, phase convergence, licensing or GUI behavior.
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

PROBE = Path(os.environ["GEOHOPPER_ANALYSIS_PROBE"])
PASSWORD = secrets.token_hex(12)
PROJECT, INITIAL = str(uuid.uuid4()), str(uuid.uuid4())
state = {}
commands = []


class Handler(BaseHTTPRequestHandler):
    """Maintain enough native state to assert phase ordering and save/solve guards."""
    def log_message(self, *_):
        """Suppress sensitive traffic."""
        pass

    def do_POST(self):
        """Handle native encrypted reads, commands and project creation."""
        raw = json.loads(self.rfile.read(int(self.headers["Content-Length"])))
        req = json.loads(encryption.decrypt(raw["RequestData"], raw["Code"], PASSWORD))
        action = req["action"]
        if self.path == "/environment":
            self.send_response(200)
            self.send_header("Content-Length", "0")
            self.end_headers()
            return
        if self.path == "/namedobjects":
            name = action["namedobjects"][0]
            if name in ("Project", "InitialPhase"):
                guid = PROJECT if name == "Project" else INITIAL
            else:
                guid = state["staged"][name]
            response = {"namedobjects": {name: {"success": True, "returnedobject": {"guid": guid, "type": name}}}}
        elif self.path == "/propertyvalues":
            query = action["propertyvalues"]
            owner, prop = query["owner"], query["propertyname"]
            if prop == "Title":
                value = "GeoHopper:AnalysisDemo"
            elif prop == "Filename":
                value = state["file"] + (".other" if state.get("wrong_file") else "")
            elif prop == "CalculationResult":
                value = 2 if state.get("fail_phase") == state["names"].get(owner) else 1
            elif prop == "LogInfo":
                value = "Controlled convergence failure"
            else:
                raise AssertionError(prop)
            response = {"queries": {owner: {"properties": {prop: {"value": value}}}}}
        elif self.path == "/commands":
            cmd, = action["commands"]
            commands.append(cmd)
            verb = cmd.split()[0]
            objects = []
            if verb == "gotostages":
                # Geometry feature GUIDs are replaced by staged SourceFeature handles.
                state["staged"] = {name: str(uuid.uuid4()) for name in state["names"].values()
                                   if name.startswith(("GH_R_", "GH_I_", "GH_L_", "GH_Plate_"))}
            if verb == "save" and cmd != "save":
                state["file"] = cmd[len("save "):].strip('"')
            if verb == "rename":
                _, guid, name = cmd.split(" ", 2)
                state["names"][guid] = name.strip('"')
            types = {"platemat": ["PlateMat"], "plate": ["Line", "Plate"], "soilmat": ["SoilMat"], "polygon": ["Polygon", "Soil"],
                     "posinterface": ["Line", "PositiveInterface"],
                     "neginterface": ["Line", "NegativeInterface"],
                     "lineload": ["Line", "LineLoad"], "phase": ["Phase"]}
            for kind in types.get(verb, []):
                guid = str(uuid.uuid4())
                objects.append({"guid": guid, "type": kind})
            ok = verb != state.get("fail_command")
            # Current PLAXIS rejects the obsolete name still present in bundled legacy HTML.
            if verb == "soilmat" and '"InterfaceStrengthMethod"' in cmd:
                ok = False
            if verb in ("activate", "deactivate") and cmd.split()[1] != "Soils":
                ok = ok and cmd.split()[1] in state["staged"].values()
            response = {"commands": [{"feedback": {"success": ok, "returnedobjects": objects,
                        "extrainfo": "" if ok else "Controlled native failure"}}]}
            if verb == "view" and "output_port" in state:
                response["commands"][0]["feedback"]["returnedobjects"] = [
                    {"type": "JSON", "json": state.get("bad_port", state["output_port"])}]
        else:
            raise AssertionError(self.path)
        response["ReplyCode"] = req["ReplyCode"]
        data, iv = encryption.encrypt(json.dumps(response), PASSWORD)
        body = json.dumps({"Response": data, "Code": iv}).encode()
        self.send_response(200)
        self.send_header("Content-Type", "application/json")
        self.send_header("Content-Length", str(len(body)))
        self.end_headers()
        self.wfile.write(body)


def run(port, folder, mode="normal", **options):
    """Run production C# in one process so transient receipt state is exercised."""
    state.clear()
    state.update(file="", names={}, staged={})
    state.update(options)
    commands.clear()
    result = subprocess.run([str(PROBE), str(port), folder, mode],
                            input=(PASSWORD + "\n").encode(), capture_output=True, timeout=30)
    assert result.returncode == 0, result.stderr.decode(errors="replace")
    return json.loads(result.stdout.decode("utf-8-sig"))


def check(name, condition):
    """Print a named assertion or fail the release build."""
    assert condition, name
    print("PASS: " + name, flush=True)


if __name__ == "__main__":
    server = ThreadingHTTPServer(("127.0.0.1", 0), Handler)
    threading.Thread(target=server.serve_forever, daemon=True).start()
    try:
        with tempfile.TemporaryDirectory(prefix="GeoHopper-analysis-") as folder:
            r = run(server.server_port, folder, mode="output")
            check("Staged build returns receipt and all phases calculate", r["BuildSuccess"] and r["HasReceipt"] and r["Success"] and len(r["Status"]) == 3)
            check("Contacts use dedicated manual-strength material",
                  any('"InterfaceStrengthDetermination" "Manual"' in c and '"Rinter" 0.67' in c for c in commands))
            check("Interface material mode explicitly custom", any('"MaterialMode" "Custom"' in c for c in commands))
            check("SI mesh dimension precedes phase setup", commands.index("meshd 0.5 True") < commands.index("gotostages"))
            check("Local refinement precedes mesh generation", next(i for i,c in enumerate(commands) if c.startswith("refine ")) < commands.index("meshd 0.5 True"))
            check("Initial gravity and later plastic calculations explicit",
                  sum('"DeformCalcType" "Gravity loading"' in c for c in commands) == 1 and sum('"DeformCalcType" "Plastic"' in c for c in commands) == 2)
            check("Gravity displacement reset only at installation",
                  any("GH_Phase_Install.Deform.ResetDisplacementsToZero True" in c for c in commands) and
                  any("GH_Phase_Load.Deform.ResetDisplacementsToZero False" in c for c in commands))
            check("Dry conditions set for every phase", sum(c.startswith("setwaterdry Soils ") for c in commands) == 3)
            check("Boundary supports set for every phase", sum(c.startswith("setproperties Deformations ") for c in commands) == 3)
            calculated = [state["names"][c.split()[1]] for c in commands if c.startswith("calculate ")]
            check("Solver invoked in parent-first order", calculated == ["GH_Phase_Initial", "GH_Phase_Install", "GH_Phase_Load"])
            check("Output opens only after final save", commands[-2] == "save" and commands[-1].startswith("view "))
            r = run(server.server_port, folder, mode="plates")
            check("Plate build and staged solve succeed", r["BuildSuccess"] and r["Success"])
            check("Shared plate material created once", sum(c.startswith("platemat ") for c in commands) == 1)
            check("Two plates created before meshing", sum(c.startswith("plate ") for c in commands) == 2 and max(i for i,c in enumerate(commands) if c.startswith("plate ")) < commands.index("gotomesh"))
            plate_geometry = {g for g,n in state["names"].items() if n.startswith("GH_Plate_")}
            check("Both plates receive material", sum(c.startswith("setmaterial ") and c.split()[1] in plate_geometry for c in commands) == 2)
            plate_handles = {g for n,g in state["staged"].items() if n.startswith("GH_Plate_")}
            check("Every phase explicitly deactivates both staged plates", sum(c.startswith("deactivate ") and c.split()[1] in plate_handles for c in commands) == 6)
            active = [(c.split()[1], state["names"][c.split()[2]]) for c in commands if c.startswith("activate ") and c.split()[1] in plate_handles]
            check("No initial plates; installation activates two; removal retains right", active == [(state["staged"]["GH_Plate_Left"], "GH_Phase_Install"), (state["staged"]["GH_Plate_Right"], "GH_Phase_Install"), (state["staged"]["GH_Plate_Right"], "GH_Phase_Remove")])
            r = run(server.server_port, folder, mode="plates", fail_command="platemat")
            check("Plate material failure stops before meshing", not r["BuildSuccess"] and not any(c.startswith("meshd ") for c in commands))
            r = run(server.server_port, folder, mode="plates", fail_command="plate")
            check("Plate creation failure yields no receipt", not r["BuildSuccess"] and not r["HasReceipt"])
            r = run(server.server_port, folder, fail_phase="GH_Phase_Install")
            check("Native failed phase stops descendants", not r["Success"] and "Controlled convergence failure" in r["Error"] and sum(c.startswith("calculate ") for c in commands) == 2)
            check("Failure retains per-phase status", len(r["Status"]) == 2 and "FAILED" in r["Status"][-1])
            r = run(server.server_port, folder, wrong_file=True)
            check("Different open project refused before calculation", r["BuildSuccess"] and not r["Success"] and not any(c.startswith("calculate ") for c in commands))
            r = run(server.server_port, folder, mode="stale")
            check("Earlier build receipt rejected", not r["Success"] and "stale" in r["Error"] and not any(c.startswith("calculate ") for c in commands))
            r = run(server.server_port, folder, fail_command="meshd")
            check("Mesh failure produces no receipt or calculation", not r["BuildSuccess"] and not r["HasReceipt"] and not any(c.startswith("calculate ") for c in commands))
            r = run(server.server_port, folder, fail_command="posinterface")
            check("Contact creation failure stops before meshing", not r["BuildSuccess"] and not any(c.startswith("meshd ") for c in commands))
    finally:
        server.shutdown()
        server.server_close()
