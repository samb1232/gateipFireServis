setlocal

:: Определение пути к текущему бат-файлу
set "batDir=%~dp0"

:: Полный путь к исполняемому файлу службы
set "exePath=%batDir%GateIPFireService.exe"

:: Установка службы
%exePath% uninstall

:: Запуск службы
pause

endlocal
