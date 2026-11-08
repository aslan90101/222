# EndorVPN

Бесплатный VPN-клиент для Windows с большим выбором стран, флагами и двуязычным
интерфейсом (RU / EN). EndorVPN — это честный GUI-обёртка над проверенными
open-source компонентами: **OpenVPN** (шифрование и туннель), **VPN Gate**
(публичный список бесплатных серверов по всему миру) и **sing-box / VLESS**
(резервный быстрый транспорт для стран, где OpenVPN недоступен).

> EndorVPN не продаёт ваши данные, не содержит рекламы и не требует регистрации.
> Серверы предоставляются волонтёрами проекта VPN Gate (Университет Цукубы, Япония).

---

## English quick start

EndorVPN is a free Windows VPN client with a big country picker (flags +
localized names), RU/EN UI and a real system-wide tunnel. It wraps OpenVPN
(traffic encryption + TAP/Wintun adapter) and pulls the public VPN Gate server
list. A sing-box based transport provides a fast fallback. No account, no ads,
no traffic analytics.

### Build from source

```powershell
# requirements: Node.js 20+, .NET Framework 4.8 (preinstalled on Windows 10/11)
npm install
npm run build:all        # UI bundle + engine + Windows .exe
wix build installer/EndorVPN.wxs -o dist/EndorVPN-Setup.exe   # optional installer
```

The build is a single self-contained **EndorVPN.exe** (C# WPF shell + embedded
web UI rendered in WebView2). No Electron, no 150 MB runtime download.

---

## Что уже работает

* 🇷🇺/🇬🇧 Переключение языка интерфейса на лету (RU/EN), запоминается.
* 🌍 Каталог стран: флаги, названия на выбранном языке, число серверов, пинг.
* ⚡ «Умный выбор» — самый быстрый сервер страны по пингу и скорости.
* 🔌 Реальное подключение: весь трафик ПК идёт через туннель, IP меняется
  глобально (браузер, игры, любые приложения).
* 🛡️ Kill-switch: при обрыве туннеля интернет блокируется, чтобы реальный IP
  не «утекёл».
* 📊 Живая статистика: текущий IP, страна выхода, скорость up/down, объём
  трафика, время сессии.
* 🧪 Проверка качества серверов (пинг / скорость) перед подключением.
* 🕶️ Режим совместимости без драйвера TAP (через sing-box), если драйвер
  установить нельзя.
* 🧹 Автоочистка: соединение, маршруты, правила фаервола и службы
  восстанавливаются после выхода/падения.

## Требования

* Windows 10 (1809+) или Windows 11, x64.
* Права администратора при первом запуске (нужны для установки драйвера
  TAP-адаптера и изменения маршрутов — так работает любой системный VPN).
* Интернет для получения списка серверов VPN Gate.

## Установка

1. Скачайте `EndorVPN.exe` из раздела **Releases**.
2. Запустите — Windows запросит UAC, подтвердите (это нужно для туннеля).
3. Выберите страну и нажмите «Подключиться».

Портативная версия не пишет ничего в реестр, кроме установки сетевого
адаптера TAP (её можно откатить кнопкой «Удалить адаптер» в настройках).

## Скриншоты

_появятся после первого релиза_

## Структура репозитория

```
ui/           Web-интерфейс (HTML/CSS/JS, RU/EN, флаги стран)
engine/       VPN-движок (C#): оркестрация OpenVPN, маршруты, kill-switch
core/         Парсер VPN Gate, замер качества, кэш серверов
installer/    WiX-инсталлятор
build/        Скрипты сборки и упаковки
```

## Лицензия

MIT. В сборку входят сторонние компоненты под их собственными лицензиями:
OpenVPN (GPLv2), TAP-Windows (GPLv2), sing-box (GPLv3), WebView2 (Microsoft).
Список расположения лицензий — `THIRD_PARTY_NOTICES.md`.
