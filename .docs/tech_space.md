# Agnosia: техническое описание (Сгенерировано ИИ)

## Назначение

**Agnosia** изолирует Android-приложения в рабочем профиле и помогает держать их в выключенном состоянии, пока пользователь ими не пользуется. Основная идея простая: приложение, которому не стоит доверять полностью, переносится в отдельную Android-среду, запускается только по явному действию пользователя и после завершения сессии снова скрывается политикой рабочего профиля.

Приложение не пытается заменить системную безопасность Android. Оно собирает несколько системных механизмов в один управляемый сценарий:

| Возможность | Что делает Agnosia |
| --- | --- |
| Рабочий профиль | Создаёт управляемую область Android и становится владельцем профиля. |
| Изоляция приложений | Копирует приложения в рабочий профиль, скрывает их и ограничивает запуск. |
| Скрытые ярлыки | Создаёт ярлык в личном профиле, который временно открывает скрытое рабочее приложение. |
| Автозаморозка | Отслеживает момент, когда пользователь покинул приложение, и снова скрывает его. |
| VPN-сценарии | Может временно отключить активный VPN перед запуском рабочего приложения и вернуть его после заморозки. |
| Lockdown | Может блокировать интернет выбранным приложениям рабочего профиля через always-on VPN lockdown. |
| Анализ разрешений | Показывает риск по комбинациям разрешений, специальных доступов и runtime-состояний. |
| Диагностика | Ведёт ограниченный журнал событий и показывает состояние рабочего профиля. |
| File Shuttle | Даёт DocumentsUI SAF-доступ к файлам второго профиля через системные content URI. |

## Архитектура

Проект разделён на три слоя. Общий Avalonia-слой не знает деталей Android API, а Android-проекты предоставляют платформенную реализацию через bridge-интерфейсы.

```text
┌────────────────────────────────────────────────────────────┐
│ Agnosia                                                     │
│ Shared Avalonia UI, ViewModels, Models, Services            │
│                                                            │
│ Views (AXAML) → DashboardWorkspaceViewModel → IPlatformBridge│
└──────────────────────────────┬─────────────────────────────┘
                               │
┌──────────────────────────────▼─────────────────────────────┐
│ Agnosia.Android.Api                                         │
│ Android API wrappers и wire-контракты                       │
│                                                            │
│ AgnosiaActions / AndroidCommandContract                     │
│ ├─ AppServiceModel / AppInventoryPayloadPager               │
│ ├─ AndroidIntentApi / AndroidPolicyApi                      │
│ ├─ AndroidProvisioningApi / AndroidPackageApi               │
│ ├─ AndroidPermissionApi / AndroidVpnApi                     │
│ └─ StorageKeys / AndroidSettingsContract                    │
└──────────────────────────────┬─────────────────────────────┘
                               │
┌──────────────────────────────▼─────────────────────────────┐
│ Agnosia.Android                                             │
│ Android bridge, orchestration, services, receivers          │
│                                                            │
│ AndroidPlatformBridge, Dashboard/Permission/App coordinators│
│ MainActivity, DummyActivity, ProxyActivity                  │
│ OverlayVpnService, LockdownVpnService                       │
│ HiddenAppSessionMonitorService                              │
│ WorkProfileLockFreezeService, DeviceAdmin/Boot receivers    │
└────────────────────────────────────────────────────────────┘
```

| Проект | Target framework | Ответственность |
| --- | --- | --- |
| `Agnosia` | `net10.0` | UI на Avalonia, MVVM, модели, общий журнал, состояние экрана и команд. |
| `Agnosia.Android.Api` | `net10.0;net10.0-android` | Общие контракты команд, переносимые DTO и тонкие Android API wrappers без сервисов и бизнес-логики. |
| `Agnosia.Android` | `net10.0;net10.0-android` | Android-реализация bridge-слоя, orchestration, локальное хранение, сервисы, receiver-ы и профильные Activity. |
| `Agnosia.Unit` | `net10.0` | Unit-тесты view model, командных контрактов, риск-анализа и state machine. |

Ключевой контракт приложения - `IPlatformBridge`. Он объединяет шесть сервисных интерфейсов:

| Интерфейс | Для чего используется |
| --- | --- |
| `IDashboardPlatformService` | Загружает состояние устройства, рабочего профиля и список приложений. |
| `IPermissionPlatformService` | Читает и запрашивает системные разрешения. |
| `IOnboardingPlatformService` | Сохраняет завершение первичной настройки. |
| `IAppCommandService` | Выполняет операции над приложениями: копирование, удаление, запуск, скрытие, ярлыки. |
| `ISettingsPlatformService` | Сохраняет настройки приложения. |
| `IPlatformEventLogReader` | Возвращает системные события и строку диагностики устройства. |

В Android-сборке `ServiceRegistry.PlatformBridge` указывает на singleton `AndroidPlatformBridge.Instance`. Если общий проект запущен вне Android, используется `UnsupportedPlatformBridge`.

Запуск отличается по профилям. В личном профиле `AndroidApp` и `MainActivity` инициализируют `AgnosiaRuntime`, `LocalStorageManager`, `SettingsManager`, Android bridge и стартовую тему. В рабочем профиле тот же APK может стартовать как profile owner, но основной UI подавляется: `MainActivity` применяет политики рабочего профиля, запускает сервис блокировочной заморозки и сразу закрывается. Поэтому пользовательский интерфейс существует только в личном профиле, а рабочая копия приложения используется как управляющий агент.

## Поток данных в UI

Главная view model - `DashboardWorkspaceViewModel`. Она не вызывает Android API напрямую. Вместо этого она работает с `IPlatformBridge`, хранит состояние экрана и преобразует снимки платформы в view model карточек приложений.

```text
Пользователь
   │
   ▼
AXAML View
   │ binding / command
   ▼
DashboardWorkspaceViewModel
   │
   ├─ LoadDashboardAsync()
   ├─ LoadAppInventoryAsync()
   ├─ RequestPermissionAsync()
   └─ Clone / Launch / Freeze / Uninstall / Shortcut
        │
        ▼
IPlatformBridge → AndroidPlatformBridge → Android API / профильные команды
```

Инвентарь приложений грузится отдельно от основного состояния. Это делает стартовый экран быстрее: сначала отображается состояние профиля и настроек, затем по необходимости подгружается список приложений и иконки. Поиск обновляется с debounce, настройки сохраняются с задержкой, а иконки загружаются пачками и с повторными попытками.

## Экранная модель

UI собран вокруг одного рабочего пространства и нескольких overlay-окон. Это важно для навигации: пользователь не переходит между отдельными Activity, почти вся работа остаётся внутри Avalonia.

| Экран / overlay | Что показывает | Главные команды |
| --- | --- | --- |
| `OverviewSectionView` | Состояние профиля, общий статус, количество приложений и изолированных пакетов. | Обновление состояния через dashboard snapshot. |
| `AppsSectionView` | Каталог личного и рабочего профиля, поиск, risk badge и карточки приложений. | Копировать, переместить, открыть, скрыть, удалить, ярлык, runtime-разрешения. |
| `SettingsSectionView` | Разрешения, тема, поведение каталога и логирование. | Сохранение настроек с debounce. |
| `OnboardingOverlayView` | Первичная настройка: приветствие, рабочий профиль, разрешения, финальный шаг. | Создать профиль, запросить доступы, завершить настройку. |
| `PermissionsOverlayView` | Детальный список системных доступов. | Открыть нужный экран Android-настроек. |
| `AppControlOverlayView` | Действия над выбранным приложением и детали риска разрешений. | Операции из `IAppCommandService`. |
| `LogOverlayView` | Журнал событий и строка диагностики устройства. | Просмотр и очистка через настройки логирования. |
| Work profile recovery overlay | Ошибка или потеря рабочего профиля. | Открыть настройки профиля или начать онбординг заново. |

Нижняя навигация переключает только секции `Overview`, `Apps` и `Settings`. Overlay-окна накладываются поверх текущей секции и не создают отдельную Android-навигацию.

## Рабочий профиль

Основной механизм изоляции - **Android Work Profile**. Agnosia запускает `DevicePolicyManager.ActionProvisionManagedProfile`, передаёт административный компонент и ключ аутентификации, а после создания профиля проверяет, что приложение действительно стало profile owner.

```text
Личный профиль
   │ StartProvisioningAsync()
   ▼
Android Managed Profile Provisioning
   │ создаёт рабочий профиль
   ▼
Рабочий профиль
   │ DummyActivity / DeviceAdminReceiver
   ▼
Agnosia проверяет profile owner и применяет политики
```

После успешной настройки рабочий профиль используется для:

| Задача | Android API |
| --- | --- |
| Скрыть или восстановить приложение | `DevicePolicyManager.setApplicationHidden(...)` |
| Включить системное приложение в рабочем профиле | `DevicePolicyManager.enableSystemApp(...)` |
| Управлять межпрофильным взаимодействием | `DevicePolicyManager.setCrossProfilePackages(...)` |
| Отозвать runtime-разрешения у рабочего приложения | `DevicePolicyManager.setPermissionGrantState(...)` |
| Применить ограничения профиля | Device policy и user restrictions через `AgnosiaUtilities` |

Если профиль удалён, недоступен или больше не управляется Agnosia, состояние переводится в один из вариантов `WorkProfileRecoveryKind`. UI показывает пользователю, что нужно удалить профиль, повторить настройку или перезапустить онбординг.

Во время провижининга Agnosia создаёт 32-байтный ключ, сохраняет его в личном профиле и передаёт в рабочий профиль через `DevicePolicyManager.ExtraProvisioningAdminExtrasBundle`. `AgnosiaDeviceAdminReceiver` в рабочем профиле сохраняет этот ключ, вызывает `setProfileEnabled`, регистрирует межпрофильные intent-фильтры и применяет ограничения. Личный профиль записывает факт провижининга через `ManagedProfileProvisionedReceiver` или финальную команду `FinalizeProvision`; если Android передал `UserHandle`, дополнительно сохраняются handle и serial рабочего пользователя.

Проверка профиля не ограничивается одним флагом. `AndroidWorkProfileDiagnosticsReader` смотрит на `UserManager`, `CrossProfileApps`, quiet mode, состояние запуска пользователя, сохранённый serial и доступность cross-profile target. После этого `AndroidProfileCommandGateway` отправляет `ProfilePing` в рабочий профиль и проверяет, что ответ подписан тем же HMAC-ключом и что Agnosia действительно является profile owner. Если версия APK в рабочем профиле отличается от личной, `AndroidDashboardReader` пытается обновить Agnosia в рабочем профиле через тот же package-install поток.

## Модель угроз

Agnosia проектируется как инструмент снижения поверхности доступа для приложений, которые пользователь вынужден держать на устройстве. Защита строится вокруг изоляции профиля, ограничения фоновой жизни приложения и контроля системных доступов.

| Что считается угрозой | Как Agnosia снижает риск |
| --- | --- |
| Приложение постоянно работает в фоне | Рабочая копия скрывается через device policy после использования. |
| Приложение видит личное окружение пользователя | Рабочий профиль отделяет данные, аккаунты и часть системного контекста от личного профиля. |
| Приложение использует чувствительные разрешения | UI показывает риск-комбинации и позволяет отозвать runtime-разрешения у рабочих приложений. |
| Приложение проверяет активный VPN | Перед запуском рабочей копии Agnosia может временно отключить VPN и вернуть его после заморозки. |
| Приложению не нужен интернет | Lockdown оставляет выбранный рабочий пакет внутри технического always-on VPN без forwarding, а остальные рабочие приложения выводит в direct-network allowlist. |
| Приложение взаимодействует между профилями | Cross-profile interaction управляется отдельной политикой. |
| Команда приходит не из доверенного профиля | Межпрофильные команды подписываются HMAC и имеют ограниченный срок действия. |

Вне зоны ответственности остаются серверная сторона приложения, данные, которые пользователь вводит вручную, скриншоты внутри самого приложения, сетевое поведение во время активной сессии и уязвимости Android/прошивки.

## Межпрофильные команды

Android разделяет личный и рабочий профили, поэтому операции в рабочем профиле выполняются только внутри фактического целевого профиля. Тихие query-команды проходят через `AndroidCommandCenter`, но команда считается успешной только если она выполнилась в запрошенном Android-профиле. Локальное прямое выполнение и локальное выполнение через silent service допустимы только для текущего профиля. Тихое выполнение в рабочем профиле маршрутизируется через явный capability transport; если топология устройства или профиля не поддерживает проверенный тихий межпрофильный канал, command center откатывается к подписанному пути через `DummyActivity`.

`DummyActivity` - совместимый fallback для мигрированных query-команд. Он не вызывает обратно `AndroidCommandCenter`; вместо этого он строит `AndroidCommandExecutionContext` из собственного контекста Activity/профиля и вызывает общий обработчик команд через `AndroidCommandHandlerExecutor`. Так бизнес-логика остаётся в одном источнике, а зависимость от `MainActivity` в рабочем профиле не появляется.

Каждый результат команды фиксирует запрошенный профиль, фактический профиль выполнения, транспорт, цепочку fallback и источник контекста. Несовпадение запрошенного и фактического профиля считается ошибкой команды.

```text
ViewModel
   │ IAppCommandService.SetFrozenAsync(app, true)
   ▼
AndroidAppCommandCoordinator
   │ создаёт Intent + подписывает HMAC
   ▼
AndroidActivityCommandGateway
   │ переносит Intent в нужный профиль
   ▼
DummyActivity
   │ проверяет подпись, выполняет action
   ▼
Result Intent → ViewModel обновляет состояние
```

Защита команд строится на `AuthenticationUtility`:

| Механизм | Назначение |
| --- | --- |
| 32-байтный ключ | Создаётся при провижининге и хранится локально в обоих профилях. |
| HMAC-SHA256 | Подписывает action, timestamp и значимые extras. |
| TTL подписи | Команда считается свежей только ограниченное время. |
| Специальный callback | Для события `WorkAppFrozen` используется отдельная подпись по пакету приложения. |
| Recovery | Если ключ потерян, личный профиль может передать новый ключ только установленному profile owner. |

`DummyActivity` не выполняет обычные команды без валидной подписи. Исключение - восстановление аутентификации, где сначала проверяется, что Activity запущена внутри profile owner.

Команды разделены по направлениям:

| Направление | Примеры action | Назначение |
| --- | --- | --- |
| Личный → рабочий | `QueryApps`, `InstallPackage`, `FreezePackage`, `UnfreezeAndLaunch`, `SetCrossProfileInteraction` | Управление приложениями и чтение состояния рабочего профиля. |
| Рабочий → личный | `WorkAppFrozen`, `FinalizeProvision` | Возврат событий о заморозке и завершении настройки. |
| Локальные | `PackageInstallerCallback` | Обработка callback-ов внутри текущего профиля без cross-profile маршрута. |

`AndroidActivityCommandGateway` отвечает за общий lifecycle команд: подписывает intent, переносит его в нужный профиль через cross-profile forwarder, запускает `DummyActivity` через `StartActivityForResult` и переводит `Result.Ok`/`Result.Canceled` в `OperationResult`. Обычные команды ждут ответ до 30 секунд, установка пакетов - до 3 минут. Если `MainActivity` временно не resumed, запросы складываются в небольшую очередь; устаревшие запросы иконок могут быть отброшены, чтобы не перегружать Activity-result канал.

## File Shuttle

File Shuttle - это SAF-мост для передачи файлов между личным и рабочим профилем через системный `DocumentsUI`. Он не выдаёт сторонним файловым менеджерам прямой доступ к чужому `/storage`: Android по-прежнему держит разные storage areas для personal/work профилей, а доступ происходит только через `content://` URI и операции `DocumentsProvider`.

Фича выключена по умолчанию. Для работы нужны:

| Условие | Зачем нужно |
| --- | --- |
| `CrossProfileFileShuttleEnabled` | Пользователь явно включает мост в настройках Agnosia. |
| `MANAGE_EXTERNAL_STORAGE` в обоих профилях | Локальный сервис каждого профиля может читать и отдавать файлы своего external storage. |
| Включённый provider-компонент | `AgnosiaUtilities.ApplyCrossProfileFileShuttleComponentState(...)` включает `AgnosiaCrossProfileDocumentsProvider` только когда тумблер активен. |
| Зарегистрированные cross-profile actions | `START_FILE_SHUTTLE_PARENT_TO_WORK` и `START_FILE_SHUTTLE_WORK_TO_PARENT` позволяют provider-у подключиться к агенту второго профиля. |

Основные компоненты:

| Компонент | Профиль | Роль |
| --- | --- | --- |
| `AgnosiaCrossProfileDocumentsProvider` | Текущий профиль, где открыт DocumentsUI | Публикует root второго профиля, отвечает на SAF-запросы `query`, `open`, `create`, `delete`, `isChild`, thumbnail и переиспользует process-wide bridge client. |
| `AgnosiaFileShuttleClientBroker` / `AgnosiaFileShuttleMessengerClient` | Текущий профиль | Держит общий bridge client, поднимает cross-profile bridge, держит callback `Messenger`, отправляет requests и ждёт responses с timeout. |
| `DummyActivity.ActionStartFileShuttle` | Второй профиль | Проверяет HMAC, направление action, тумблер и all-files permission, затем привязывается к локальному service. |
| `AgnosiaFileShuttleService` | Второй профиль | Bound foreground service, который работает только внутри canonical `Environment.ExternalStorageDirectory` и возвращает метаданные, JSON-списки или `ParcelFileDescriptor`. |

Поток запроса:

```text
DocumentsUI в профиле A
   │ SAF вызывает provider Agnosia
   ▼
AgnosiaCrossProfileDocumentsProvider
   │ создаёт callback Messenger
   │ стартует DummyActivity в профиле B
   ▼
DummyActivity в профиле B
   │ проверяет подпись и настройки
   │ bindService(AgnosiaFileShuttleService)
   ▼
AgnosiaFileShuttleService в профиле B
   │ возвращает service Messenger
   ▼
Provider в профиле A
   │ отправляет loadFiles/openFile/createFile/deleteFile
   ▼
DocumentsUI получает SAF rows или ParcelFileDescriptor
```

IPC построен без `.aidl`: используются `Android.OS.Messenger`, `Message`, `Bundle` и `ParcelFileDescriptor`. Метаданные файлов передаются JSON-строками через source-generated `AgnosiaFileShuttleJsonContext`; файловые потоки и thumbnails передаются descriptor-ами в response bundle. Ошибки возвращаются строкой `ExtraError`, provider логирует их и отдаёт пустой cursor или `FileNotFoundException` там, где этого ожидает SAF.

Запуск cross-profile bridge идёт через `PendingIntent`, а не прямой `Context.StartActivity(...)`. Это важно для Android 14/15/16 background activity launch rules: `AndroidPendingIntentApi.CreateBackgroundActivityStartPendingIntent(...)` задаёт creator-side `SetPendingIntentCreatorBackgroundActivityStartMode(...)`, а `PendingIntent.Send(...)` получает sender-side `SetPendingIntentBackgroundActivityStartMode(...)`. Перед открытием Files из Agnosia bridge preconnect выполняется из видимой `MainActivity`, после чего `DocumentsProvider` переиспользует общий client. Если Files открыт вручную, provider всё ещё пробует best-effort подключение, но Android может вернуть `BAL_BLOCK`, provider дождётся timeout и покажет пустой root.

Границы безопасности:

| Граница | Поведение |
| --- | --- |
| Путь | Service принимает только canonical пути внутри `Environment.ExternalStorageDirectory`; dummy root мапится на этот каталог. |
| Профиль | Action должен соответствовать текущему профилю: parent-to-work выполняется только в work, work-to-parent - только в personal. |
| Доступ | Если File Shuttle выключен или all-files permission не выдан в целевом профиле, bridge возвращает ошибку подключения. |
| Публичный API | Наружу открыт только `DocumentsProvider` с `android.permission.MANAGE_DOCUMENTS`; прямой filesystem bridge не экспортируется. |

## Онбординг и разрешения

Онбординг состоит из создания рабочего профиля и выдачи системных доступов, без которых отдельные функции не могут работать стабильно.

| Разрешение / доступ | Профиль | Зачем нужно |
| --- | --- | --- |
| Рабочий профиль | Личный и рабочий | Основа изоляции и управления приложениями. |
| Уведомления | Личный | Нужны foreground-сервисам и фоновой активности. |
| Установка APK | Рабочий | Нужна для копирования пользовательских приложений в рабочий профиль. |
| Usage Stats | Рабочий | Нужен монитору скрытых приложений, чтобы понять, когда приложение покинуто. |

`AndroidPermissionCoordinator` читает состояние этих доступов. Для доступов рабочего профиля он отправляет запросы в `DummyActivity`, потому что соответствующие настройки должны открываться в рабочем профиле.

Флаг завершения онбординга хранится в локальном хранилище. View model периодически перепроверяет состояние профиля и разрешений, чтобы перевести пользователя на следующий шаг без ручного обновления экрана.

Запросы разрешений используют разные Android-механизмы. Уведомления запрашиваются через runtime permission `POST_NOTIFICATIONS`, Usage Stats и установка APK открываются из рабочего профиля. VPN-доступ и overlay-доступ запрашиваются не в онбординге, а как требования модуля `VPN Guard`: VPN через `VpnService.prepare()`, overlay через страницу настроек приложения, где пользователь вручную включает `SYSTEM_ALERT_WINDOW`. После возврата в приложение view model перечитывает разрешения на `OnResume`, если этот тип доступа меняется вне Agnosia.

## Локальное хранение

Постоянное состояние хранится в `SharedPreferences` с именем `agnosia.preferences` и режимом `FileCreationMode.Private`. Хранилище инициализируется через `LocalStorageManager` в каждом профиле отдельно.

| Группа данных | Ключи / примеры | Для чего нужны |
| --- | --- | --- |
| Состояние настройки | `has_setup`, `is_setting_up`, `onboarding_completed` | Понимание, завершён ли онбординг и идёт ли создание профиля. |
| Привязка рабочего профиля | `managed_profile_user_handle`, `managed_profile_user_serial`, timestamps | Диагностика и восстановление после удаления или сбоя профиля. |
| Аутентификация команд | `auth_key` | HMAC-ключ для межпрофильных команд. |
| Настройки UI и каталога | `show_all_apps`, `app_theme`, `logging_enabled` | Поведение интерфейса, списка приложений и журнала. |
| VPN-настройки | `disable_vpn_before_work_launch`, `enable_vpn_after_work_freeze`, `vpn_after_work_freeze_client`, `tunguska_automation_token`, `have_active_vpn_session` | Автоматизация отключения и повторного запуска VPN. |
| Lockdown | `lockdown_enabled`, `lockdown_blocked_packages` | Always-on lockdown VPN и список рабочих пакетов без интернета. |
| Скрытые приложения | `hidden_shortcut_metadata:*`, `hidden_app_active_session` | Метаданные ярлыков и восстановление активной сессии после перезапуска сервиса. |
| Журнал | `log_entries` | Последние 100 событий платформы. |

Часть настроек синхронизируется в рабочий профиль через `AgnosiaActions.SynchronizePreference`, чтобы сервисы рабочего профиля видели те же флаги, что и UI в личном профиле. Сейчас синхронизируются `logging_enabled` и `disable_vpn_before_work_launch`: рабочий профиль должен знать, писать ли локальные события и нужно ли учитывать VPN-сценарий при запуске через ярлык. При выключении логирования журнал очищается в обоих профилях, если рабочий профиль доступен.

## Управление приложениями

Операции над приложениями проходят через `AndroidAppCommandCoordinator`. Он выбирает нужный путь в зависимости от профиля, системности приложения и доступности APK.

| Операция | Как выполняется |
| --- | --- |
| Копировать в рабочий профиль | Для пользовательских приложений передаётся APK и split APK в `PackageInstaller`; для системных вызывается `enableSystemApp`. |
| Копировать в личный профиль | Запускается package operation из рабочего профиля обратно в личный. |
| Переместить в рабочий профиль | Сначала копирование в рабочий профиль, затем удаление из личного. |
| Скрыть / восстановить | В рабочем профиле вызывается `setApplicationHidden`. |
| Удалить | Пользовательские приложения удаляются через `PackageInstaller`; системные в рабочем профиле скрываются. |
| Отозвать runtime-разрешения | Только для пользовательских приложений рабочего профиля. |
| Межпрофильный доступ | Изменяется список пакетов, которым разрешён cross-profile interaction. |

После копирования приложения из личного профиля Agnosia пытается сразу подготовить ярлык и скрыть рабочую копию. Для скрытых рабочих приложений это важный сценарий: пользователь запускает их через ярлык, а не из списка приложений рабочего профиля.

Пользовательские приложения копируются не через файловый менеджер, а через `PackageInstaller.Session`: Agnosia берёт `SourceDir` и `SplitSourceDirs`, пишет все части APK в install session и ждёт callback. Если Android возвращает `PendingUserAction`, `DummyActivity` открывает системный экран подтверждения и продолжает операцию после результата. Перед удалением скрытого пакета Agnosia временно снимает hidden state, иначе системный uninstall может не увидеть пакет. Команда `MoveToWork` в UI является составной операцией: сначала clone в рабочий профиль, затем uninstall из личного; если удаление не удалось, рабочая копия остаётся созданной, а статус сообщает о частичном успехе.

## Каталог приложений

Каталог строится из двух независимых запросов: личный профиль читается локально, рабочий профиль опрашивается через `DummyActivity`. Оба результата приводятся к `AppSnapshot`, чтобы UI не зависел от Android-типов.

```text
AndroidDashboardReader.LoadAppInventoryAsync()
   ├─ QueryAppsAsync(Personal)
   │    └─ PackageManager.GetInstalledApplications(...)
   └─ QueryAppsAsync(Work)
        └─ signed Intent → DummyActivity → PackageManager внутри work profile
```

При чтении приложения Agnosia собирает:

| Поле | Источник |
| --- | --- |
| Название, package name, launchability | `PackageManager` и launch intent. |
| Системность и installed/hidden state | `ApplicationInfo`, `DevicePolicyManager.isApplicationHidden`. |
| APK и split APK | `ApplicationInfo.SourceDir`, `SplitSourceDirs`. |
| Runtime grant state | `PackageInfo.RequestedPermissionsFlags`. |
| Foreground service types | `PackageInfo.Services`. |
| Special access | Secure settings и AppOps. |
| Иконка | Кэш `AndroidAppIconResolver`, затем очередь прогрева иконок. |
| Risk analysis | `AppPermissionRiskCatalog.Analyze(...)`. |

По умолчанию системные и служебные пакеты скрываются из каталога. Переключатель `Показывать все пакеты` включает их отображение, но иконки системных приложений всё равно не подгружаются, чтобы не тратить ресурсы на низкополезные элементы.

Иконки имеют два уровня оптимизации. Android-слой хранит PNG в памяти и в `CacheDir/app-icons`, ключ строится из хэша package name и version code. UI-слой дополнительно собирает видимые запросы в батчи с задержкой 60 ms и пропускает их через один gate, чтобы не запускать много параллельных profile activity-команд. Если иконка ещё не прогрета, карточка сначала отображается без неё, а повторная загрузка приходит позже из кэша.

## Скрытые ярлыки

Ярлык скрытого приложения создаётся в два шага, потому что Android должен увидеть метаданные приложения, которое обычно скрыто.

```text
1. Подготовка в рабочем профиле
   ├─ временно показать пакет
   ├─ дождаться доступности пакета
   ├─ прочитать label, target activity и icon
   └─ снова скрыть пакет

2. Создание в личном профиле
   ├─ передать метаданные
   └─ вызвать ShortcutManager.requestPinShortcut()
```

Нажатие на ярлык запускает не само приложение напрямую, а внутренний маршрут:

```text
Pinned shortcut
   ▼
личный профиль
   ▼
DummyActivity / UnfreezeAndLaunch
   ▼
рабочий профиль
   ▼
ProxyActivity
   ├─ показывает скрытое приложение
   ├─ запускает target activity
   └─ стартует HiddenAppSessionMonitorService
```

Так Agnosia может открыть приложение, которое было скрыто через device policy, и затем вернуть его в скрытое состояние.

Метаданные ярлыка хранятся в `SharedPreferences` под ключом `hidden_shortcut_metadata:{package}`. В них лежат shortcut id, package name, target activity, label, base64-иконка и случайный token. При запуске pinned shortcut `ProxyActivity` проверяет token, поэтому произвольный intent с тем же action не должен открыть скрытое приложение. Если ярлык уже закреплён, Agnosia обновляет его через `ShortcutManager.updateShortcuts()` и сразу скрывает пакет; если создаётся новый ярлык, launcher запрашивает подтверждение пользователя, а `ShortcutPinReceiver` завершает скрытие после callback-а.

## Автоматическая заморозка

`HiddenAppSessionMonitorService` отслеживает сессию скрытого приложения после запуска через ярлык. Логика принятия решения вынесена в `HiddenAppSessionMonitorStateMachine`, что позволяет тестировать её отдельно от Android-сервиса.

```text
WaitingForTargetForeground
        │
        │ target foreground или системный делегированный экран
        ▼
TargetForegroundOrDelegated
        │
        │ приложение ушло в фон и это подтверждено
        ▼
InactiveCandidate
        │
        │ прошло 10 секунд без возврата target task
        ▼
Completed → setApplicationHidden(..., true)
```

Сервис не прячет приложение по одному слабому признаку. Он сверяет несколько источников:

| Источник | Что даёт |
| --- | --- |
| `UsageStatsManager.queryEvents()` | События foreground/background, resumed/paused. |
| `ActivityManager.AppTasks` | Проверку, что task приложения всё ещё существует и относится к целевому пакету. |
| `PowerManager.IsInteractive` | Немедленную заморозку при неактивном экране. |
| Список системных экранов | Settings, PermissionController, PackageInstaller, DocumentsUI и похожие делегированные flows не завершают сессию. |

Тайминги монитора:

| Параметр | Значение | Назначение |
| --- | ---: | --- |
| Fast poll | 500 ms | Стартовое окно и кандидат на неактивность. |
| Steady poll | 1500 ms | Приложение видно или идёт системный делегированный flow. |
| Idle poll | 3 s | Целевое приложение ещё не было замечено. |
| Initial launch grace | 45 s | Ожидание первого foreground-сигнала без преждевременного скрытия. |
| Post-launch transient UI grace | 45 s | Запас для системных экранов после запуска. |
| User background hide delay | 10 s | Задержка перед скрытием после подтверждённого ухода пользователя. |
| Initial fast polling window | 10 s | Более частое наблюдение сразу после старта. |

При блокировке экрана `WorkProfileLockFreezeService` и `LockFreezeCleanupJobService` подчищают незавершённые сессии. Состояние активной сессии сохраняется, чтобы приложение можно было скрыть даже если сервис был перезапущен.

После успешной заморозки рабочий профиль должен сообщить об этом личному профилю. Основной путь - подписанный `PendingIntent`, созданный перед запуском приложения и переданный в рабочий профиль. Если callback недоступен, `HiddenAppSessionMonitorService` использует резервную cross-profile activity-команду `WorkAppFrozen`. Это событие используется не для самой заморозки, а для пост-обработки в личном профиле: убрать overlay и при необходимости отправить команду запуска VPN-клиенту.

## VPN-сценарии

VPN-логика решает две отдельные задачи:

1. Перед запуском рабочего приложения временно убрать активный VPN из личного профиля.
2. После заморозки рабочего приложения вернуть выбранный VPN-клиент, если он был активен до запуска.

### Временное отключение VPN

`TransientVpnDisconnectService` - короткоживущий `VpnService`. Если Android уже выдал Agnosia право управлять VPN, сервис создаёт минимальный VPN-интерфейс и сразу закрывает его. Android переключает активный VPN на Agnosia, прежний VPN-клиент теряет активное соединение, после закрытия интерфейса VPN остаётся выключенным.

```text
Проверить активный VPN
   │
   ├─ VPN нет → запускать рабочее приложение
   │
   └─ VPN есть
        ▼
      VpnService.prepare()
        ▼
      transient interface 10.73.0.1/32, MTU 1280
        ▼ 350 ms
      закрыть interface
        ▼ 120 ms
      запускать рабочее приложение
```

Флаг `have_active_vpn_session` сохраняет, был ли VPN активен до отключения. Он нужен для последующего восстановления.

Отключение VPN встроено в два пути запуска: команду `LaunchAsync` из UI и запуск через pinned shortcut. В обоих случаях сначала проверяется настройка `disable_vpn_before_work_launch`, затем активный VPN через `ConnectivityManager`. Если Android требует подтверждение `VpnService.prepare()`, пользователь видит системный экран. Если после transient VPN активный VPN всё ещё обнаруживается, запуск рабочего приложения прерывается, потому что сторонний клиент мог сразу подключиться обратно.

### Восстановление VPN

Когда рабочее приложение снова скрыто, рабочий профиль отправляет событие `WorkAppFrozen` в личный профиль. Обработчик `WorkAppFrozenHandler` вызывает `AndroidVpnAutomationApi.EnableConfiguredVpnAfterWorkFreezeAsync()` и скрывает overlay-индикатор.

Поддерживаемые клиенты автоматизации:

| Клиент | Механизм запуска |
| --- | --- |
| FlClash | Activity command. |
| Clash Meta for Android | Explicit Activity command. |
| Happ | Toggle broadcast. |
| Tunguska | Activity command с automation token. |
| INCY | Start broadcast. |
| Exclave | Quick toggle Activity. |
| husi | Quick toggle Activity. |
| NekoBox+ | Quick toggle Activity, поддерживаются `com.nb4a.plus` и `moe.nb4a`. |

Если VPN уже активен, Agnosia не отправляет повторную команду. Для toggle-only клиентов UI показывает предупреждение, потому что такой вызов может не только включить, но и выключить VPN.

## Overlay-индикатор

`OverlayVpnService` умеет рисовать небольшой полупрозрачный квадрат 24 dp в правом верхнем углу экрана. Он задуман как технический индикатор VPN-сценария и работает только при выданном доступе `SYSTEM_ALERT_WINDOW`.

Сервис не принимает ввод, не забирает фокус и показывается после успешного временного отключения VPN перед запуском рабочего приложения. Обработчик `WorkAppFrozen` всегда пытается скрыть overlay через bind к сервису после попытки вернуть VPN-клиент. Overlay остаётся вспомогательной возможностью: отсутствие видимого квадрата не блокирует отключение VPN, запуск рабочего приложения, заморозку или восстановление VPN.

## Lockdown

Lockdown блокирует интернет выбранным приложениям рабочего профиля. Включение модуля выполняется через cross-profile команду в рабочий профиль, где Agnosia является profile owner. Рабочий агент вызывает `DevicePolicyManager.setAlwaysOnVpnPackage(admin, packageName, lockdownEnabled: true, lockdownAllowlist)` и держит `LockdownVpnService` как always-on VPN.

Список заблокированных пакетов хранится в рабочем профиле в `lockdown_blocked_packages`. При изменении списка `DummyActivity` обновляет storage, сбрасывает кэш инвентаря и просит `LockdownVpnController` пересобрать DPM-политику. Контроллер строит `directNetworkAllowlist`: все видимые установленные пользовательские приложения рабочего профиля, кроме заблокированных. Этот список передаётся в `DevicePolicyManager` как lockdown allowlist.

`LockdownVpnService` создаёт минимальный VPN-интерфейс с default routes и добавляет `directNetworkAllowlist` в `VpnService.Builder.addDisallowedApplication(...)`. Это намеренно инвертированная схема: незаблокированные приложения исключаются из технического VPN и идут напрямую через системную сеть, а заблокированные остаются внутри VPN без packet-forwarding слоя и не получают интернет. `addDisallowedApplication()` само по себе не блокирует сеть; блокировку даёт сочетание always-on VPN, lockdown и отсутствия проксирования для приложений, оставшихся внутри VPN.

Скрытые приложения не добавляются в DPM lockdown allowlist: Android может считать такой пакет несуществующим и отклонить `setAlwaysOnVpnPackage` с `NameNotFoundException`. Поэтому `ProxyActivity` после `setApplicationHidden(false)` ждёт, пока пакет снова станет видимым для `PackageManager`, затем обновляет Lockdown-политику перед `StartActivity`. Системные приложения не показываются как управляемые Lockdown-цели.

`LockdownVpnService` сейчас является минимальным always-on VPN endpoint для DPC-политики и списка `disallowedApplication`. Он не содержит полноценный userspace TCP/IP forwarder или tun2socks-движок. Если понадобится режим, где разрешённый трафик должен идти через внешний VPN/прокси вместо прямой системной сети, к этому сервису нужно добавить отдельный packet-forwarding слой и пересмотреть allowlist-схему.

## Android-компоненты

Android-проект остаётся тонким entry point, но содержит компоненты, без которых profile owner-сценарий не работает.

| Компонент | Роль |
| --- | --- |
| `MainActivity` | Запускает Avalonia UI в личном профиле. В рабочем профиле применяет политики и закрывается без основного UI. |
| `DummyActivity` | Принимает подписанные межпрофильные команды и выполняет операции внутри целевого профиля. |
| `ProxyActivity` | Запускает временно показанное рабочее приложение и стартует монитор скрытой сессии. |
| `AgnosiaDeviceAdminReceiver` | Административный receiver для profile owner-политик. |
| `ManagedProfileProvisionedReceiver` | Обрабатывает завершение создания managed profile. |
| `WorkAppFrozenReceiver` | Передаёт событие заморозки рабочего приложения в личный профиль. |
| `PackageInstallerCallbackReceiver` | Получает результат установки или удаления APK. |
| `ShortcutPinReceiver` | Получает callback создания pinned shortcut. |
| `LockFreezeStartupReceiver` | Запускает cleanup после загрузки устройства. |
| `HiddenAppSessionMonitorService` | Foreground-сервис мониторинга скрытого приложения. |
| `WorkProfileLockFreezeService` | Следит за блокировкой экрана и завершает активную скрытую сессию. |
| `LockFreezeCleanupJobService` | Safety net для подчищения зависших сессий. |
| `OverlayVpnService` | Реализует показ overlay-индикатора после временного отключения VPN и скрытие после `WorkAppFrozen`. |
| `TransientVpnDisconnectService` | Коротко занимает VPN-слот, чтобы отключить активный сторонний VPN. |
| `LockdownVpnService` | Always-on VPN endpoint для Lockdown; исключает незаблокированные рабочие пакеты из технического VPN. |

## Manifest и системные требования

Манифест фиксирует, что приложение рассчитано на устройства с device admin и managed users. Без этих возможностей Agnosia не может создать рабочий профиль и управлять пакетами внутри него.

| Manifest entry | Зачем нужен |
| --- | --- |
| `android.software.device_admin` | Требуется для profile owner и device policy API. |
| `android.software.managed_users` | Требуется для рабочих профилей. |
| `com.agnosia.app.permission.CROSS_PROFILE_COMMAND` | Signature-permission для профильной команды через `DummyActivity`. |
| `ACCESS_NETWORK_STATE` | Проверка активного VPN через `ConnectivityManager`. |
| `FOREGROUND_SERVICE`, `FOREGROUND_SERVICE_SPECIAL_USE`, `FOREGROUND_SERVICE_SYSTEM_EXEMPTED` | Foreground-сервисы монитора и transient VPN. |
| `PACKAGE_USAGE_STATS` | Наблюдение за активностью скрытого приложения. |
| `RECEIVE_BOOT_COMPLETED` | Startup cleanup после перезагрузки. |
| `REQUEST_INSTALL_PACKAGES`, `REQUEST_DELETE_PACKAGES` | Установка и удаление APK в профильных сценариях. |
| `POST_NOTIFICATIONS` | Уведомления foreground-сервисов на Android 13+. |
| `SYSTEM_ALERT_WINDOW` | Overlay-индикатор. |
| `queries` для VPN-клиентов | Позволяет Android видеть поддерживаемые пакеты автоматизации VPN. |

Android-проект собирается как APK с `ApplicationId` `com.agnosia.app`. Debug-сборка отключает стандартный Android debugger server, потому что приложение работает сразу в двух профилях, а транспорт отладки использует фиксированный localhost-порт.

Часть компонентов объявлена не в XML, а через Android attributes в C#-классах. Поэтому итоговый manifest формируется из `Properties/AndroidManifest.xml` и атрибутов `[Activity]`, `[Service]`, `[BroadcastReceiver]`, `[IntentFilter]`. Launcher вынесен в `activity-alias` `com.agnosia.app.LauncherActivity`, а в рабочем профиле этот alias отключается, чтобы пользователь не запускал второй экземпляр UI из work launcher.

## Анализ рисков разрешений

`AppPermissionRiskCatalog` оценивает не отдельные разрешения, а комбинации признаков. Сейчас в каталоге **27 critical-правил** и **43 dangerous-правила**.

Входные данные `AppPermissionRiskInput`:

| Категория | Примеры |
| --- | --- |
| Manifest permissions | `CAMERA`, `RECORD_AUDIO`, `ACCESS_FINE_LOCATION`, `INTERNET`, `QUERY_ALL_PACKAGES`. |
| Runtime grant state | Какие чувствительные runtime-разрешения выданы или отклонены. |
| Foreground service types | `camera`, `microphone`, `location`, `mediaProjection`. |
| Special access | Accessibility, Notification Listener, Overlay, Usage Stats, VPN. |
| AppOps | Фактический доступ к камере, микрофону и локации. |
| SDK | Версия устройства и target SDK приложения. |
| Observed signals | Активный MediaProjection, VPN control, assistant screen content. |

Оценка строится из группированных правил: если несколько правил относятся к одной группе, в итог идёт сильнейшее из них. Это снижает эффект двойного счёта для похожих признаков.

| Компонент score | Что отражает |
| --- | --- |
| Data Sensitivity | Чувствительность данных: геолокация, микрофон, камера, SMS, файлы. |
| Persistence | Способность жить в фоне: boot receiver, foreground service, exact alarm. |
| Exfiltration | Каналы передачи данных: интернет, Bluetooth, NFC, Wi-Fi, SMS, запись файлов. |
| Control Surface | Управляющие поверхности: Accessibility, Overlay, Usage Stats, VPN. |
| Stealth | Запрос обхода энергосбережения. |
| Legitimacy Penalty | Снижение риска, если runtime-разрешение отклонено или AppOps блокирует доступ. |
| Confidence | Уверенность выше, если есть runtime/special-access/AppOps сигналы. |

Итоговые уровни:

| Уровень | Условие |
| --- | --- |
| `Safe` | Нет совпавших правил или grouped score ниже dangerous-порога. |
| `Dangerous` | Есть dangerous-сигнал или score не ниже 4. |
| `Critical` | Совпало critical-правило с эффективными разрешениями либо score не ниже 8 при высокой уверенности. |

Примеры critical-комбинаций:

| Комбинация | Почему опасно |
| --- | --- |
| Геолокация в фоне + интернет | Постоянная передача местоположения. |
| Микрофон + автозапуск/обход батареи + интернет | Долгая фоновая запись и канал вывода данных. |
| Камера + foreground service камеры + интернет | Съёмка с возможностью передачи. |
| Accessibility Service + интернет | Доступ к UI и потенциальное управление действиями пользователя. |
| Notification Listener + Overlay + интернет | Риск социальной инженерии и чтения уведомлений. |
| Полный доступ к файлам + persistence + интернет | Доступ к большому объёму данных и канал вывода. |

В UI риск отображается на карточке приложения. Для пользовательских приложений можно раскрыть детали: manifest-разрешения, runtime-разрешения, risky permissions, matched rule IDs и разложение score.

## Диагностика и журнал

`BoundedAppEventLogService` хранит последние 100 строк. Он импортирует события платформы из `AndroidAppLogArchive`, удаляет дубликаты по ID и форматирует записи с профилем, уровнем и тегом.

```text
12:40:18  INF  [PER/AgnosiaPlatformBridge] ...
12:40:20  WRN  [WRK/AgnosiaHiddenSession] ...
```

Журнал используется для пользовательской диагностики: понять, почему Android отклонил действие, какой профиль недоступен, почему приложение не заморозилось или почему VPN-клиент не принял команду.

Платформенный журнал пишет события в `AndroidAppLogArchive` с задержанным flush примерно 1 секунду и ограничением 100 записей на профиль. При открытии log overlay личный профиль загружает свои записи, затем, если рабочий профиль доступен и отвечает как profile owner, подтягивает рабочие записи через `QueryLogs`. `BoundedAppEventLogService` в UI импортирует их по ID, сортирует по времени и не показывает дубликаты при повторном обновлении.

## Отказоустойчивость

Большая часть Android-операций может быть отклонена системой, профилем или сторонним приложением. Поэтому Agnosia старается не считать действие успешным только по факту запуска Intent.

| Ситуация | Поведение |
| --- | --- |
| Рабочий профиль создан, но не отвечает | Приложение помечает профиль как требующий удаления или повторной настройки. |
| Потерян HMAC-ключ | Запускается recovery-команда, которая принимает новый ключ только внутри profile owner. |
| Тихий транспорт рабочего профиля недоступен | `AndroidCommandCenter` записывает `silent_work_transport_unavailable` и откатывается к подписанному `DummyActivity`; для рабочих команд он не должен возвращать данные личного профиля. |
| APK после установки ещё не виден | `DummyActivity` ждёт доступности пакета с retry перед подготовкой ярлыка. |
| Скрытие после установки не прошло сразу | Повторные попытки скрытия выполняются ограниченное время. |
| Монитор не видит foreground-событие | Приложение не скрывается по неподтверждённому таймауту, а оставляется видимым до более надёжного сигнала. |
| Активная task всё ещё относится к целевому приложению | Заморозка откладывается, даже если UsageStats показал неактивность. |
| Сервис был перезапущен | Активная скрытая сессия читается из локального состояния. |
| Логирование повреждено или отключено | Повреждённый JSON журнала очищается, при отключении логирования записи удаляются. |
| Версия Agnosia в рабочем профиле устарела | Личный профиль пытается переустановить актуальный APK в рабочий профиль и повторить owner-check. |
| Activity-команда стартует, пока `MainActivity` не resumed | Запрос ставится в очередь, а устаревшие фоновые icon-запросы могут быть отменены. |
| Transient VPN не смог отключить активный VPN | Запуск рабочего приложения отменяется, чтобы не обещать скрытие VPN-состояния. |
| File Shuttle показывает пустой root | Проверяются `AgnosiaFileShuttleClient`, `AgnosiaDocumentsProvider`, `START_FILE_SHUTTLE_*`, `BAL_BLOCK`, timeout, `MANAGE_EXTERNAL_STORAGE` и состояние тумблера в обоих профилях. |

## Коротко о главном потоке

```text
1. Пользователь создаёт рабочий профиль.
2. Agnosia становится profile owner и применяет политики.
3. Пользователь копирует приложение в рабочий профиль.
4. Agnosia создаёт ярлык и скрывает рабочую копию.
5. Пользователь запускает приложение через ярлык.
6. Agnosia временно показывает пакет, при необходимости отключает VPN и запускает приложение.
7. Монитор ждёт, пока пользователь покинет приложение.
8. Пакет снова скрывается, overlay убирается, VPN при необходимости запускается обратно.
```
