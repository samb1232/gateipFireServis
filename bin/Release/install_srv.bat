$serviceName = "WebPortalService"
$exePath = ".\WindowsApplication.exe"

# Install service
sc.exe create $serviceName binpath= $exePath start= auto

# Delay for service to install
Start-Sleep -Seconds 3

# Run service
Start-Service $serviceName
