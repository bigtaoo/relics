"""Local CDN for phones that reach the PC over Wi-Fi (iOS: no adb reverse, no readable log without a Mac).

python tools/bench/phone_cdn.py [--port 8000]
  GET  /...           files of artifacts/cdn, like `python -m http.server 8000 --directory artifacts/cdn`
  GET  /ios           a page of relics:// links, one per bench case (same cases as android.sh); open it
                      in Safari on the phone and tap a case, swipe the app away before the next one
  POST /bench/<name>  bench lines from BenchReport, kept in artifacts/bench/ios/<name>.log

The links carry this server's address (cdn=...), which the app keeps for later starts (BootConfig.CdnRoot).
Windows asks once whether Python may accept connections on private networks: allow it.
"""
import argparse, html, os, re, socket, time, urllib.parse
from functools import partial
from http.server import SimpleHTTPRequestHandler, ThreadingHTTPServer

ROOT = os.path.normpath(os.path.join(os.path.dirname(__file__), "..", ".."))
OUT = os.path.join(ROOT, "artifacts", "bench", "ios")

CASES = [
    ("demo", "", "Shop demo (no bench)"),
    ("hotcheck", "-hotcheck", "Hot update check (cube + golden values)"),
    ("base", "-bench", "Board only"),
    ("skin254", "-bench -crowd 254", "300 units, skinned"),
    ("vat254", "-bench -crowd 254 -vat", "300 units, VAT"),
    ("fx30", "-bench -crowd 254 -vat -fx 30", "VAT + 30 effects"),
    ("fx60", "-bench -crowd 254 -vat -fx 60", "VAT + 60 effects"),
    ("fx120", "-bench -crowd 254 -vat -fx 120", "VAT + 120 effects"),
    ("soak", "-bench -crowd 254 -vat -fx 30 -fps 30 -duration 600", "Heat: 10 min at 30 fps"),
    ("sim", "-simbench -seeds 3", "Battle logic (a few minutes)"),
]


def lan_ip():
    s = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    try:
        s.connect(("10.255.255.255", 1))  # no packet is sent; picks the outgoing interface
        return s.getsockname()[0]
    finally:
        s.close()


class Handler(SimpleHTTPRequestHandler):
    def log_message(self, fmt, *a):
        print(f"{time.strftime('%H:%M:%S')} {self.client_address[0]} {fmt % a}", flush=True)

    def do_GET(self):
        if self.path.rstrip("/") != "/ios":
            return super().do_GET()
        cdn = f"http://{self.headers.get('Host')}"
        rows = []
        for name, args, label in CASES:
            query = urllib.parse.urlencode({"cdn": cdn, "name": name, "args": args})
            rows.append(f'<p><a href="relics://run?{html.escape(query)}">{html.escape(label)}</a> <small>{html.escape(args)}</small></p>')
        body = ("<!doctype html><meta name=viewport content='width=device-width'><title>Relics bench</title>"
                "<style>body{font:18px -apple-system,sans-serif;margin:16px}a{display:inline-block;padding:8px 0}</style>"
                f"<h3>Relics bench ({html.escape(cdn)})</h3><p>Swipe the app away before each run.</p>" + "".join(rows)).encode()
        self.send_response(200)
        self.send_header("Content-Type", "text/html; charset=utf-8")
        self.send_header("Content-Length", str(len(body)))
        self.end_headers()
        self.wfile.write(body)

    def do_POST(self):
        m = re.fullmatch(r"/bench/([\w.-]+)", self.path)
        data = self.rfile.read(int(self.headers.get("Content-Length", 0)))
        if not m:
            self.send_response(404)
            self.end_headers()
            return
        os.makedirs(OUT, exist_ok=True)
        with open(os.path.join(OUT, m.group(1) + ".log"), "wb") as f:
            f.write(data)
        print(f"== {m.group(1)}\n{data.decode('utf-8', 'replace')}", flush=True)
        self.send_response(200)
        self.send_header("Content-Length", "0")
        self.end_headers()


if __name__ == "__main__":
    ap = argparse.ArgumentParser()
    ap.add_argument("--port", type=int, default=8000)
    port = ap.parse_args().port
    print(f"On the phone, open http://{lan_ip()}:{port}/ios in Safari", flush=True)
    ThreadingHTTPServer(("0.0.0.0", port), partial(Handler, directory=os.path.join(ROOT, "artifacts", "cdn"))).serve_forever()
