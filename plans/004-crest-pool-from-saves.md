---
task: "004 — пул гребней по данным сейвов"
created: 2026-11-11
status: draft
verify: pending
---

# 004 — Пул гребней строится по данным сейвов

## 1. Цель

Меню прислужницы предлагает гребни, которых нет в текущем прохождении, но которые
открыты в любом другом слоте. Пул = объединение открытых гребней по слотам 0..4,
минус открытые в текущем прохождении, пересечённое с нормальными гребнями
(`!IsHidden && IsBaseVersion`).

Проверяемо:

- На сейве, где открыты 2 нормальных гребня, а в соседнем слоте — 5, меню показывает
  3 гребня (те, которых нет в текущем); лог `[CREST] pool source=saves slots=5
  elsewhere=5 here=2 pool=3 names=<...>`.
- Когда в текущем прохождении открыты все нормальные гребни, диалог заканчивается без
  меню и с логом `[CREST] offer ended: all normal crests unlocked here (slot N)`.
- Когда во всех слотах не открыто ни одного нормального гребня, меню показывает все
  нормальные гребни, лог содержит `reason=nothing-anywhere`.
- Выбор гребня по-прежнему применяется через `ToolCrest.Unlock()` +
  `SetEquippedCrest` + `SendEquippedChangedEvent(force: true)`, без записи ачивок руками.
- `dotnet build -c Release` и `dotnet test CrestPicker.Tests` зелёные.

## 2. Границы

Не входит:

- Запись в чужие сейвы и в сейв текущего прохождения — только чтение.
- Пометка ачивок (`FIRST_CREST`, `ALL_CRESTS`) — ачивки идут своим путём через
  `ToolCrest.Unlock()`; обходов нет.
- Чтение сейвов с сервера/облака сверх того, что отдаёт `Platform.Current.ReadSaveSlot`.
- UI-индикация загрузки (спиннер, «подождите»): на время чтения меню просто ещё не
  открыто, экран не меняется.
- Горячая клавиша `F9` не меняет контракт: она работает по кэшу, при отсутствии кэша —
  по фолбэку «все нормальные».
- Правки `CrestPicker.Tests/*`, `TESTING.md`, `docs/` — зона `tester`.
- Пересборка пула при выходе в главное меню внутри сессии: кэш привязан к слоту,
  смена слота кэш сбрасывает (см. T3).
- Дедупликация одинаковых имён внутри одного слота сверх `Distinct` по имени.

Разрешённые файлы: `Pure/CrestPoolRules.cs`, `Pure/CrossSavePool.cs` (новый),
`CrestPoolFromSaves.cs` (новый), `CrestPickerService.cs`, `CrestSelectMenu.cs` (только
точка отказа, если понадобится), `Plugin.cs`.

## 3. Research

### Как игра сама определяет «нормальный открытый гребень»

- `ToolItemManager.cs:1512-1515` (декомпил) — `GetUnlockedCrestsCount` =
  `!crest.IsHidden && crest.IsBaseVersion && crest.IsUnlocked`; эталон фильтра.
- `ToolCrest.cs:150` — `IsUnlocked` у гребня читается **только** из сейва:
  `PlayerData.instance.ToolEquips.GetData(name).IsUnlocked`.
- `ToolCrest.cs:164` — `IsBaseVersion => !previousVersion`, то есть базовость берётся из
  ассета, а не из сейва.
- `ToolCrestsData.cs:18` — `Data.IsUnlocked`; `:20` — `Slots` (на пул не влияет).
- `SerializableNamedList.cs:65-68` — `GetData(name)` возвращает `default` для
  неизвестного имени, не бросает. Значит отсутствующий гребень = «не открыт».

### Чтение сейва по слоту

- `Platform.cs:142` — `SaveSlotCount = 5`; `:395-402` — `IsSaveSlotIndexValid` =
  `0..4`; `:429` — `abstract void ReadSaveSlot(int, Action<byte[]>)`.
- `DesktopPlatform.cs:316-344` — файл читается синхронно, но колбэк отдаётся через
  `CoreLoop.InvokeSafe`, то есть **на следующем тике**, не в том же вызове. Пустой слот
  даёт `bytes == null`.
- `GameManager.cs:4858-4867` — `GetJsonForSaveBytes` (instance) расшифровывает при
  `gameConfig.useSaveEncryption`; `:4869-4882` — `GetJsonForSaveBytesStatic`, безопасен
  при живом `GameManager.instance`. Использовать статический вариант.
- `SaveDataUtility.cs:114-119` — `DeserializeSaveData<T>(json)` только парсит JSON в новый
  объект, статические `PlayerData.instance`/`SceneData.instance` не трогает.
- `GameManager.cs:4643` — та же цепочка
  (`ReadSaveSlot` → `GetJsonForSaveBytes` → `DeserializeSaveData<SaveGameData>`) применяется
  самой игрой при загрузке слота; значит формат и вызов подтверждены.
- `SaveGameData.cs:6` — нужные данные в `playerData`; `PlayerData.cs:2744` — `ToolEquips`.

Цепочка для одного слота:
`Platform.Current.ReadSaveSlot(slot, cb)` → `GameManager.GetJsonForSaveBytesStatic(bytes)` →
`SaveDataUtility.DeserializeSaveData<SaveGameData>(json).playerData.ToolEquips.GetValidNames()`
→ фильтр по именам из `ToolItemManager.GetAllCrests()`.

### Что в репозитории уже есть

- `CrestPickerService.cs:28-74` — текущий пул: `ToolItemManager.GetAllCrests()` (`:34`),
  факты `CrestFacts` (`:45-46`), решение через `CrestPoolRules.Select` (`:49`),
  диагностический лог (`:54-65`).
- `Pure/CrestPoolRules.cs:35-43` — `IsSelectable` уже содержит нужный фильтр
  `!isBase || isHidden → false`; `:63-87` — `Select` (режимы `All`/`Unlocked`, минус надетый).
- `CrestSelectMenu.cs:70-75` — пустой пул уже обрабатывается: меню не открывается, есть
  предупреждение. Точка отказа для случая 4 есть, но с неверной формулировкой причины.
- `Plugin.cs:255-290` — `RequestOffer` (проверки `EnableSnailChoice`, уже открытого меню,
  слота, флага однократности); `:283` — грейс `OfferGrace = 2f`; `:293-311` —
  `UpdatePendingOffer`, который и есть место ожидания асинхронного пула.
- `ChurchkeeperPatch.cs:89-106` — `OfferCrestAction.OnEnter` вызывает `RequestOffer()` и
  сразу `Finish()`; значит «взаимодействие закончилось» = оффер просто не открывает окно.
- `Plugin.cs:90-97` — ключи `CrestPool` (`CrestPoolMode`) и `IgnoreUnlockState` (default
  `true`); `:158` — режим читается один раз в `Awake`.
- `TESTING.md:32` — сейчас 111 тестов; тестовый проект подключает `Pure/*.cs` как
  исходники, новый файл в `Pure/` подхватится без правки csproj.

### Ловушки, найденные по коду

- `IgnoreUnlockState` сейчас `true` по умолчанию и в `CrestPoolRules.Select` (`:69`)
  подменяет режим на `All`. Если не поправить, новый режим будет мёртвым: любой оффер
  получит «все нормальные». Нужен приоритет нового режима над флагом и default `false`.
- `Plugin.cs:158` — режим не перечитывается на лету, хотя комментарий конфига обещает
  обратное. Не чиним здесь, но и не полагаемся на «летучесть».
- Декомпил игры лежит во временном каталоге `%TEMP%\opencode\silksong\dec` (`plans/000-handoff.md:166-167`);
  при его потере ссылки на строки декомпила не проверить — план помечает такие факты
  как «декомпил», а не как «репо».

## 4. Режим тестирования

`after` — новые правила пула появляются как чистые функции вместе с кодом, живое чтение
сейвов (`Platform.Current`) и поведение FSM тестом не покрываются; `tester` пишет тесты
после T1–T2, ручной чек-лист после T6–T8.

## 5. Задачи

### Этап 1 — чистые правила (без изменения поведения)

- [ ] **T1 (S)** — вынести «нормальный гребень» в `Pure/CrestPoolRules.cs`
  - добавить `internal static bool IsNormal(bool isBase, bool isHidden) => isBase && !isHidden;`
  - `IsSelectable` (`:35-43`) начинает вызывать `IsNormal` вместо повторения условия;
  - приёмка: `dotnet test CrestPicker.Tests` зелёный (все текущие тесты `CrestPoolSelectTests`
    и `CrestPoolRulesTests` проходят без правок), `dotnet build -c Release` зелёный.

- [ ] **T2 (M)** — новая чистая функция сборки пула `Pure/CrossSavePool.cs`
  - типы:
    ```csharp
    internal enum CrestPoolEmpty { None, AllNormalsUnlockedHere, NothingAnywhere, NothingNewElsewhere }
    internal readonly record struct CrestPoolSelection(
        List<CrestFacts> Crests, CrestPoolEmpty Empty, int NormalTotal, int UnlockedHere);
    internal static CrestPoolSelection Build(
        IEnumerable<CrestFacts> all,
        IEnumerable<IEnumerable<string>> otherSlots,
        bool includeEquipped);
    ```
  - правила: `normal = CrestPoolRules.IsNormal`; `union` = `Distinct` по имени из
    `otherSlots`; остаток = нормальные, у которых имя в `union` и `!IsUnlocked`;
  - если остаток пуст: все нормальные `IsUnlocked` → `AllNormalsUnlockedHere`;
    `union` пуст → `NothingAnywhere`; иначе `NothingNewElsewhere`; в двух последних
    случаях `Crests` = все нормальные;
  - надетый гребень вырезается тем же правилом, что в `Select` (`includeEquipped`);
  - порядок результата = порядок `all`;
  - приёмка: файл не содержит `using UnityEngine`/`Assembly-CSharp`; `dotnet build` зелёный;
    логика покрывается тестами T2 из раздела 6 (пишет `tester`).

### Этап 2 — чтение сейвов (Unity-слой)

- [ ] **T3 (M)** — `CrestPoolFromSaves.cs`: сессионный кэш и асинхронное чтение слотов
  - состояние: `enum LoadState { Idle, Loading, Ready, Failed }`, поля `_state`, `_slot`,
    `_queue`, `_elsewhere` (`HashSet<string>`), `_startedAt`, `_deadline`;
  - `Begin(int currentSlot)`: если `_state == Ready && _slot == currentSlot` — сразу `Ready`;
    иначе сброс, `_queue = 0..4 минус currentSlot`, `_state = Loading`,
    `_deadline = Time.unscaledTime + TimeoutSeconds` (`const float TimeoutSeconds = 2f`);
  - `Tick()`: если `Loading` — взять **один** слот из очереди, вызвать
    `Platform.Current.ReadSaveSlot`; в колбэке: `bytes == null` → пропустить слот;
    иначе `GameManager.GetJsonForSaveBytesStatic` + `SaveDataUtility.DeserializeSaveData<SaveGameData>`
    + `ToolEquips.GetValidNames()` → добавить имена; пустая очередь → `_state = Ready`;
  - десериализация только в колбэке (он приходит от `CoreLoop`, то есть на главном потоке);
    никаких `Task.Run` — `SaveDataUtility` не потокобезопасен;
  - `Platform.Current == null` → `_state = Failed` с логом;
  - по истечении `_deadline` в `Loading` → `_state = Failed`, лог
    `[CREST] saves read timeout after 2s slots=<сколько успели>`;
  - публично для слоя выше: `LoadState State`, `IReadOnlyCollection<string> Elsewhere`,
    `int SlotsRead`, `static CrestPoolFromSaves Instance` (или статические члены, как в
    остальных файлах мода);
  - приёмка: в игре в логе `[CREST] saves read slot=0 bytes=12345 names=Hunter,Reaper` по
    строке на слот и финальный `[CREST] saves read done slots=4 names=<n>`; ввод героя во
    время чтения не блокируется, кадровый провал не заметен (один слот за кадр).

### Этап 3 — конфиг и подстановка пула

- [ ] **T4 (S)** — режим `FromSaves` в конфиге
  - в `Pure/CrestPoolRules.cs` добавить член `CrestPoolMode.FromSaves` с комментарием
    «объединение открытых по всем слотам минус открытые здесь»;
  - `Plugin.cs:90` — default `CrestPoolMode.FromSaves`, текст описания обновить;
  - `Plugin.cs:92-94` — `IgnoreUnlockState` default `false`, в описании указать, что при
    `CrestPool=FromSaves` ключ игнорируется;
  - в `CrestPoolRules.Select` (`:69`) или в вызывающем коде: при
    `mode == FromSaves` флаг `ignoreUnlock` не применяется;
  - приёмка: `dotnet build -c Release` и `dotnet test` зелёные; при `CrestPool=FromSaves`,
    `IgnoreUnlockState=true` в логе `[CREST] pool mode: FromSaves (IgnoreUnlockState ignored).`

- [ ] **T5 (M)** — `CrestPickerService.GetSelectableCrests` собирает пул из сейвов
  - оставить сбор `ToolCrest`/`CrestFacts` (`:34-47`) как есть;
  - при `PoolMode == FromSaves`: если `LoadState.Ready` — `CrossSavePool.Build(facts,
    Instance.Elsewhere, includeEquipped)`; если `Loading`/`Failed` — `Build(facts,
    Array.Empty<IEnumerable<string>>(), includeEquipped)` (даёт «все нормальные», лог
    `[CREST] pool fallback reason=<loading|failed>`);
  - при `AllNormalsUnlockedHere` вернуть пустой список и лог причины (решение об отказе
    принимает `Plugin`, см. T6);
  - при `NothingAnywhere`/`NothingNewElsewhere` вернуть все нормальные с логом причины;
  - сохранить прежние режимы `All`/`Unlocked` через `CrestPoolRules.Select` без изменений;
  - лог одной строкой: `[CREST] pool source=saves|config slots=5 elsewhere=<n> here=<n>
    pool=<n> reason=<none|...> names=<...>`;
  - приёмка: в игре имена в логе совпадают с ожидаемым объединением; `F9` при
    `LoadState != Ready` открывает меню со всеми нормальными, ошибок в логе нет.

### Этап 4 — поведение оффера

- [ ] **T6 (S)** — ожидание чтения и отказ от меню
  - `Plugin.RequestOffer` (`:255-290`): после проверок слота вызвать
    `CrestPoolFromSaves.Begin(slot)`;
  - `Plugin.UpdatePendingOffer` (`:293-311`): вызывать `CrestPoolFromSaves.Tick()` каждый
    кадр; окно открывать только при `State == Ready`; грейс `OfferGrace` остаётся
    верхней границей, по его истечении — тот же фолбэк «все нормальные» (пул уже умеет
    это через `Failed`, T5) и прежнее сообщение `Offer dropped: game not ready`;
  - если `GetSelectableCrests()` вернул пустой список — меню не открывать, писать
    `[CREST] offer ended: all normal crests unlocked here (slot N)`, `CrestOfferState`
    не трогать;
  - проверить `CrestSelectMenu.cs:71-75`: предупреждение остаётся для отладочных путей,
    но для оффера причина пишется отдельным сообщением;
  - приёмка: на сейве со всеми нормальными гребнями после диалога меню не появляется,
    герой управляем, в логе ровно одна строка `offer ended: ...`; на обычном сейве меню
    открывается как раньше.

- [ ] **T7 (S)** — сброс кэша при смене слота
  - в `Plugin.RequestOffer` (или в `Begin`) сравнивать `CurrentSlotId()` с `_slot`;
    несовпадение → полный сброс кэша в `CrestPoolFromSaves`;
  - приёмка: в игре выход в главное меню, загрузка другого слота, повторный диалог —
    в логе новый `saves read slot=...` набор, старые имена не используются; повторный
    диалог в том же слоте не выполняет чтение (`saves cache hit slot=N`).

### Этап 5 — диагностика

- [ ] **T8 (S)** — логи и передача чек-листа `tester`
  - единый префикс `[CREST]`; строки: `saves read slot=`, `saves read done slots=`,
    `saves cache hit slot=`, `pool source=`, `pool fallback reason=`, `offer ended:`;
  - в ответе `tester` перечислить, что осталось ручным: отказ при полном наборе,
    показ чужого пула, `F9` при незавершённом чтении, смена слота;
  - приёмка: `grep` по коду показывает, что каждая строка печатается из одного места,
    маркеры совпадают с разделом 6; новых маркеров вне `[CREST]` нет.

## 6. Стратегия тестов

Чистые функции (`Pure/`) — автотесты, `tester` пишет файл
`CrestPicker.Tests/CrossSavePoolTests.cs` (и при необходимости дополняет
`CrestPoolRulesTests.cs`):

- `Union_DedupesAcrossSlots` — одно имя в двух слотах даёт один кандидат;
- `Build_SubtractsUnlockedHere` — открытое здесь не попадает в пул;
- `Build_KeepsOnlyNormal` — апгрейд (`IsBase=false`) и скрытый гребень из сейва не попадают;
- `Build_PreservesListOrder` — порядок как в `all`;
- `Build_AllNormalsUnlockedHere_ReturnsAllNormalsUnlockedHere` — пустой список + причина;
- `Build_NothingAnywhere_ReturnsAllNormalWithReason`;
- `Build_NothingNewElsewhere_ReturnsAllNormalWithReason` (остаток пуст, но не все нормальные
  открыты здесь);
- `Build_ExcludesEquipped_OnlyWhenAsked` — `includeEquipped=false` вырезает надетый;
- `Build_MissingSlot_IsJustEmpty` — пустая коллекция слота не ломает результат;
- `Build_UnlockedHere_ButNotInAnySave_NotInPool` — «здесь» важнее объединения;
- `IsNormal_HiddenOrUpgrade_IsFalse` (T1).

Команды:

- `dotnet test CrestPicker.Tests`
- `dotnet test CrestPicker.Tests --filter FullyQualifiedName~CrossSavePoolTests`
- `dotnet build -c Release` — обязательна после T3–T7: тестовый проект не видит Unity-слой.

Ручной чек-лист (не покрывается тестами): `Platform.Current.ReadSaveSlot` и его колбэк,
`GetJsonForSaveBytesStatic` с шифрованием, однокадровая обработка слотов, отказ от меню и
поведение FSM `Churchkeeper/Conversation`. Дописывает `tester` в `TESTING.md`.

## 7. Риски

- **Асинхронность чтения вешает оффер.** `ReadSaveSlot` отдаёт колбэк на следующем тике
  (`DesktopPlatform.cs:340-343`), на консолях может быть дольше. Вероятность высокая,
  последствие — меню не открывается или открывается с пустым пулом. Что делаем: свой
  таймаут 2 с внутри `CrestPoolFromSaves`, существующий грейс оффера как верхняя граница,
  фолбэк «все нормальные», лог причины; меню не открывается до `Ready`.
- **Провал кадра на десериализации `SaveGameData`.** Полный `PlayerData` на слот — это
  заметная работа. Вероятность средняя, последствие — рывок при диалоге. Что делаем:
  один слот за кадр (T3), кэш на сессию; если рывок всё равно виден, следующий шаг —
  отдельный поток с запретом на `SaveDataUtility` (он не потокобезопасен) — вне этого плана.
- **`IgnoreUnlockState=true` делает режим мёртвым.** Вероятность высокая (ключ сейчас
  default `true`), последствие — фича не работает, а логи выглядят здоровыми. Что
  делаем: T4 меняет default и приоритет, приёмка проверяет лог `IgnoreUnlockState ignored`.
- **`PlayerData.instance` подменяется при чтении сейва.** По коду
  `DeserializeSaveData` статику не трогает (`SaveDataUtility.cs:114-119`), но если кто-то
  использует `GameManager.SetLoadedGameData`-путь вместо десериализации, текущее
  прохождение будет испорчено. Вероятность низкая, последствие критическое. Что делаем:
  в T3 разрешены только `GetJsonForSaveBytesStatic` + `DeserializeSaveData`; любое
  использование `GameManager.SetLoadedGameData`/`LoadGame` — блокер при ревью.
- **Шифрование сейвов на разных платформах.** `GetJsonForSaveBytes` зависит от
  `gameConfig.useSaveEncryption` и `Platform.Current.IsFileSystemProtected`. Вероятность
  низкая (проверяем на десктопе), последствие — исключение на каждом слоте. Что делаем:
  каждый слот в своём `try/catch`, битый слот пропускается с логом; пул считается по
  остальным.
- **Смена слота внутри сессии.** Кэш объединения по чужим слотам станет неверным, если
  текущий слот сменился (старый «чужой» стал текущим). Вероятность средняя, последствие —
  в пул попадут гребни из текущего прохождения. Что делаем: T7, ключ кэша — id слота.
- **Точки отката.** Каждый этап откатывается отдельно: T1–T2 ни на что не влияют;
  T3 без потребителя; T4–T5 отключаются конфигом `CrestPool=All`; T6–T7 откатываются
  одним коммитом до прежнего `UpdatePendingOffer`. Полный откат фичи — вернуть default
  `CrestPool=All` и `IgnoreUnlockState=true`, файлы можно оставить.
- **Декомпил во временном каталоге.** Ссылки на `ToolItemManager.cs:1512-1515` и другие
  строки декомпила не воспроизведутся после очистки `%TEMP%`. Вероятность средняя,
  последствие — план нельзя перепроверить по строкам. Что делаем: в T1–T2 истина
  фиксируется тестами на правилах, а не на номерах строк декомпила.

## Журнал верификации

Дописывается `tester` после каждой проверки. Статус — строго из enum
`passed | failed | not_run | inconclusive`.

### 2026-11-11 — pending

- статус: `not_run`
- команды и точный вывод: —
- замечания: план составлен по коду на 11.11.2026 (HEAD `af149e0`) и декомпилу игры;
  число тестов до работы — 111 (`TESTING.md:32`).
