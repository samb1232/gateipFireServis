Сервис открытия дверей при срабатывании пожарной сигнализации для СКУД  GATE IP WEB

Инструкция по установке:

1.  Аппаратная часть.
Выбирается контроллер, к которому подключается выход с пожарной сигнализации.
Сигнал от пожарки подключается к контроллеру к входу например - Z8.
Далее создается виртуальная дверь с именем например "FireDoor".
На вход Z8 ставиться режим - свободный проход.

Логика работы службы - при поступлении сигнала от пожарки на вход Z8, виртуальная дверь "FireDoor" переходит в режим свободного прохода.
Сервис следит за этой дверью каждые 5 сек. И если она перешла в режим свободного прохода, то все двери переводяться в режим свободного прохода.
При возвращении виртуальной двери "FireDoor" в норму, все двери переключаются в норму.

3.  Программная чясть
   Скачяйте все файлы из папки Release
   
   
