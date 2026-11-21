// Serves index.html on http://localhost:5397 (another origin than the host, like the Blazor WASM client).
require("http").createServer((q, s) => { s.writeHead(200, { "Content-Type": "text/html" }); s.end(require("fs").readFileSync(__dirname + "/index.html")); }).listen(5397);
