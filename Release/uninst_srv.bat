setlocal

:: Определение пути к текущему бат-файлу
set "batDir=%~dp0"

:: Полный путь к исполняемому файлу службы
set "exePath=%batDir%GateIPFireService.exe"

:: Удаление службы
%exePath% uninstall

endlocal
