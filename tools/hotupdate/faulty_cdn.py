"""Faulty local CDN for hot update failure tests (design/07 §3), used by failure_scenario.py.

python faulty_cdn.py ROOT [--rate KBps] [--stall-after BYTES] [--drop-after BYTES] [--port 8000]
  --rate         global bandwidth cap for bundle bodies (0 = unlimited)
  --stall-after  after sending this many body bytes of a .bundle, stop sending and hold the socket
  --drop-after   after sending this many body bytes of a .bundle, close the socket
Supports "Range: bytes=N-" (206) so resume can be tested. Logs one line per request.
"""
import argparse, os, re, socket, sys, threading, time
from http.server import ThreadingHTTPServer, BaseHTTPRequestHandler

ap = argparse.ArgumentParser()
ap.add_argument("root")
ap.add_argument("--port", type=int, default=8000)
ap.add_argument("--rate", type=float, default=0)
ap.add_argument("--stall-after", type=int, default=-1)
ap.add_argument("--drop-after", type=int, default=-1)
args = ap.parse_args()

lock = threading.Lock()
next_free = [time.monotonic()]


def throttle(n):
    if args.rate <= 0:
        return
    with lock:
        now = time.monotonic()
        start = max(now, next_free[0])
        next_free[0] = start + n / (args.rate * 1024)
        wait = next_free[0] - now
    time.sleep(wait)


def log(msg):
    print(f"{time.strftime('%H:%M:%S')} {msg}", flush=True)


class Handler(BaseHTTPRequestHandler):
    protocol_version = "HTTP/1.1"

    def log_message(self, *a):
        pass

    def do_GET(self):
        path = os.path.join(args.root, self.path.lstrip("/").split("?")[0])
        if not os.path.isfile(path):
            log(f"404 {self.path}")
            self.send_response(404)
            self.send_header("Content-Length", "0")
            self.end_headers()
            return
        size = os.path.getsize(path)
        start = 0
        m = re.match(r"bytes=(\d+)-", self.headers.get("Range", ""))
        if m:
            start = int(m.group(1))
            if start >= size:
                log(f"416 {self.path} range {start}")
                self.send_response(416)
                self.send_header("Content-Length", "0")
                self.end_headers()
                return
            self.send_response(206)
            self.send_header("Content-Range", f"bytes {start}-{size - 1}/{size}")
        else:
            self.send_response(200)
        self.send_header("Content-Length", str(size - start))
        self.send_header("Accept-Ranges", "bytes")
        self.end_headers()
        bundle = path.endswith(".bundle")
        sent = 0
        status = "ok"
        with open(path, "rb") as f:
            f.seek(start)
            while True:
                chunk = f.read(16 * 1024)
                if not chunk:
                    break
                if bundle and args.stall_after >= 0 and sent + len(chunk) > args.stall_after:
                    chunk = chunk[: max(0, args.stall_after - sent)]
                    self.wfile.write(chunk)
                    sent += len(chunk)
                    log(f"STALL {self.path} at {start + sent}/{size}")
                    try:
                        while True:
                            time.sleep(1)
                            self.connection.getpeername()
                    except OSError:
                        pass
                    status = "stalled-closed"
                    break
                if bundle and args.drop_after >= 0 and sent + len(chunk) > args.drop_after:
                    chunk = chunk[: max(0, args.drop_after - sent)]
                    self.wfile.write(chunk)
                    sent += len(chunk)
                    self.connection.shutdown(socket.SHUT_RDWR)
                    status = "dropped"
                    break
                if bundle:
                    throttle(len(chunk))
                try:
                    self.wfile.write(chunk)
                except OSError:
                    status = "client-closed"
                    break
                sent += len(chunk)
        log(f"{self.command} {self.path} {'range ' + str(start) + ' ' if m else ''}{start + sent}/{size} {status}")
        if status != "ok":
            self.close_connection = True


log(f"serving {args.root} on {args.port} rate={args.rate} stall={args.stall_after} drop={args.drop_after}")
ThreadingHTTPServer(("127.0.0.1", args.port), Handler).serve_forever()
