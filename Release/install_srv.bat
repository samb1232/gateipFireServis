@echo off
setlocal

:: Определение пути к текущему бат-файлу
set "batDir=%~dp0"

:: Установка имени службы
set "serviceName=FireDoorService"

:: Полный путь к исполняемому файлу службы
set "exePath=%batDir%FireDoorService.exe"

:: Установка службы
sc.exe create %serviceName% binpath= "%exePath%" start= auto

:: Задержка для установки службы
timeout /t 60

:: Запуск службы
net start %serviceName%

endlocal
