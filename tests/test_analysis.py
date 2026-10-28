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
                state["anchor_prestress"] = {}
                state["staged"] = {name: str(uuid.uuid4()) for name in state["names"].values()
                                   if name.startswith(("GH_R_", "GH_I_", "GH_L_", "GH_Plate_", "GH_Anchor_"))}
            if verb == "save" and cmd != "save":
                state["file"] = cmd[len("save "):].strip('"')
            if verb == "rename":
                _, guid, name = cmd.split(" ", 2)
                state["names"][guid] = name.strip('"')
            types = {"anchormat": ["AnchorMat"], "n2nanchor": ["Line", "NodeToNodeAnchor"], "platemat": ["PlateMat"], "plate": ["Line", "Plate"], "soilmat": ["SoilMat"], "polygon": ["Polygon", "Soil"],
                     "posinterface": ["Line", "PositiveInterface"],
                     "neginterface": ["Line", "NegativeInterface"],
                     "lineload": ["Line", "LineLoad"], "phase": ["Phase"]}
            for kind in types.get(verb, []):
                guid = str(uuid.uuid4())
                objects.append({"guid": guid, "type": kind})
                state.setdefault("types", {})[guid] = kind
            ok = verb != state.get("fail_command")
            if verb == "refine":
                ok = ok and state.get("types", {}).get(cmd.split()[1]) in ("Polygon", "Line")
            # Current PLAXIS rejects the obsolete name still present in bundled legacy HTML.
            if verb == "soilmat" and '"InterfaceStrengthMethod"' in cmd:
                ok = False
            if "AdjustPrestress" in cmd:
                parts = cmd.split()
                # Named SourceFeatures have no staged prestress property. The apply
                # command instead targets all generated anchor cut elements.
                valid = (len(parts) == 6 and parts[:4] ==
                         ["apply", "NodeToNodeAnchors", '"setproperties"', '"AdjustPrestress"']
                         and state["names"].get(parts[4], "").startswith("GH_Phase_")
                         and parts[5] == "False")
                ok = ok and valid and not state.get("fail_prestress")
                if ok:
                    for name in state["staged"]:
                        if name.startswith("GH_Anchor_"):
                            state["anchor_prestress"][(name + "_1", parts[4])] = False
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
            check("Anchor-free model skips native anchor collection", not any("AdjustPrestress" in c for c in commands))
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
            r = run(server.server_port, folder, mode="excavation")
            check("Dry excavation completes three prepared phases", r["BuildSuccess"] and r["Success"] and len(r["Status"]) == 3)
            check("K0 native initialization selected once", sum('"DeformCalcType" "K0 procedure"' in c for c in commands) == 1)
            check("K0 automatic material determination explicit", sum('"K0Determination" "Automatic"' in c for c in commands) == 1)
            check("Both interface faces created with segmented contacts", sum(c.startswith("posinterface ") for c in commands) == 4 and sum(c.startswith("neginterface ") for c in commands) == 4)
            check("Both plate lines refined before meshing", sum(c.startswith("refine ") and state["types"].get(c.split()[1]) == "Line" for c in commands) == 2 and max(i for i,c in enumerate(commands) if c.startswith("refine ")) < commands.index("meshd 0.5 True"))
            excavation_guid = next(g for g,n in state["names"].items() if n == "GH_Phase_Excavate")
            active = {c.split()[1] for c in commands if c.startswith("activate ") and c.split()[2] == excavation_guid}
            check("Excavated soil is absent from activation snapshot", state["staged"]["GH_R_Cut"] not in active)
            check("Excavated-face contacts inactive while retained contacts remain", all((g in active) == (not n.endswith("InnerTop")) for n,g in state["staged"].items() if n.startswith("GH_I_")))
            check("Both plates remain active after excavation", all(g in active for n,g in state["staged"].items() if n.startswith("GH_Plate_")))
            r = run(server.server_port, folder, mode="excavation", fail_command="refine")
            check("Line refinement failure stops before mesh and calculation", not r["BuildSuccess"] and not any(c.startswith(("meshd ","calculate ")) for c in commands))
            r = run(server.server_port, folder, mode="excavation", fail_command="neginterface")
            check("Negative interface failure stops before meshing", not r["BuildSuccess"] and not any(c.startswith("meshd ") for c in commands))
            r = run(server.server_port, folder, mode="struts")
            check("Strut installation and deeper excavation complete", r["BuildSuccess"] and r["Success"] and len(r["Status"]) == 6)
            check("Shared anchor material is created once", sum(c.startswith("anchormat ") for c in commands) == 1)
            check("Anchor material retains per-element EA and spacing", any(c.startswith("anchormat ") and '"EA" 2100000000' in c and '"LSpacing" 3' in c for c in commands))
            geometry_anchors = {g for g,t in state["types"].items() if t == "NodeToNodeAnchor"}
            check("Both anchors assigned their native material", sum(c.startswith("setmaterial ") and c.split()[1] in geometry_anchors for c in commands) == 2)
            staged_anchors = {g for n,g in state["staged"].items() if n.startswith("GH_Anchor_")}
            check("Each phase deactivates all staged anchors first", sum(c.startswith("deactivate ") and c.split()[1] in staged_anchors for c in commands) == 12)
            check("Prestress applied once per phase to native cut collection", sum(c.startswith('apply NodeToNodeAnchors "setproperties" "AdjustPrestress" ') for c in commands) == 6)
            check("All generated anchor elements receive every phase value", len(state["anchor_prestress"]) == 12 and all(v is False for v in state["anchor_prestress"].values()))
            activated = [(c.split()[1],state["names"][c.split()[2]]) for c in commands if c.startswith("activate ") and c.split()[1] in staged_anchors]
            check("Anchors activate only at installation and later", len(activated) == 5 and [n for _,n in activated] == ["GH_Phase_Struts","GH_Phase_Struts","GH_Phase_Deepen","GH_Phase_Deepen","GH_Phase_Remove"])
            check("Removed strut stays inactive in final snapshot", activated[-1][0] == state["staged"]["GH_Anchor_Strut2"])
            deeper = next(g for g,n in state["names"].items() if n == "GH_Phase_Deepen")
            check("Second excavation volume stays inactive", not any(c == "activate " + state["staged"]["GH_R_Cut2"] + " " + deeper for c in commands))
            r = run(server.server_port, folder, mode="struts", fail_prestress=True)
            check("Prestress failure names element collection and phase", not r["BuildSuccess"] and "Preparing phase Initial: disabling prestress adjustment for all anchor elements" in r["BuildError"])
            check("Prestress failure prevents receipt and solve", not r["HasReceipt"] and not any(c.startswith("calculate ") for c in commands))
            for verb in ("anchormat", "n2nanchor"):
                r = run(server.server_port, folder, mode="struts", fail_command=verb)
                check(verb + " failure stops before meshing and solve", not r["BuildSuccess"] and not r["HasReceipt"] and not any(c.startswith(("meshd ","calculate ")) for c in commands))
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
