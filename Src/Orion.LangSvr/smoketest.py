import subprocess, json, threading, time, sys, os

TYPES = ["namespace","type","class","enum","interface","struct","typeParameter","parameter",
		 "variable","property","enumMember","event","function","method","macro","keyword",
		 "modifier","comment","string","number","regexp","operator"]
MODS = ["declaration","definition","readonly","static","deprecated","abstract","async",
		"modification","documentation","defaultLibrary"]

cmd = sys.argv[1:] if len(sys.argv) > 1 else [os.path.join("Src", "Orion.LangSvr", "bin", "Debug", "net9.0", "Orion.LangSvr.exe")]
p = subprocess.Popen(cmd, stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.PIPE)

def send(msg):
	body = json.dumps(msg).encode("utf-8")
	p.stdin.write(b"Content-Length: " + str(len(body)).encode() + b"\r\n\r\n" + body)
	p.stdin.flush()

messages = []
def reader():
	while True:
		header = b""
		while b"\r\n\r\n" not in header:
			c = p.stdout.read(1)
			if not c: return
			header += c
		length = 0
		for line in header.decode("ascii","replace").split("\r\n"):
			if line.lower().startswith("content-length:"):
				length = int(line.split(":")[1].strip())
		body = b""
		while len(body) < length:
			chunk = p.stdout.read(length - len(body))
			if not chunk: return
			body += chunk
		try: messages.append(json.loads(body.decode("utf-8")))
		except: pass

errlines = []
threading.Thread(target=reader, daemon=True).start()
threading.Thread(target=lambda: [errlines.append(l.decode("utf-8","replace")) for l in iter(p.stderr.readline, b"")], daemon=True).start()

caps = {"textDocument": {"semanticTokens": {
	"dynamicRegistration": False, "requests": {"full": True, "range": False},
	"tokenTypes": TYPES, "tokenModifiers": MODS, "formats": ["relative"]}}}
send({"jsonrpc":"2.0","id":1,"method":"initialize","params":{"processId":None,"rootUri":None,"capabilities":caps}})
time.sleep(0.8)
send({"jsonrpc":"2.0","method":"initialized","params":{}})

init = next((m for m in messages if m.get("id")==1), None)
scap = init.get("result",{}).get("capabilities",{}) if init else {}
print("textDocumentSync:", json.dumps(scap.get("textDocumentSync")))
print("semanticTokensProvider advertised:", scap.get("semanticTokensProvider") is not None)
print("definitionProvider advertised:", scap.get("definitionProvider") is not None)

# --- diagnostics ---
send({"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{
	"uri":"file:///bad.src","languageId":"orion","version":1,"text":"i32 main()\n{\n    return missing_var;\n}\n"}}})

# --- semantic tokens ---
add_src = "i32 add(i32 a, i32 b)\n{\n    i32 sum = a + b;\n    return sum;\n}\n"
send({"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{
	"uri":"file:///add.src","languageId":"orion","version":1,"text":add_src}}})
time.sleep(0.5)
send({"jsonrpc":"2.0","id":3,"method":"textDocument/semanticTokens/full","params":{"textDocument":{"uri":"file:///add.src"}}})
# hover over `a` (parameter, line 2 col 14) and `sum` (local, line 3 col 11)
send({"jsonrpc":"2.0","id":4,"method":"textDocument/hover","params":{"textDocument":{"uri":"file:///add.src"},"position":{"line":2,"character":14}}})
send({"jsonrpc":"2.0","id":5,"method":"textDocument/hover","params":{"textDocument":{"uri":"file:///add.src"},"position":{"line":3,"character":11}}})
# F12 over `sum` in `return sum;` -> its declaration on line 2
send({"jsonrpc":"2.0","id":6,"method":"textDocument/definition","params":{"textDocument":{"uri":"file:///add.src"},"position":{"line":3,"character":11}}})

time.sleep(2.0)
diags = next((m["params"]["diagnostics"] for m in messages
			  if m.get("method")=="textDocument/publishDiagnostics" and m["params"]["uri"].endswith("bad.src")), None)
print("\n=== DIAGNOSTICS (bad.src) ===")
print((diags[0]["message"] if diags else "(none)"))

resp = next((m for m in messages if m.get("id")==3), None)
data = resp.get("result",{}).get("data") if resp else None
print("\n=== SEMANTIC TOKENS (add.src) ===")
if data:
	line = 0; ch = 0
	for i in range(0, len(data), 5):
		dl, dc, ln, ty, mo = data[i:i+5]
		line += dl
		ch = dc if dl > 0 else ch + dc
		modnames = [MODS[b] for b in range(len(MODS)) if mo & (1<<b)]
		print(f"  line {line} col {ch} len {ln} -> {TYPES[ty]}" + (f" [{','.join(modnames)}]" if modnames else ""))
else:
	print("(no token data)")

print("\n=== HOVER (add.src) ===")
for hid, what in [(4, "over 'a'  (line2 col14)"), (5, "over 'sum'(line3 col11)")]:
	r = next((m for m in messages if m.get("id")==hid), None)
	val = (r or {}).get("result",{})
	contents = val.get("contents",{}).get("value") if val else None
	print(f"  {what}: {contents!r}")

print("\n=== DEFINITION (add.src) ===")
r = next((m for m in messages if m.get("id")==6), None)
res = (r or {}).get("result")
if isinstance(res, list) and res:
	res = res[0]
if isinstance(res, dict):
	print(f"  over 'sum'(line3 col11): {res.get('uri')} line {res.get('range',{}).get('start',{}).get('line')}")
else:
	print(f"  over 'sum'(line3 col11): {res!r}")

send({"jsonrpc":"2.0","id":9,"method":"shutdown","params":None}); time.sleep(0.2)
send({"jsonrpc":"2.0","method":"exit","params":None}); time.sleep(0.2)
try: p.terminate()
except: pass
if errlines: sys.stderr.write("\n=== SERVER STDERR ===\n" + "".join(errlines[:12]))
