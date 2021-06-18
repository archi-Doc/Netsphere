# Set-ExecutionPolicy RemoteSigned

# Run
docker run --mount type=bind,source=$(pwd)/.app,destination=/app --rm -p 49152:49152/udp archidoc422/netsphere-basictest `
-mode receive -dir /app -port 49152
Write-Output ""

Write-Output "" "Press any key to exit."
Read-Host
