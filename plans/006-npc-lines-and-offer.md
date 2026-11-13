---
task: "006 — прислужница говорит фразы и открывает выбор гребня"
created: 2026-11-13
status: draft
verify: pending
---

# 006 — Прислужница говорит фразы и открывает выбор гребня

## 1. Цель

После первого разговора подход Хорнет к `Bonetown/Churchkeeper` даёт цепочку:
молитва (уже есть) → после первого повтора клипа прислужница говорит от одной до трёх
фраз в ванильном диалоговом окне → после последней фразы вызывается `Plugin.RequestOffer()`
и открывается окно выбора гребня. Тексты фраз живут в конфиге мода; пустая строка = фразы нет.

Проверяемо:

- В логе на подход: `[TALK] start lines=<n>` → `[TALK] end` → строка оффера из `Plugin`
  (`[CREST] Offer requested for slot <n>` либо `[CREST] Offer skipped: already used in slot <n>`).
- Окно диалога показывает ровно те фразы, что в конфиге, по одной на страницу.
- После реального выбора гребня в слоте повторный подход: фразы есть, окна нет,
  в логе `[CREST] Offer skipped: already used in slot <n>`.
- Отказ от выбора (окно закрыто без выбора) флаг не тратит: следующий подход снова
  даёт окно (`[CREST] Offer requested for slot <n>`).
- Все три `Line1..Line3` пустые: диалога нет, окно открывается сразу.
- `dotnet build -c Release` и `dotnet test CrestPicker.Tests` зелёные.

## 2. Границы

Не входит:

- Новые ключи локализации игры и правка таблиц (`CM_talk`, `CM_notice`): тексты — в конфиге мода.
- Правка ванильного разговора первого визита и хука `Conversation/End` — остаются как есть.
- Семантика `state.json`: флаг по-прежнему ставится только при реальном выборе
  (`Plugin.cs:440-450`), отказ окно не тратит.
- Новые ключи `OfferGate`/`OfferGateRuntime` и новый флаг «разговаривали»: гейт цепочки
  остаётся один, `PlayerData.churchKeeperIntro` — единственный источник «первый разговор был».
- Изменение окна магазина (`ShopCrestMenu`, `OwnShopWindow`, `ShopCosmetics`) и `CrestSelectMenu`.
- Новые горячие клавиши и новые конфиг-ключи сверх `Line1`, `Line2`, `Line3`.
- Правки `CrestPicker.Tests/*`, `TESTING.md`, `docs/` — зона `tester` (карточка `docs/features/F03.md`
  тоже его).

Разрешённые файлы: `CrestOfferSpeaker.cs` (новый), `Pure/OfferLines.cs` (новый),
`Pure/OfferTalkFlow.cs` (новый), `ChurchkeeperGreeting.cs`, `ChurchkeeperDiag.cs`, `Plugin.cs`.

## 3. Research

### Точка входа и текущее состояние цепочки

- `Plugin.cs:349-384` — `RequestOffer()`: проверки `EnableSnailChoice`, открытого меню,
  слота, `CrestOfferState.IsOffered(slot)`; затем `QueueOpen(fromDialogue: true)`.
  Это готовая точка «после фраз»: дублировать проверки в новом коде не нужно.
- `Plugin.cs:391-396` — `QueueOpen` ставит грейс `OfferGrace = 2f`; `Plugin.cs:399-437` —
  `UpdatePendingOffer` открывает окно только при `GameState.PLAYING`, иначе по истечении
  грейса пишет `[CREST] Offer dropped: game not ready`.
- `Plugin.cs:440-450` — `OnMenuClosed(selected, fromDialogue)`: `MarkOffered(slot)` только
  при реальном выборе из диалогового оффера.
- `CrestOfferState.cs:28-31` — `IsOffered(slot)` читает `BepInEx/config/CrestPicker/state.json`.
- `OfferGateRuntime.cs:38-61` — `ShouldRunChain()` возвращает true только для
  `OfferGateReason.Ok`; `Pure/OfferGate.cs:52-71` — причины `NoSaveCrests | NothingNew |
  IntroNotDone | AlreadyOffered`. Ключ `IgnoreAlreadyOffered` (`Plugin.cs:130-133`) по
  умолчанию `true`, то есть `AlreadyOffered` в гейте не наступает, а однократность окна
  держит сам `RequestOffer`. Значит для F03 гейт менять не нужно: фразы идут по гейту,
  окно — по `RequestOffer`.
- `ChurchkeeperGreeting.cs:361-408` — `CanPray`: выключатель, радиус, чужой диалог,
  открытое меню, `EnableSnailChoice`, `churchKeeperIntro`, `ShouldRunChain()`;
  `:411-435` — `BeginPray`; `:442-475` — коратина `Pray`, повторяет клип, пока `_praying`.
  `:332-336` — `_noticed` сбрасывается при выходе за `GreetExitRadius`; это же место годится
  для сброса «фразы уже были в этом заходе».
- `ChurchkeeperGreeting.cs:339-344` — остановка молитвы идёт через `CanPray` в `Step`;
  чтобы молитва гасла на время фраз, достаточно добавить в `CanPray` условие «идёт наш диалог».
- Плана 005 в коде нет: `Pure/HailPolicy.cs` и `Pure/GreetingText.cs` не заводились,
  волна заменена непрерывной молитвой (`plans/005-npc-greeting.md:378-390`). Искать
  эти файлы при реализации не нужно.

### Диалоговое окно: что подтверждено в декомпиле

Декомпил игры лежит в `%TEMP%\opencode\silksong\dec` (каталог временный, номера строк
не воспроизводятся после очистки; истина — тесты `Pure/` и логи).

- `DialogueBox.cs:165-175` — `StartConversation(string text, NPCControlBase instigator,
  bool overrideContinue, DisplayOptions displayOptions, Action onDialogueEnd = null,
  Action onDialogueCancelled = null)`: без инстанса окна (`:167-169`) вызов молча ничего
  не делает; иначе `ShowShared` + коратина `RunDialogue`.
- `DialogueBox.cs:208-237` — `ShowShared`: `ParseTextForDialogueLines`, затем
  `RunCancelledCallback()` **предыдущего** разговора (`:234`) и `StopAllCoroutines()`
  (`:235`). Отсюда правило: новый разговор не «ждёт», а убивает текущий.
- `DialogueBox.cs:468-558` — `ParseTextForDialogueLines`: реплики режет только по тегам
  `<page>` (NPC) и `<hpage>` (Хорнет); каждая реплика `Trim`, пустые выбрасываются.
  На `<` без закрывающего `>` (`:505-508`) пишет `Debug.LogError` и возвращает
  уже накопленные реплики — то есть хвост текста теряется.
- `DialogueBox.cs:332-342` — `RunDialogue` выходит сразу без `instigator`; при отсутствии
  `textMesh` (`:334-337`) тоже. В этих случаях ни один колбэк не вызывается.
- `DialogueBox.cs:406-418` — после цикла реплик: `OnDialogueBoxEnded()`, `onEnd?.Invoke()`
  (`:410`), затем при `instigator.AutoEnd` — `EndConversation()`.
- `DialogueBox.cs:263-299` — `EndConversation` → `CloseAndEnd` → `instigator.EndDialogue()`.
- `DialogueBox.cs:366-369` — цикл реплик прерывается по `conversationEnded`, но код после
  цикла всё равно исполняется: **`onDialogueEnd` вызывается и при принудительном
  `EndConversation`**, то есть «конец» не означает «все реплики показаны».
- `DialogueBox.cs:149-153` — `OnLeavingScene` (событие `LEAVING SCENE` на HUD): сначала
  `RunCancelledCallback()`, затем `EndConversation(isDialogueRunning)`. Значит на выходе
  из сцены возможны **оба** колбэка, в этом порядке.
- Итог по колбэкам: `onDialogueCancelled` — единственный сигнал «разговор перехвачен/прерван»
  (новый `StartConversation` или уход из сцены); `onDialogueEnd` — «дошли до конца», но также
  и «принудительно закрыли»; ни один не гарантирован при уничтожении окна или `instigator`.
  Отсюда требование: обработчик конца идемпотентен, а зависшее состояние снимается сторожем.

### Почему нельзя брать `PlayMakerNPC` прислужницы как instigator

- `PlayMakerNPC.cs:129-147` — `OnStartDialogue` шлёт `interactEvent` (по умолчанию
  `INTERACT`) в свой `dialogueFsm`; если событие не принято — `EnableInteraction()`,
  `CallEndAction()`, `IsRunningDialogue = false`, то есть диалог обрывается.
  С `instigator`-прислужницей наш текст запустит ванильную ветку `Conversation` поверх.
- `PlayMakerNPC.cs:38-40` — `AutoEnd => false`, `AutoCallEndAction => false`.
- `PlayMakerNPC.cs:44` — `IsRunningDialogue`: дешёвая проверка «ванильный разговор идёт»,
  годится как условие «не начинать наши фразы».
- `BasicNPCBase.cs:57-77` — рабочий образец наследника `NPCControlBase`:
  `OnStartDialogue` → `DisableInteraction()` + `DialogueBox.StartConversation(...)`,
  `OnEndDialogue` → `EnableInteraction()`.
- `NPCControlBase.cs:81-89` — у базы `AutoEnd => true` и `AutoCallEndAction => true`,
  `AllowMovePlayer => true`; `:344-348` — публичный `StartDialogueImmediately()`:
  `SendStartingDialogue()` + `StartDialogue()` без подвода героя.
- `NPCControlBase.cs:356-365` — `StartDialogue`: `HeroTalkAnimation.EnterConversation(this)`
  при `HeroAnimBeginTypes.Auto`, затем `StartedDialogue` и виртуальный `OnStartDialogue`.
  `HeroTalkAnimation.cs:7-16` — `AnimationTypes.LookForward = 0`, то есть дефолтное поле
  не переводит героя в позу разговора; ввод блокируется другим механизмом (ниже).
- `InteractableBase.cs:342-348` — `DisableInteraction()` ставит
  `InteractManager.BlockingInteractable = this` и `HeroController.RelinquishControl()`:
  пока идут наши фразы, герой не может нажать «Speak» — это и есть защита от двойного диалога.
  `:356-368` — `EnableInteraction()` снимает блокировку и возвращает управление.
- `InteractableBase.cs:327-334` — `ShowInteraction` срабатывает только при коллайдере
  героя внутри: объект-говорящий **без коллайдера** не даёт второй подсказки «Speak».
- `InteractManager.cs:90-99` — `BlockingInteractable` читается через Unity-каст `(bool)`,
  поэтому уничтоженный объект сравнивается с `null` и блокировка не залипает навсегда.

### Голос прислужницы

- `NPCSpeakingAudio.cs:86-98` — компонент в `Awake` подписывается на
  `npc.StartedDialogue`, `npc.StartedNewLine`, `npc.EndingDialogue` у `NPCControlBase`
  на том же объекте; `:156-187` — на каждой реплике NPC играет клип из таблицы.
  Без этого компонента на нашем говорящем фразы будут **без голоса**.
- `NPCSpeakingAudio.cs:52-63` — публичное свойство `Table` (геттер/сеттер);
  `:136-154` — публичный `SetTableForSpeaker(string speakerEvent, RandomAudioClipTable table)`.
  Значит таблицу можно скопировать у прислужницы и поставить своему компоненту без рефлексии.
- Имя `CM_talk` в декомпиле не встречается (`grep` по `dec/*.cs` — нет совпадений):
  имя таблицы живёт в ассетах. План берёт таблицу у самой прислужницы, а поиск по имени
  оставляет запасным путём.
- `HeroTalkAnimation.cs:345-359` — `InternalSetTalking` играет `TalkAudioTable` только
  для реплик Хорнет; для реплик NPC голос идёт исключительно через `NPCSpeakingAudio`.

### Диагностика, которая уже есть

- `ChurchkeeperDiag.cs:282-283` — дамп печатает имена компонентов объекта, но не разбирает
  `NPCSpeakingAudio` и его таблицу. T1 добавляет это.
- `ChurchkeeperDiag.cs:23-28` — вход `Dump()`, вешается на `F5` (`Plugin.cs:321-325`).

### Тестовая обвязка

- `CrestPicker.Tests/CrestPicker.Tests.csproj:17` — `Compile Include="..\Pure\*.cs"`:
  новый файл в `Pure/` попадает в тестовый проект автоматически, править csproj не нужно.

## 4. Режим тестирования

`after` — новая логика выносится в `Pure/` (`OfferLines`, `OfferTalkFlow`) и покрывается
тестами `tester` после T2–T3; живое поведение (диалоговое окно, голос, порядок цепочки)
тестом не покрывается и проверяется в игре по чек-листу.

## 5. Задачи

- [ ] **T1 (S)** — `ChurchkeeperDiag.cs`: дамп голосовой таблицы прислужницы
  - в дамп компонентов объекта добавить разбор `NPCSpeakingAudio` на объекте прислужницы:
    строка `[NPC-TALK] npcspeakingaudio='<path>' table='<name>'`; таблицу брать
    через публичное свойство `Table` в `try/catch` (геттер при пустом списке
    спикеров пишет `Debug.LogError`);
  - отдельной строкой `[NPC-TALK] CM_talk found='<name>'|none` — поиск
    `RandomAudioClipTable` по имени `CM_talk` через `Resources.FindObjectsOfTypeAll`;
  - только чтение, ничего не меняет;
  - Тест: `none`.
  - Приёмка: `dotnet build -c Release` зелёный; в Bonetown `F5` даёт строку `[NPC-TALK]`
    с именем таблицы (или явное `table=none`) и результат поиска `CM_talk`.

- [ ] **T2 (S)** — `Pure/OfferLines.cs`: сборка текста диалога из строк конфига
  - `internal static string Compose(IReadOnlyList<string?> lines)`: `Trim` каждой строки,
    пустые и `null` выбрасываются, строки склеиваются через `<page>`;
    строка, содержащая `<` или `>`, выбрасывается (иначе `ParseTextForDialogueLines`
    обрежет диалог на теге); пустой результат — `string.Empty`;
  - без `using UnityEngine`, без игровых типов;
  - Тест: `after` (тесты пишет `tester`).
  - Приёмка: файл без `using UnityEngine`; `dotnet build` зелёный; поведение покрыто
    тестами раздела 6.

- [ ] **T3 (S)** — `Pure/OfferTalkFlow.cs`: состояние «фразы идут»
  - `internal sealed class OfferTalkFlow` с `Talking`, `Finished` и методами
    `Begin()`, `End()`, `Abort()`, `Rearm()`;
  - правила: `Begin()` → true только когда `!Talking && !Finished`; `End()` → true только
    на первом вызове после `Begin()` (второй вызов из второго колбэка возвращает false);
    `End()` без `Begin()` → false; `Abort()` гасит `Talking` и ставит `Finished`;
    `Rearm()` возвращает в исходное состояние;
  - Тест: `after`.
  - Приёмка: файл без `using UnityEngine`; `dotnet build` зелёный; двойной колбэк
    «end + cancelled» не даёт двух действий (тест раздела 6).

- [ ] **T4 (M)** — `CrestOfferSpeaker.cs`: говорящий на дочернем объекте прислужницы
  - `internal sealed class CrestOfferSpeaker : NPCControlBase` — наследник без логики
    интерфейса: `AllowMovePlayer => false`; на объекте **нет** коллайдера и триггеров;
  - `internal static CrestOfferSpeaker? Attach(GameObject host)`: создать дочерний объект
    `CrestPicker Speaker` (позиция хоста), `AddComponent<CrestOfferSpeaker>()`, затем
    `AddComponent<NPCSpeakingAudio>()` (порядок важен: `NPCSpeakingAudio.Awake` ищет
    `NPCControlBase` на том же объекте), затем таблицу: `NPCSpeakingAudio` прислужницы
    через `GetComponent<NPCSpeakingAudio>()` → `.Table`; если таблицы нет —
    поиск по имени `CM_talk`; если нет и её — лог `[TALK] voice missing`, фразы без голоса;
    таблицу ставить через `SetTableForSpeaker(null, table)`;
  - `internal bool Say(string text, Action onFinished)`:
    - пустой `text` → `[TALK] skipped: empty text`, вернуть false (диалог не начинать);
    - иначе `StartDialogueImmediately()` и вернуть true;
  - `OnStartDialogue()`: `DisableInteraction()`, затем
    `DialogueBox.StartConversation(text, this, false, DialogueBox.DisplayOptions.Default,
    onDialogueEnd: Finish, onDialogueCancelled: Finish)`; лог `[TALK] start lines=<n>`
    (число реплик — по `<page>` + 1) и `[TALK] voice table='<name>'`;
    если инстанса окна нет — `StartConversation` молча ничего не делает, поэтому после
    вызова проверить, что диалог реально пошёл (см. сторож в T5) и не залипнуть;
  - `OnEndDialogue()`: `EnableInteraction()`;
  - `Finish()`: `[TALK] end` / `[TALK] cancelled` по источнику, затем `onFinished?.Invoke()`
    (ровно один раз за `Say` — свой локальный флаг, независимо от `OfferTalkFlow`);
  - `OnDestroy()`: если диалог ещё шёл — `EnableInteraction()` под проверкой
    `HeroController.instance != null` (иначе герой остаётся без управления);
  - Тест: `none` (живой слой).
  - Приёмка: `dotnet build -c Release` зелёный; в игре фразы видны в ванильном окне,
    у прислужницы слышен её голос, второй подсказки «Speak» нет, после последней фразы
    в логе `[TALK] end`.

- [ ] **T5 (M)** — `ChurchkeeperGreeting.cs`: фразы в цепочке
  - поле `private readonly OfferTalkFlow _flow = new();` и `private CrestOfferSpeaker? _speaker;`
  - `Pray` (`:442-475`): после первого повтора клипа (`_loopCount == 1`) вызвать `TryTalk()`;
  - `TryTalk()`: `_speaker ??= CrestOfferSpeaker.Attach(gameObject)`; если
    `!_flow.Begin()` — выход; текст `OfferLines.Compose(new[] { Plugin.OfferLine1,
    Plugin.OfferLine2, Plugin.OfferLine3 })`; `_speaker.Say(text, OnTalkFinished)`;
    если `Say` вернул false — `_flow.End()` и сразу `Plugin.RequestOffer()`;
  - `OnTalkFinished()`: `if (_flow.End()) Plugin.RequestOffer();` — единственное место,
    где решается «фразы кончились, открываем окно»; повторный колбэк гасится `OfferTalkFlow`;
  - `CanPray` (`:361-408`): добавить `_flow.Talking` → причина `talk`, чтобы молитва
    гасла на время фраз; `npc.IsRunningDialogue` оставить как есть;
  - `Step` (`:321-358`): в месте сброса `_noticed` (выход за `GreetExitRadius`)
    добавить `_flow.Rearm()` — фразы снова возможны на следующем заходе;
  - сторож в `Update`: если `_flow.Talking` и (`_speaker == null` или
    `_speaker.gameObject == null`) — `_flow.Abort()`, лог `[TALK] aborted: speaker gone`;
    если `_flow.Talking` дольше 60 с — `_flow.Abort()` и лог `[TALK] aborted: timeout`
    (окно диалога уничтожено, колбэков не будет);
  - Тест: `none`.
  - Приёмка: в игре подход после первого разговора даёт «молитва → фразы → окно»;
    молитва на время фраз останавливается (`[GREET] pray stopped (reason=talk)`);
    после закрытия окна молитва возобновляется, фразы в этом заходе не повторяются;
    после отхода за `GreetExitRadius` и нового подхода фразы снова есть.

- [ ] **T6 (S)** — `Plugin.cs`: тексты фраз в конфиге
  - три ключа `Config.Bind("General", "Line1" | "Line2" | "Line3", <английский дефолт>, "...")`
    с пояснением «одна фраза прислужницы; пусто — фразы нет»;
  - `internal static ConfigEntry<string> OfferLine1/2/3` — читаются на лету;
  - дефолты (английские, по одной строке): `"You carry more than one crest, don't you?"`,
    `"The others remember what this shell has forgotten."`, `"Take one. Choose well."`;
  - новых ключей сверх этих трёх нет;
  - Тест: `none`.
  - Приёмка: `dotnet build -c Release` зелёный; в
    `BepInEx/config/io.github.triojohn.crestpicker.cfg` появились `Line1..Line3`;
    при пустом значении соответствующая фраза не показывается.

- [ ] **T7 (S)** — маркеры логов и передача чек-листа `tester`
  - префикс `[TALK]` для всего нового поведения, `[NPC-TALK]` — только диагностика T1;
  - строки: `start lines=`, `voice table=`, `voice missing`, `end`, `cancelled`,
    `skipped: empty text`, `aborted: speaker gone`, `aborted: timeout`;
  - в отчёте перечислить, что осталось ручным: порядок «молитва → фразы → окно»,
    голос, отсутствие второго «Speak», поведение при уходе из сцены во время фраз,
    повторный подход после выбора и после отказа;
  - Тест: `none`.
  - Приёмка: каждая строка печатается из одного места; новых префиксов вне
    `[TALK]`/`[NPC-TALK]` нет.

## 6. Стратегия тестов

Автотесты пишет `tester`:

- `CrestPicker.Tests/OfferLinesTests.cs`:
  - `Compose_KeepsOrderAndTrims`;
  - `Compose_DropsBlankAndNull`;
  - `Compose_JoinsWithPageTag` (ровно `<page>` между фразами);
  - `Compose_AllEmpty_IsEmpty`;
  - `Compose_DropsLineWithAngleBracket`;
  - `Compose_SingleLine_HasNoPageTag`.
- `CrestPicker.Tests/OfferTalkFlowTests.cs`:
  - `Begin_WhenIdle_IsTrue`;
  - `Begin_Twice_SecondIsFalse`;
  - `End_AfterBegin_FirstIsTrue_SecondIsFalse` (двойной колбэк end+cancelled);
  - `End_WithoutBegin_IsFalse`;
  - `Begin_AfterFinished_IsFalse`;
  - `Rearm_AfterFinished_AllowsBegin`;
  - `Abort_StopsTalking_AndBlocksUntilRearm`.

Команды:

- `dotnet test CrestPicker.Tests`
- `dotnet test CrestPicker.Tests --filter FullyQualifiedName~OfferLinesTests`
- `dotnet test CrestPicker.Tests --filter FullyQualifiedName~OfferTalkFlowTests`
- `dotnet build -c Release` — обязательна после T4–T6: тестовый проект не видит Unity-слой.

Ручной чек-лист (в `TESTING.md` пишет `tester`):

1. После первого разговора подойти: молитва → фразы → окно выбора.
2. Фразы соответствуют конфигу; правка `Line2` в cfg подхватывается без перезапуска.
3. Все три строки пустые: диалога нет, окно открывается сразу.
4. Голос прислужницы слышен на каждой фразе; при отсутствии таблицы — фразы без голоса
   и одна строка `[TALK] voice missing`.
5. Во время фраз кнопка взаимодействия не открывает ванильный разговор (нет второго окна).
6. Выйти из сцены во время фраз: окно выбора не открывается задним числом
   (`[CREST] Offer dropped: game not ready` или отсутствие строки оффера).
7. Выбрать гребень → отойти → подойти: фразы есть, окна нет
   (`[CREST] Offer skipped: already used in slot <n>`).
8. Закрыть окно без выбора → отойти → подойти: окно снова открывается.
9. Второй подход без отхода (стоим рядом): фразы не повторяются, окно не переоткрывается.

## 7. Риски

- **Двойной диалог: игрок жмёт «Speak», пока идут наши фразы.** Ванильный `PlayMakerNPC`
  начнёт свой разговор, `ShowShared` убьёт наш (`DialogueBox.cs:234-235`), наш `onEnd`
  не вызовется. Вероятность средняя, последствие — окно не открывается, в логе
  `[TALK] cancelled`. Что делаем: `DisableInteraction()` в `OnStartDialogue` блокирует
  взаимодействия и забирает управление героя (`InteractableBase.cs:342-348`);
  `onDialogueCancelled` → `Finish` → `OfferTalkFlow.End` (ровно один раз);
  перед началом фраз — проверка `npc.IsRunningDialogue`.
- **`onDialogueEnd` не приходит.** Не вызывается при перехвате окна, при уничтожении
  `DialogueBox`/`instigator` (`DialogueBox.cs:334-342`), а при уходе из сцены приходят оба
  колбэка (`:149-153`). Вероятность средняя, последствие — окно не открывается или
  открывается дважды. Что делаем: оба колбэка ведут в один идемпотентный `Finish`,
  `OfferTalkFlow.End` возвращает true только один раз, зависание снимает сторож
  `[TALK] aborted: timeout` (60 с) и сброс при смене сцены (компонент живёт на объекте
  прислужницы и пересоздаётся со сценой).
- **`onDialogueEnd` приходит при принудительном закрытии.** `EndConversation` прерывает
  цикл реплик, но `onEnd` всё равно вызывается (`DialogueBox.cs:366-369`, `:410`).
  Вероятность низкая, последствие — оффер запрошен, когда игрок уже ушёл.
  Что делаем: `RequestOffer` → `QueueOpen` с грейсом 2 с и проверкой `GameState.PLAYING`
  (`Plugin.cs:399-437`), окно просто не откроется и в логе будет `Offer dropped`.
- **Фразы без голоса.** `NPCSpeakingAudio` — отдельный компонент со своей таблицей;
  у нашего говорящего его нет по умолчанию. Вероятность высокая без T4, последствие —
  молчаливые фразы. Что делаем: T4 добавляет компонент и копирует таблицу прислужницы
  через публичное `Table`/`SetTableForSpeaker`; T1 подтверждает, что таблица у неё есть;
  при отсутствии таблицы фича работает без голоса и пишет `voice missing`.
- **Таблица `CM_talk` не находится по имени.** Имя в декомпиле не встречается
  (`grep` пуст), оно в ассетах. Вероятность средняя для поиска по имени, последствие —
  нет голоса. Что делаем: основной путь — таблица с объекта прислужницы, поиск по имени
  только запасной; T1 печатает оба результата.
- **Текст с `<` в конфиге обрезает диалог.** `ParseTextForDialogueLines` на незакрытом
  теге пишет ошибку и возвращает накопленное (`DialogueBox.cs:505-508`).
  Вероятность низкая, последствие — часть фраз пропадает. Что делаем: `OfferLines.Compose`
  выбрасывает строки с `<`/`>`; это покрыто тестом.
- **Зависший `Talking` блокирует цепочку.** Если колбэков не будет, `CanPray` навсегда
  вернёт `talk`, молитва и окно пропадут. Вероятность низкая, последствие — мёртвая фича
  до перезахода. Что делаем: сторож по времени и по пропаже говорящего, `Abort()`,
  плюс `Rearm()` при выходе за `GreetExitRadius`.
- **Герой остаётся без управления.** `DisableInteraction` делает `RelinquishControl`,
  возврат — только `EnableInteraction`. Вероятность низкая (уход из сцены во время фраз),
  последствие — неуправляемый герой. Что делаем: `EnableInteraction()` в `OnEndDialogue`
  и в `OnDestroy` под проверкой `HeroController.instance != null`; при загрузке сцены
  управление восстанавливает сама игра.
- **Вторая подсказка «Speak» у прислужницы.** Появится, если у говорящего есть коллайдер
  и он попадает в `InteractManager` (`InteractableBase.cs:327-334`). Вероятность низкая,
  последствие — два взаимодействия на одну кнопку. Что делаем: говорящий — дочерний
  объект без коллайдеров, проверка в ручном чек-листе (пункт 5).
- **Кириллица в тексте фраз.** Шрифт окна зависит от языка игры; русский текст на
  английской локализации может дать пустые глифы (см. `plans/005-npc-greeting.md:285-288`).
  Вероятность средняя, последствие — нечитаемый текст. Что делаем: дефолты латиницей,
  проверка кириллицы в конфиге — пункт ручного чек-листа.
- **Номера строк декомпила.** `%TEMP%\opencode\silksong\dec` — временный каталог
  (`plans/005-npc-greeting.md:304-307`). Вероятность средняя, последствие — план нельзя
  перепроверить по строкам. Что делаем: поведение фиксируется тестами `Pure/` и логами
  `[TALK]`, а не номерами строк.
- **Точки отката.** T1–T3 ни на что не влияют; T4–T6 отключаются пустыми `Line1..Line3`
  (диалога нет) и `GreetEnabled=false` (вся цепочка стоит); полный откат — убрать
  `TryTalk()` из `Pray` и `CrestOfferSpeaker.Attach`. Ванильный разговор и окно выбора
  не меняются.

## Журнал верификации

Дописывается `tester` после каждой проверки. Статус — строго из enum
`passed | failed | not_run | inconclusive`.

### 2026-11-13 — pending

- статус: `not_run`
- команды и точный вывод: —
- замечания: план составлен по коду репозитория и декомпилу игры на 13.11.2026;
  наличие `NPCSpeakingAudio` и его таблицы у прислужницы из кода не видно — даёт T1.

### 2026-11-13 — T2, T3: автотесты `Pure/OfferLines`, `Pure/OfferTalkFlow`

- статус: `passed`
- команды и точный вывод:
  - `dotnet test CrestPicker.Tests -c Release` до правки тестов → `Пройден! : не пройдено 0, пройдено 126, всего 126`
  - `dotnet test CrestPicker.Tests -c Release --filter "FullyQualifiedName~OfferLinesTests|FullyQualifiedName~OfferTalkFlowTests"` → `Пройден! : не пройдено 0, пройдено 13, всего 13`
  - `dotnet test CrestPicker.Tests -c Release` после → `Пройден! : не пройдено 0, пройдено 139, всего 139`
- тесты: `CrestPicker.Tests/OfferLinesTests.cs` (6), `CrestPicker.Tests/OfferTalkFlowTests.cs` (7)
- замечания: режим `after` — реализация T2/T3 в `ccc972a` уже в HEAD, поэтому падение
  «до фикса» не воспроизводимо без правки продуктового кода; тесты написаны по разделу 6
  и по поведению из T2/T3. `TESTING.md` обновлён: таблица, счётчик 139, чек-лист цепочки
  «молитва → фразы → окно». Коммит и push не выполнены: `git add` и `git commit` отклонены
  правами (`Permission denied: shell`), нужен коммит от оркестратора.

