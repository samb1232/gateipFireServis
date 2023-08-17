$serviceName = "WebPortalService"

# Stop and delete service
Stop-Service $serviceName
sc.exe delete $serviceName
