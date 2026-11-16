# Контекст для продолжения работы на Windows

Инструкции для работы на Windows-машине. Прочитай его целиком, затем `README.md`,
`docs/design.md` и `docs/acceptance.md`, и продолжай отсюда.

## Что это

FreeTurnX для Windows — нативный клиент [free-turn-proxy](https://github.com/hackdiaz-dev/free-turn-proxy).
Он поднимает системный VPN (WireGuard / AmneziaWG) через TURN-релей звонка или напрямую и
маршрутизирует трафик по программам, доменам и подсетям. Это порт клиента для macOS
([freeturn-macos](https://github.com/huhhuh78787878-collab/freeturn-macos), версия 0.2.0), функции
те же. Образец функций и UX — Android-клиент
[turn-proxy-android](https://github.com/hackdiaz-dev/turn-proxy-android).

- Репозиторий: `huhhuh78787878-collab/freeturn-win`, ветка `main`.
- Версия 0.1.0 (`Directory.Build.props`) ещё не выпускалась: на macOS запустить её было негде.
- Telegram-канал проекта (в «О проекте» и в разовом предложении подписаться): https://t.me/+ZVdbpZB0w_Q1MTdi
- Название в интерфейсе — **FreeTurnX**. Имена репозиториев, службы (`FreeTurnDaemon`), адаптера
  (`FreeTurn`) и папок данных (`FreeTurn`) не меняются.

## Главная задача на Windows

Всё собрано и проверено тестами логики, но **ни разу не запускалось на Windows**. Нужно:

1. Собрать, установить и пройти чек-лист `docs/acceptance.md`, по ходу исправляя найденное.
2. Когда чек-лист пройден, выпустить релиз v0.1.0 (см. «Релиз»).

Сильнее всего рискуют то, что нельзя было проверить без Windows:
- служба: установка и запуск, ACL канала, права на `%ProgramData%\FreeTurn\daemon`;
- wintun внутри движка: поднимается ли адаптер `FreeTurn` и ставятся ли маршруты;
- привязка сокетов ядра к физическому интерфейсу (`IP_UNICAST_IF`), чтобы трафик ядра шёл мимо
  туннеля: признак ошибки — переподключения по кругу сразу после подъёма туннеля;
- выбор физического интерфейса (`NetworkWatcher`, `Formatters.NetworkSelect`);
- WPF: тема Fluent, трей, окно капчи на WebView2, импорт QR со снимка экрана;
- автообновление: тихий установщик `/S` с UAC и повторный запуск приложения.

## Устройство (кратко; подробно в docs/design.md)

Три процесса, протокол между ними — строки JSON (`ProtocolVersion = "1"`):

| Процесс | Где | Что делает |
|---|---|---|
| `FreeTurnX.exe` | `src/FreeTurnX` (WPF, .NET 10, single-file, self-contained) | интерфейс, трей, хранение, конфиги |
| `freeturn-core.exe` | `helper/cmd/freeturn-core` (Go) | обёртка над пакетом `mobile` ядра: TURN/релей, капча, stdin/stdout; запускается от пользователя |
| `freeturnd.exe` | `helper/cmd/freeturnd` (Go) | служба `FreeTurnDaemon` (LocalSystem): запускает движок Amnezia Box с wintun; канал `\\.\pipe\freeturnd` |

- Движок — `amnezia-box.exe` (форк sing-box 1.12, теги `with_gvisor,with_wireguard,with_awg`);
  подписанный wintun.dll вшит в sing-tun и грузится из памяти. Своих драйверов нет — так решил
  пользователь (вариант A).
- Ядро слушает UDP на `127.0.0.1:<порт>`; движок поднимает туннель и шлёт WG/AWG на этот порт.
- Служба гасит движок, когда приложение отключилось от канала: если убить FreeTurnX.exe,
  интернет не должен пропасть.
- Конфиг от приложения проходит `Sanitize` в службе: пути к файлам запрещены.
- ACL канала: `D:P(A;;GA;;;SY)(A;;GA;;;BA)(A;;GRGW;;;IU)`.

### Код

- `src/FreeTurn.Core` (net10.0, без WPF — тестируется где угодно): `Models`, `Store`, `Link`
  (`freeturn://` = base64url JSON, порядок полей как в Go), `WGConfig`, `CoreConfig`, `Routing`
  (`EngineConfig`, `EscapeRegex` — экранирование под RE2, а не `Regex.Escape`), `Captcha`, `Updates`,
  `Connection`, `ConnectionController`, `Formatters`, `Logs`.
- `src/FreeTurn.Core.Tests` — 91 тест xUnit, среди них `LocalizationTests`.
- `src/FreeTurnX`: `AppModel`, `Services/*` (CoreProcess, DaemonClient, NetworkWatcher, Updater,
  QrImport, TrayIcon, AppIcons, System), `Views/*`. Интерфейс собирается кодом (`Views/Ui.cs`), без XAML-разметки.
- `helper/internal/bridge` — мост к ядру, `bind_windows.go` — привязка сокетов.
- `helper/internal/daemon` — служба: `server.go`, `engine.go`, `sanitize.go`, `platform_windows.go`.
- `installer/freeturnx.nsi` — NSIS: проверка архитектуры и ОС, вшитый загрузчик WebView2, установка
  службы, протокол `freeturn:`, удаление.
- `third_party/` — подмодули: `free-turn-proxy` (v4.1.5), `amnezia-box` (v1.260910.0), без локальных правок.

## Правила проекта

- Интерфейс по-русски. Ключ локализации — сам русский текст: `L("…")` и `F("…", args)`. Английский
  перевод лежит в `src/FreeTurnX/Resources/Strings.en.json`. Тесты требуют перевод для каждого
  ключа, запрещают лишние и пустые записи и проверяют совпадение `%@` / `%d`. Добавил строку —
  добавь перевод.
- Секреты (адреса, ключи, ссылка на звонок) не попадают в логи и не видны в приватном режиме:
  в формах `PasswordBox`, логи проходят через `Redactor`.
- Варианты иконки: `standard` (в интерфейсе «Classic»), `black`, `x`. В «О проекте» всегда логотип `x`.
- Разделы окна собираются один раз и кэшируются (`MainWindow.pages`): пересборка давала задержку.
- Коммиты — на английском, автор `huhhuh78787878-collab <357285562+huhhuh78787878-collab@users.noreply.github.com>`
  (настроено в локальном git-конфиге; на новой машине выставь заново). Личное имя и почту
  пользователя в коммиты не пиши.
- Документация и заметки к релизам — по-русски.

## Сборка на Windows

Нужны: Git, Go 1.26+, .NET SDK 10, NSIS 3 (`makensis` в PATH).

```powershell
git clone --recurse-submodules https://github.com/huhhuh78787878-collab/freeturn-win
cd freeturn-win
git config user.name huhhuh78787878-collab
git config user.email 357285562+huhhuh78787878-collab@users.noreply.github.com

# Тесты
dotnet test src/FreeTurn.Core.Tests
cd helper; go test ./...; cd ..

# Бинарники под x64 (для arm64: GOARCH=arm64, -r win-arm64)
$env:GOOS="windows"; $env:GOARCH="amd64"
cd helper; go build -trimpath -ldflags "-checklinkname=0 -s -w -X github.com/samosvalishe/free-turn-proxy/mobile.version=v4.1.5" -o ..\build\x64\ ./cmd/...; cd ..
cd third_party\amnezia-box; go build -trimpath -ldflags "-s -w" -tags "with_gvisor,with_wireguard,with_awg" -o ..\..\build\x64\amnezia-box.exe ./cmd/sing-box; cd ..\..
dotnet publish src/FreeTurnX -c Release -r win-x64 -o build\x64\app
move build\x64\app\FreeTurnX.exe build\x64\
```

`scripts/build.sh` — та же сборка для macOS/Linux (.NET и NSIS там идут в Docker). На Windows
её можно запустить из Git Bash, если поставить `dotnet` и `makensis` и заменить два вызова
`docker run …` на прямые команды. Можно и завести `scripts/build.ps1`.

Установщик (нужен `build\MicrosoftEdgeWebview2Setup.exe`, ссылка — в `scripts/build.sh`):

```powershell
makensis -DVERSION=0.1.0 -DARCH=x64 -DSRC=build\x64 -DOUT=dist\FreeTurnX-0.1.0-x64-setup.exe installer\freeturnx.nsi
```

### Отладка без установщика

- Служба вручную (в консоли администратора): `build\x64\freeturnd.exe install`, затем
  `sc query FreeTurnDaemon`; удалить — `freeturnd.exe uninstall`. Состояние и логи службы лежат в
  `%ProgramData%\FreeTurn\daemon`.
- Данные приложения: `%LOCALAPPDATA%\FreeTurn`.
- Ядро отдельно: `freeturn-core.exe -state-dir <папка>`; на stdin подаются строки JSON. Пример
  конфига — `helper/internal/bridge/testdata/core-config-*.json`.
- Проверить конфиг движка: `amnezia-box.exe check -c <файл>`.
- Ход ручной пробы на macOS — `scripts/proof.sh` в freeturn-macos (ядро + движок без приложения).
  Для Windows его можно повторить в PowerShell.

## Релиз (после приёмки)

- В `dist/` должны лежать только `FreeTurnX-<v>-x64-setup.exe`, `FreeTurnX-<v>-arm64-setup.exe` и
  `SHA256SUMS.txt`: автообновление ищет установщик своей архитектуры по префиксу `FreeTurnX-` и сверяет сумму.
- `gh release create v0.1.0 dist/* -R huhhuh78787878-collab/freeturn-win --title "FreeTurnX 0.1.0"`,
  заметки по-русски. Пушить и выпускать — с аккаунта `huhhuh78787878-collab` (`gh auth switch`).
  На macOS основным был аккаунт iRedTea; после публикации на него переключались обратно.
- Подписи Authenticode нет: SmartScreen предупредит, и в заметках об этом надо сказать.

## Что дальше (после 0.1.0)

`PLAN.txt` в freeturn-macos — список того, чего нет по сравнению с Android. Этап 3 — управление
сервером: установка по SSH и пользователи сервера. Под это планируется отдельная веб-админка
`freeturn-admin`. Десктопные клиенты повторяют функции macOS-клиента.
