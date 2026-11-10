---
task: "003 — собственное окно магазина вместо ванильного окна Пебба"
created: 2026-11-10
status: draft
verify: pending
---

# 003 — Собственное окно магазина (клон префаба)

## 1. Цель

Мод открывает свой экземпляр окна магазина, созданный из префаба `shopPrefab` живого `ShopOwnerBase`, и не меняет ванильное окно Пебба: ни объект, ни сток, ни позицию, ни `AlphaSelf`.

Проверяемо:

- В Bonetown при открытом ванильном магазине Пебба наш выбор гребня открывается в отдельном окне; `[WATCH] state` и `[WATCH] rootPos` ванильного окна не меняются за сессию.
- После закрытия нашего окна магазин Пебба открывается и закрывается штатно, ванильный сток на месте (число и `ShopItem` совпадают с логом до входа).
- В логе нет `[WATCH] root glide`, `[SHOP] guard raised alpha`, `[STOCK] owner stock re-apply blocked`.
- В логе `[OWN] clone` ровно один экземпляр на сцену, `[OWN] released reason=scene` при выходе из Bonetown.
- `dotnet build -c Release` и `dotnet test CrestPicker.Tests` зелёные.

## 2. Границы

Не входит:

- Изменение визуала окна, текстов, локализации, схемы гребня (`ShopCrestSchema`, `ShopCosmetics`) — только источник окна.
- Одновременная работа двух наших окон; задача — изоляция от ванильного, не мультиоконность.
- Правки `CrestPicker.Tests/*`, `TESTING.md`, `docs/` — зона `tester`.
- Удаление `CrestSelectMenu` (остаётся фолбэком при любой ошибке своего окна).
- Замена `ShopMenuStock` или ванильного `shop_control` собственной реализацией FSM.
- Поддержка других торговцев, кроме Пебба и прислужницы (они в одной сцене, префаб тот же).

Разрешённые файлы: `OwnShopWindow.cs` (новый), `ShopCrestMenu.cs`, `ShopWatch.cs`, `ShopTrace.cs`, `ShopRenderDiag.cs`, `ShopDiag.cs`, `AlphaTrace.cs`, `Plugin.cs`, `Pure/OwnWindowPolicy.cs` (новый), `Pure/WindowPinPolicy.cs` (удаление), `Pure/ShopPanelPolicy.cs`.

## 3. Research

### Ванильное окно и почему оно общее

- `ShopOwnerBase` держит окно в `private static ShopMenuStock _spawnedShop` (`ShopOwnerBase.cs:12`); `SpawnUpdateShop` перезаписывает сток и `Title` при каждом чтении `ShopObject`, `OnDestroy` уничтожает окно.
- Пебб и прислужница в одной сцене Bonetown, окно одно на сцену — отсюда конфликт: наш выбор и ванильный магазин делят один объект.
- `ShopMenuStock` независим, синглтона нет; FSM `shop_control` внутри префаба. Значит префаб можно клонировать, и клон получит свой FSM и свой сток.
- Префаб достаётся рефлексией из поля `shopPrefab` живого `ShopOwnerBase` (из разведки; имя поля в декомпиле не перепроверялось в этом прогоне — проверяется T1).

### Как устроен наш текущий захват ванильного окна

- `ShopCrestMenu.cs:53–54` — уже есть рефлексия `_spawnedShop`; `:186` `FindLiveOwner`, `:192` `SpawnedStock() ?? FallbackStock(owner)`, `:198` `stock.gameObject` как корень, `:204` `ControlFsm(root)`.
- `ShopCrestMenu.cs:231` `_origPosition` ванильного окна, `:253` `SendEvent("SHOP UP")`, `:557` `SendEvent("CLOSE SHOP WINDOW")`.
- `ShopCrestMenu.cs:1882` `ShopOwnerStockResetPatch` — Harmony-префикс `ShopOwnerBase.SpawnUpdateShop`, глушит ванильный ресток на время нашей сессии (глобальный побочный эффект на Пебба).
- `ShopCrestMenu.cs:76` `Swapped` — словарь восстановления ванильного стока при закрытии; при своём окне восстановление не нужно.

### Пять мест поиска владельца окна

- `ShopWatch.cs:309`, `ShopTrace.cs:166`, `ShopRenderDiag.cs:408`, `ShopDiag.cs:114`, `ShopCrestMenu.cs:1688` — `Resources.FindObjectsOfTypeAll<ShopOwnerBase>()`. После перехода нужен один кэшированный владелец.

### Ручное управление альфой (удаляется)

- `ShopWatch.cs:37` `WindowName = "Shop Menu(Clone)"`, `:105` `GameObject.Find(WindowName)`; `ShopWatch.cs:143` `StepRoot` (метода `PinRoot` в коде нет — план опирается на `StepRoot`), `:194` `StopGlide`; поля `:58,:61,:64`, константы `:25,:28,:31,:34`; вызов глида в `Update` `:70–82` и в `Sample` `:125`.
- `Pure/WindowPinPolicy.cs:11` — `RootTarget`/`StepTowards`, единственный потребитель `ShopWatch`. Тестов на файл нет (в `CrestPicker.Tests/` файла `WindowPinPolicyTests.cs` нет).
- `ShopCrestMenu.cs:96` `_fade`, `:90` `_windowSeenOpen`, `:113` `_lowAlphaFrames`, `:116` `_guardRaised`; методы `SafeAlpha` `:613`, `RestoreAlpha` `:682`, `FadeOut` `:776`, `GuardTeardown` `:832` (пишет `AlphaSelf = 1` на `:867`), `CheckOpenTiming` `:882` (условие по альфе), `TraceRootAlpha` `:908`.
- `Pure/ShopPanelPolicy.cs:32–35` `ShouldWriteRootAlpha` всегда `false` — заглушка той же эпохи.
- `AlphaTrace.cs` — Harmony-постфикс на сеттер `AlphaSelf`, диагностика этих же записей; фильтр по имени `WindowMark = "Shop Menu"` (`AlphaTrace.cs:32`).

### Что уже есть и переиспользуется

- Рефлексия полей и хелперы: `SpawnedShopField` (`ShopCrestMenu.cs:53`), `StockArrayField`/`AvailableStockField` (`:56,:59`), `HasEvent`, `ActiveState`, `FindChild`, `ControlFsm`.
- Вся сессия (`TryOpen`, `SwapStock`, `BuildItems`, покупательский хук, `TickPurchase`) остаётся; меняется только источник `_stock`/`_root`/`_control`.
- `ShopCosmetics` и `ShopCrestSchema` ищут узлы относительно `_root` (`ShopCosmetics.cs:1585–1601`, `ShopCrestSchema.cs:46`), глобального поиска окна по имени у них нет — достаточно передать корень клона.
- Образец настройки хоткея: `Plugin.cs:99–103`.

### Риск `BroadcastAll` (не подтверждено)

- Ванильные события `SHOP WINDOW UP` / `SHOP CLOSED` рассылаются PlayMaker-действием `BroadcastAll`; область действия (только иерархия окна или шире) в этом прогоне не установлена. Проверяется этапом 0.

## 4. Режим тестирования

`after` — чистая политика жизненного цикла клона появляется вместе с кодом, живое поведение (FSM, BroadcastAll, рендер) проверяется только ручным чек-листом; тесты пишет `tester` после задач с пометкой `after`.

## 5. Задачи

### Этап 0 — риск `BroadcastAll`

- [ ] **T1 (S, ~10 мин)** — `OwnShopWindow`: рефлексия префаба, спавн клона по хоткею
  - файлы: `OwnShopWindow.cs` (новый), `Plugin.cs`
  - сделать:
    1. Новый `internal static class OwnShopWindow` с `FieldInfo shopPrefab` от `typeof(ShopOwnerBase)` и методом `TrySpawn()`: `Instantiate` префаба, имя корня `"CrestPicker Shop Menu"` (обязательно не `"Shop Menu(Clone)"`, иначе `GameObject.Find` и `[ALPHATRACE]` путают окна), родитель — новый `GameObject("CrestPicker Shop Window")` в сцене.
    2. Хоткей `OwnWindowHotkey` (по умолчанию `F7`) в секции `General`, вызов из `Plugin.Update` по образцу `Plugin.cs:213`.
    3. Логи: `[OWN] prefab ok 'name'`, `[OWN] prefab missing`, `[OWN] clone spawned id=N owner='...'`, `[OWN] reused id=N`.
  - тесты: `none` — рефлексия и `Instantiate` без движка не проверяются.
  - приёмка: F7 в Bonetown даёт одну строку `[OWN] clone spawned`; второе нажатие даёт `[OWN] reused`, второй клон не появляется; ванильное окно Пебба не изменилось (сравнить `[WATCH]` до/после).
  - проверка: сборка, лог игры.

- [ ] **T2 (S, ~10 мин)** — открытие клона своим `SHOP UP`
  - файлы: `OwnShopWindow.cs`
  - сделать: методы `ControlFsm()` (FSM `shop_control` на корне клона) и `Open()`: проверка состояния `Idle` и наличия события `SHOP UP`, затем `SendEvent("SHOP UP")` только в FSM клона; логи `[OWN] state 'Idle'`, `[OWN] sent 'SHOP UP'`.
  - тесты: `none` — PlayMaker на живом объекте.
  - приёмка: F7-клон открывается и виден; ванильное окно не открылось; в логе `[OWN] sent 'SHOP UP'`, `[OPEN]` отсутствует в ванильном окне (если печатается его состояние — `Idle`).
  - проверка: лог, экран.

- [ ] **T3 (M, ~15 мин)** — матрица изоляции при двух окнах
  - файлы: `OwnShopWindow.cs` (только логи наблюдения)
  - сделать: опрос раз в 0.5 с состояния FSM клона и `AlphaSelf` корня клона, лог только при изменении: `[OWN] tick state='X' alpha=0.00`.
  - сценарий проверки (ручной, один прогон):
    1. Bonetown, клон не создан. Поговорить с Пеббом, открыть ванильный магазин. Записать `[WATCH] state`, `alpha`, `rootPos`.
    2. Не закрывая магазин, нажать F7, затем второй раз (открыть клон). Смотреть `[OWN]` две минуты: не сменилось ли состояние/альфа клона от ванильных событий.
    3. Закрыть ванильный магазин (выйти из диалога). Смотреть `[OWN] tick`: реагирует ли клон на ванильные `SHOP CLOSED` / `SHOP WINDOW UP`.
    4. Обратный порядок: клон открыт (F7 дважды), открыть магазин Пебба, закрыть его; затем закрыть клон через его FSM `CLOSE SHOP WINDOW` и сверить `[WATCH] state/alpha/rootPos` ванильного окна.
  - что смотреть в логе: любые строки `[OWN] tick` во время шагов 2–4 (означают вмешательство), `[ALPHATRACE] path='CrestPicker Shop Menu...'` в момент ванильных событий, изменение `[WATCH]` при действиях с клоном.
  - тесты: `none` — измерение живого поведения.
  - приёмка: заполнена таблица «событие → кто реагирует» по 4 шагам; вердикт в журнале: изоляция есть / есть перекрёстный эффект (тогда T4 обязателен, этап 1 не начинается без него).
  - проверка: лог игры, скриншот двух окон.

- [ ] **T4 (S, ~10 мин)** — заглушка клона, пока ванильное окно активно *(только если T3 показал перекрёстный эффект)*
  - файлы: `OwnShopWindow.cs`, `Pure/OwnWindowPolicy.cs` (новый)
  - сделать: `Pure/OwnWindowPolicy.ShouldPause(bool vanillaWindowActive)` → `true`, когда ванильное окно активно; `OwnShopWindow.Tick()` читает `_spawnedShop.activeInHierarchy` рефлексией и вызывает `root.SetActive(!paused)`; при попытке открыть сессию во время паузы — `Fallback("vanilla shop window active")`; лог `[OWN] paused by vanilla window` / `[OWN] resumed`.
  - тесты: `after` — `ShouldPause` покрывает `tester`.
  - приёмка: повтор матрицы T3 даёт ноль строк `[OWN] tick` при открытом ванильном магазине; после его закрытия клон снова открывается.
  - проверка: повтор шагов T3, лог.

### Этап 1 — своё окно как единственный источник сессии

- [ ] **T5 (M, ~15 мин)** — жизненный цикл клона
  - файлы: `OwnShopWindow.cs`, `Plugin.cs`
  - сделать:
    1. Один клон на сцену: хранить `Current`, `Owner`, `SceneHandle`; при смене сцены или исчезновении владельца — `Destroy` корня и лог `[OWN] released reason=scene|owner`.
    2. Подписка на смену сцены по образцу `ShopDiagScenePatch` (`ShopDiag.cs:599–605`) или проверка `scene.handle` в `Tick`.
    3. Не класть клон в `_spawnedShop` и не переиспользовать `ShopOwnerBase.OnDestroy` как единственный механизм очистки.
  - тесты: `after` — политика `ShouldRebuild(cachedScene, scene, cachedOwnerId, ownerId)` в `Pure/OwnWindowPolicy.cs` покрывается `tester`.
  - приёмка: вход в Bonetown → выход в другую сцену → повторный вход: в логе два `[OWN] clone spawned` и один `[OWN] released`; в иерархии не больше одного корня `CrestPicker Shop Window`.
  - проверка: лог, иерархия (F8-дамп `ShopDiag` или глазная проверка).

- [ ] **T6 (S, ~10 мин)** — `Pure/OwnWindowPolicy`
  - файлы: `Pure/OwnWindowPolicy.cs`
  - сделать: чистые функции без Unity-типов: `ShouldRebuild(int cachedScene, int scene, int cachedOwnerId, int ownerId)`, `RootName` (константа `"CrestPicker Shop Menu"`), `ShouldPause(bool vanillaWindowActive)` (если T4 не понадобился — оставить, используется как страховка).
  - тесты: `after` — `CrestPicker.Tests/OwnWindowPolicyTests.cs` (создаёт `tester`).
  - приёмка: файл компилируется в основной сборке и в тестовом проекте (`Pure/*.cs` подключается как исходники, `CrestPicker.Tests.csproj:17`).
  - проверка: `dotnet build -c Release`.

- [ ] **T7 (M, ~15 мин)** — сессия на клоне вместо ванильного окна
  - файлы: `ShopCrestMenu.cs`
  - сделать:
    1. `TryOpen`: `ShopOwnerBase? owner` и `ShopMenuStock? stock` брать из `OwnShopWindow` (`Current`/`Owner`), а не из `FindLiveOwner()`/`SpawnedStock()`.
    2. Если клона нет — `OwnShopWindow.TrySpawn()`; отказ спавна — `Fallback("own shop window unavailable")`.
    3. Убрать требование «окно активно в иерархии» для ванильного объекта, оставить проверку для клона.
    4. Лог `[OWN] session root='CrestPicker Shop Menu' stock=...`.
  - тесты: `none` — сборка сессии на живых объектах.
  - приёмка: выбор гребня открывает клон; в логе `[OWN] session` и ни одной строки, где корнем сессии назван `Shop Menu(Clone)`.
  - проверка: прогон в игре через диалог прислужницы, лог.

- [ ] **T8 (S, ~10 мин)** — убрать восстановление ванильного стока и позиции
  - файлы: `ShopCrestMenu.cs`
  - сделать: удалить `Swapped` (`:76`), `SpawnedStock()` (`:1398`-окрестность), `FallbackStock(owner)` (`:1398`), восстановление `_origPosition` ванильного окна в закрытии; сток клона пересобирается каждую сессию через `SwapStock`.
  - тесты: `none` — удаление кода, проверяется поведением.
  - приёмка: две сессии подряд открываются с полным списком гребней; в логе нет `[STOCK] restore`; ванильный `[WATCH]` не показывает смены стока.
  - проверка: две сессии подряд в игре, лог.

- [ ] **T9 (S, ~10 мин)** — позиция: двигаем клон, ванильное окно не трогаем
  - файлы: `ShopCrestMenu.cs`
  - сделать: при открытии выставить `position` корня клона относительно героя (переиспользовать текущую формулу с `UiLift`, `:51`); `_origPosition` хранить для клона, восстановление — только для клона.
  - тесты: `none`.
  - приёмка: клон появляется по центру у героя; `[WATCH] rootPos` ванильного окна за сессию не меняется ни разу.
  - проверка: лог `[WATCH] rootPos` до/после, экран.

- [ ] **T10 (S, ~10 мин)** — закрытие только клона
  - файлы: `ShopCrestMenu.cs`
  - сделать: в `TickPurchase`/отмене слать `CLOSE`/`CLOSE SHOP WINDOW` только в `_control` клона; `_windowFsm` (окно внутри клона) — тоже клон; логи `[OWN] sent 'CLOSE' target=clone`, `[OWN] closed`.
  - тесты: `none`.
  - приёмка: после выбора гребня и после отмены клон закрыт, ванильное окно в `Idle` и не показывалось; `[SHOP] sent 'Close'` относится к клону.
  - проверка: лог `[OWN]`/`[SHOP]`, экран.

- [ ] **T11 (S, ~10 мин)** — покупка в клоне
  - файлы: `ShopCrestMenu.cs`
  - сделать: `HandleTempPurchase` продолжает работать по `TempItems` (ключ — экземпляр `ShopItem`), но проверять, что пойманный `ShopItemStats` принадлежит иерархии клона (`stats.transform.IsChildOf(_root)`); иначе игнорировать с логом `[SHOP] purchase ignored (vanilla row)`.
  - тесты: `none` — живой хук.
  - приёмка: покупка гребня в клоне надевает гребень и закрывает клон; покупка в ванильном магазине Пебба не запускает наш обработчик (лог `purchase ignored`).
  - проверка: два прогона (наш выбор, обычная покупка у Пебба), лог.

### Этап 2 — удаление ванильных связей и альфа-логики

- [ ] **T12 (M, ~15 мин)** — `ShopWatch`: убрать глид альфы, смотреть клон по ссылке
  - файлы: `ShopWatch.cs`
  - сделать: удалить `WindowName` (`:37`), `_window`/`_windowId` поиск через `GameObject.Find` (`:103–123`) — брать `OwnShopWindow.Current`; удалить `GlideInterval`/`GlideDuration`/`GlideEpsilon`/`GlideSettle` (`:25,:28,:31,:34`), `_nextGlide`, `_gliding`, `_glideDir` (`:58,:61,:64`), блок глида в `Update` (`:70–82`), `StepRoot` (`:143`), `StopGlide` (`:194`), вызов `StepRoot` в `Sample` (`:125`).
  - тесты: `after` — сборка и весь набор тестов остаются зелёными (удаление без переноса логики).
  - приёмка: `grep -n "WindowPinPolicy\|Glide\|AlphaSelf" ShopWatch.cs` не находит строк; `[WATCH]` печатает `owner`/`state`/`alpha` клона и не меняет альфу.
  - проверка: `dotnet build -c Release`, `dotnet test CrestPicker.Tests`, прогон в игре.

- [ ] **T13 (M, ~15 мин)** — `ShopCrestMenu`: убрать ручное управление альфой
  - файлы: `ShopCrestMenu.cs`
  - сделать: удалить `_fade` (`:96`), `_windowSeenOpen` (`:90`), `_lowAlphaFrames` (`:113`), `_guardRaised` (`:116`), `SafeAlpha` (`:613`), `RestoreAlpha` (`:682`), `FadeOut` (`:776`), `GuardTeardown` (`:832`), `CheckOpenTiming` (`:882`), `TraceRootAlpha` (`:908`), вызовы `GuardTeardown`/`CheckOpenTiming` из цикла сессии; `TeardownShop` оставить только как аварийное закрытие клона без записи `AlphaSelf` (убрать ветку `FadeOut`).
  - тесты: `after` — сборка + тесты.
  - приёмка: `grep -n "AlphaSelf\|_guardRaised\|_lowAlphaFrames\|_windowSeenOpen" ShopCrestMenu.cs` пусто; в логе нет `[SHOP] guard raised alpha` и `[SHOP] teardown done emergency ... alpha=`.
  - проверка: build, test, прогон с отменой и покупкой.

- [ ] **T14 (S, ~5 мин)** — удалить `Pure/WindowPinPolicy.cs`
  - файлы: `Pure/WindowPinPolicy.cs` (удалить)
  - сделать: удалить файл целиком после T12 (единственный потребитель — `ShopWatch`).
  - тесты: `after` — `dotnet test CrestPicker.Tests` (файл не имеет тестов, но компилируется в тестовый проект).
  - приёмка: `grep -rn "WindowPinPolicy" --include=*.cs .` пусто; build и tests зелёные.
  - проверка: build, test.

- [ ] **T15 (S, ~5 мин)** — убрать `ShopPanelPolicy.ShouldWriteRootAlpha`
  - файлы: `Pure/ShopPanelPolicy.cs`
  - сделать: удалить метод `ShouldWriteRootAlpha` (`:32–35`) и его упоминания; `PanelsHiddenOnClose`/`ShouldHidePanels` оставить.
  - тесты: `after` — `tester` убирает кейс из `CrestPicker.Tests/ShopPanelPolicyTests.cs` (зона `tester`; задача возвращается оркестратору, если тест красный).
  - приёмка: метод удалён, `dotnet test CrestPicker.Tests` зелёный.
  - проверка: build, test.

- [ ] **T16 (S, ~10 мин)** — одно место поиска владельца
  - файлы: `ShopWatch.cs`, `ShopTrace.cs`, `ShopRenderDiag.cs`, `ShopDiag.cs`, `ShopCrestMenu.cs`
  - сделать: заменить пять вызовов `Resources.FindObjectsOfTypeAll<ShopOwnerBase>()` (`ShopWatch.cs:309`, `ShopTrace.cs:166`, `ShopRenderDiag.cs:408`, `ShopDiag.cs:114`, `ShopCrestMenu.cs:1688`) на `OwnShopWindow.Owner`; `FindLiveOwner()` удалить.
  - тесты: `after` — build/test.
  - приёмка: `grep -rn "FindObjectsOfTypeAll<ShopOwnerBase>" --include=*.cs .` пусто; диагностика печатает того же владельца (`owner='...'`).
  - проверка: build, F8-дамп в игре.

- [ ] **T17 (S, ~10 мин)** — удалить `ShopOwnerStockResetPatch`
  - файлы: `ShopCrestMenu.cs`
  - сделать: удалить Harmony-класс `ShopOwnerStockResetPatch` (`:1877–1902`) — ванильный ресток больше не влияет на клон.
  - тесты: `after` — build/test.
  - приёмка: в логе нет `[STOCK] owner stock re-apply blocked`; ванильный магазин Пебба после нашей сессии показывает свой обычный сток.
  - проверка: две сессии, лог, экран.

- [ ] **T18 (S, ~5 мин)** — снять `AlphaTrace`
  - файлы: `AlphaTrace.cs` (удалить), `Plugin.cs` (`:188–195` блок `Apply`)
  - сделать: удалить диагностику записей `AlphaSelf` — после T12/T13 писать альфу больше нечему, кроме ванилы.
  - тесты: `after` — build/test.
  - приёмка: `grep -rn "ALPHATRACE" --include=*.cs .` пусто.
  - проверка: build, лог.

### Этап 3 — закрытие

- [ ] **T19 (S, ~10 мин)** — сквозная проверка сборки и тестов
  - файлы: —
  - сделать: `dotnet build -c Release`, `dotnet test CrestPicker.Tests`, прогон в игре по чек-листу из раздела «Стратегия тестов».
  - тесты: `after`.
  - приёмка: build 0 errors/0 warnings; tests 0 failed; чек-лист пройден, статусы внесены в журнал.
  - проверка: вывод команд дословно.

- [ ] **T20 (S, ~10 мин)** — финальная зачистка мёртвых символов
  - файлы: по результатам grep
  - сделать: `grep` по `_spawnedShop`, `WindowName`, `StepRoot`, `SafeAlpha`, `_origPosition`, `Swapped`, `UiLift`, `_boundShift`; удалить всё, у чего не осталось потребителей; хоткей `OwnWindowHotkey` оставить (нужен для ручного чек-листа).
  - тесты: `after` — build/test.
  - приёмка: каждый оставшийся символ имеет хотя бы одного потребителя (`grep` показывает вызов, не только объявление).
  - проверка: build, test, grep.

## 6. Стратегия тестов

Чистое, покрывается юнит-тестами (создаёт `tester`):

- `Pure/OwnWindowPolicy.cs`:
  - `ShouldRebuild` — та же сцена и тот же владелец → `false`; другая сцена → `true`; владелец исчез (`ownerId == 0`) → `true`.
  - `ShouldPause` — ванильное окно активно → `true`; неактивно → `false`.
  - `RootName` не равен `"Shop Menu(Clone)"` и не совпадает с ванильным именем (защита от `GameObject.Find`-коллизии).
- Файл тестов: `CrestPicker.Tests/OwnWindowPolicyTests.cs`.
- Команда: `dotnet test CrestPicker.Tests`.

Живое, только ручной чек-лист (переносится `tester` в `TESTING.md`):

1. Bonetown, поговорить с Пеббом — магазин открывается как без мода; F7-клон при этом не мешает, `[OWN] tick` молчит.
2. Прислужница, выбор гребня — окно клона по центру у героя, список гребней полный, `[OWN] session`, нет `[WATCH] root glide`.
3. Покупка гребня — гребень надет, клон закрыт, `[OWN] closed`; ванильное окно не появлялось.
4. Отмена выбора — клон закрыт, оффер не потрачен (следующий диалог снова предлагает).
5. После выхода из нашего окна открыть магазин Пебба — сток обычный, позиция окна на месте, `[WATCH] rootPos` не менялся.
6. Покупка в ванильном магазине Пебба — наш обработчик не срабатывает (`[SHOP] purchase ignored (vanilla row)`).
7. Выход из Bonetown и возврат — ровно один клон, `[OWN] released reason=scene` один раз.
8. Многократные открытия/закрытия (5 раз) — нет накопления клонов и остатков на экране.

Лог-маркеры: `[OWN]`, `[SHOP]`, `[WATCH]`, `[STOCK]`. Отрицательные маркеры: `[WATCH] root glide`, `[SHOP] guard raised alpha`, `[STOCK] owner stock re-apply blocked`, `[ALPHATRACE]`.

## 7. Риски

- `BroadcastAll` ванильных событий достаёт до клона — вероятность средняя, последствие: клон закрывается/открывается сам, ванильные события ломают нашу сессию. Меряем в T3; если есть — T4 (пауза клона при активном ванильном окне); если не помогает — стоп этапа 1, откат к `aea290b`, эскалация.
- Совпадение имён окон ломает поиск по имени (`ShopWatch.cs:105`, `AlphaTrace.cs:137`) — вероятность высокая при отсутствии переименования, последствие: мод фейдит чужое окно. Смягчение: T1 задаёт `CrestPicker Shop Menu`, T12 убирает поиск по имени.
- Клон-префаб держит ссылки на общие объекты сцены (`ShopItem`, `LocalisedString`, TMP-ассеты), и подмена стока протекает в ванильный магазин — вероятность средняя, последствие: у Пебба пропадает сток. Проверка: чек-лист п.5, сравнение стока до/после; смягчение — писать только в `stock`/`availableStock` клона (T7/T8), не трогать `ShopItem` ванильных строк.
- Глобальный Harmony-хук `ShopItemStats.SetPurchased` ловит покупку у Пебба — вероятность средняя, последствие: наш обработчик закрывает не то окно. Смягчение: проверка `IsChildOf(_root)` в T11.
- Владелец уничтожается вместе со сценой, клон остаётся — вероятность низкая, последствие: висящее окно в другой сцене. Смягчение: T5, очистка по `scene.handle`.
- Имя поля `shopPrefab` не подтверждено — вероятность низкая, последствие: `FieldInfo == null`, своё окно не создаётся. Смягчение: лог `[OWN] prefab missing` и фолбэк в `CrestSelectMenu`.
- `ShopOwnerBase.OnDestroy` уничтожает только `_spawnedShop`, наш клон не в этом поле — риск низкий, но клон не должен попадать в `_spawnedShop` (проверяется T5/T7).
- Тайминги открытия: клон может не успеть прогреться до `SHOP UP` — вероятность средняя, последствие: пустое окно. Смягчение: спавн заранее на входе в сцену (T5), проверка состояния `Idle` перед `SendEvent`.

### Точки отката

- Ветка/коммит на этап: `git revert` коммита этапа возвращает рабочее состояние; этапы коммитятся отдельно (этап 0, этап 1, этап 2, этап 3).
- Откат без git: `OwnShopWindow` вызывается только из `TryOpen`; при проблеме достаточно вернуть в `TryOpen` получение `stock` из `_spawnedShop` (одна точка, T7) — остальные этапы не затрагивают ванильное окно.
- Полный откат: `git reset --hard aea290b` (перед этим сохранить план; план в отдельном коммите, его не терять).
- После этапа 2 откат дороже: `WindowPinPolicy` и `AlphaTrace` удалены; откат этапа — `git revert` соответствующего коммита, не ручная реставрация.

## 8. Журнал верификации

Дописывается `tester` после каждой проверки. Статус — строго из enum `passed | failed | not_run | inconclusive`.

### 2026-11-10 — pending

- статус: `not_run`
- команды и точный вывод: —
- замечания: этап 0 (T3) — обязательный гейт: без вердикта по изоляции `BroadcastAll` этап 1 не начинать.
