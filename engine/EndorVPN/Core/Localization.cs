using System;
using System.Collections.Generic;

namespace EndorVPN.Core
{
    /// <summary>
    /// RU/EN string table. Deliberately a plain dictionary: the UI reads keys
    /// through Loc.T(...) and a language switch triggers a full re-render.
    /// </summary>
    public static class Loc
    {
        public static event Action Changed;

        private static string _lang = "ru";

        public static string Lang
        {
            get { return _lang; }
            set
            {
                var v = (value == "en") ? "en" : "ru";
                if (v == _lang) return;
                _lang = v;
                var h = Changed;
                if (h != null) h();
            }
        }

        /// <summary>Picks the language for a fresh install from the OS locale.</summary>
        public static string DetectDefault()
        {
            try
            {
                var c = System.Globalization.CultureInfo.InstalledUICulture;
                if (c != null && c.TwoLetterISOLanguageName == "ru") return "ru";
                var c2 = System.Globalization.CultureInfo.CurrentUICulture;
                if (c2 != null && c2.TwoLetterISOLanguageName == "ru") return "ru";
            }
            catch { }
            return "en";
        }

        public static string T(string key)
        {
            var table = _lang == "en" ? En : Ru;
            string v;
            if (table.TryGetValue(key, out v)) return v;
            if (Ru.TryGetValue(key, out v)) return v;
            return key;
        }

        public static string T(string key, params object[] args)
        {
            try { return string.Format(T(key), args); }
            catch { return T(key); }
        }

        private static readonly Dictionary<string, string> Ru = new Dictionary<string, string>
        {
            { "app.tag", "бесплатный · без рекламы" },
            { "status.disconnected", "Не подключено" },
            { "status.connecting", "Подключение…" },
            { "status.connected", "Защищено" },
            { "status.disconnecting", "Отключение…" },
            { "status.error", "Ошибка подключения" },
            { "status.reconnecting", "Переподключение…" },
            { "hero.hint.connect", "Нажмите, чтобы подключиться" },
            { "hero.hint.disconnect", "Нажмите, чтобы отключиться" },
            { "hero.hint.busy", "Устанавливаем туннель…" },
            { "hero.hint.cancel", "Нажмите, чтобы отменить" },
            { "hero.hint.cancelsuffix", " · нажмите, чтобы отменить" },
            { "hero.hint.nocountry", "Выберите страну из списка" },
            { "ip.real", "Ваш IP" },
            { "ip.vpn", "IP через VPN" },
            { "ip.refresh", "Обновить" },
            { "ip.unknown", "неизвестно" },
            { "ip.checking", "проверка…" },
            { "metrics.down", "Скорость ↓" },
            { "metrics.up", "Скорость ↑" },
            { "metrics.session", "Сессия" },
            { "metrics.traffic", "Трафик" },
            { "picker.title", "Выбор страны" },
            { "picker.search", "Найти страну" },
            { "picker.smart", "Умный выбор" },
            { "picker.empty", "Ничего не найдено" },
            { "picker.emptyhint", "Попробуйте изменить запрос" },
            { "picker.noservers", "Серверы не загрузились" },
            { "picker.noservers.hint", "Проверьте интернет и нажмите «Обновить»" },
            { "picker.refresh", "Обновить список серверов" },
            { "picker.loading", "Загружаем серверы…" },
            { "picker.never", "ещё не обновляли" },
            { "picker.updated", "обновлено в {0}" },
            { "picker.servers", "серверов" },
            { "tabs.all", "Все" },
            { "tabs.fast", "Быстрые" },
            { "tabs.favorite", "Избранные" },
            { "tabs.nearby", "Рядом" },
            { "row.server.one", "1 сервер" },
            { "row.server.many", "{0} серверов" },
            { "row.bestping", "пинг {0} мс" },
            { "row.selected", "выбрано" },
            { "row.fast", "быстрый" },
            { "row.load", "загрузка {0}%" },
            { "settings.title", "Настройки" },
            { "settings.language", "Язык интерфейса" },
            { "settings.killswitch", "Kill-switch" },
            { "settings.killswitch.hint", "Блокировать интернет при обрыве туннеля" },
            { "settings.autoconnect", "Автоподключение" },
            { "settings.autoconnect.hint", "Подключаться при запуске EndorVPN" },
            { "settings.launch", "Запускать с Windows" },
            { "settings.launch.hint", "Добавить в автозагрузку" },
            { "settings.transport", "Транспорт" },
            { "settings.transport.auto", "Авто" },
            { "settings.transport.tcp", "TCP 443" },
            { "settings.transport.udp", "UDP" },
            { "settings.transport.hint", "TCP надёжнее при блокировках, UDP быстрее." },
            { "settings.servers", "Серверы" },
            { "settings.source", "Источники" },
            { "settings.engine", "Движок" },
            { "settings.version", "Версия" },
            { "settings.removeDriver", "Удалить сетевой драйвер EndorVPN" },
            { "settings.removeDriver.hint", "Удаляет адаптер из системы. VPN перестанет работать до повторной установки." },
            { "settings.made", "Сделано для свободного интернета" },
            { "logs.title", "Журнал подключения" },
            { "logs.copy", "Копировать" },
            { "logs.copied", "Журнал скопирован" },
            { "admin.title", "Нужны права администратора" },
            { "admin.text", "Чтобы менять IP всего компьютера, EndorVPN устанавливает сетевой драйвер и маршруты. Перезапустите приложение от имени администратора." },
            { "admin.restart", "Перезапустить от администратора" },
            { "admin.later", "Позже" },
            { "toast.connected", "Подключено: {0}" },
            { "toast.disconnected", "Отключено" },
            { "toast.failed", "Не удалось подключиться" },
            { "toast.tryingnext", "Пробуем следующий сервер…" },
            { "toast.servers", "Загружено серверов: {0}" },
            { "toast.noservers", "Не удалось получить список серверов" },
            { "toast.ipchanged", "IP изменён: {0}" },
            { "toast.ipsame", "IP не изменился — проверьте туннель" },
            { "toast.ipfailed", "Не удалось проверить IP" },
            { "toast.selectcountry", "Сначала выберите страну" },
            { "toast.killswitch.on", "Kill-switch включён" },
            { "toast.killswitch.off", "Kill-switch выключен" },
            { "toast.admin", "Нужны права администратора" },
            { "toast.driverfail", "Не удалось установить сетевой драйвер" },
            { "toast.driverok", "Сетевой драйвер готов" },
            { "toast.smart", "Умный выбор: {0}" },
            { "toast.copied", "Скопировано" },
            { "toast.retry", "Повторная попытка через другой сервер" },
            { "toast.cancelled", "Подключение отменено" },
            { "err.start", "Движок не запустился" },
            { "err.timeout", "Превышено время подключения" },
            { "err.auth", "Сервер отклонил подключение" },
            { "err.network", "Сервер недоступен" },
            { "err.driver", "Не удалось открыть сетевой адаптер" },
            { "err.unknown", "Неизвестная ошибка" },
            { "err.cancelled", "Отменено" },
            { "stage.fetch", "Получаем список серверов…" },
            { "stage.driver", "Готовим сетевой адаптер…" },
            { "stage.tunnel", "Устанавливаем туннель…" },
            { "stage.routes", "Настраиваем маршруты…" },
            { "stage.verify", "Проверяем новый IP…" },
            { "unit.mbps", "Мбит/с" },
            { "unit.mb", "МБ" },
            { "unit.gb", "ГБ" },
            { "unit.ms", "мс" },
            { "tray.show", "Открыть EndorVPN" },
            { "tray.connect", "Подключиться" },
            { "tray.disconnect", "Отключиться" },
            { "tray.quit", "Выход" },
            { "tray.minimized", "EndorVPN свёрнут в трей" },
            { "ctx.close", "Закрыть" },
            { "quality.checking", "проверяем…" },
        };

        private static readonly Dictionary<string, string> En = new Dictionary<string, string>
        {
            { "app.tag", "free · no ads" },
            { "status.disconnected", "Not connected" },
            { "status.connecting", "Connecting…" },
            { "status.connected", "Protected" },
            { "status.disconnecting", "Disconnecting…" },
            { "status.error", "Connection failed" },
            { "status.reconnecting", "Reconnecting…" },
            { "hero.hint.connect", "Click to connect" },
            { "hero.hint.disconnect", "Click to disconnect" },
            { "hero.hint.busy", "Establishing tunnel…" },
            { "hero.hint.cancel", "Click to cancel" },
            { "hero.hint.cancelsuffix", " · click to cancel" },
            { "hero.hint.nocountry", "Pick a country from the list" },
            { "ip.real", "Your IP" },
            { "ip.vpn", "VPN IP" },
            { "ip.refresh", "Refresh" },
            { "ip.unknown", "unknown" },
            { "ip.checking", "checking…" },
            { "metrics.down", "Download" },
            { "metrics.up", "Upload" },
            { "metrics.session", "Session" },
            { "metrics.traffic", "Traffic" },
            { "picker.title", "Choose a country" },
            { "picker.search", "Search country" },
            { "picker.smart", "Smart pick" },
            { "picker.empty", "Nothing found" },
            { "picker.emptyhint", "Try a different query" },
            { "picker.noservers", "Servers did not load" },
            { "picker.noservers.hint", "Check your connection and press Refresh" },
            { "picker.refresh", "Refresh server list" },
            { "picker.loading", "Loading servers…" },
            { "picker.never", "never refreshed" },
            { "picker.updated", "updated at {0}" },
            { "picker.servers", "servers" },
            { "tabs.all", "All" },
            { "tabs.fast", "Fastest" },
            { "tabs.favorite", "Favorites" },
            { "tabs.nearby", "Nearby" },
            { "row.server.one", "1 server" },
            { "row.server.many", "{0} servers" },
            { "row.bestping", "{0} ms ping" },
            { "row.selected", "selected" },
            { "row.fast", "fast" },
            { "row.load", "{0}% load" },
            { "settings.title", "Settings" },
            { "settings.language", "Interface language" },
            { "settings.killswitch", "Kill-switch" },
            { "settings.killswitch.hint", "Block the internet if the tunnel drops" },
            { "settings.autoconnect", "Auto-connect" },
            { "settings.autoconnect.hint", "Connect when EndorVPN starts" },
            { "settings.launch", "Start with Windows" },
            { "settings.launch.hint", "Add to startup" },
            { "settings.transport", "Transport" },
            { "settings.transport.auto", "Auto" },
            { "settings.transport.tcp", "TCP 443" },
            { "settings.transport.udp", "UDP" },
            { "settings.transport.hint", "TCP survives blocking better, UDP is faster." },
            { "settings.servers", "Servers" },
            { "settings.source", "Sources" },
            { "settings.engine", "Engine" },
            { "settings.version", "Version" },
            { "settings.removeDriver", "Remove EndorVPN network driver" },
            { "settings.removeDriver.hint", "Removes the adapter. VPN stops working until reinstalled." },
            { "settings.made", "Made for a free internet" },
            { "logs.title", "Connection log" },
            { "logs.copy", "Copy" },
            { "logs.copied", "Log copied" },
            { "admin.title", "Administrator rights required" },
            { "admin.text", "To route the whole PC through the VPN, EndorVPN installs a network driver and routes. Restart the app as administrator." },
            { "admin.restart", "Restart as administrator" },
            { "admin.later", "Later" },
            { "toast.connected", "Connected: {0}" },
            { "toast.disconnected", "Disconnected" },
            { "toast.failed", "Could not connect" },
            { "toast.tryingnext", "Trying the next server…" },
            { "toast.servers", "Servers loaded: {0}" },
            { "toast.noservers", "Could not fetch the server list" },
            { "toast.ipchanged", "IP changed: {0}" },
            { "toast.ipsame", "IP did not change — check the tunnel" },
            { "toast.ipfailed", "Could not check the IP" },
            { "toast.selectcountry", "Pick a country first" },
            { "toast.killswitch.on", "Kill-switch enabled" },
            { "toast.killswitch.off", "Kill-switch disabled" },
            { "toast.admin", "Administrator rights required" },
            { "toast.driverfail", "Network driver installation failed" },
            { "toast.driverok", "Network driver ready" },
            { "toast.smart", "Smart pick: {0}" },
            { "toast.copied", "Copied" },
            { "toast.retry", "Retrying via another server" },
            { "toast.cancelled", "Connection cancelled" },
            { "err.start", "Engine failed to start" },
            { "err.timeout", "Connection timed out" },
            { "err.auth", "Server rejected the connection" },
            { "err.network", "Server unreachable" },
            { "err.driver", "Could not open the network adapter" },
            { "err.unknown", "Unknown error" },
            { "err.cancelled", "Cancelled" },
            { "stage.fetch", "Fetching servers…" },
            { "stage.driver", "Preparing network adapter…" },
            { "stage.tunnel", "Establishing tunnel…" },
            { "stage.routes", "Configuring routes…" },
            { "stage.verify", "Verifying new IP…" },
            { "unit.mbps", "Mbps" },
            { "unit.mb", "MB" },
            { "unit.gb", "GB" },
            { "unit.ms", "ms" },
            { "tray.show", "Show EndorVPN" },
            { "tray.connect", "Connect" },
            { "tray.disconnect", "Disconnect" },
            { "tray.quit", "Quit" },
            { "tray.minimized", "EndorVPN minimized to tray" },
            { "ctx.close", "Close" },
            { "quality.checking", "checking…" },
        };

        private static readonly Dictionary<string, string[]> CountryRu = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            { "AE", new[] { "ОАЭ" } }, { "AM", new[] { "Армения" } }, { "AR", new[] { "Аргентина" } },
            { "AT", new[] { "Австрия" } }, { "AU", new[] { "Австралия" } }, { "AZ", new[] { "Азербайджан" } },
            { "BA", new[] { "Босния и Герцеговина" } }, { "BD", new[] { "Бангладеш" } }, { "BE", new[] { "Бельгия" } },
            { "BG", new[] { "Болгария" } }, { "BO", new[] { "Боливия" } }, { "BR", new[] { "Бразилия" } },
            { "BY", new[] { "Беларусь" } }, { "CA", new[] { "Канада" } }, { "CH", new[] { "Швейцария" } },
            { "CL", new[] { "Чили" } }, { "CN", new[] { "Китай" } }, { "CO", new[] { "Колумбия" } },
            { "CR", new[] { "Коста-Рика" } }, { "CY", new[] { "Кипр" } }, { "CZ", new[] { "Чехия" } },
            { "DE", new[] { "Германия" } }, { "DK", new[] { "Дания" } }, { "EE", new[] { "Эстония" } },
            { "EG", new[] { "Египет" } }, { "ES", new[] { "Испания" } }, { "FI", new[] { "Финляндия" } },
            { "FR", new[] { "Франция" } }, { "GB", new[] { "Великобритания" } }, { "GE", new[] { "Грузия" } },
            { "GR", new[] { "Греция" } }, { "HK", new[] { "Гонконг" } }, { "HR", new[] { "Хорватия" } },
            { "HU", new[] { "Венгрия" } }, { "ID", new[] { "Индонезия" } }, { "IE", new[] { "Ирландия" } },
            { "IL", new[] { "Израиль" } }, { "IN", new[] { "Индия" } }, { "IR", new[] { "Иран" } },
            { "IS", new[] { "Исландия" } }, { "IT", new[] { "Италия" } }, { "JP", new[] { "Япония" } },
            { "KE", new[] { "Кения" } }, { "KG", new[] { "Киргизия" } }, { "KH", new[] { "Камбоджа" } },
            { "KR", new[] { "Южная Корея" } }, { "KZ", new[] { "Казахстан" } }, { "LA", new[] { "Лаос" } },
            { "LK", new[] { "Шри-Ланка" } }, { "LT", new[] { "Литва" } }, { "LU", new[] { "Люксембург" } },
            { "LV", new[] { "Латвия" } }, { "MA", new[] { "Марокко" } }, { "MD", new[] { "Молдавия" } },
            { "MK", new[] { "Северная Македония" } }, { "MM", new[] { "Мьянма" } }, { "MN", new[] { "Монголия" } },
            { "MX", new[] { "Мексика" } }, { "MY", new[] { "Малайзия" } }, { "NG", new[] { "Нигерия" } },
            { "NL", new[] { "Нидерланды" } }, { "NO", new[] { "Норвегия" } }, { "NP", new[] { "Непал" } },
            { "NZ", new[] { "Новая Зеландия" } }, { "PA", new[] { "Панама" } }, { "PE", new[] { "Перу" } },
            { "PH", new[] { "Филиппины" } }, { "PK", new[] { "Пакистан" } }, { "PL", new[] { "Польша" } },
            { "PT", new[] { "Португалия" } }, { "PY", new[] { "Парагвай" } }, { "QA", new[] { "Катар" } },
            { "RO", new[] { "Румыния" } }, { "RS", new[] { "Сербия" } }, { "RU", new[] { "Россия" } },
            { "SA", new[] { "Саудовская Аравия" } }, { "SE", new[] { "Швеция" } }, { "SG", new[] { "Сингапур" } },
            { "SI", new[] { "Словения" } }, { "SK", new[] { "Словакия" } }, { "TH", new[] { "Таиланд" } },
            { "TR", new[] { "Турция" } }, { "TW", new[] { "Тайвань" } }, { "TZ", new[] { "Танзания" } },
            { "UA", new[] { "Украина" } }, { "US", new[] { "США" } }, { "UZ", new[] { "Узбекистан" } },
            { "VE", new[] { "Венесуэла" } }, { "VN", new[] { "Вьетнам" } }, { "ZA", new[] { "ЮАР" } },
        };

        /// <summary>Localized country name by ISO2; falls back to the English name from the feed.</summary>
        public static string Country(string iso2, string fallbackEnglish)
        {
            if (string.IsNullOrEmpty(iso2)) return fallbackEnglish ?? "?";
            if (Lang == "ru")
            {
                string[] v;
                if (CountryRu.TryGetValue(iso2, out v)) return v[0];
            }
            return string.IsNullOrEmpty(fallbackEnglish) ? iso2 : fallbackEnglish;
        }
    }
}
