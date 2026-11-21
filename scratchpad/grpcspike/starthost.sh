# Starts the spike's standalone WebApi host on :5398 (Development, grpcdemo as a local module, tenants acme and globex).
# Usage: bash starthost.sh <logfile> [extra env assignments, e.g. Kestrel__EndpointDefaults__Protocols=Http1AndHttp2]
here="$(cd "$(dirname "$0")" && pwd)"
log="$1"; shift
cd "$here/../../src/WebApi/WebApi"
env ASPNETCORE_ENVIRONMENT=Development ASPNETCORE_URLS=http://localhost:5398 \
  Modules__Local__grpcdemo="$here/WebApi.Modules.GrpcDemo/bin/Debug/net10.0" \
  Tenancy__Tenants__0__Id=acme Tenancy__Tenants__1__Id=globex \
  Access__Grants__0__Tenant=acme Access__Grants__0__UserId=editor1 Access__Grants__0__Permission=grpcdemo.read Access__Grants__0__Scope=tenant \
  "$@" dotnet run --no-build --no-launch-profile > "$here/$log" 2>&1
