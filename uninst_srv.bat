$serviceName = "WebPortalService"

# ��������� � �������� ������
Stop-Service $serviceName
sc.exe delete $serviceName
