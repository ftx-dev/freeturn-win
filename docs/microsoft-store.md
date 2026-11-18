# Microsoft Store / MSIX

Подготовка канала начата для 0.2.4. Приложение ещё **не опубликовано** в Microsoft Store. MSIX из CI — неподписанный пакет для загрузки в Partner Center, а не доверенный установщик для пользователей.

## Что требуется от владельца

1. Зарегистрировать учётную запись разработчика в [Partner Center](https://partner.microsoft.com/dashboard), пройти проверку личности и зарезервировать FreeTurnX.
2. В разделе Product identity получить Package/Identity/Name, Package/Identity/Publisher, PublisherDisplayName и Store Product ID. Это публичные идентификаторы, не секреты.
3. Запросить разрешение Microsoft на `runFullTrust`, `packagedServices` и `localSystemServices`: клиент использует службу LocalSystem для управления TUN. Для последних двух возможностей Microsoft ограничивает круг допускаемых разработчиков; одобрение индивидуального аккаунта и приложения не гарантировано. См. [ограниченные возможности](https://learn.microsoft.com/windows/apps/package-and-deploy/app-capability-declarations).
4. Подготовить карточку, политику конфиденциальности, возрастной рейтинг, доступные рынки и инструкции для сертификации. Первую отправку выполнить в Partner Center после тестирования установленного пакета.

Ссылка вида `https://apps.microsoft.com/detail/PRODUCT_ID` добавляется в README только после появления доступной карточки. Не используйте выдуманный ID.

## Сборка

На Windows с .NET 10, Go, NSIS и Windows SDK:

```powershell
./scripts/Build-Windows.ps1
./scripts/Package-MSIX.ps1 -IdentityName '<Partner Center Name>' -Publisher '<Partner Center Publisher>' -PublisherDisplayName '<PublisherDisplayName>'
```

Результат: `dist/msix/FreeTurnX-<version>-x64.msix` и ARM64. MakeAppx проверяет манифест без отключения валидации. Microsoft подписывает принятый пакет; локальная сборка не получает доверенную подпись автоматически.

Workflow `Microsoft Store packages` запускается вручную и после публикации GitHub release. Для включения задать repository variables `STORE_IDENTITY_NAME`, `STORE_PUBLISHER`, `STORE_PUBLISHER_DISPLAY_NAME`. До получения identity задание пропускается. Артефакт `microsoft-store-upload` предназначен для Partner Center.

## Обновления и совместимость

- Упакованный клиент определяется по package identity Windows. Проверка обновлений открывает Microsoft Store; EXE-обновления GitHub не используются.
- Обычный EXE продолжает обновляться через GitHub Releases.
- MSIX управляет установкой и удалением службы; ручные кнопки службы недоступны.
- Минимум для пакета со службой — Windows 10 1903. EXE сохраняет поддержку 1809.
- Перед тестовой установкой MSIX удалить EXE-вариант вместе со службой: оба используют имя `FreeTurnDaemon`. Совместная установка пока не поддерживается.
- Проверить WebView2 Runtime, CAPTCHA, запуск службы, loopback WG, обновление пакета и удаление службы на отдельной машине. Проверка схемы MakeAppx не заменяет эти проверки.

## Автоматическая отправка

Отправка в Store пока не включена: нет реального приложения, одобрения restricted capabilities и первоначальной заявки. После их получения подключить [Store submission API](https://learn.microsoft.com/windows/uwp/monetize/create-and-manage-submissions-using-windows-store-services): связанный Entra tenant, приложение с разрешением Developer и секреты `STORE_TENANT_ID`, `STORE_CLIENT_ID`, `STORE_CLIENT_SECRET` в GitHub environment `microsoft-store`. Пароли и client secret не отправлять в чат или репозиторий. Автопубликация требует отдельной проверки загрузки, commit заявки и статуса сертификации, а не только создания ZIP/MSIX.
