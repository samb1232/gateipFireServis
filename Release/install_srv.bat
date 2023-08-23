setlocal

:: Определение пути к текущему бат-файлу
set "batDir=%~dp0"

:: Полный путь к исполняемому файлу службы
set "exePath=%batDir%GateIPFireService.exe"

:: Установка службы
%exePath% install

:: Задержка для установки службы
timeout /t 2

:: Запуск службы
%exePath% start

endlocal
