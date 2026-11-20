# Stops the spike's own WebApi host (the one running from the Platform-grpc-spike worktree), nothing else.
powershell -NoProfile -c "Get-CimInstance Win32_Process | Where-Object { \$_.ExecutablePath -like '*Platform-grpc-spike*soft.square.net.WebApi.exe' } | ForEach-Object { Stop-Process -Id \$_.ProcessId -Force; \$_.ProcessId }"
