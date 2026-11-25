// Arabic: the Design canvas, Code editor and Style tokens pages (the Content Designer's packs).
export default {
  messages: {
    // Design canvas
    'Design canvas': 'لوحة التصميم',
    'The Content Designer’s surface (<code>MudCanvas</code>, pack <code>designer</code>), ported from VsDock’s post designer. A page in a device frame (Mobile 375, Tablet 768, Desktop 1280, AMP), with a selection box, drop markers, a span handle, in-place text editing, zoom from 25 to 300 % and panning. It hit-tests while the pointer moves and calls the host <strong>once per finished gesture</strong>; the host owns the model and re-renders.':
      'سطح مصمّم المحتوى (<code>MudCanvas</code>، الحزمة <code>designer</code>)، منقول من مصمّم المنشورات في VsDock. صفحة داخل إطار جهاز (الجوال 375، اللوحي 768، سطح المكتب 1280، AMP)، مع مربع تحديد وعلامات إفلات ومقبض لعدد الأعمدة وتحرير النص في مكانه وتكبير من 25 إلى 300 % وتحريك العرض. يحدّد الهدف أثناء حركة المؤشر ويستدعي المضيف <strong>مرة واحدة لكل إيماءة مكتملة</strong>؛ والمضيف يملك النموذج ويعيد العرض.',
    'A news post in four views': 'منشور إخباري في أربعة عروض',
    'The same markup in every view: each block carries its tokens per breakpoint (<code>data-mb-span-lg="2"</code>, <code>data-mb-hidden="true"</code>), and the editorial pack’s container queries measure the frame, not the window. Resizing sets the span for the current view only. Switch the page to Arabic: before/after and the span handle mirror.':
      'الترميز نفسه في كل عرض: تحمل كل كتلة رموزها لكل نقطة توقف (<code>data-mb-span-lg="2"</code>، <code>data-mb-hidden="true"</code>)، واستعلامات الحاوية في الحزمة التحريرية تقيس الإطار لا النافذة. تغيير الحجم يضبط عدد الأعمدة للعرض الحالي فقط. حوّل الصفحة إلى العربية: ينعكس «قبل/بعد» ومقبض الأعمدة.',
    'From Blazor': 'من Blazor',
    'The studio renders the blocks with a JS root component (or its own markup, <code>adopt: true</code>) and hears one <code>OnMudEvent</code> per gesture.':
      'يعرض الاستوديو الكتل بمكوّن جذري لـ JS (أو بترميزه الخاص، <code>adopt: true</code>) ويتلقى <code>OnMudEvent</code> واحدًا لكل إيماءة.',
    Canvas: 'اللوحة',
    Toolbox: 'صندوق الأدوات',
    Paragraph: 'فقرة',
    Heading: 'عنوان',
    Image: 'صورة',
    Quote: 'اقتباس',
    Columns: 'أعمدة',
    Mobile: 'الجوال',
    Tablet: 'اللوحي',
    Desktop: 'سطح المكتب',
    AMP: 'AMP',
    'Arabic page (RTL)': 'صفحة عربية (من اليمين لليسار)',
    'Show hidden blocks': 'إظهار الكتل المخفية',
    'Drag a block onto the page. Double-click text to edit it. Alt+↑ / Alt+↓ move the selection, Delete removes it, Ctrl+wheel zooms, the middle button pans.':
      'اسحب كتلة إلى الصفحة. انقر مرتين على النص لتحريره. ‏Alt+↑ / Alt+↓ لنقل التحديد، وDelete لحذفه، وCtrl مع العجلة للتكبير، والزر الأوسط لتحريك العرض.',
    Options: 'الخيارات',
    Gestures: 'الإيماءات',
    Methods: 'الدوال',
    Tokens: 'الرموز',

    // Code editor
    'Code editor': 'محرر الشيفرة',
    '<code>MudMonaco</code> (pack <code>monaco</code>) is the Monaco editor, self-hosted: its ES modules are a chunk loaded the first time an editor opens, and its workers come from UI.MudTail’s own files, never a CDN. It starts as a textarea, so typing works at once, and stays one when Monaco can’t load. Colours follow the MudTail theme: switch to dark mode and the editor follows.':
      '<code>MudMonaco</code> (الحزمة <code>monaco</code>) هو محرر Monaco مستضافًا ذاتيًا: وحداته من نوع ES جزء يُحمَّل أول مرة يُفتح فيها محرر، وعمّاله من ملفات UI.MudTail نفسها، لا من شبكة توزيع. يبدأ حقلَ نص عاديًا فتعمل الكتابة فورًا، ويبقى كذلك إن تعذّر تحميل Monaco. والألوان تتبع سمة MudTail: انتقل إلى الوضع الداكن ويتبعك المحرر.',
    'JSON with a JSON Schema': 'JSON مع مخطط JSON Schema',
    'DynamicFields exports each kind as a JSON Schema; here a post that breaks it (<code>"tone": "loud"</code>). Hover the squiggle, or press Ctrl+Space inside <code>"style"</code> for the allowed values. The list under it is <code>getProblems()</code>.':
      'يصدّر DynamicFields كل نوع مخططَ JSON Schema؛ وهنا منشور يخالفه (<code>"tone": "loud"</code>). مرّر المؤشر فوق الخط المتعرّج، أو اضغط Ctrl+Space داخل <code>"style"</code> لترى القيم المسموحة. والقائمة تحته هي <code>getProblems()</code>.',
    'Read-only output, with a language picker': 'مخرجات للقراءة فقط، مع منتقي اللغة',
    'The studio’s Output panel: the responsive HTML, AMP HTML or JSON-LD, read-only. <code>languages</code> limits the picker.':
      'لوحة المخرجات في الاستوديو: HTML المتجاوب أو AMP HTML أو JSON-LD، للقراءة فقط. ويحدّ <code>languages</code> من خيارات المنتقي.',
    'The textarea fallback': 'البديل: حقل نص عادي',
    '<code>monaco: false</code> keeps the plain editor (it is also what shows while Monaco loads, and offline). Tab inserts two spaces; Escape then Tab leaves the field.':
      '<code>monaco: false</code> يُبقي المحرر العادي (وهو أيضًا ما يظهر أثناء تحميل Monaco ودون اتصال). ‏Tab يُدرج مسافتين؛ وEscape ثم Tab يغادر الحقل.',
    'No problems': 'لا توجد مشكلات',
    Problems: 'المشكلات',

    // Style tokens
    'Style tokens': 'رموز التنسيق',
    'The <code>editorial</code> pack is the CSS behind DynamicFields’ style properties (<code>width</code>, <code>align</code>, <code>spacing</code>, <code>tone</code>, <code>size</code>, <code>level</code>, <code>aspect</code>, <code>columns</code>, <code>layout</code>) and the designer’s per-view <code>order</code>, <code>hidden</code> and <code>span</code>. A renderer writes attributes, never classes: <code>data-mb-&lt;token&gt;</code> for the base and <code>data-mb-&lt;token&gt;-&lt;sm|md|lg|xl&gt;</code> where a view changes it. Breakpoints are container queries on <code>data-mb-page</code>, so the same CSS works in the canvas frame and on the live site; every rule is a logical property, and colours come from the palette.':
      'الحزمة <code>editorial</code> هي CSS خصائص التنسيق في DynamicFields (<code>width</code>، <code>align</code>، <code>spacing</code>، <code>tone</code>، <code>size</code>، <code>level</code>، <code>aspect</code>، <code>columns</code>، <code>layout</code>) وخصائص كل عرض في المصمّم: <code>order</code> و<code>hidden</code> و<code>span</code>. يكتب العارض سمات لا أصنافًا: <code>data-mb-&lt;token&gt;</code> للأساس و<code>data-mb-&lt;token&gt;-&lt;sm|md|lg|xl&gt;</code> حيث يغيّره عرض ما. ونقاط التوقف استعلامات حاوية على <code>data-mb-page</code>، فيعمل CSS نفسه في إطار اللوحة وعلى الموقع؛ وكل قاعدة خاصية منطقية، والألوان من لوحة الألوان.',
    'One page, any width': 'صفحة واحدة، بأي عرض',
    'Drag the slider: the page is the container, so the layout changes with its width, not the window’s. Below md the aside is hidden; from md the inverse paragraph goes first; from lg the figure spans two of three columns.':
      'اسحب المنزلق: الصفحة هي الحاوية، فيتغيّر التخطيط مع عرضها لا مع عرض النافذة. دون md يُخفى الجانب؛ ومن md تتقدّم الفقرة المعكوسة؛ ومن lg يمتد الشكل على عمودين من ثلاثة.',
    'Page width': 'عرض الصفحة',
  },
};
