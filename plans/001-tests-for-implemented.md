---
task: "001 — тесты для уже реализованного функционала"
created: 2026-11-09
status: draft
verify: pending
---

# 001 — Тесты для уже реализованного функционала

## Цель

Решения, которые сейчас размазаны по Unity-слою (`ShopCosmetics`, `ShopCrestMenu`,
`ShopCrestSchema`, `CrestPickerService`), переехали в `Pure/` чистыми функциями, и
`dotnet test CrestPicker.Tests` покрывает их тестами. После этого поломка позиции
метки, её видимости, порядка восстановления стока, таймингов и применения гребня
падает в тестах, а не в игре.

Проверка успеха: число тестов выросло, все зелёные, поведение мода не изменилось
(DLL собирается, ручной чек-лист `TESTING.md` проходит без новых отличий).

## Границы

- не входит: изменение поведения, «заодно починить» открытые дефекты 1–4 из `TESTING.md:104-116`;
- не входит: тесты на сам рендер TMP, PlayMaker-переходы, Harmony-патчи на живых объектах;
- не входит: новые cfg-ключи и режимы косметики (`ShowIcons`/`ShowPrice`/`ShowMaterialIcon`
  сейчас не существуют, см. Research);
- не входит: правка `Pure/*.cs` «по вкусу» — только перенос существующих решений;
- не входит (кандидат на отдельный план): `CrestSelectMenu.NextIndex` (`CrestSelectMenu.cs:113`)
  уже чистый, но лежит в Unity-файле; его перенос в `Pure/` не входит в эти 10 задач;
- не входит: рефакторинг логов, чтобы они читали значения из новых чистых структур,
  кроме случаев, когда без этого нельзя сохранить текущий вывод.

## Research

### Покрыто сейчас (33 теста)
`TESTING.md:8-24`: `CrestPoolRules`, `ConfigRules`, `PurchaseClose`, `CentreSource`,
`Geometry`, `OfferStore`, `CrestLabel`. Тестовый проект компилирует `..\Pure\*.cs`
как исходники (`CrestPicker.Tests/CrestPicker.Tests.csproj:17`), поэтому новый файл в
`Pure/` подхватывается автоматически, без правки csproj. xunit 2.9.2, net8.0 (`:8,22`).

### Где лежат решения, которые надо вынести

**Позиция названия строки** — `ShopCosmetics.cs:342-414` (`PinLabelTransforms`):
- три ветки привязки: селектор (`:375-379`), иконка строки (`:380-384`), центр строки (`:385-389`);
- важный нюанс: в ветке «центр строки» конфигурируемые `RowLabelOffsetX/Y` **не применяются**,
  X берётся из `RowCentreX(vanilla, position.x)` (`:419-428`), Y = ванильный `position.y`;
- затем из мировых координат вычитается центр собственного ректа метки (`:391-392`, `:435-443`).

**Видимость названия строки** — `ShopCosmetics.cs:524-568`:
- `rowLive = row.activeInHierarchy`, `inside = RowInsideList(vanilla)` (`:608-628`),
  `empty = renderer.bounds.size.x <= 0.0001f`, `alpha = RowAlpha(row)` (`:591-600`);
- правило: `visible = rowLive && inside && !empty && alpha > 0.01f` (`:546`); при `alpha == 0.01f`
  метка уже скрыта;
- причина скрытия выводится фиксированным порядком `inactive → outside → empty → alpha`
  (`:584`) — этот порядок и есть контракт для теста.

**Порядок восстановления** — `ShopCrestMenu.cs:953-1027`:
`ShopCrestSchema.Hide()` (`:972`) → `ShopInputLock.Unblock` (`:973`) → cosmetics (`:985`) →
stock (`:986-996`) → stock lists + `BuildLists` (`:997-1001`) → teardown (`:1002`) →
alpha (`:1003`) → prompts (`:1004`) → position (`:1005-1012`), всё в `finally`
сбрасывает сессию (`:1020-1026`);
- идемпотентность: ранний выход при `foreign || idle` (`:957-969`), где
  `idle = !IsOpen && _stock == null`;
- «аварийность» — только параметр `emergency`, он влияет исключительно на
  `TeardownShop(emergency)` (`:545-555`: без emergency шаг пишет лог и ничего не делает).
  Порядок шагов от `emergency` не зависит.

**Закрытие после покупки** — `Pure/PurchaseClose.cs:17-47`, тесты
`CrestPicker.Tests/PurchaseCloseTests.cs:8-47`. Уже покрыто: 0.5 с после сообщения,
3 с фолбэк, 5 с `ForceIdle`, «до порогов ничего». Не покрыто: повторный вызов
(функция без состояния — возвращает то же решение) и то, что после `CloseWindow`
до 5 с решение остаётся `None`.

**Косметика** — `ShopCosmetics.cs:958-992`: скрываются `itemSprite`, `costSprite`,
`materialCostIcon` (`:980-982`) **безусловно**, режимов видимости не существует
(grep по `ShowIcons|ShowPrice|ShowMaterial` — совпадений нет). Реальные ветки, которые
стоит покрыть, другие:
- ранг источника подписи: `Item desc` → 0, `Item name` → 1, иначе 2 (`:1978`);
- размер собственной метки: свой рект, если `width/height > 1f`, затем рект строки,
  иначе 400×80 (`:712-726`);
- набор скрываемых частей строки (`:980-982`) — фиксированный, фиксируется тестом как контракт.

**Тексты панели деталей** — `ShopCrestSchema.cs:719-728` (`WriteText`):
перезапись только когда `label != null`, `want.Length > 0` и `SafeText(label) != want`
(`:721`); сравнение точное, нормализации пробелов в коде **нет**. Значит тест на
нормализацию зафиксировал бы новое поведение — в этой задаче его не вводим.

**Вписывание схемы** — `ShopCrestSchema.cs:1036-1066` (`FitContainer`) и `:941-1007` (точки):
- `targetH = PanelFill(0.8) * panel.y * Scale` (`:40,870-872`), `targetW = PanelFill * panel.x * Scale`;
- `scaleApplied = targetH / measuredH`, затем `min` с `targetW / bounds.size.x`, если оба > 0 (`:1050-1054`);
- `measuredH <= 0.0001f` → масштаб не применяется вовсе (`:1045-1048`);
- диаметр точки: `wanted = h2 * DotSize`, для закрытого слота ещё `* 0.85` (`:43,941,958`);
- смещение точек: `DotOffsetY * h2` (`:1021`).

**Подтверждение: клэмп** — заявленного `Geometry.ClampToRect(point, rect)` в коде **нет**
(grep по `ClampToRect` — 0 совпадений). Есть `Geometry.ClampAxis` (`Pure/Geometry.cs:28-44`,
уже покрыт 3 тестами, `GeometryTests.cs:27-43`) и его обёртка
`ShopCrestSchema.ClampShift/ClampAxis` (`:498-515`) с собственной веткой «не влезает → 0».
Выносить надо именно обёртку: у неё есть потребитель (`:480`), в отличие от гипотетического
`ClampToRect`.

**Таймауты и грейс** — `ShopCrestMenu.cs:1447,1458` (900 с), `Plugin.cs:42,236,259`
(грейс оффера 2 с), `Pure/PurchaseClose.cs:20,23,26` (0.5/3/5 с). Логика — сложение
константы с меткой времени, отдельных веток нет.

**Смена гребня** — `CrestPickerService.cs:93-123` (`ApplyCrest`): `!crest.IsUnlocked` →
`Unlock()`; затем всегда `SetEquippedCrest(name)` и `SendEquippedChangedEvent(force: true)`.
Почему force обязателен: `plans/000-handoff.md:112-113` — без второго вызова остаётся
мувсет прежнего гребня.

### Что уже есть и переиспользуется
- `Geometry.IsSane`/`ClampAxis` — `Pure/Geometry.cs:14-44`, расширяем тем же файлом.
- `CrestLabel.LabelOrName` — `Pure/CrestLabel.cs:13`.
- `PurchaseClose` + его тесты — расширяем, не дублируем.
- Тестовый проект уже подключает `Pure/*.cs` — `CrestPicker.Tests.csproj:17`.

## Режим тестирования

`after` — сначала `ivan` переносит решение в `Pure/` без изменения поведения, затем
`tester` пишет тесты; обратный порядок дал бы тесты на ещё не существующие сигнатуры.

## Задачи

### T1 (M) — позиция названия строки
- В `Pure/LabelAnchor.cs`:
  `enum LabelAnchorKind { Selector, Icon, Row }`,
  `struct LabelPlacement { float X; float Y; LabelAnchorKind Anchor; }`,
  `static LabelPlacement Resolve(bool hasSelector, float selectorX, bool hasIcon, float iconX,
  float iconY, float rowCentreX, float fallbackY, float offsetX, float offsetY,
  float rectOffsetX, float rectOffsetY)`.
  Правила переносятся 1:1 с `ShopCosmetics.cs:369-392`: ветка `Selector` — X = `selectorX + offsetX`,
  Y = (`hasIcon ? iconY : fallbackY`) + `offsetY`; `Icon` — X = `iconX + offsetX`, Y = `iconY + offsetY`;
  `Row` — X = `rowCentreX`, Y = `fallbackY`, смещения **не** применяются; в конце
  `X -= rectOffsetX`, `Y -= rectOffsetY`.
- В `ShopCosmetics.cs` остаётся: поиск селектора, иконки, строки, чтение конфига и
  запись `target.position`; вместо арифметики — вызов `LabelAnchor.Resolve(...)` и лог
  `anchor=` из `LabelPlacement.Anchor`.
- Тесты (`CrestPicker.Tests/LabelAnchorTests.cs`): `Selector_BeatsIconAndRow`,
  `Icon_UsedWhenNoSelector`, `RowCentre_UsedWhenNothingElse`,
  `Offsets_AppliedOnSelectorAndIcon_NotOnRow`, `RectOffset_SubtractedFromResult`,
  `SelectorWithoutIcon_UsesFallbackY`, `RowWithoutIcon_KeepsFallbackY`.
- приёмка: тесты зелёные, в игре лог `[SHOPFX] row label placed ... anchor=selector`
  печатает те же `selectorX`/`iconX`/`labelX`, что и до переноса.

Риск: ветка `Row` игнорирует `offsetX/Y` — это текущее поведение, а не недосмотр; тест
фиксирует его явно, иначе при первом же рефакторинге смещения «починят» и метки уедут.

### T2 (M) — видимость названия строки
- В `Pure/LabelVisibility.cs`:
  `enum LabelHiddenReason { Visible, Inactive, Outside, Empty, Alpha }`,
  `static LabelHiddenReason Evaluate(bool rowActive, bool insideList, float meshWidth, float fadeAlpha)`,
  константы `MinMeshWidth = 0.0001f`, `MinAlpha = 0.01f`.
  Правило с `ShopCosmetics.cs:546` и порядок причин с `:584`.
- В `ShopCosmetics.cs` остаётся: чтение `RowAlpha`, `activeInHierarchy`, `bounds.size.x`
  и включение/выключение рендерера; решение — из `Evaluate`, причина для лога — из enum
  (строки `inactive/outside/empty/alpha` не меняются).
- Тесты (`LabelVisibilityTests.cs`): `AllConditionsMet_IsVisible`,
  `InactiveRow_HidesAsInactive`, `RowOutsideList_HidesAsOutside`,
  `EmptyMesh_HidesAsEmpty`, `ZeroAlpha_HidesAsAlpha`,
  `AlphaExactlyThreshold_IsHidden`, `MeshExactlyThreshold_IsHidden`,
  `InactiveWinsOverOtherReasons` (порядок причин).
- приёмка: тесты зелёные; лог `[SHOPFX] row label hidden ... why=` даёт те же значения,
  что до переноса.

### T3 (M) — порядок восстановления при закрытии
- В `Pure/RestorePlan.cs`:
  `enum RestoreStep { Cosmetics, Stock, StockLists, Teardown, Alpha, Prompts, Position }`,
  `static RestoreStep[] Steps()`, `static bool ShouldRestore(bool isOpen, bool hasStock, bool foreignSession)`,
  `static bool TeardownTouchesVanilla(bool emergency)`.
  `Steps()` повторяет `ShopCrestMenu.cs:985-1012`, `ShouldRestore` — условие раннего выхода
  `:957-959`, `TeardownTouchesVanilla` — `:545-555`.
- В `ShopCrestMenu.Restore` остаётся: вызов `ShouldRestore`, цикл по `Steps()` с
  существующим `Step(name, action)` (`:1033-1043`), сам `TeardownShop`.
- Тесты (`RestorePlanTests.cs`): `Steps_HaveFixedOrder`,
  `Steps_SameForNormalAndEmergency`, `TeardownTouchesVanilla_OnlyWhenEmergency`,
  `ShouldRestore_FalseWhenForeignSession`, `ShouldRestore_FalseWhenIdleNoStock`,
  `ShouldRestore_TrueForOpenSession`, `Steps_RepeatCallReturnsSameOrder` (идемпотентность).
- приёмка: тесты зелёные; порядок шагов в логе `[SHOP] restore step ...` совпадает с прежним.

### T4 (S) — закрытие после покупки: добить тесты
- Код не меняется (`Pure/PurchaseClose.cs` уже есть).
- В `PurchaseCloseTests.cs` добавить: `MessageSeen_LateMessage_StillClosesAtMessagePlusGrace`,
  `AfterCloseWindow_BeforeForceDeadline_ReturnsNone` (`elapsed = 4.99f`, `emergencySent: true`,
  сообщение было), `RepeatedCall_SameInputSameDecision` (двойной вызов на 0.7 с и на 5 с).
- приёмка: 4 существующих теста + 3 новых зелёные, поведение не тронуто.

### T5 (M) — косметика строк: части, источник подписи, размер
- В `Pure/RowCosmetics.cs`:
  `enum RowPart { Icon, CostSprite, MaterialIcon, VanillaCostLabel }`,
  `static RowPart[] HiddenParts()`, `static int LabelRank(string? sourceName)`,
  `static (float W, float H) FallbackSize(float ownW, float ownH, float rowW, float rowH)`.
  Ранг — `ShopCosmetics.cs:1978`, размер — `:712-726` (порог `> 1f`, дефолт 400×80),
  набор частей — `:980-982`.
- В `ShopCosmetics.cs` остаётся: поиск объектов по `FieldInfo`, `SetActive`, вызов
  `RowCosmetics.FallbackSize(...)` в `FallbackSize` и `LabelRank` в `VisibleLabelSource`.
- Тесты (`RowCosmeticsTests.cs`): `HiddenParts_AreTheFourRowPieces`,
  `LabelRank_DescBeatsNameBeatsOther`, `LabelRank_NullIsOther`,
  `FallbackSize_PrefersOwnRect`, `FallbackSize_FallsBackToRowRect`,
  `FallbackSize_DefaultWhenBothTiny` (граница: ровно `1f` не годится — нужно `> 1f`).
- приёмка: тесты зелёные; выбранный источник и размер метки в игре те же.

Примечание: режимов `ShowIcons`/`ShowPrice`/`ShowMaterialIcon` в коде нет (Research).
`HiddenParts()` — точка расширения на будущее, но пока контракт фиксирует текущее
безусловное скрытие.

### T6 (S) — тексты панели деталей
- В `Pure/TextRules.cs`: `static bool NeedsRewrite(string? current, string? wanted)`:
  `wanted` пустой/`null` → `false`; `current == null` считается `""`; иначе побайтовое сравнение
  строк, как в `ShopCrestSchema.cs:721`.
- В `ShopCrestSchema.WriteText` (`:719-728`) остаётся только присваивание `label.text = want`
  после положительного решения.
- Тесты (`TextRulesTests.cs`): `EqualText_NoRewrite`, `DifferentText_Rewrites`,
  `EmptyWanted_NoRewrite`, `NullCurrent_WithWanted_Rewrites`, `NullWanted_NoRewrite`,
  `WhitespaceOnly_IsNotEqual` (фиксирует: нормализации нет).
- приёмка: тесты зелёные; поведение записи текста не изменилось.

### T7 (M) — схема гребня: вписывание и точки
- В `Pure/SchemaFit.cs`:
  `static bool TryFitScale(float panelW, float panelH, float fill, float scaleMultiplier,
  float silhouetteW, float silhouetteH, out float result)`;
  `static float DotDiameter(float silhouetteHeight, float dotSize, bool locked)`;
  `static float DotOffset(float silhouetteHeight, float offsetFactor)`;
  константы `LockedDotFactor = 0.85f`, `MinSize = 0.0001f`.
  Логика с `ShopCrestSchema.cs:870-872,1050-1054` и `:941,958,1021`.
- В `ShopCrestSchema.FitContainer`/`Fit` остаётся: чтение bounds и применение
  `localScale`/`localPosition`.
- Тесты (`SchemaFitTests.cs`): `FitScale_UsesHeightLimit`,
  `FitScale_WidthNarrowsResult`, `FitScale_ZeroSilhouetteHeight_ReturnsFalse`,
  `FitScale_ZeroPanel_FallsBackToBaseHeight` (проверить текущую ветку `TryPanelSize` до переноса!),
  `DotDiameter_LockedIsSmaller`, `DotDiameter_ZeroHeight_IsZero`,
  `DotOffset_ScalesWithHeight`, `DotOffset_ZeroHeight_IsZero`.
- приёмка: тесты зелёные; лог `[SCHEMA] fit panel=... scale=` даёт прежние числа.

Риск: у `FitContainer` два разных источника цели (`BaseHeight * Scale * sizeFactor` против
`PanelFill * panel * Scale`). Если не вынести выбор цели вместе с масштабом, тест зафиксирует
половину правила. Поэтому `TryFitScale` принимает уже готовую пару `(panelW, panelH, fill,
scaleMultiplier)` и не решает, брать ли `BaseHeight` — это остаётся в Unity-слое.

### T8 (S) — клэмп подтверждения: вынести обёртку, не изобретать `ClampToRect`
- В `Pure/Geometry.cs` добавить
  `static float ClampDelta(float centre, float half, float min, float max)`:
  `half * 2f > max - min` → `0f`, иначе `ClampAxis(centre, half, min, max) - centre`
  (перенос `ShopCrestSchema.cs:507-515`).
- В `ShopCrestSchema.ClampAxis` остаётся только вызов `Geometry.ClampDelta`.
- Тесты (`GeometryTests.cs`): `ClampDelta_PullsBackFromMaxSide`,
  `ClampDelta_PullsBackFromMinSide`, `ClampDelta_InsideSpan_IsZero`,
  `ClampDelta_ExactCorner_IsZero`, `ClampDelta_Oversize_IsZero`,
  `ClampDelta_InvertedBounds_DoesNotThrow`.
- приёмка: тесты зелёные; схема на подтверждении не вылезает за бэкборд как раньше.

### T9 (S) — таймауты и грейс в одном месте
- В `Pure/Deadlines.cs`: константы `SessionTimeout = 900f`, `OfferGrace = 2f`;
  `static float OpenTimeoutAt(float startedAt)`, `static float OfferExpiresAt(float startedAt)`.
  Тайминги покупки остаются в `PurchaseClose` — не дублировать.
- В `ShopCrestMenu.cs:1447,1458` и `Plugin.cs:42,236,259` — только вызовы.
- Тесты (`DeadlinesTests.cs`): `OpenTimeout_IsFifteenMinutes`,
  `OfferExpiry_IsTwoSeconds`, `OfferExpiry_FromRequestTime`, `Timeout_BeforeDeadlineIsNotExpired`.
- приёмка: тесты зелёные, числа те же 900 и 2.

Ценность низкая: ветвей нет, тест фиксирует только константы. Ставится последней;
если время кончится — режется без потери для остальных задач.

### T10 (M) — смена гребня: решение
- В `Pure/CrestApply.cs`:
  `readonly struct ApplyDecision { bool UnlockNow; bool SendForceEvent; }`,
  `static ApplyDecision Decide(bool isUnlocked, bool wasEquippedSame)`.
  `UnlockNow = !isUnlocked`; `SendForceEvent = true` **всегда** — с обоснованием
  `plans/000-handoff.md:112-113`; `wasEquippedSame` попадает в структуру только для
  паритета с текущим логом (`CrestPickerService.cs:113-115`) — если `ivan` не найдёт
  потребителя, поле удаляется, а тест на 4 комбинации остаётся на `UnlockNow`.
- В `CrestPickerService.ApplyCrest` остаётся: `crest.Unlock()`, `SetEquippedCrest`,
  `SendEquippedChangedEvent(force: decision.SendForceEvent)`.
- Тесты (`CrestApplyTests.cs`): `Locked_NeverEquipped_UnlocksAndForces`,
  `Locked_EquippedSame_UnlocksAndForces`, `Unlocked_OtherEquipped_NoUnlockForces`,
  `Unlocked_EquippedSame_NoUnlockForces`.
- приёмка: 4 теста зелёные; лог `[CREST] Applied ... unlockedNow=` даёт те же значения.

## Стратегия тестов

- Все новые тесты — xunit `[Fact]`, namespace `CrestPicker.Tests`, файлы
  `CrestPicker.Tests/<Module>Tests.cs`; новых пакетов не нужно (`CrestPicker.Tests.csproj:20-24`).
- Команда: `dotnet test CrestPicker.Tests`. Точечно:
  `dotnet test CrestPicker.Tests --filter FullyQualifiedName~LabelAnchorTests`.
- Сборка мода остаётся отдельной: `dotnet build -c Release` — обязательна после переноса
  из Unity-слоя, потому что тестовый проект не видит `ShopCosmetics.cs`/`ShopCrestMenu.cs`.
- Для каждого переноса `tester` дополнительно фиксирует ручную проверку по `TESTING.md`
  (§2) там, где лог-маркер меняется.
- Ожидаемый прирост: +40…50 тестов, всего ≈ 75.

## Порядок выполнения

1. T2 (видимость) — самая ветвистая и самая «дефектная» логика, ловит дефект 1 сразу.
2. T1 (позиция) — три ветки привязки, свежие коммиты `0c0c40a`, `c494f33`; регрессия тут дорогая.
3. T3 (порядок восстановления) — дефекты 2–4 живут именно в остатках после закрытия.
4. T4 (PurchaseClose +3 теста) — минута работы, уже почти покрыто.
5. T6 (тексты) — тривиально, низкий риск.
6. T7 (схема) — самая сложная математика, но руки дойдут после главного.
7. T10 (смена гребня) — изолированно, без UI.
8. T5 (косметика строк) — вынос есть, но ценность средняя.
9. T8 (клэмп) — маленькая обёртка.
10. T9 (константы) — режется при нехватке времени.

### Три приоритетные задачи
- **T2** — единственное место, где четыре независимых условия складываются в одно
  решение, и именно там сейчас открытый дефект (пустые/висящие названия строк). Тест даёт
  мгновенный сигнал при любой правке видимости.
- **T1** — три ветки привязки с разными правилами смещений; последние три коммита
  (`plans/000-handoff.md:135-137`) правили ровно это, и ни одна ветка не защищена тестом.
- **T3** — порядок шагов восстановления и ранний выход; ошибка тут оставляет чужой сток,
  селектор и кнопки после закрытия (дефекты 2–4) и воспроизводится только в игре.

## Не покрываем тестами

- Сам факт отрисовки TMP (глифы, атлас, материал, `ForceMeshUpdate`) — шрифт живёт в
  бандле игры; проверяется глазами и логом `[SHOPFX] tmptext ... verts=`
  (`ShopCosmetics.cs:2013-2018`).
- PlayMaker-переходы (`SHOP UP`, `CLOSE SHOP WINDOW`, `FLUERS UP/DOWN`, возврат в `Idle`) —
  нужен живой FSM; чек-лист `TESTING.md:49-61`.
- Harmony-патчи на живых объектах (`SetPurchased`, `PlayMakerFSM.Start` в
  `ChurchkeeperPatch.cs`) — требуют загруженной сцены.
- Альфа fade-групп и guard-таймер (60 кадров, `ShopCrestMenu.cs:734-768`) — завязано
  на `NestedFadeGroupBase.Update`.
- Поиск объектов в сцене (`Resources.FindObjectsOfTypeAll`, `GetComponentsInChildren`) —
  это Unity API, а не решение.

Всё перечисленное остаётся в ручном чек-листе `TESTING.md` §2.

## Риски

- Перенос арифметики меняет порядок операций float — вероятность средняя, последствие:
  метка/схема съезжает на доли; что делаем: тесты на точные значения из текущего кода,
  при расхождении в игре откатываем конкретную задачу, не подгоняем тест (`TESTING.md:73-81`).
- Ветка «центр строки» игнорирует `offsetX/Y` — вероятность высокая, что кто-то сочтёт это
  багом; последствие: «исправление» уводит метки; что делаем: тест
  `Offsets_AppliedOnSelectorAndIcon_NotOnRow` + комментарий в `LabelAnchor`.
- `LabelRank`/`FallbackSize`/`HiddenParts` могут оказаться без потребителя, если
  `ivan` перенесёт не всё; последствие: мёртвый код; что делаем: в приёмке T5 —
  каждый член вызывается из `ShopCosmetics.cs`, иначе член не добавляется.
- `TextRules` без нормализации пробелов — вероятность, что попросят «заодно нормализовать»;
  последствие: изменение поведения и падение ручного чек-листа; что делаем: нормализация —
  отдельный план с отдельным тестом.
- `TryFitScale` фиксирует только часть правила (см. T7) — вероятность средняя; последствие:
  тест зелёный, а схема не вписалась; что делаем: приёмка по логу `[SCHEMA] fit ... scale=`
  в игре, а не только по тестам.
- Тестовый проект не компилирует Unity-слой — вероятность высокая, что перенос сломает
  сборку мода и это не заметят по `dotnet test`; последствие: красный `dotnet build`;
  что делаем: `dotnet build -c Release` в приёмке каждой задачи, где правится Unity-файл.

## Журнал верификации

Дописывается `tester` после каждой проверки. Статус — строго из enum
`passed | failed | not_run | inconclusive`.

### 2026-11-09 — pending

- статус: `not_run`
- команды и точный вывод: —
- замечания: план составлен по коду на 09.11.2026; число тестов до работы — 33.
