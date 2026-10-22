"""Verify production result reads against independent encrypted Input and Output servers.

Responses model captured PLAXIS 2025.1 JSON scalar envelopes, never the FE engine.
"""
import json
import threading
import tempfile
import uuid
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

import encryption
import test_analysis as fixture

INFO = str(uuid.uuid4())
PHASE = str(uuid.uuid4())  # Deliberately different from every Input phase handle.
output_commands = []


class OutputHandler(BaseHTTPRequestHandler):
    """Provide a separate Output object namespace and controlled result/error scenarios."""
    def log_message(self, *_):
        """Do not log traffic or credentials."""
        pass

    def do_POST(self):
        """Return authenticated Output metadata, phase identity and scalar displacements."""
        raw = json.loads(self.rfile.read(int(self.headers["Content-Length"])))
        req = json.loads(encryption.decrypt(raw["RequestData"], raw["Code"], fixture.PASSWORD))
        action, state = req["action"], fixture.state
        if self.path == "/namedobjects":
            name = action["namedobjects"][0]
            assert name in ("GeneralInfo", "GH_Phase_Load"), name
            response = {"namedobjects": {name: {"success": not state.get("missing_phase", False) or name == "GeneralInfo",
                        "returnedobject": {"guid": INFO if name == "GeneralInfo" else PHASE, "type": name}}}}
        elif self.path == "/propertyvalues":
            q = action["propertyvalues"]
            prop, owner = q["propertyname"], q["owner"]
            values = {"Title": "GeoHopper:AnalysisDemo", "Filename": state["file"],
                      "UnitLength": "m", "Name": "GH_Phase_Load"}
            if state.get("wrong_output") or (state.get("switch_output") and len(output_commands) >= 4):
                values["Filename"] += ".other"
            if state.get("wrong_units"):
                values["UnitLength"] = "mm"
            if state.get("wrong_phase"):
                values["Name"] = "GH_Phase_Install"
            response = {"queries": {owner: {"properties": {prop: {"value": values[prop]}}}}}
        elif self.path == "/commands":
            cmd, = action["commands"]
            output_commands.append(cmd)
            tokens = cmd.split()
            assert tokens[0] == "getsingleresult" and tokens[1] == PHASE, cmd
            assert tokens[-1] == "False", cmd
            value = -.002 if tokens[2] == "ResultTypes.Soil.Ux" else -.01
            if state.get("missing_result") and len(output_commands) == 4:
                value = "not found"
            feedback = {"success": not state.get("native_error", False), "extrainfo": "Controlled Output error",
                        "returnedobjects": [{"type": "JSON", "json": value}], "returnedvalues": []}
            if state.get("legacy_values"):
                feedback["returnedobjects"] = []
                feedback["returnedvalues"] = [value]
            response = {"commands": [{"feedback": feedback}]}
        else:
            raise AssertionError(self.path)
        response["ReplyCode"] = "wrong" if state.get("bad_reply") else req["ReplyCode"]
        data, iv = encryption.encrypt(json.dumps(response), fixture.PASSWORD)
        body = json.dumps({"Response": data, "Code": iv}).encode()
        self.send_response(200)
        self.send_header("Content-Type", "application/json")
        self.send_header("Content-Length", str(len(body)))
        self.end_headers()
        self.wfile.write(body)


def run(input_server, output_server, folder, **options):
    """Reset state and execute a complete workflow in one production C# process."""
    output_commands.clear()
    return fixture.run(input_server.server_port, folder, output_port=output_server.server_port, **options)


if __name__ == "__main__":
    input_server = ThreadingHTTPServer(("127.0.0.1", 0), fixture.Handler)
    output_server = ThreadingHTTPServer(("127.0.0.1", 0), OutputHandler)
    for server in (input_server, output_server):
        threading.Thread(target=server.serve_forever, daemon=True).start()
    check = fixture.check
    try:
        with tempfile.TemporaryDirectory(prefix="GeoHopper-results-") as folder:
            r = run(input_server, output_server, folder)
            check("Separate Output namespace returns ordered samples", r["Success"] and [p["Id"] for p in r["Samples"]] == ["Base", "Top"])
            check("Signed displacement values survive Output decoding", r["Samples"][0]["Ux"] == -.002 and r["Samples"][0]["Uy"] == -.01)
            check("Equal sampled translations give zero chord rotation", r["Rotation"] == 0)
            check("Reads use explicit unsmoothed interpolation", len(output_commands) == 4 and all(c.endswith("False") for c in output_commands))
            r = run(input_server, output_server, folder, legacy_values=True)
            check("Legacy scalar responses remain supported", r["Success"])
            for option, text in [("wrong_output", "Output project"), ("wrong_units", "length unit"),
                                 ("wrong_phase", "phase identity"), ("missing_phase", "object unavailable"),
                                 ("bad_reply", "Reply code")]:
                r = run(input_server, output_server, folder, **{option: True})
                check(option + " blocks result queries", not r["Success"] and text in r["Error"] and not output_commands and not r["Samples"])
            r = run(input_server, output_server, folder, missing_result=True)
            check("Missing final value discards every partial sample", not r["Success"] and not r["Samples"] and "no numeric value" in r["Error"])
            r = run(input_server, output_server, folder, native_error=True)
            check("Native Output failure propagates without numeric values", not r["Success"] and not r["Samples"] and "Controlled Output error" in r["Error"])
            r = run(input_server, output_server, folder, switch_output=True)
            check("Project switch during read discards samples", not r["Success"] and not r["Samples"] and "changed while reading" in r["Error"])
            r = run(input_server, output_server, folder, bad_port=100000)
            check("Invalid returned Output port rejected", not r["Success"] and "invalid Output" in r["Error"] and not output_commands)
            for mode in ("stale", "recalculate"):
                r = run(input_server, output_server, folder, mode=mode)
                check(mode + " invalidates calculated receipt", not r["Success"] and "stale" in r["Error"] and not output_commands)
    finally:
        for server in (input_server, output_server):
            server.shutdown()
            server.server_close()
