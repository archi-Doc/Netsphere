# Set-ExecutionPolicy RemoteSigned

# Run
docker run --mount type=bind,source=c:/app/logvolume,destination=/logs --rm -p 49152:49152/udp archidoc422/netsphere-basictest -port 49152
Write-Output ""

Write-Output "" "Press any key to exit."
Read-Host
