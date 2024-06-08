$serviceName = "QontaHub.RunnerService"
New-Item -Type Directory -Force c:\tmp\Runner\_diag
New-Item -Type Directory -Force c:\tmp\Runner\bin

Get-Service $serviceName | Stop-Service
# Clear-EventLog -LogName Application

dotnet publish --configuration Debug --self-contained true `
    --output c:\tmp\Runner\bin .\src\QontaHub.RunnerService\QontaHub.RunnerService.csproj

dotnet publish --configuration Debug --self-contained true `
    --output c:\tmp\Runner\bin .\src\QontaHub.Runner\QontaHub.Runner.csproj
    
Copy-Item -Recurse -Path .\src\QontaHub.RunnerService\bin\Debug\net8.0-windows\win-arm64\* `
    c:\tmp\Runner\bin
Copy-Item -Recurse -Path .\src\QontaHub.Runner\bin\Debug\net8.0\win-arm64\QontaHub.Runner.* `
    c:\tmp\Runner\bin

Remove-Service $serviceName
New-Service -Name $serviceName -BinaryPathName c:\tmp\runner\bin\QontaHub.RunnerService.exe | Start-Service

