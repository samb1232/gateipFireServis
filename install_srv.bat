$serviceName = "WebPortalService"
$exePath = "bin\Debug\WindowsApplication.exe"

# ��������� ������
sc.exe create $serviceName binpath= $exePath start= auto

# �������� ��� ��������� ������
Start-Sleep -Seconds 3

# ������ ������
Start-Service $serviceName
