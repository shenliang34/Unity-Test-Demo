Test server for streaming responses

Files created:
- test_server.py  -- Python HTTP server (POST /stream) that streams response.txt using chunked transfer.
- response.txt    -- Sample text file whose contents are streamed back to clients.

Run the server (Windows cmd.exe):

```cmd
python test_server.py
```

Example: use curl to POST and show streaming output (keeps chunks as they arrive):

```cmd
curl -X POST http://localhost:8000/stream --data "hello" -N
```

Example: use Python requests to stream and print chunks:

```py
import requests
r = requests.post('http://localhost:8000/stream', data='x', stream=True)
for chunk in r.iter_content(chunk_size=None):
    if chunk:
        print(chunk.decode('utf-8'), end='')
```

Notes:
- The server uses HTTP chunked transfer encoding to simulate streaming; Unity `UnityWebRequest` or `HttpClient` clients can read the response progressively.
- Adjust `CHUNK_SIZE` and `CHUNK_DELAY` inside `test_server.py` to control chunk granularity and speed.
This is a test streaming response file.
It contains multiple lines to demonstrate chunked streaming.
Line 1: Hello, Unity!
Line 2: This server streams data in small chunks.
Line 3: You can use this file to test partial/streamed reads.
Line 4: End of message.
#!/usr/bin/env python3
"""
Simple test HTTP server that accepts POST /stream and streams the contents
of `response.txt` back to the client using HTTP chunked transfer encoding.

Usage:
    python test_server.py

Then POST to http://localhost:8000/stream to receive the streamed response.
"""
import time
import os
from http.server import ThreadingHTTPServer, BaseHTTPRequestHandler

HOST = '0.0.0.0'
PORT = 8000
RESPONSE_FILE = 'response.txt'
CHUNK_SIZE = 1024  # bytes per chunk
CHUNK_DELAY = 0.3  # seconds between chunks to simulate streaming


class StreamHandler(BaseHTTPRequestHandler):
    def do_POST(self):
        if self.path != '/stream':
            self.send_error(404, 'Not Found')
            return

        # read and ignore request body if any
        try:
            content_length = int(self.headers.get('Content-Length', 0))
        except Exception:
            content_length = 0
        if content_length > 0:
            _ = self.rfile.read(content_length)

        if not os.path.exists(RESPONSE_FILE):
            self.send_response(500)
            self.send_header('Content-Type', 'text/plain; charset=utf-8')
            self.end_headers()
            self.wfile.write(b'Response file not found')
            return

        # Send response headers for chunked transfer
        self.send_response(200)
        self.send_header('Content-Type', 'text/plain; charset=utf-8')
        self.send_header('Transfer-Encoding', 'chunked')
        self.end_headers()

        try:
            with open(RESPONSE_FILE, 'rb') as f:
                while True:
                    chunk = f.read(CHUNK_SIZE)
                    if not chunk:
                        break
                    # chunked encoding: <len in hex> CRLF <data> CRLF
                    size_line = f"{len(chunk):X}\r\n".encode('utf-8')
                    try:
                        self.wfile.write(size_line)
                        self.wfile.write(chunk)
                        self.wfile.write(b"\r\n")
                        self.wfile.flush()
                    except BrokenPipeError:
                        # client disconnected
                        break

                    # simulate streaming delay
                    time.sleep(CHUNK_DELAY)

            # terminating chunk
            try:
                self.wfile.write(b"0\r\n\r\n")
                self.wfile.flush()
            except BrokenPipeError:
                pass

        except Exception as e:
            # If any error occurs while streaming, just log and close
            self.log_error('Error while streaming: %s', str(e))

    def do_GET(self):
        # debug endpoint to fetch the whole response file
        if self.path != '/file':
            self.send_error(404, 'Not Found')
            return
        if not os.path.exists(RESPONSE_FILE):
            self.send_response(404)
            self.end_headers()
            self.wfile.write(b'Not found')
            return
        self.send_response(200)
        self.send_header('Content-Type', 'text/plain; charset=utf-8')
        self.send_header('Content-Length', str(os.path.getsize(RESPONSE_FILE)))
        self.end_headers()
        with open(RESPONSE_FILE, 'rb') as f:
            self.wfile.write(f.read())

    def log_message(self, format, *args):
        # keep minimal logs; override to include client address
        print("[HTTP] %s - %s" % (self.address_string(), format % args))


def run_server(host=HOST, port=PORT):
    addr = (host, port)
    server = ThreadingHTTPServer(addr, StreamHandler)
    print(f"Starting test server at http://{host}:{port}")
    print("POST /stream to receive streamed response (from response.txt)")
    try:
        server.serve_forever()
    except KeyboardInterrupt:
        print('\nShutting down')
        server.shutdown()


if __name__ == '__main__':
    run_server()

