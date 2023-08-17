$serviceName = "WebPortalService"

# Остановка и удаление службы
Stop-Service $serviceName
sc.exe delete $serviceName
