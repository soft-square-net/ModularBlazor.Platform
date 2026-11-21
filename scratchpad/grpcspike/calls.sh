# Runs the standard set of calls against a base URL: calls.sh <baseUrl> <web|h2>
here="$(cd "$(dirname "$0")" && pwd)"
C="dotnet $here/GrpcSpikeClient/bin/Debug/net10.0/GrpcSpikeClient.dll"; A=$(cat $here/tokens/admin.txt); E=$(cat $here/tokens/editor.txt)
echo "== anonymous"; $C $1 $2
echo "== admin (platform-admin, no tenant)"; $C $1 $2 $A - enforce
echo "== editor1, X-Tenant acme (granted grpcdemo.read)"; $C $1 $2 $E acme enforce
echo "== editor1, token tenant only"; $C $1 $2 $E - enforce
echo "== editor1, X-Tenant globex (token is for acme)"; $C $1 $2 $E globex
true
