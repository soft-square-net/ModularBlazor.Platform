---
title: لوحات الاستوديو
description: تضيف وحدة عميل لوحة إلى استوديو وحدة أخرى (مساحة عمل MudDock) بـ [DockPanel] دون أن ترجع إليها؛ ويركّبها المضيف عبر مكون جذر عام واحد.
sidebar:
  order: 11
---

**الاستوديو** مساحة عمل كاملة لوحدة ما مبنية على MudDock من MudTail: مصمم المحتوى مثلا، بلوحات المستكشف واللوحة القماشية
والخصائص والمشكلات. وتضيف وحدات أخرى لوحات إليه دون أن ترجع إليه: تضيف وحدة Elsa لوحة حالة سير العمل إلى مصمم المحتوى،
وتضيف وحدة غرفة الأخبار شريط الوكالات. كل لوحة مكون من وحدته، ولا تنزل الوحدة إلا حين تعرض اللوحة أول مرة.

## المساهمة بلوحة

صرّح بها خاصيةً على مستوى التجميع، كـ[مساهمات الهيكل](/ar/guides/layouts/):

```csharp title="Module.Elsa/AssemblyInfo.cs"
[assembly: DockPanel("workflow-status", "Workflow", typeof(WorkflowStatusPanel),
    ForStudio = "contentdesigner",             // مفتاح الاستوديو، ثابت تنشره وحدة الاستوديو
    ForContext = new[] { "content.post" },     // ما دام الاستوديو يعرض خبرا
    Area = DockPanelAreas.Right,               // left أو right (الافتراضي) أو bottom
    Icon = "workflow", Order = 50,
    Permissions = new[] { "elsa.workflows.read" })]
```

| الخاصية | معناها |
| --- | --- |
| `ForStudio` | مطلوبة. مفتاح الاستوديو بأحرف صغيرة (`"contentdesigner"`). وتسقط اللوحة التي بلا مفتاح بتحذير |
| `ForContext` | مفاتيح سياق (`"{module}.{entity}"`). لا يسرد الاستوديو اللوحة إلا ما دام عنصره الحالي يحمل أحدها. بلا مفاتيح: دائما |
| `Area` | موضع لوحة الأدوات أول مرة: `left` أو `right` أو `bottom`. وللمستخدم نقلها، ويحفظ الاستوديو الترتيب |
| `Document` | `true`: تفتح تبويب مستند في الوسط (واحد لكل عنصر: محرر، معاينة، إطار Elsa Studio)، لا لوحة أدوات |
| `Icon`، `Order`، `Permissions` | كما في كل مساهمة: أيقونة Lucide، والأصغر أولا، وأي صلاحية منها |

يقرأ الكتالوج الخاصية من بيانات الوحدة الوصفية عند رفعها، فيسرد الاستوديو اللوحة قبل تنزيل الوحدة. ويسقط الخادم اللوحات التي
لا يملك المستخدم أيا من صلاحياتها، كما يفعل بمداخل القوائم. والعناوين إنجليزية تترجم من ملفات `i18n/*.json` للوحدة.

المكون مكون عادي في وحدته، وقد يأخذ هذه المعاملات، ولا يعطى إلا ما يصرح به:

```razor title="Module.Elsa/Panels/WorkflowStatusPanel.razor"
@inject IModuleDialogs Dialogs

@code {
  [Parameter] public ContributionContext? Context { get; set; } // ContextKey "content.post"، وContextValue معرف الخبر
  [Parameter] public string PanelId { get; set; } = "";         // معرف نسخة اللوحة في MudDock
  [Parameter] public string Title { get; set; } = "";

  [CascadingParameter] public Task<AuthenticationState>? Auth { get; set; }
  [CascadingParameter(Name = "Culture")] public string? Culture { get; set; }
}
```

تعمل كالصفحة: حالة الدخول وتتالي `Culture` حاضران، ويتبع `lang` و`dir` اللغة، وتعمل خدمات التطبيق من داخلها
(‏`IModuleDialogs` و`IPageContext` و`IStringLocalizer<T>`). وتبقى أنماط وحدتها وسكربتاتها على الصفحة ما دامت اللوحة مركّبة.
وتعرض داخل حد أخطاء: اللوحة الفاشلة تعرض خطأها وزر **حاول مرة أخرى**، ويستمر الاستوديو في العمل.

## استضافة اللوحات (الاستوديو)

يسأل الاستوديو `IStudioPanels` عن لوحاته، ويعطي MudDock تعريفا لكل لوحة، مكونه مكون الجذر العام في المضيف
(‏`StudioPanels.RootComponent`، أي `"mb-module-panel"`) و**مفتاحه معرف اللوحة**:

```csharp
var contributed = Panels.For(ContentDesignerStudio.Key, PageContext.Values.Keys)
  .Select(p => new
  {
    key = p.Id,                            // "Module.Elsa:workflow-status"
    title = p.LocalTitle,
    icon = p.Icon,
    document = p.Document,
    allowMultiple = p.Document,
    component = StudioPanels.RootComponent // "mb-module-panel"
  });

Panels.Changed += Rebuild;                 // دخول، خروج، تثبيت وحدة أو تفريغها
```

ضع لوحة الأدوات الجديدة بـ `addPanel(key, groupId, zone)` في المنطقة التي يسميها `Area`. ويضع الاستوديو ما يعرضه في سياق
الصفحة (`PageContext.Set(ContentContext.Post, postId)`)؛ فتأخذ كل لوحة مركّبة أول مفاتيح `ForContext` المضبوطة، وتعيد العرض
حين تتغير القيمة.

| `IStudioPanels` | |
| --- | --- |
| `For(studio, contextKeys?)` | لوحات الاستوديو بالترتيب (`Order`، ثم معرف الوحدة، ثم المعرف). ومع مفاتيح السياق: ما كان `ForContext` فيه فارغا أو يضم أحدها |
| `Find(id)` | لوحة بمعرفها، أو null إن لم تكن في كتالوج المستخدم |
| `Changed` | ربما تغيرت القائمة |

## كيف يفعلها المضيف

يركّب MudDock كل لوحة مكونَ جذر JS في Blazor، ومكونات الجذر يجب تسجيلها في `Program.cs` قبل بدء التطبيق، وهو ما لا تقدر
عليه وحدة تحمّل لاحقا. لذا يسجل BlzWebClient مكونا واحدا، `Components/ModulePanelHost.razor`، باسم `mb-module-panel`:

- يقرأ معرف اللوحة من `PanelKey` (يمرر MudDock ‏`panelId` و`panelKey` و`title`)، أو من معامل `Contribution` حين يركّب دون
  MudDock. ومعاملا `ContextKey` / `ContextValue` يتقدمان على سياق الصفحة.
- يجد اللوحة في `IStudioPanels`، ويحمّل وحدتها عبر `IShellContributions.ResolveAsync`، ويعرض المكون بـ `DynamicComponent` داخل
  `ErrorBoundary` وتتالي `Culture`.
- مكون الجذر JS يشارك التطبيق عارضه ونطاق حقن الاعتماديات في WebAssembly، فالخدمات المحددة النطاق هي خدمات التطبيق نفسها،
  والنوافذ التي تفتحها اللوحة تظهر في `ModuleDialogHost` كغيرها.
- **التفريغ القابل للجمع:** لا شيء يمسك نوع وحدة إمساكا ثابتا. المساهمة نصوص (الاسم الكامل للمكون)، واللوحة تمسك `Type` المحلول
  ما دامت وحدته محملة فقط. فحين يفرغ `ModuleManager` الوحدة أو يستبدلها، تسقط اللوحة النوع وتعيد العرض بدونه، وتأخذ النوع
  الجديد حين تحمل الوحدة ثانية. واللوحة التي تخرج من كتالوج المستخدم تعرض غير متاحة.
