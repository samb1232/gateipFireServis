setlocal

:: Остановка службы
net stop FireDoorService

:: Удаление службы
sc.exe delete FireDoorService

endlocal
