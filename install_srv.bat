$serviceName = "WebPortalService"
$exePath = "bin\Debug\WindowsApplication.exe"

# Установка службы
sc.exe create $serviceName binpath= $exePath start= auto

# Задержка для установки службы
Start-Sleep -Seconds 3

# Запуск службы
Start-Service $serviceName
