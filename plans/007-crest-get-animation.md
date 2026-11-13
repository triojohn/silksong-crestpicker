---
task: "007 — при получении гребня в нашем окне играет ванильная реакция получения, без попапа"
created: 2026-11-13
status: draft
verify: pending
---

# 007 — Реакция получения гребня без попапа

## 1. Цель

Когда игрок получает гребень через наше окно (магазин `ShopCrestMenu` или оффер
`CrestSelectMenu`), в мире проигрывается ровно то, что игра показывает при находке
предмета: вспышка героя, эффект получения и звук получения. Всплывающего окна
уведомления сверху (`ToolCrestUIMsg`) при этом нет.

Проверяемо:

- В логе на новом гребне одна строка `[CREST] reaction played for '<name>'`
  (или `[CREST] reaction unavailable: component missing`).
- Повторный выбор уже открытого гребня строки реакции не даёт.
- `dotnet build -c Release` и `dotnet test CrestPicker.Tests` зелёные.
- В игре: покупка нового гребня в нашем окне — вспышка + звук получения, окна
  уведомления сверху нет.

## 2. Границы

Не входит:

- Подавление попапа кодом: наш путь его не вызывает (см. раздел 3), Harmony-патчи на
  `ToolCrestUIMsg.Spawn` / `ShowToolCrestUIMsg` не добавляем.
- Новые клипы, эффекты и звуки: берём ванильные поля компонента реакции.
- Изменение `ToolCrest.Unlock()` и его побочек (`ReportCrestUnlocked`,
  `InventoryPaneList.SetNextOpen`, `DisplayNewIndicator`).
- Изменение логики выбора, пула, окна магазина, оффера и его фраз.
- Новые конфиг-ключи и горячие клавиши.
- Перенос реакции на момент закрытия окна — только если ручная проверка покажет, что
  эффект не виден (отдельная задача, см. риск 2).
- Правки `CrestPicker.Tests/*`, `TESTING.md`, `docs/` — зона `tester`.

Разрешённые файлы: `CrestPickerService.cs`. При подтверждённой проблеме видимости —
дополнительно `CrestSelectMenu.cs` / `ShopCrestMenu.cs` (см. риск 2), но не в этой задаче.

## 3. Research

Декомпил игры: `%TEMP%\opencode\silksong\dec` (временный каталог, номера строк не
воспроизводятся после очистки; истина — логи и ручной чек-лист).

### Что игра делает при находке предмета

- `CollectableItemHeroReaction.cs:41-56` — два статических входа `DoReaction()` и
  `DoReaction(Vector2 effectOffset, bool smallEffect = false)`; оба зовут
  `InternalDoReaction` у статического `_instance` (`:5`, ставится в `Start`, `:28-31`).
  Если инстанса нет — вызов молча ничего не делает.
- `CollectableItemHeroReaction.cs:58-79` — сама реакция: `spriteFlash.flashFocusHeal()`
  (`:60-63`), спавн `itemGetEffectPrefab` в позиции героя с случайным поворотом
  (`:73-77`), `itemGetSound.SpawnAndPlayOneShot(vector)` (`:78`). Клипы/префаб/звук —
  сериализованные поля сцены (`:7-17`), из кода их подставлять не нужно.
- `SpriteFlash.cs:654-657` — `flashFocusHeal()`: белая вспышка 0.85, без состояния героя
  и без анимации: поза и управление не меняются.
- Кто вызывает: `CollectableItem.cs:284,300`, `CollectableItemPickup.cs:364,368`,
  `CollectableRelic.cs:195`, `GenericPickup.cs:63`, `FakeCollectable.cs:52`,
  `ToolItem.cs:561` — то есть любое получение предмета, включая инструменты.
- **Специальной анимации «получение гребня» в игре нет**: в `HeroAnimationController.cs`
  нет ни одного метода/clip про crest/get; единственная презентация находки —
  `CollectableItemHeroReaction`. Поэтому «та же анимация» = вспышка + эффект + звук.

### Что происходит при разблокировке гребня

- `ToolCrest.cs:224-261` — `Unlock()`: ранний выход при `IsUnlocked` (`:226-229`);
  запись `SaveData` с `DisplayNewIndicator = true` (`:236-257`); для апгрейда —
  `previousVersion.Unlock()` и перенос слотов; `ToolItemManager.ReportCrestUnlocked(IsBaseVersion)`
  (`:259`); `InventoryPaneList.SetNextOpen("Tools")` (`:260`).
- `ToolCrest.cs:263-266` — `Get(bool showPopup = true)` игнорирует `showPopup` и просто
  зовёт `Unlock()`. Значит параметр попапа у гребня декоративный.
- `ToolItemManager.cs:1500-1510` — `ReportCrestUnlocked`: ачивки `FIRST_CREST` и прогресс
  `ALL_CRESTS`. Никакого UI-сообщения.
- **Попап сверху — не следствие `Unlock()`**: `ToolCrestUIMsg.Spawn` вызывается только из
  PlayMaker-экшена `HutongGames.PlayMaker.Actions/ShowToolCrestUIMsg.cs:34`, а тот живёт
  в FSM сцены. `ToolCrestUIMsg.cs:22-29` — обёртка над `UIMsgBase<ToolCrest>.Spawn`;
  `UIMsgBase.cs:43-100` — показ, затем ожидание кнопки пропуска (`:79-84`) и звук
  подтверждения (`:85`). Своего звука «получения» попап не играет.
- Наш путь (`CrestPickerService.ApplyCrest` → `crest.Unlock()`) этот FSM не запускает,
  поэтому попап сейчас не появляется и подавлять нечего. Это и есть ответ на «без окна».

### Наши точки получения гребня

- `CrestPickerService.cs:182-212` — `ApplyCrest(ToolCrest)`: единственная точка, где мод
  выдаёт гребень. Внутри `:192-197` — `bool unlockedNow`; `Unlock()` только при
  `!crest.IsUnlocked`; затем `ToolItemManager.SetEquippedCrest` (`:199`) и
  `SendEquippedChangedEvent(force: true)` (`:200`); всё в `try/catch` (`:190-211`).
- `ShopCrestMenu.cs:649-677` — `HandleTempPurchase` (Harmony-postfix покупки): берёт
  гребень из `TempItems`, пишет `[SHOP] crest purchased from live shop`, ставит
  `ShopCrestSchema.ConfirmActive = true`, зовёт `CrestPickerService.ApplyCrest(crest)`
  (`:668`), затем `Plugin.OnMenuClosed(true, _fromDialogue)` и `BeginPurchaseWait()`.
- `CrestSelectMenu.cs:490-516` — `Close(apply: true)`: `PlaySubmitSound()`, затем
  `CrestPickerService.ApplyCrest(_crests[_index])` (`:504`), `Closed?.Invoke`, разблок
  героя, `Destroy`. Идемпотентность держит `_closed` (`:492-497`).
- Других вызовов `ApplyCrest` нет (`grep` по репозиторию: только эти два), горячая
  клавиша `F9` открывает оффер, а не применяет гребень напрямую (`Plugin.cs:373-413`).

### Что уже есть, чтобы не дублировать

- `CrestPickerService.ApplyCrest` — готовый общий шлюз для обоих окон: реакцию ставим
  туда, а не в каждое окно (один вызов вместо двух, оба окна закрыты одним изменением).
- `unlockedNow` (`CrestPickerService.cs:192-197`) — готовый признак «гребень реально
  новый»: им и гейтим реакцию, отдельная проверка `IsUnlocked` не нужна.
- Логирование в стиле проекта: `Plugin.Log.LogInfo("[CREST] ...")`.

### Тестовая обвязка

- `CrestPicker.Tests/CrestPicker.Tests.csproj:17` — `Compile Include="..\Pure\*.cs"`;
  тестовый проект не видит Unity-слой, поэтому изменение `CrestPickerService` покрывается
  только `dotnet build` и ручной проверкой.
- В `Pure/` выносить нечего: решение — один булев гейт `unlockedNow` плюс один вызов
  ванильного метода. Обёртка-класс на один вызов была бы лишним слоем.

## 4. Режим тестирования

`none` — вся работа это один вызов ванильного метода за существующим гейтом
`unlockedNow`; в `Pure/` нет логики, которую стоило бы выносить, живое поведение
(вспышка, эффект, звук) проверяется в игре по чек-листу раздела 6.

## 5. Задачи

- [ ] **T1 (S)** — `CrestPickerService.cs`: реакция получения при реальной разблокировке
  - в `ApplyCrest`, после `SetEquippedCrest`/`SendEquippedChangedEvent` и только при
    `unlockedNow`, вызвать ванильную реакцию:
    `if (unlockedNow) PlayGetReaction(crest);`
  - новый приватный метод `PlayGetReaction(ToolCrest crest)`:
    - `try/catch` вокруг всего тела (сбой реакции не должен ломать `ApplyCrest`);
    - наличие компонента: `UnityEngine.Object.FindObjectOfType<CollectableItemHeroReaction>()`;
      если `null` — `Plugin.Log.LogWarning("[CREST] reaction unavailable: component missing")`
      и выход (иначе `DoReaction()` молча ничего не сделает, `CollectableItemHeroReaction.cs:43`);
    - иначе `CollectableItemHeroReaction.DoReaction();` и
      `Plugin.Log.LogInfo($"[CREST] reaction played for '{crest.name}'")`;
    - в `catch` — `Plugin.Log.LogWarning($"[CREST] reaction failed for '{crest.name}': {ex.Message}")`;
  - `DoReaction()` без аргументов — тот же вариант, что у обычного получения предмета
    (`CollectableItem.cs:300`): смещение нулевое, дальше прибавляется
    `itemGetEffectOffset` сцены (`CollectableItemHeroReaction.cs:64-72`);
  - Тест: `none`.
  - Приёмка: `dotnet build -c Release` зелёный; в логе на новом гребне ровно одна строка
    `[CREST] reaction played for '<name>'`; на уже открытом гребне строки нет; попапа
    `ToolCrestUIMsg` нет.

- [ ] **T2 (S)** — маркеры логов и передача ручного чек-листа `tester`
  - все новые строки — с префиксом `[CREST]`, тексты ровно как в T1
    (`reaction played for`, `reaction unavailable: component missing`, `reaction failed for`);
  - новых префиксов не вводить, существующие `[SHOP]`/`[CREST]` строки не менять;
  - в отчёте перечислить, что осталось ручным: видимость вспышки и эффекта при открытом
    окне магазина, слышимость звука, отсутствие попапа, отсутствие реакции на повторном
    выборе уже открытого гребня, поведение в окне оффера;
  - Тест: `none`.
  - Приёмка: каждая новая строка печатается из одного места; в отчёте есть список
    ручных пунктов для `TESTING.md`.

## 6. Стратегия тестов

Автотестов на эту задачу нет: изменение живёт в Unity-слое и состоит из одного вызова.

Регресс — существующий набор:

- `dotnet build -c Release` — обязательна, тестовый проект Unity-слой не видит.
- `dotnet test CrestPicker.Tests` — ожидается `не пройдено 0, пройдено 139`.

Ручной чек-лист (в `TESTING.md` пишет `tester`):

1. Купить новый (закрытый) гребень в нашем окне магазина: вспышка героя, эффект получения
   и звук получения; окна уведомления сверху нет; в логе одна строка
   `[CREST] reaction played for '<name>'`.
2. Выбрать новый гребень в окне оффера из диалога: то же поведение.
3. Выбрать уже открытый гребень (пул `All` / `IgnoreUnlockState=true`): реакции нет,
   строки `reaction played` нет.
4. Выбрать гребень повторно в той же сессии после первого получения: реакции нет.
5. Отмена окна (закрытие без выбора): реакции нет, звук отмены как раньше.
6. Отсутствие компонента реакции в сцене (проверка на сцене без героя-объекта, если такая
   доступна): строка `[CREST] reaction unavailable: component missing`, игра не падает.
7. После покупки окно магазина закрывается как раньше (`[SHOP] purchase done` →
   `sent 'Close'` → `window closed`), инвентарь открывается на вкладке Tools
   (ванильная побочка `ToolCrest.cs:260`).

## 7. Риски

- **Двойная реакция.** `ApplyCrest` зовётся из двух окон, но каждое гарантирует один
  вызов: магазин удаляет предмет из `TempItems` (`ShopCrestMenu.cs:659`), окно оффера
  закрывается один раз (`CrestSelectMenu.cs:492-497`). Вероятность низкая, последствие —
  двойная вспышка. Что делаем: гейт `unlockedNow` — второй вызов на том же гребне уже
  видит `IsUnlocked == true` и реакции не даёт; чек-лист пункт 4.
- **Эффект не виден при открытом окне.** Реакция играет сразу после `Unlock()`, когда
  панель магазина ещё на экране; герой может быть вне кадра или за панелью, а эффект
  спавнится в позиции героя (`CollectableItemHeroReaction.cs:64-77`). Вероятность
  средняя, последствие — слышен только звук. Что делаем: сначала ставим вызов сразу
  (как в игре); если прогон покажет невидимый эффект — отдельная задача переносит вызов
  в момент закрытия окна (`CrestSelectMenu.Close` после `Destroy`, магазин — после
  закрытия ванильного FSM в `TickPurchase`). В этом плане не делаем.
- **Звук во время открытого меню.** `AudioEvent.SpawnAndPlayOneShot` не блокируется
  открытым окном, звук наложится на звук покупки. Вероятность высокая, последствие —
  лёгкая каша из двух эффектов на одном кадре. Что делаем: принимаем; если мешает —
  тот же перенос на закрытие окна (риск 2).
- **`_instance` реакции отсутствует.** Компонент живёт на объекте героя в сцене
  (`CollectableItemHeroReaction.cs:5,28-31`); если его нет, `DoReaction()` молча ничего
  не делает. Вероятность низкая (герой есть в любой игровой сцене), последствие — тишина
  без диагностики. Что делаем: проверка `FindObjectOfType` и строка
  `reaction unavailable: component missing` (T1).
- **Попап всё-таки появится.** Из кода видно, что попап спавнит только PlayMaker-экшен
  сцены (`ShowToolCrestUIMsg.cs:34`), но ассеты FSM в декомпиле недоступны. Вероятность
  низкая, последствие — окно уведомления перекрывает игру и ждёт кнопку пропуска
  (`UIMsgBase.cs:79-84`). Что делаем: наш путь его не вызывает, проверка — чек-лист
  пункты 1-2; если появится, это отдельная задача с Harmony-prefix на
  `ToolCrestUIMsg.Spawn`.
- **Побочки `Unlock()` меняют поведение.** `InventoryPaneList.SetNextOpen("Tools")`
  (`ToolCrest.cs:260`) и ачивки (`ToolItemManager.cs:1500-1510`) уже работают сегодня;
  задача их не трогает. Вероятность нулевая для регресса, последствие — нет. Что делаем:
  ничего, только чек-лист пункт 7.
- **Номера строк декомпила.** `%TEMP%\opencode\silksong\dec` — временный каталог.
  Вероятность средняя, последствие — план нельзя перепроверить по строкам. Что делаем:
  поведение фиксируется логами `[CREST] reaction ...` и чек-листом, а не номерами строк.
- **Точки отката.** T1 — удалить вызов `PlayGetReaction` и сам метод; T2 — только логи.
  Окна, пул, оффер и `Unlock()` не меняются.

## Журнал верификации

Дописывается `tester` после каждой проверки. Статус — строго из enum
`passed | failed | not_run | inconclusive`.

### 2026-11-13 — pending

- статус: `not_run`
- команды и точный вывод: —
- замечания: план составлен по коду репозитория и декомпилу игры на 13.11.2026.
  Наличие `CollectableItemHeroReaction` в сцене Bonetown из кода не видно — проверяет
  T1 и чек-лист пункт 6.
