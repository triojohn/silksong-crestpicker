# CrestPicker — требования и технические факты

Документ-реестр для плагина выбора знака в Hollow Knight: Silksong.
Составлен по декомпилу игры (`C:\Users\trioj\AppData\Local\Temp\opencode\silksong\dec`,
временный каталог — при потере дампа факты перепроверять через `ilspycmd`).

- GUID: `io.github.triojohn.crestpicker`
- Процесс: `Hollow Knight Silksong.exe`
- Версия: `1.0.0` (синхронно: `Directory.Build.props` и `Plugin.cs`)
- Состояние кода: этапы 0–3 сделаны (см. `PLAN.md`), работает в игре

Обозначения: **R** — функциональное требование, **X** — запрет,
**F** — проверенный технический факт.

---

## 1. Назначение

После диалога с прислужницей капеллы (Chapel Maid) в `Bonetown` игрок получает
выбор знака из меню мода вместо ванильной выдачи. По умолчанию список — только
уже открытые знаки (`IsUnlocked`), флаг `GrantAllCrests` включает все семь.

Требования:

| # | Требование |
|---|---|
| R1 | Оффер показывается после окончания диалога прислужницы капеллы |
| R2 | Список — «нормальные» знаки (`IsBaseVersion && !IsHidden`), по умолчанию только открытые |
| R3 | Выбор сразу меняет знак и мувсет, без перезагрузки сцены |
| R4 | Оффер однократен на слот сейва; отмена флаг не тратит |
| R5 | Ошибка любой подсистемы не роняет игру: всё в try/catch, лог в BepInEx |
| X1 | Мод не глушит ванильную выдачу знака (v1) |

---

## 2. Реестр технических фактов (F)

Все пункты проверены по декомпилу `Assembly-CSharp.dll`; отмеченные «в игре» —
подтверждены прогоном на живом сейве.

### 2.1 Знаки

| # | Факт |
|---|---|
| F1 | Список всех знаков: `ToolItemManager.GetAllCrests()` |
| F2 | Знак по строковому ID: `ToolItemManager.GetCrestByName(string)` → `ToolCrest` |
| F3 | Открыт ли знак: `ToolCrest.IsUnlocked` (bool) |
| F4 | Ванильная выдача: `ToolCrest.Unlock()` — сам выставляет `IsUnlocked`, слоты и `DisplayNewIndicator`, дёргает ачивки `FIRST_CREST` / `ALL_CRESTS` |
| F5 | Выдача без ачивок: ручная запись `PlayerData.instance.ToolEquips.GetData(id)` / `SetData(id, data)` |
| F6 | Смена знака в рантайме: `ToolItemManager.SetEquippedCrest(id)`, затем обязательно `ToolItemManager.SendEquippedChangedEvent(force: true)` — без второго вызова остаётся мувсет прежнего знака (в игре) |
| F7 | «Нормальные» знаки = `IsBaseVersion && !IsHidden`; на практике 7 штук |

Строковые ID знаков:

| ID | Знак |
|---|---|
| `Hunter` | Охотница |
| `Reaper` | Жнец |
| `Wanderer` | Странник |
| `Warrior` | Зверь |
| `Witch` | Ведьма |
| `Toolmaster` | Архитектор |
| `Spell` | Шаман |
| `Cursed` | скрытый, в выбор не входит |
| `Cloakless` | скрытый, в выбор не входит |

### 2.2 Диалоги и меню

| # | Факт |
|---|---|
| F8 | Меню с выбором и покупкой как у торговца: `SimpleShopMenu` + `SimpleShopMenuOwner`, колбэк закрытия `ClosedMenu(bool didPurchase, int purchaseIndex)` |
| F9 | Альтернатива: `UISelectionList` / `UISelectionListItem`. Текстового API у элемента нет — подпись ставится в дочерний `TMP_Text` |
| F10 | Только для двух вариантов: `DialogueYesNoBox.Open(...)` |
| F11 | Закрыть ванильный диалог перед показом своего UI: `DialogueBox.EndConversation(returnHud, onBoxHidden)` |

### 2.3 FSM

| # | Факт |
|---|---|
| F12 | FSM-правка адресуется ключом `(sceneName, objectName, fsmName)` в postfix `PlayMakerFSM.Start` (в игре) |
| F13 | Хелперы FSM — `Silksong.FsmUtil` (NuGet, в проект не подключён; вставка действия сделана вручную через `FsmState.Actions`) |

### 2.4 Валюта

| # | Факт |
|---|---|
| F14 | Гео: `CurrencyManager.AddGeo(n)` либо `PlayerData.instance.AddGeo(n)`; поле сейва — `geo` |
| F15 | Предмета «Rosary String» в инвентаре нет: `RosaryCacheString` — сценовый брейкабл, не покупка |

### 2.5 Прислужница капеллы и точка оффера

| # | Факт |
|---|---|
| F16 | Прислужница капеллы = сцена `Bonetown`, объект `Churchkeeper`, FSM `Conversation` (в игре) |
| F17 | Конец диалога — состояние `End`; все ветки сходятся в него, вход по событию `CONVO_END` (в игре) |
| F18 | Вставка `FsmStateAction` в начало `FsmState.Actions` работает: действие выполняется до остальных и не ломает FSM (в игре) |
| F19 | В момент `OnEnter` состояния `End` игра ещё не в `GameState.PLAYING`, поэтому меню открывается отложенно из `Update` с грейс-периодом (в игре) |

### 2.6 Чётки (нужны для заглушки, этап 5)

| # | Факт |
|---|---|
| F20 | ID предметов-чёток и номинал: `Rosary_Set_Frayed` 30, `Rosary_Set_Small` 60, `Rosary_Set_Medium` 120, `Rosary_Set_Large` 220, `Rosary_Set_Huge_White` 340 |
| F21 | Выдача чёток: `CollectableItemManager.GetItemByName("Rosary_Set_Small").Get(true)` |
| F22 | `CollectableItemManager.GetAllCollectables()` даёт полный список предметов с `GetDisplayName(ReadSource.Inventory)` — источник имён для лога |

### 2.7 Меню и хранение состояния

| # | Факт |
|---|---|
| F23 | Префабы `SimpleShopMenu` / `UISelectionList` из мода недоступны (нет ссылок на ассеты), поэтому меню собрано своё на uGUI |
| F24 | `UIManager.instance.UICanvas` — рабочий родитель для собственной панели; `UIManager.uiAudioPlayer` даёт `PlaySelect`/`PlaySubmit`/`PlayCancel` |
| F25 | Шрифт подписи берётся из первого живого `TMP_Text` через `Resources.FindObjectsOfTypeAll<TMP_Text>()`, иначе `TMP_FontAsset.defaultFontAsset` |
| F26 | Блокировка героя на время меню: `HeroController.instance.AddInputBlocker(this)` / `RemoveInputBlocker(this)` |
| F27 | `PlayerData.profileID` — идентификатор слота сейва; годится как ключ однократности. Флаг мода хранится в `BepInEx/config/CrestPicker/state.json` (формат `{"<slot>": true}`), а не в `PlayerData` |

---

## 3. Ответственные участки кода и патчи

| Файл | Участок | Что делает |
|---|---|---|
| `Plugin.cs` | `Awake` | конфиг, self-check, `Harmony.PatchAll` |
| `Plugin.cs` | `RequestOffer` / `UpdatePendingOffer` | приём заявки от FSM и отложенное открытие меню |
| `ChurchkeeperPatch.cs` | `PlayMakerFSM.Start` (postfix) | вставка `OfferCrestAction` в `Bonetown/Churchkeeper/Conversation/End` |
| `CrestPickerService.cs` | `GetSelectableCrests` / `ApplyCrest` | фильтр по сейву и переключение знака |
| `CrestSelectMenu.cs` | весь класс | собственное меню, ввод, звуки, блокировка героя |
| `CrestOfferState.cs` | `IsOffered` / `MarkOffered` | флаг однократности по слоту |
| `Probe.cs` | `Internal_SceneLoaded` (postfix) | диагностический дамп FSM и списков предметов, по умолчанию выключен |

---

## 4. Риск: на раннем сюжете пул выбора равен одному знаку

**Решение принято (v1):** по умолчанию список фильтруется по `IsUnlocked`, то
есть на свежем сейве в нём один `Hunter`. Все семь знаков включаются флагом
`GrantAllCrests = true` в конфиге — осознанный выбор игрока, по умолчанию
выключен.

Отброшенные варианты:

1. Выдавать все 7 всегда — ломает прогрессию и досрочно даёт ачивки.
2. Выдавать выбранный знак принудительно через ручную запись `ToolEquips` — тогда
   «выбор» превращается в «выдать любой один», и мод перестаёт быть выбором.

---

## 5. Открытые вопросы

1. Нужно ли избегать ачивок `FIRST_CREST` / `ALL_CRESTS`: сейчас используется
   `Unlock()` (F4), ачивки могут сработать досрочно. Альтернатива — ручная запись
   `ToolEquips` (F5).
2. Ключи локализации вместо литералов в меню: подходящий sheet не найден.
3. Иконки знаков в списке — API для спрайта знака не проверен.
4. Заглушка ванильной выдачи (этап 5): предмет-замена найден (F20, F21),
   остаётся решение, делать ли семь FSM-правок.
