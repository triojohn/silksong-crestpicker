---
task: "005 — прислужница окликает и машет при подходе"
created: 2026-11-13
status: draft
verify: pending
---

# 005 — Прислужница окликает и машет при подходе

## 1. Цель

После первого разговора с `Bonetown/Churchkeeper` повторный подход Хорнет к прислужнице
даёт: волну и оклик от NPC, затем несколько реплик в диалоговом окне, затем (если гребень
ещё не выбран в этом слоте) открывается окно выбора гребня. Волна и реплики — каждый
подход с кулдауном; окно — только пока выбор не сделан.

Проверяемо:

- Первый подход до разговора: ничего не происходит, окно не открывается.
- После первого разговора и отхода: подход в радиусе даёт в логе
  `[GREET] hail via=fsm|clip name=<...>`, NPC машет, затем
  `[GREET] talk lines=<n>` и окно диалога с фразами, затем окно выбора гребня.
- Повторный подход в том же слоте после выбора гребня: волна и фразы есть,
  окна выбора нет (лог `[POOL] offer skipped: already used in slot N`).
- `dotnet build -c Release` и `dotnet test CrestPicker.Tests` зелёные.

## 2. Границы

Не входит:

- Новые реплики в таблицы локализации игры: текст живёт в конфиге мода.
- Изменение ванильного разговора первого визита: хук `Conversation/End` остаётся как есть.
- Изменение семантики флага `state.json`: он по-прежнему ставится только при реальном
  выборе гребня (`Plugin.cs:355-365`), отказ от выбора даёт повторное окно на следующем
  подходе (так описано в `docs/features/F01.md:12`).
- Открытие окна в обход `Plugin.RequestOffer` (пул, чтение сейвов, `CrestSelectMenu`).
- Правки `CrestPicker.Tests/*`, `TESTING.md`, `docs/` — зона `tester`.
- Новые горячие клавиши сверх одной диагностической (`F6`), новых конфиг-ключей сверх
  `GreetingText`.

Разрешённые файлы: `ChurchkeeperDiag.cs` (новый), `ChurchkeeperGreeting.cs` (новый),
`Pure/HailPolicy.cs` (новый), `Pure/GreetingText.cs` (новый), `ChurchkeeperPatch.cs`
(только вынос матчера), `Plugin.cs`.

## 3. Research

### Подход к NPC и что уже есть в репозитории

- `ChurchkeeperPatch.cs:25-61` — хук уже стоит: `PlayMakerFSM.Start` →
  `OnFsmStarted` вставляет `OfferCrestAction` в голову состояния `End` FSM
  `Churchkeeper/Conversation`. Это единственная точка, где мод сейчас знает про NPC.
- `ChurchkeeperPatch.cs:63-72` — матчер `IsChurchkeeperConversation`: сцена `Bonetown`,
  `go.name == "Churchkeeper"`, `FsmName == "Conversation"`. Для оклика нужен более общий
  матчер «это объект прислужницы, FSM любой» — вынести в `IsChurchkeeper(PlayMakerFSM)`.
- `Plugin.cs:264-299` — `RequestOffer`: проверки `EnableSnailChoice`, открытого меню,
  слота, `CrestOfferState.IsOffered`, затем `QueueOpen(fromDialogue: true)`.
  Это готовая точка входа для окна после фраз, менять её не нужно.
- `Plugin.cs:388-392` — `IsInGame()` (`GameState.PLAYING`), `Plugin.cs:214-258` — `Update`,
  куда вешается `Tick()` приветствия.
- `CrestOfferState.cs:28-50`, `Pure/OfferStore.cs:96-117` — флаг «уже предлагали» по слоту
  в `BepInEx/config/CrestPicker/state.json`, ключ — номер слота.

### Как игра сама водит диалог

- `NPCControlBase.cs:298-310` — `StartDialogueMove()`: `SendStartingDialogue()`, затем либо
  коратина `MovePlayer`, либо сразу `StartDialogue()`. `:344-348` —
  `StartDialogueImmediately()` — то же без подвода героя. Оба публичные.
- `NPCControlBase.cs:356-365` — `StartDialogue()` поднимает `HeroTalkAnimation.EnterConversation(this)`
  (герой встаёт в позу разговора), затем `StartedDialogue` и виртуальный `OnStartDialogue()`.
- `NPCControlBase.cs:81-83` — `AutoEnd => true`, `AutoCallEndAction => true` у базы;
  `:397-414` — `EndDialogue()` → `CallEndAction()` → `HeroTalkAnimation.ExitConversation()`,
  `EndedDialogue?.Invoke()`, `GameManager.DoQueuedSaveGame()`.
- `NPCControlBase.cs:188-200` — публичные события `StartingDialogue`, `StartedDialogue`,
  `OpeningDialogueBox`, `StartedNewLine`, `LineEnded`, `EndingDialogue`, `EndedDialogue`.
  `EndedDialogue` — штатный сигнал «диалог закончился», без хаков.
- `BasicNPCBase.cs:57-71` — образец наследника: диалог запускается в `OnStartDialogue()`
  через `DialogueBox.StartConversation(dialogue, this, ...)`.
- `PlayMakerNPC.cs:38-40` — у `PlayMakerNPC` `AutoEnd => false`, `AutoCallEndAction => false`,
  а `:129-147` `OnStartDialogue()` шлёт `interactEvent` (по умолчанию `INTERACT`) в свой
  `dialogueFsm`. Значит прислужницу **нельзя** использовать как instigator напрямую:
  это запустит ванильную ветку разговора.
- `PlayMakerNPC.cs:44` — публичное `IsRunningDialogue` — дешёвая проверка «ванильный
  диалог уже идёт».
- `PlayMakerNPC.cs:6-10` — константы `CONVO_END`, `LINE_END`, `CONVO_END_FORCED`.

### Диалоговое окно и фразы

- `DialogueBox.cs:165-175` — `StartConversation(string text, NPCControlBase instigator, bool overrideContinue, DisplayOptions, Action onDialogueEnd = null, Action onDialogueCancelled = null)`:
  текст режется на строки, запускается коратина, `instigator` обязателен (`:338-342` —
  без него коратина выходит сразу).
- `DialogueBox.cs:468` — `ParseTextForDialogueLines` режет текст на реплики; разделитель
  смотреть в реализации (используется ванильными строками), поэтому в моде проще подавать
  готовые строки по одной реплике.
- `DialogueBox.cs:410` — `onEnd?.Invoke()` после последней реплики; `:415-418` — при
  `instigator.AutoEnd` окно закрывается само через `EndConversation()`.
- `DialogueBox.cs:263-299` — `EndConversation()` → `CloseAndEnd` → `instigator.EndDialogue()`
  (то есть `EndedDialogue` всё равно поднимется).
- `DialogueBox.cs:13-25` — `DialogueLine { IsPlayer, Text, Event }`; `IsPlayer=false` —
  реплика NPC, `IsPlayer=true` — реплика Хорнет (для «оклика» хватит реплики NPC).
- `DialogueBox.cs:167-169` — при отсутствии инстанса окна вызов молча ничего не делает.
- Локализация: `Language.Get(key, sheet)` используется в игре повсеместно
  (`AchievementListener.cs:37`), но класс `Language`/`LocalisedString` лежит вне
  декомпилированного `Assembly-CSharp` — API известен только по вызовам
  (`LocalisedString.Sheet/.Key/.ToString(bool)`, `BasicNPCBase.cs:60-61`). Заводить
  собственные ключи в таблицы игры — вне задачи, поэтому текст берём строкой из конфига.

### Волна и оклик

- `TriggerEnterEvent.cs:230-290` — детектор входа: фильтр по `checkLayer`, тегам,
  затем `fsmTarget.SendEvent(fsmEvent)`. Слой героя — 9 (`InteractableBase.cs:283`,
  `HeroPlatformStick.cs:151`).
- `InteractableBase.cs:135-166` — `EnterDetector` публичный, `TriggerEnterEvent.OnTriggerEntered`
  публичное событие; `:227-233` — `Awake` подписывает детекторы.
- `InteractableBase.cs:305-317`, `:332` — `ShowInteraction()` вызывает
  `InteractManager.AddInteractible(this, ...)`: любой лишний `NPCControlBase` на объекте
  с коллайдером даст вторую подсказку «Speak». Отсюда требование к T4: instigator живёт
  на отдельном дочернем объекте **без коллайдера**.
- `Extensions.cs:170-190` — `PlayAnimWait(this tk2dSpriteAnimator, string)` =
  `GetClipByName(anim)` + `Play(clip)`; `GrubBGControl.cs:70` —
  `StartCoroutine(anim.PlayAnimWait("Home Wave"))` как готовый приём «проиграть клип по имени».
- Имена клипов и состояние `Keeper Hail` живут в ассетах сцены `Bonetown`, в декомпиле их
  нет: единственный источник — рантайм-дамп (T1). Кандидаты для проверки: клипы `Wave`,
  `Hail`, `Home Wave`; FSM `Churchkeeper Intro Scene`, состояние `Keeper Hail`.
- `BasicNPCBase.cs:37`, `DamageHero.cs:147` — `fsm.IsEventValid(name, isRequired: false)`:
  штатная проверка «такое событие у FSM есть», без исключений и логов PlayMaker.
- `Probe.cs:140-205` и `ShopDiag.cs:150-192` — готовый образец дампа: перебор
  `Resources.FindObjectsOfTypeAll<PlayMakerFSM>()` с фильтром по сцене, печать
  `from -> event -> to` и сводки имён событий. T1 переиспользует этот приём.

### Что уже подтверждено по флагу «первый разговор состоялся»

- `PlayerData.cs:997` — поле `churchKeeperIntro`; `:999` `churchKeeperCursedConvo`,
  `:1031` `ChurchKeeperLeftBasement`. Кандидат на «уже разговаривали», но что именно его
  ставит — из декомпила не видно (ставит FSM сцены). Проверяется T1.

## 4. Режим тестирования

`after` — решения «махать/не махать» и разбор текста выносятся в `Pure/`, тесты пишет
`tester` после T2–T3; живое поведение (клип, событие FSM, диалоговое окно) тестом
не покрывается и проверяется в игре по чек-листу.

## 5. Задачи

- [ ] **T1 (S)** — `ChurchkeeperDiag.cs`: рантайм-дамп прислужницы по `F6`
  - по образцу `ShopDiag.cs:150-192`: перебрать `PlayMakerFSM` сцены `Bonetown`, для
    объектов, чьё имя содержит `Churchkeeper`, напечатать `fsmName`, `ActiveStateName`,
    все переходы `state -> event -> to` и сводку имён событий;
  - напечатать компоненты-аниматоры объекта (`tk2dSpriteAnimator`, `Animator`) и для
    `tk2dSpriteAnimator` — имена клипов из `Library.clips`; если библиотека недоступна,
    проверить `GetClipByName` для кандидатов `Wave`, `Hail`, `Home Wave`, `Idle`;
  - напечатать `TriggerEnterEvent` на объекте: `checkLayer`, `checkTags`, `fsmEvent`;
  - напечатать `PlayerData.instance.churchKeeperIntro` и `GameManager.instance.profileID`;
  - в `OfferCrestAction.OnEnter` (`ChurchkeeperPatch.cs:92`) добавить одну строку лога
    `[GREET-DUMP] churchKeeperIntro=<v> at Conversation/End` — до и после первого разговора;
  - маркер `[GREET-DUMP]`, только чтение, ничего не меняет;
  - приёмка: `dotnet build -c Release` зелёный; в игре в Bonetown `F6` даёт в логе имя FSM
    с состоянием `Keeper Hail` (или явный вывод, что такого состояния нет), список клипов
    аниматора и значение `churchKeeperIntro`; строки `[GREET-DUMP]` появляются при
    завершении ванильного разговора.

- [ ] **T2 (S)** — `Pure/HailPolicy.cs`: чистое решение о волне
  - `internal static bool ShouldHail(float now, float lastHailAt, float distance,
    float nearDistance, float cooldown, bool armed, bool canTalk)`;
  - `internal static bool Rearm(float distance, float rearmDistance)`;
  - правила: `!canTalk` → false; `!armed` → false; `now - lastHailAt < cooldown` → false
    (`lastHailAt < 0` — ещё не махали, разрешено); `distance > nearDistance` → false;
  - `Rearm` — true, когда `distance > rearmDistance` (гистерезис: отошёл — снова можно);
  - константы по умолчанию: `nearDistance = 6f`, `rearmDistance = 8f`, `cooldown = 2f`;
  - приёмка: файл без `using UnityEngine`; `dotnet build` зелёный; поведение покрыто
    тестами T2 из раздела 6 (пишет `tester`).

- [ ] **T3 (S)** — `Pure/GreetingText.cs`: текст фраз из конфига
  - `internal static string[] Split(string? raw)`: режет по `\n` и `\r`, обрезает пробелы,
    выбрасывает пустые строки, возвращает пустой массив для `null`/пробелов;
  - приёмка: `dotnet build` зелёный; пустой текст не даёт ни одной реплики
    (тест T3 из раздела 6).

- [ ] **T4 (M)** — `ChurchkeeperGreeting.cs`: оклик, фразы, окно
  - регистрация из уже существующего `PlayMakerFsmStartPatch`
    (`ChurchkeeperPatch.cs:76-83`): если объект — прислужница (`IsChurchkeeper`, вынести
    из `:63-72`), запомнить `PlayMakerNPC`, FSM и позицию; повторный `Start` того же
    объекта не должен плодить подписки;
  - `Tick()` из `Plugin.Update`: сброс ссылок, если объект уничтожен (смена сцены);
    `armed` сбрасывается при загрузке сцены и ставится по `HailPolicy.Rearm`; волна
    вызывается по `HailPolicy.ShouldHail`, где
    `canTalk = IsInGame() && !menuOpen && !npc.IsRunningDialogue && !_talking && TalkedBefore()`;
  - `TalkedBefore()` — `PlayerData.instance.churchKeeperIntro` (единственное место чтения
    флага, чтобы T6 менял одну строку);
  - волна: `TryHail()` — сначала FSM: найти FSM, у которого `IsEventValid(hailEvent, false)`,
    и отправить событие; иначе клип: `tk2dSpriteAnimator.GetClipByName(name)` по списку
    кандидатов из T1 и `Play(clip)`; успешный путь логируется
    `[GREET] hail via=fsm name=<...>` / `[GREET] hail via=clip name=<...>`, неуспех —
    `[GREET] hail unavailable: <reason>`; имена события и клипов — константы файла,
    заполняются по результату T1;
  - через `WaveTalkDelay` (0.6 с) — фразы: `GreetingSpeaker` (вложенный наследник
    `NPCControlBase`) на дочернем объекте `CrestGreetingSpeaker` без коллайдера,
    `AllowMovePlayer => false`, в `OnStartDialogue()` —
    `DialogueBox.StartConversation(text, this, false, DialogueBox.DisplayOptions.Default)`;
    каждая строка из `GreetingText.Split(Plugin.GreetingText)` уходит отдельным вызовом,
    следующая — из обработчика `EndedDialogue`; после последней — `Plugin.RequestOffer()`;
  - защита от повторного входа: `_talking` ставится перед первой репликой и снимается
    в `EndedDialogue` последней; при `DialogueBox` без инстанса реплики не начнутся —
    писать `[GREET] dialogue box missing` и открывать окно выбора сразу, чтобы фича
    не терялась;
  - приёмка: в игре после первого разговора подход даёт волну, затем фразы, затем окно
    выбора; в логе ровно одна строка `hail via=` на подход, `talk lines=<n>` и следом
    строка оффера из `Plugin`.

- [ ] **T5 (S)** — `Plugin.cs`: конфиг и тик
  - `Config.Bind("General", "GreetingText", <2–3 фразы по умолчанию>, "...")` — текст
    реплик, по строке на реплику; читается на лету;
  - `internal static string GreetingText` — текущее значение (обновлять в `Update` или
    читать `_greetingText.Value` в `ChurchkeeperGreeting`);
  - в `Update` (`Plugin.cs:214-258`) — `ChurchkeeperGreeting.Tick()` рядом с
    `InventoryDiag.Poll()`;
  - гейт `EnableSnailChoice`: при выключенном ключе волна и фразы тоже не играют;
  - приёмка: `dotnet build -c Release` зелёный; в `BepInEx/config/...cfg` появился ключ
    `GreetingText`; при пустом значении волна есть, фраз нет, окно открывается сразу.

- [ ] **T6 (S, условная — только если T1 покажет, что `churchKeeperIntro` ставится не
  концом разговора)** — свой флаг «разговаривали» в `state.json`
  - `Pure/OfferStore.cs`: `IsTalked(slots, slot)` / `MarkTalked(slots, slot)` с ключом
    `talked:<slot>`, чтобы не ломать существующий формат `{"3": true}`;
  - `CrestOfferState` — обёртки, вызов `MarkTalked` из `OfferCrestAction.OnEnter`;
  - `ChurchkeeperGreeting.TalkedBefore()` переключается на новый флаг;
  - приёмка: на чистом слоте волны нет до первого разговора; после разговора и перезахода
    в игру волна есть (флаг переживает перезапуск).

- [ ] **T7 (S)** — маркеры логов и передача чек-листа `tester`
  - префиксы: `[GREET]` для поведения, `[GREET-DUMP]` для диагностики;
  - строки: `hail via=`, `hail unavailable:`, `talk lines=`, `dialogue box missing`,
    `greeting armed/disarmed`;
  - в отчёте перечислить, что осталось ручным: имена события/клипа из T1, волна, порядок
    «волна → фразы → окно», повторный подход после выбора, отсутствие второй подсказки
    «Speak» у NPC;
  - приёмка: каждая строка печатается из одного места, новых префиксов вне `[GREET]` нет.

## 6. Стратегия тестов

Чистые функции — автотесты, `tester` пишет `CrestPicker.Tests/HailPolicyTests.cs` и
`CrestPicker.Tests/GreetingTextTests.cs`:

- `ShouldHail_NotArmed_IsFalse`;
- `ShouldHail_WithinCooldown_IsFalse`, `ShouldHail_AfterCooldown_IsTrue`;
- `ShouldHail_FarAway_IsFalse`, `ShouldHail_AtBoundary_IsTrue` (ровно `nearDistance`);
- `ShouldHail_CanTalkFalse_IsFalse` (диалог идёт / меню открыто);
- `ShouldHail_NeverHailedBefore_IsTrue` (`lastHailAt < 0`);
- `Rearm_BeyondRearmDistance_IsTrue`, `Rearm_BetweenNearAndRearm_IsFalse` (гистерезис);
- `Split_KeepsOrderAndTrims`, `Split_DropsBlankLines`, `Split_Null_IsEmpty`,
  `Split_CrLf_IsOneLinePerPhrase`;
- `Split_EmptyString_IsEmpty`.

Команды:

- `dotnet test CrestPicker.Tests`
- `dotnet test CrestPicker.Tests --filter FullyQualifiedName~HailPolicyTests`
- `dotnet build -c Release` — обязательна после T4–T6: тестовый проект не видит Unity-слой.

Ручной чек-лист (в `TESTING.md` пишет `tester`): волна и её клип, оклик, порядок
«волна → фразы → окно», повторный подход, отсутствие второй подсказки взаимодействия,
поведение при отказе от выбора, кириллица в репликах на русской и английской локализации.

## 7. Риски

- **Имя состояния/события оклика неизвестно.** `Keeper Hail` и `WAVE` взяты из описания
  фичи, в декомпиле их нет. Вероятность высокая, последствие — волна не играет, фича
  выглядит мёртвой при здоровых логах. Что делаем: T1 дампит FSM и клипы, T4 имеет два
  пути (FSM-событие, затем клип) и пишет `hail unavailable: <reason>`; имена — константы
  в одном файле.
- **Отправка события в чужое состояние FSM.** Если у текущего состояния нет перехода на
  событие, PlayMaker его проигнорирует, а при неудачной формулировке можно перезапустить
  интро-сцену (повторный вход, блокировка ввода). Вероятность средняя, последствие —
  сломанный NPC. Что делаем: `IsEventValid(..., false)` перед отправкой; отправлять только
  когда `ActiveStateName` совпадает с состоянием покоя из T1; при сомнении путь «клип»
  вместо события.
- **Второй `NPCControlBase` на объекте NPC.** `InteractableBase.ShowInteraction`
  (`:332`) добавляет интерактив в `InteractManager`, у NPC появится вторая подсказка
  «Speak». Вероятность средняя, последствие — два диалога на одну кнопку. Что делаем:
  instigator на отдельном дочернем объекте без коллайдера, проверка в ручном чек-листе.
- **Реплики не появляются из-за отсутствия `DialogueBox`.** `StartConversation` при
  отсутствии инстанса молча выходит (`DialogueBox.cs:167-169`), тогда `EndedDialogue`
  не поднимется и окно выбора не откроется. Вероятность низкая (Bonetown — игровая сцена
  с HUD), последствие — фича не работает. Что делаем: T4 открывает окно сразу, если
  диалог не стартовал, и пишет `dialogue box missing`.
- **Кириллица в шрифте диалога.** Шрифт окна зависит от языка игры; русский текст на
  английской локализации может дать квадраты. Вероятность средняя, последствие —
  нечитаемый текст. Что делаем: текст в конфиге, дефолт русский, проверка на двух
  локализациях в ручном чек-листе; при проблеме — латинский дефолт.
- **Волна срабатывает при загрузке сцены, если герой стоит рядом.** `TriggerEnterEvent`
  отдаёт накопленные входы при активации (`TriggerEnterEvent.cs:199-207`), а поллинг
  расстояния видит близость сразу после загрузки. Вероятность средняя, последствие —
  волна «из ниоткуда» и лишний диалог. Что делаем: `armed` сбрасывается на загрузке сцены
  и ставится только после выхода за `rearmDistance`, плюс кулдаун.
- **`churchKeeperIntro` ставится не концом разговора.** Вероятность средняя (ставит FSM
  сцены), последствие — волна до первого разговора или никогда. Что делаем: T1 логирует
  значение до и после разговора; при неверной семантике включается T6 со своим флагом.
- **Конфликт с ванильным взаимодействием.** Игрок жмёт «Speak» в момент, когда мод
  начинает свои реплики. Вероятность средняя, последствие — два окна подряд. Что делаем:
  гейт `npc.IsRunningDialogue` (`PlayMakerNPC.cs:44`) и собственный `_talking`.
- **Точки отката.** T1–T3 ни на что не влияют (диагностика и чистые функции); T4–T5
  отключаются пустым `GreetingText` и `EnableSnailChoice=false`; T6 откатывается чтением
  `churchKeeperIntro` обратно. Ванильный разговор и окно выбора не меняются, поэтому
  полный откат — удалить `ChurchkeeperGreeting.Tick()` из `Plugin.Update`.
- **Декомпил во временном каталоге.** Ссылки на `NPCControlBase.cs:298` и прочие строки
  декомпила не воспроизводятся после очистки `%TEMP%`. Вероятность средняя, последствие —
  план нельзя перепроверить по строкам. Что делаем: истина фиксируется тестами на `Pure/`
  и логами `[GREET]`, а не номерами строк.

## Что требует проверки в игре

1. T1: `F6` в Bonetown — есть ли FSM с состоянием `Keeper Hail`, какие у него события,
   какие клипы у аниматора прислужницы, чему равен `churchKeeperIntro` до и после
   разговора.
2. Волна и оклик видны и слышны, клип проигрывается один раз, без зацикливания.
3. Порядок «волна → фразы → окно выбора» и отсутствие окна до первого разговора.
4. Повторный подход после выбора гребня: волна и фразы есть, окна нет.
5. Повторный подход после отказа: волна и фразы есть, окно снова открывается.
6. Нет второй подсказки «Speak» у NPC и второго диалога по одной кнопке.
7. Кириллица читается на русской локализации и не ломается на английской.

## Журнал верификации

Дописывается `tester` после каждой проверки. Статус — строго из enum
`passed | failed | not_run | inconclusive`.

### 2026-11-13 — pending

- статус: `not_run`
- команды и точный вывод: —
- замечания: план составлен по коду на 13.11.2026 (HEAD `4ee5ede`) и декомпилу игры;
  имена состояния `Keeper Hail`, события `WAVE` и клипов волны из декомпила не
  подтверждены — их даёт T1.

## Журнал

- T1 — `ChurchkeeperDiag.cs`: рантайм-дамп прислужницы по `F5` (F6 занят DebugMod),
  маркеры `[NPC]`, `[NPC-FSM]`, `[NPC-ANIM]`, `[NPC-TRIGGER]`, `[NPC-PD]`, только чтение;
  сборка 0 предупреждений, тесты 126/126. Коммит `00e0d7c`. Статус: сделано.
- T4 (урезанная версия: только волна и оклик, без реплик и окна выбора) —
  `Pure/GreetingPolicy.cs`, `ChurchkeeperGreeting.cs` (MonoBehaviour на объекте
  прислужницы, привязка по сцене `Bonetown`), ключи `GreetEnabled/GreetRadius/
  GreetCooldown/GreetClip`, маркер `[GREET]`, FSM не трогается; сборка 0 предупреждений,
  тесты 126/126. Статус: сделано.
