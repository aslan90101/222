# EndorVPN

Бесплатный VPN-клиент для Windows с большим выбором стран, флагами и двуязычным
интерфейсом (RU / EN). EndorVPN — GUI-обёртка над open-source компонентами:
**OpenVPN 2.6** (шифрование и системный туннель) и публичными бесплатными
серверами **VPN Gate** (Университет Цукубы, Япония) и **VPNBook**.

> Без рекламы, без регистрации, без телеметрии. Весь трафик системы уходит через
> туннель: приложение поднимает TAP-адаптер, переписывает маршрут по умолчанию и
> подменяет DNS.

---

## Скачать

**[EndorVPN-win64.zip](https://github.com/aslan90101/222/releases/download/v1.0.0/EndorVPN-win64.zip)** — последний релиз.

1. Распакуйте архив в любую папку (например `C:\EndorVPN`).
2. Запустите `EndorVPN.exe` **от имени администратора** (нужно для установки
   TAP-драйвера и изменения таблицы маршрутизации). При первом запуске Windows
   спросит подтверждение UAC — это нормально.
3. Выберите страну и нажмите большую кнопку питания.

При первом подключении EndorVPN сам установит TAP-драйвер (входит в комплект,
подпись OpenVPN). Больше ничего ставить не нужно.

---

## English quick start

A free Windows VPN client with a big country picker (flags + localized names),
RU/EN UI and a real system-wide tunnel. No account, no ads, no analytics.

1. Download **[EndorVPN-win64.zip](https://github.com/aslan90101/222/releases/download/v1.0.0/EndorVPN-win64.zip)** and unpack it anywhere.
2. Run `EndorVPN.exe` **as administrator** (needed for the TAP driver and the
   default route). The UAC prompt on first launch is expected.
3. Pick a country, press the big power button. The TAP driver installs itself on
   the first connect.

---

## Что умеет

* 🌍 Каталог стран: флаги, названия на выбранном языке, число серверов, живой пинг.
* ⚡ **Smart pick** — выбирает самый быстрый сервер по измеренному пингу и скорости.
* 🔒 **Настоящий системный туннель**: `route-gateway` + `redirect-gateway def1`,
  DNS `1.1.1.1` / `8.8.8.8`, `block-outside-dns`. IP меняется для всех программ,
  а не только для браузера.
* 🧭 Kill switch: при разрыве трафик не утекает в открытый интернет.
* 🔁 Автоперебор серверов: до 4 попыток по 30 секунд, с живым статусом
  «пробую хост…».
* 🛡 TAP-драйвер ставится сам при первом подключении и удаляется из настроек.
* 🇷🇺/🇬🇧 Переключение языка на лету, выбор запоминается.
* ⭐ Избранные страны, поиск, вкладки «Все / Быстрые / Избранные / Рядом».
* 📊 Счётчики скорости и трафика, таймер сессии.
* 🖥 Сворачивание в трей, запуск свёрнутым, единственный экземпляр.

## Почему это работает

Клиент не «проксирует браузер». Он запускает настоящий `openvpn.exe` (входит в
комплект) с конфигом, который поднимает TAP-адаптер `EndorVPN`, забирает у
провайдера маршрут по умолчанию и отправляет весь IPv4/IPv6-трафик в туннель.
Пока соединение активно, внешний IP у всех приложений — адрес выходного узла.

Серверные профили приходят из двух бесплатных источников:

| Источник | Что даёт | Авторизация |
|---|---|---|
| VPN Gate (Цукуба) | десятки стран, TCP/UDP, добровольческие узлы | логин/пароль выдаёт сам VPN Gate (AUTH_FAILED с части IP) |
| VPNBook | 10 стран, стабильные узлы | общий логин, парсится со страницы провайдера |

Список обновляется в фоне (оба источника параллельно), кэшируется локально в
зашифрованном виде и переиспользуется при следующем запуске.

## Сборка из исходников

Требования: Windows + .NET Framework 4.8 (есть в Windows 10/11 из коробки),
MSBuild / Visual Studio Build Tools.

```powershell
# движок: OpenVPN 2.6.12 + TAP-драйвер распаковываются в engine/
# (это делает CI, см. .github/workflows/build.yml)
msbuild engine\EndorVPN\EndorVPN.csproj /p:Configuration=Release

# готовая папка dist\EndorVPN\ собирается шагом "bundle" в том же workflow
```

CI на каждый пуш собирает `EndorVPN.exe`, проверяет движок (`openvpn.exe
--version`), гоняет headless-тест реального туннеля (`--e2e-connect`, проверяет
смену IP), снимает скриншоты UI и публикует артефакты.

## Устройство репозитория

```
engine/EndorVPN/          WPF-приложение (C#)
  Program.cs              вход, режимы --smoke-test / --e2e-connect
  MainWindow.xaml(.cs)    весь интерфейс и оркестрация подключения
  Core/VpnEngine.cs       запуск openvpn, разбор состояния, перебор серверов
  Core/ConfigBuilder.cs   генерация .ovpn из профиля провайдера
  Core/ServerSource.cs    VPN Gate CSV -> список серверов
  Core/VpnBookSource.cs   VPNBook: страница + профили + учётка
  Core/TapManager.cs      установка/удаление TAP-драйвера
  Core/Localization.cs    строки RU/EN и названия стран
  Core/Flags.cs           271 встроенный флаг (base64 PNG)
ci/                       скрипты CI: smoke, скриншоты, e2e-тест туннеля
```

## Ограничения

* Только Windows (WPF + TAP-драйвер).
* Бесплатные узлы бывают перегружены: скорость зависит от выбранной страны.
* Часть IP-адресов VPN Gate получает `AUTH_FAILED` — клиент автоматически
  переходит к следующему серверу или к VPNBook.
* Нужны права администратора: без них нельзя поставить драйвер и маршрут.

## Лицензии

Код EndorVPN — MIT (см. `LICENSE`). OpenVPN и TAP-драйвер — GPLv2, флаги —
MIT (lipis/flag-icons). Подробности в `THIRD_PARTY_NOTICES.md`.
