# CrestPicker — вводная для нового агента

Состояние на 09.11.2026. Источник фактов — код репо, `PLAN.md`, `REQUIREMENTS.md`,
`TESTING.md`, декомпил игры и прогоны в игре. Непроверенное помечено явно.

## 1. Что это

Мод выбора гребня (crest) для Hollow Knight: Silksong. Когда прислужница капеллы
(Chapel Maid) в Bonetown заканчивает диалог, вместо ванильной выдачи открывается
меню выбора в стиле магазина: строки, цены, панель деталей, экран подтверждения.

- GUID: `io.github.triojohn.crestpicker`, версия `1.0.0`
  (`Directory.Build.props` и `Plugin.cs` — синхронно).
- BepInEx 5 + HarmonyX, `netstandard2.1`, nullable включён.
- Процесс: `Hollow Knight Silksong.exe`.
- Оффер однократен на слот сейва; отмена флаг не тратит.

## 2. Сборка и запуск

1. Скопировать `SilksongPath.props.example` в `SilksongPath.props` (gitignored),
   выставить `SilksongFolder`; `SilksongPluginsFolder` по умолчанию
   `$(SilksongFolder)/BepInEx/plugins`.
2. `dotnet build -c Release` — таргет `CopyToPlugins` копирует
   `CrestPicker.dll` в `BepInEx/plugins`.
3. Запустить игру, в `BepInEx/LogOutput.log` искать `CrestPicker 1.0.0 loaded.`
4. Конфиг: `BepInEx/config/io.github.triojohn.crestpicker.cfg`
   (секции `General`, `Probe`). Флаг однократности:
   `BepInEx/config/CrestPicker/state.json`.

Ключи конфига (дефолт): `GrantAllCrests` false (устаревший, true = `CrestPool=All`),
`CrestPool` All, `EnableSnailChoice` true, `TestHotkey` F9, `ShopDumpHotkey` F8,
`UseLiveShop` true, `ShowCrestSchema` true, `SchemaRender` Clone,
`CrestSchemaScale` 1, `CrestDotSize` 0.11, `CrestDotOffsetY` -0.02,
`ConfirmSchemaScale` 1, `ConfirmText` «Вы уверены?», `ConfirmButtonText` «Выбрать»,
`PurchaseText` «Знак получен», `ShrinkCrestNameToFit` true,
`RowLabelDebugAscii` false, `RowLabelOffsetX/Y` 0, `TextCentreSource` columns,
секция `Probe`: `Enabled` false.

Лог-маркеры для проверки: `[CREST]`, `[SHOPFX]`, `[SCHEMA]`, `[STOCKTRACE]`,
`[SHOP] purchase done` → `[SHOP] sent 'Close'` → `[SHOP] window closed`.

## 3. Тесты

```
dotnet test CrestPicker.Tests
```

33 теста, все зелёные (проверено 09.11.2026). Покрыт только `Pure/`: тестовый проект
подключает `Pure/*.cs` как исходники и не ссылается на DLL игры. Всё живое поведение
(сцены, PlayMaker, TMP, Harmony на живых объектах) — ручной чек-лист в `TESTING.md`.

Правила:
- тест не подгоняется под сломанный код; сначала выясняется, что неверно — код или ожидание;
- тестовые файлы принадлежат агенту `tester`; `ivan` их не создаёт и не правит
  (в том числе через shell — это обход прав, а не находчивость);
- новая чистая логика идёт в `Pure/` и покрывается тестом.

## 4. Архитектура

Корень:

- `Plugin.cs` — точка входа: конфиг, отложенный оффер с грейсом 2 с, `Harmony.PatchAll`.
- `ChurchkeeperPatch.cs` — постфикс `PlayMakerFSM.Start`: вставляет `OfferCrestAction`
  в голову состояния `End` FSM `Bonetown/Churchkeeper/Conversation`.
- `CrestPickerService.cs` — пул гребней и применение выбранного (без UI).
- `CrestSelectMenu.cs` — собственное uGUI-меню (фолбэк без живого магазина), ввод,
  звуки, блокировка героя.
- `CrestOfferState.cs` — флаг однократности на слот, путь к `state.json`, логирование.
- `Probe.cs` — диагностика: дамп FSM ранних сцен и списков предметов, по умолчанию выкл.
- `ShopCrestMenu.cs` — сессия живого окна магазина: перенос окна к герою, подмена стока
  временными `ShopItem.CreateTemp`, событие `SHOP UP`, хук покупки, teardown.
- `ShopCosmetics.cs` — косметика живого окна: скрытие иконок, цен, счётчиков валюты,
  тексты деталей и подтверждения, метки строк, центрирование, селектор.
- `ShopCrestSchema.cs` — схема гребня в панели деталей и на подтверждении
  (режимы `Clone` / `VanillaSprites` / `Generated`).
- `ShopInputLock.cs` — блокировка управления героем одним статическим токеном.
- `ShopDiag.cs` — разовый дамп объектов магазина в Bonetown и дамп по `F8`.
- `ShopTrace.cs` — 10 сэмплов окна после открытия (окно, список, fade строк).
- `ShopRenderDiag.cs` — дамп камер, layer mask, проекции панели и рендереров.
- `StockTrace.cs` — лог каждого вызова `ShopMenuStock.SetStock` со стеком.

`Pure/` (без UnityEngine и Assembly-CSharp):

- `CrestPoolRules.cs` — режимы пула и фильтр `IsBaseVersion && !IsHidden`, минус надетый.
- `CrestLabel.cs` — локализованное имя либо имя ассета, если имя пустое.
- `ConfigRules.cs` — миграции значений конфига (`CrestSchemaScale`, `CrestDotSize`).
- `CentreSource.cs` — разбор `TextCentreSource`, неизвестное значение → фолбэк.
- `Geometry.cs` — отбраковка NaN/∞/|x| ≥ 1000, клампинг оси.
- `OfferStore.cs` — чтение/запись `state.json`, битый JSON → пустой словарь.
- `PurchaseClose.cs` — тайминги закрытия: 0.5 с после сообщения о покупке, 3 с без
  сообщения, `ForceIdle` через 5 с.

`docs/crest-shop-centering.md` — разбор, почему метки строк центрируются именно так,
и какие якоря не сработали.

## 5. Ключевые факты об игре

- Окно магазина открывается событием `SHOP UP` в FSM `shop_control`; состояние окна —
  `Open Window`, закрытие — возврат в `Idle`.
- Сток перезаливает `ShopOwnerBase.SpawnUpdateShop` — на время сессии его надо
  блокировать (Harmony-постфикс в `ShopCrestMenu.cs`), иначе игра вернёт ванильный сток.
- Покупка ловится постфиксом `ShopItemStats.SetPurchased`.
- Строки списка — `Shop Item Template(Clone)`; внутри `Item Sprite`, `Item cost`,
  `Cost Sprite`, `Material Cost Icon`. Полосу выделения рисует отдельный объект
  `Item List Group/selector`.
- Панель деталей — `Item List Group/Item Details`, тексты `Item name` и `Item desc`.
  У них компонент `ChangeFontByLanguage` — единственное место, где русский текст
  рисуется штатно.
- Почему кириллица в строке не работает: `perpetua_tmpro` не содержит кириллических
  глифов, а `russian_body` без своего материала даёт пустой меш. Отсюда путь: клонировать
  видимый TMP из `Item Details` вместе с шрифтом и материалом.
- Смена гребня — два вызова: `ToolItemManager.SetEquippedCrest(id)` и затем
  `SendEquippedChangedEvent(force: true)`; без второго остаётся мувсет прежнего гребня.

## 6. Грабли (набитые шишки)

- Не писать `AlphaSelf` внутрь клона: у клона свой fade-груп, альфу надо вести от
  строки-источника.
- Не держать свою метку видимой силой: видимость берётся из fade строки
  (`RowAlpha`), иначе метка висит на прокрученных и закрытых строках.
- Не проверять `icon.enabled`: мод сам гасит иконку строки, такая проверка врёт.
- Окно само владеет fade и закрытием: не закрывать своим кодом в обход FSM, иначе
  остаются селектор, кнопки и тексты.
- Схема гребня живёт под нейтральным родителем вне fade-групп окна
  (`CrestPicker Schemas`), иначе гаснет вместе с чужим fade.
- `ShopItem.CreateTemp` не помечает позицию купленной, поэтому ваниль после покупки
  открывает окно заново — закрытие инициирует сам мод (`PurchaseClose`).
- `russian_body` без своего материала = пустой меш; шрифт и материал неразделимы.

## 7. Состояние на сегодня

Последние коммиты (08–09.11.2026):

```
0c0c40a fix: anchor row labels to selector bar
c494f33 fix: anchor row labels to row icon with configurable offset
e7e36e2 fix: label visibility from row fade, not from hidden icon
ad3d474 fix: row labels centered in row and hidden with row
21e7ebf fix: row names with fixed size, icons hidden
1ed10d4 refactor: rows keep only selection highlight, details panel owns all text
cfbcc7f feat: crest name and description in details panel, row labels off by default
```

Работает (проверено в игре до последнего коммита):

- окно магазина открывается поверх ванильного, ввод блокируется;
- сток подменяется на гребни, покупка применяет гребень;
- имя и описание выбранного гребня рисуются по-русски в панели деталей;
- схема гребня рисуется в панели деталей и на подтверждении.

Открыто:

- остатки после закрытия: текст «Предмет куплен» и имя гребня (PLAN, дефект 3);
- после закрытия остаются `UISelectionList` и кнопки подтверждения (дефект 4);
- схема/знак гаснет последним (дефект 2) — **не проверено** после последних коммитов;
- привязка названий строк к `selector` (`0c0c40a`) — **не проверено** в игре,
  нужен прогон с логом `[SHOPFX] row label placed ... anchor=selector`;
- дефект 1 (пустые названия строк) закрыт кодом клонированием видимого TMP —
  **не проверено** на всех строках и на гребнях без локализованного имени.

## 8. Инструменты

- `%TEMP%\opencode\silksong\shot.ps1` — снимок окна игры.
- `%TEMP%\opencode\silksong\ocr.ps1` — распознавание текста на снимке, языки `ru`
  и `en-US`.
- `%TEMP%\opencode\silksong\dec` — декомпил игры (`Assembly-CSharp`), `pmdec` —
  декомпил PlayMaker. Каталог временный: при потере дампа пересобрать через `ilspycmd`.

## 9. Публикация

Репозиторий: https://github.com/triojohn/silksong-crestpicker (ветка `master`).

Открытые задачи из `PLAN.md`:

- этап 4 — полировка: тексты, иконки, кнопка отказа, повторный визит;
- этап 5 — заглушка ванильной выдачи (7 FSM-правок, кандидат — чётки
  `Rosary_Set_Small`, 60 шт.), отложен;
- тег `v1.0.0`;
- публикация в Thunderstore (в `.gitignore` уже есть `thunderstore/temp` и
  `thunderstore/dist`; сама папка не заведена).

## 10. Идея отдельного мода

Скип интро + автозагрузка последнего сейва. Точка входа —
`UIManager.UIContinueGame(slot, saveData)` после `UIManager.Start`; так можно обойти
стартовое меню и сразу продолжить последний слот. Готовых модов с автозагрузкой
последнего сейва на момент записи не найдено (**не проверено** — поиск не повторялся).
