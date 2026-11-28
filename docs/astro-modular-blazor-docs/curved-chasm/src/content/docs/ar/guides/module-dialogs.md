---
title: نوافذ الحوار بين وحدات العميل
description: تفتح وحدة عميل نافذة حوار توفرها وحدة أخرى، بمعرفها، وتنتظر نتيجتها، دون أن تشير إحداهما إلى الأخرى (IModuleDialogs).
sidebar:
  order: 7
---

تبنى وحدات العميل وتثبت كل واحدة على حدة، فلا تشير وحدة إلى أخرى أبدا. ومع ذلك تستطيع وحدة أن تفتح نافذة حوار توفرها وحدة
أخرى: تطلبها من المضيف **بمعرف النافذة**. فيحمل المضيف الوحدة المالكة، ويعرض النافذة في إطار الحوار في MudTail، ويعيد النتيجة
إلى الوحدة الطالبة.

```mermaid
sequenceDiagram
    participant A as Module A (a page)
    participant H as Host (BlzWebClient)
    participant B as Module B
    A->>H: ShowAsync("Module.Shifts/pick-shift", { unitId })
    H->>H: Module.Shifts in the user's catalog?
    H->>B: EnsureLoadedAsync (download on first use, dependencies first)
    H->>H: keep B's styles and scripts on the page
    H->>B: render the [ClientDialog] component in the dialog frame
    B-->>H: Dialog.Close({ shiftId, ... }) or Cancel
    H-->>A: DialogResult (JSON)
```

يجري كل ذلك في المتصفح، في بيئة .NET واحدة: لا رحلة إلى الخادم، ولا وسيط رسائل، ولا مركز SignalR. وحين يحتاج الخادم أو
مستخدم آخر أن يسأل المستخدم شيئا، أوصل ذلك الحدث إلى العميل (SignalR) واعرض النافذة عبر الخدمة نفسها.

## توفير نافذة حوار (الوحدة B)

ضع `[ClientDialog]` على مكون. معرفه `"{module id}/{name}"`، ويجب أن يبدأ بمعرف الوحدة نفسها في الكتالوج (يتجاهل المضيف،
مع رسالة في وحدة التحكم، معرفا يقع تحت معرف وحدة أخرى).

```razor title="Module.Shifts/Dialogs/PickShiftDialog.razor"
@attribute [ClientDialog("Module.Shifts/pick-shift", Title = "Pick a shift")]

@*
  Contract, for other modules:
    parameters  { unitId?: string }
    result      { shiftId, unit, startsAt }
*@

<div class="mud-dialog-content">
  …the module's own UI, calling its own API…
</div>
<div class="mud-dialog-actions">
  <button type="button" class="mud-button-root mud-button-text mud-button-medium" @onclick="Dialog.Cancel">Cancel</button>
  <button type="button" class="mud-button-root mud-button-filled mud-button-primary mud-button-medium"
          @onclick="() => Dialog.Close(new { shiftId = _id, unit = _unit, startsAt = _startsAt })">Choose</button>
</div>

@code {
  [CascadingParameter] public IDialogReference Dialog { get; set; } = default!;

  private sealed record Parameters(Guid? UnitId);

  protected override async Task OnAfterRenderAsync(bool firstRender)
  {
    if (!firstRender) return;
    var unitId = Dialog.GetParameters<Parameters>()?.UnitId;
    …
  }
}
```

يرسم المضيف الإطار: الغطاء وشريط العنوان وزر الإغلاق. ويرسم المكون المحتوى وأزراره، عادة `div.mud-dialog-content` و
`div.mud-dialog-actions`، وينهي النافذة عبر `IDialogReference` المتتالي:

| العضو | ما يفعله |
| --- | --- |
| `Close(object? data)` | يغلق النافذة بنتيجة؛ تتلقى الوحدة الطالبة `data` بصيغة JSON |
| `Cancel()` | يغلقها بلا نتيجة |
| `GetParameters<T>()` | معاملات الطالب بنوع النافذة نفسها |
| `Parameters` | الشيء نفسه، بصيغة `JsonElement` |

النافذة مكون عادي في وحدتها: تحقن الخدمات (`IHttpClientFactory`، `MudTailRuntime`، ...) وتستخدم أنماط وحدتها، التي تبقى في
الصفحة ما دامت النافذة مفتوحة، أيا كانت الصفحة التي فتحتها.

## فتح نافذة حوار (الوحدة A)

احقن `IModuleDialogs` وانتظر النتيجة. لا تعلن الوحدة A اعتمادها على الوحدة B ولا تشير إلى أي من أنواعها؛ بل تقرأ النتيجة في
سجل (record) خاص بها.

```razor
@inject IModuleDialogs Dialogs

@code {
  private sealed record PickedShift(int ShiftId, string Unit, DateTimeOffset StartsAt);

  private async Task PickAsync()
  {
    var result = await Dialogs.ShowAsync("Module.Shifts/pick-shift", new { unitId = sportsDeskId },
        new DialogOptions { Title = "Pick a sports shift" });
    if (result.Canceled) return;
    var picked = result.As<PickedShift>();
  }
}
```

| | |
| --- | --- |
| `ShowAsync(id, parameters?, options?, cancellationToken?)` | يكتمل حين تغلق النافذة. تنتقل المعاملات والنتيجة بصيغة JSON للويب (camelCase، دون حساسية لحالة الأحرف) |
| `DialogResult.Canceled` | صحيح بعد ✕، أو Escape، أو نقرة على الغطاء، أو `Cancel()`، أو الانتقال من الصفحة، أو رمز الإلغاء |
| `DialogResult.As<T>()` / `Data` | النتيجة بنوع الطالب، أو بصيغة `JsonElement` |
| `DialogOptions` | `Title`، و`MaxWidth` (طول CSS)، و`CloseOnOverlayClick`، و`CloseOnEscape`؛ وما لم يضبط يؤخذ من `[ClientDialog]` |
| `IsAvailable(id)` | هل الوحدة المالكة في كتالوج المستخدم (وتسرد تلك النافذة)، لإخفاء زر لن يعمل. لا ينزل شيء |
| `FindProvider(capability)` / `FindProviders(capability)` | النافذة (أو النوافذ) التي توفر قدرة، من الكتالوج ([أدناه](#البحث-عن-نافذة-حوار-بقدرتها)) |
| `ModuleDialogException` | معرف مجهول، أو وحدة لا يحق للمستخدم استخدامها، أو وحدة فشل تحميلها، أو وحدة بلا تلك النافذة |

قد تفتح نافذة نافذة أخرى: تتراكب، والأحدث في الأعلى، والعليا وحدها تتلقى النقرات.

## البحث عن نافذة حوار بقدرتها

بعض الطالبين لا يهمهم أي وحدة توفر النافذة، بل ما تفعله: "دع المستخدم يختار صورة" تصلح مع أي وحدة تخزن الملفات. تعلن
نافذة كهذه ما توفره بـ `Provides`، ويطلب الطالب القدرة لا المعرف:

```razor
@attribute [ClientDialog("Module.Storage/pick-asset", Title = "Pick a file", Provides = new[] { PickerCapabilities.Asset })]
```

```csharp
var id = Dialogs.FindProvider(PickerCapabilities.Asset);   // "Module.Storage/pick-asset"، أو null: أخف الزر
var picked = await Dialogs.PickAsync(PickerCapabilities.Asset, new PickerParameters(Multiple: true, Accept: "image/*"));
if (picked is not null) Use(picked.Items);                 // null: ألغى المستخدم
```

يقرأ الكتالوج `Provides` من بيانات الوحدة الوصفية عند رفعها (كل قيمة بالشكل `"{kind}:{name}"` بأحرف صغيرة، وتسقط غيرها
بتحذير)، فيعمل `FindProvider` و`FindProviders` (كلها، بترتيب معرف الوحدة) قبل تنزيل الوحدة. والوحدة التي يسردها خادم أقدم
بلا قائمة قدرات، فلا يعثر عليها إلا بمعرفها.

### عقد أدوات الاختيار

كل نافذة `picker:*` تأخذ المعاملات نفسها وتغلق بالنتيجة نفسها، فيستطيع الطالب استخدام أي مزود. و`PickerParameters`
و`PickerResult` و`PickedItem` (في ModularBlazor.Abstractions) هي هذه الأشكال بلغة C#:

```json
// المعاملات
{ "multiple": false, "selected": ["12"], "accept": "image/*", "publicOnly": true }
// النتيجة
{ "ids": ["12"], "items": [{ "id": "12", "text": "logo.png", "thumbnailUrl": "…", "url": "…" }] }
```

| الحقل | معناه |
| --- | --- |
| `multiple` | يجوز للمستخدم اختيار عدة عناصر |
| `selected` | المعرفات المختارة سلفا، تعرض مختارة |
| `accept` | للأصول فقط: ما يجوز اختياره، كخاصية `accept` في HTML (`"image/*"`، `".pdf,.docx"`) |
| `publicOnly` | للأصول فقط: الملفات ذات الرابط العام (للمحتوى المنشور) |
| `ids` | المعرفات المختارة، نصا، بترتيب اختيارها |
| `items` | ما يكفي لعرض كل عنصر دون سؤال وحدته: `text`، و`thumbnailUrl` / `url` إن وجدا |

تتجاهل أداة الاختيار ما لا يخصها من الحقول. القدرات القياسية هي `picker:asset` (التخزين) و`picker:keyword` و`picker:term`
و`picker:concept` (‏`PickerCapabilities`). وأي قدرة أخرى تتبع الشكل نفسه `"{kind}:{name}"`، وتوثق الوحدة التي تعرّفها أشكالها.

## العقد

معرف النافذة وشكلا JSON هما العقد بين الوحدتين، كمسار HTTP وأجسامه. اكتبها بجوار المكون (كما في المثال)، وأبقها متوافقة مع ما
سبق، وأعط العقد المتغير معرفا جديدا (`Module.Shifts/pick-shift-v2`) ريثما ينتقل الطالبون إليه. لا شيء يفحص العقد وقت
الترجمة: اختبر الاستدعاء في ساحة تجربة (playground) تثبت الوحدتين.

الوصول يتبع الكتالوج. فكتالوج المستخدم لا يضم إلا الوحدات التي تسمح بها أدواره ومستأجره، فتفشل نافذة وحدة لا يحق له
استخدامها بـ `ModuleDialogException` (افحص `IsAvailable` أولا). أما ما تقرؤه النافذة أو تغيره فتقرره الواجهة البرمجية لوحدتها،
كما في صفحاتها.

## كيف يفعلها المضيف

- يسجل `ModuleManager` مكونات `[ClientDialog]` في كل وحدة محملة، كما يسجل تخطيطات `[LayoutKey]`، ويسقطها حين تفرغ الوحدة.
- يفحص `ModuleDialogService` (‏`IModuleDialogs`) الكتالوج، ويستدعي `EnsureLoadedAsync` للوحدة المالكة (اعتمادياتها أولا،
  كما للصفحة)، ويمسك الوحدة في `ActiveModuleService` ما دامت النافذة مفتوحة، فتسري أنماطها وسكربتاتها على صفحة وحدة أخرى.
- يعرض `Components/ModuleDialogHost.razor`، المرسوم مرة واحدة في `App.razor` خارج الموجه، النوافذ المفتوحة في إطار
  `mud-dialog-*` في MudTail. وكل نافذة داخل `ErrorBoundary`، فتعرض النافذة الفاشلة خطأها بدل أن تكسر الصفحة. وينتقل التركيز
  إلى النافذة حين تفتح.
