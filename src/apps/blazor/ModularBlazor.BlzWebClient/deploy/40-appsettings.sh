#!/bin/sh
# Run by the nginx image's entrypoint before nginx starts: point the client at this deployment.
# A WebAssembly app reads no server-side environment, so the URLs go into wwwroot/appsettings.json.
set -eu
file=/usr/share/nginx/html/appsettings.json
if [ -n "${OIDC_AUTHORITY:-}" ]; then
    sed -i -E "s#(\"Authority\": *\")[^\"]*#\1${OIDC_AUTHORITY}#" "$file"
fi
if [ -n "${WEBAPI_BASE_URL:-}" ]; then
    sed -i -E "s#(\"BaseUrl\": *\")[^\"]*#\1${WEBAPI_BASE_URL}#" "$file"
fi
# The publish output also has precompressed copies; they would still carry the build-time URLs.
rm -f "$file.br" "$file.gz"
echo "appsettings.json: Authority=${OIDC_AUTHORITY:-(unchanged)} BaseUrl=${WEBAPI_BASE_URL:-(unchanged)}"
