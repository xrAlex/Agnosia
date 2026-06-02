<p align="center">
  <img src=".assets/icon.png" alt="Agnosia" width="160" height="160">
</p>

<h1 align="center">Agnosia</h1>

**Agnosia** - приложение для Android, которое помогает держать сомнительные приложения отдельно от основного профиля.

Иногда приложением приходится пользоваться, даже если доверия к нему нет. Его нельзя удалить из-за работы, общения, банка, доставки, местных сервисов или просто потому, что без него неудобно. (Привет MAX, VK, Яндекс браузер)

Agnosia переносит такие приложения в рабочий профиль. Это отдельное пространство Android, где приложение меньше видит о вашем основном профиле, других приложениях и привычном окружении телефона.

Приложение остаётся доступным, но не живёт рядом с личными данными постоянно. Вы запускаете его, когда нужно, а после использования Agnosia снова ограничивает его работу.

## Что делает Agnosia

- Изоляция приложений в рабочем профиле
- Автоматическая заморозка приложений после использования
- Анализ рисков разрешений приложений
- Скрытие факта использования VPN от приложений в рабочем профиле
- Временное отключение активного VPN перед запуском изолированных приложений
- Автоматический перезапуск VPN после заморозки приложения
- Отзыв разрешений у приложений в рабочем профиле
- Контроль кросс-профильного взаимодействия
- Автозаморозка при блокировке экрана
- Позволяет передавать файлы между профилями
- Экономия энергии за счёт ограничения фоновой работы

## Документация

- [Руководство пользователя](.docs/user_space.md)
- [Техническое описание](.docs/tech_space.md)

## Требования

- Android 12+ (API 31)
- Поддержка создания рабочих профилей на устройстве

## Скачать

- [APK для arm64-v8a](https://github.com/xrAlex/Agnosia/releases/latest/download/Agnosia-arm64-v8a.apk)
- [APK для armeabi-v7a](https://github.com/xrAlex/Agnosia/releases/latest/download/Agnosia-armeabi-v7a.apk)
- [Универсальный APK](https://github.com/xrAlex/Agnosia/releases/latest/download/Agnosia-universal.apk)

<details>
<summary>Скриншоты приложения</summary>

<p align="center">
  <img src=".assets/screen_1.png" alt="Экран Agnosia 1" width="240">
  <img src=".assets/screen_2.png" alt="Экран Agnosia 2" width="240">
  <img src=".assets/screen_3.png" alt="Экран Agnosia 3" width="240">
</p>

<p align="center">
  <img src=".assets/screen_4.png" alt="Экран Agnosia 4" width="240">
  <img src=".assets/screen_5.png" alt="Экран Agnosia 5" width="240">
</p>

</details>

## Важно

Agnosia не является абсолютной защитой и не заменяет осторожность. Она не защитит от данных, которые вы сами передаёте приложению или сервису.

Цель проекта - уменьшить доступ приложений к данным и упростить изоляцию нежелательного ПО.

Лучший способ защититься от приложения, которому вы не доверяете, - **удалить его**. Agnosia нужна для тех случаев, когда удалить приложение нельзя или неудобно, но оставлять его в основном профиле тоже не хочется.

## Полезные ссылки

- [Habr: Цифровая тень: аудит популярных Android-приложений](https://habr.com/ru/articles/1029004/)
- [Habr: MAX и реверс-инжиниринг приложения](https://habr.com/ru/articles/1006666/)
- [Habr: Зачем Яндекс.Браузеру эти данные?](https://habr.com/ru/articles/878236/)
- [RKS Global: российские приложения ищут VPN](https://files.rks.global/russian_apps_search_for_vpn_ru.pdf)
- [Privacy International: Meta and Yandex break security to save their business model](https://privacyinternational.org/long-read/5621/meta-and-yandex-break-security-save-their-business-model)
