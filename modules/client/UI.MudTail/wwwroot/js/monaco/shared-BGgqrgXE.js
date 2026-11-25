// node_modules/monaco-editor/esm/external/vscode-languageserver-types/lib/esm/main.js
var e;
(function(e) {
	function t(e) {
		return typeof e == "string";
	}
	e.is = t;
})(e ||= {});
var t;
(function(e) {
	function t(e) {
		return typeof e == "string";
	}
	e.is = t;
})(t ||= {});
var n;
(function(e) {
	e.MIN_VALUE = -2147483648, e.MAX_VALUE = 2147483647;
	function t(t) {
		return typeof t == "number" && e.MIN_VALUE <= t && t <= e.MAX_VALUE;
	}
	e.is = t;
})(n ||= {});
var r;
(function(e) {
	e.MIN_VALUE = 0, e.MAX_VALUE = 2147483647;
	function t(t) {
		return typeof t == "number" && e.MIN_VALUE <= t && t <= e.MAX_VALUE;
	}
	e.is = t;
})(r ||= {});
var i;
(function(e) {
	function t(e, t) {
		return e === Number.MAX_VALUE && (e = r.MAX_VALUE), t === Number.MAX_VALUE && (t = r.MAX_VALUE), {
			line: e,
			character: t
		};
	}
	e.create = t;
	function n(e) {
		let t = e;
		return K.objectLiteral(t) && K.uinteger(t.line) && K.uinteger(t.character);
	}
	e.is = n;
})(i ||= {});
var a;
(function(e) {
	function t(e, t, n, r) {
		if (K.uinteger(e) && K.uinteger(t) && K.uinteger(n) && K.uinteger(r)) return {
			start: i.create(e, t),
			end: i.create(n, r)
		};
		if (i.is(e) && i.is(t)) return {
			start: e,
			end: t
		};
		throw Error(`Range#create called with invalid arguments[${e}, ${t}, ${n}, ${r}]`);
	}
	e.create = t;
	function n(e) {
		let t = e;
		return K.objectLiteral(t) && i.is(t.start) && i.is(t.end);
	}
	e.is = n;
})(a ||= {});
var o;
(function(e) {
	function t(e, t) {
		return {
			uri: e,
			range: t
		};
	}
	e.create = t;
	function n(e) {
		let t = e;
		return K.objectLiteral(t) && a.is(t.range) && (K.string(t.uri) || K.undefined(t.uri));
	}
	e.is = n;
})(o ||= {});
var s;
(function(e) {
	function t(e, t, n, r) {
		return {
			targetUri: e,
			targetRange: t,
			targetSelectionRange: n,
			originSelectionRange: r
		};
	}
	e.create = t;
	function n(e) {
		let t = e;
		return K.objectLiteral(t) && a.is(t.targetRange) && K.string(t.targetUri) && a.is(t.targetSelectionRange) && (a.is(t.originSelectionRange) || K.undefined(t.originSelectionRange));
	}
	e.is = n;
})(s ||= {});
var c;
(function(e) {
	function t(e, t, n, r) {
		return {
			red: e,
			green: t,
			blue: n,
			alpha: r
		};
	}
	e.create = t;
	function n(e) {
		let t = e;
		return K.objectLiteral(t) && K.numberRange(t.red, 0, 1) && K.numberRange(t.green, 0, 1) && K.numberRange(t.blue, 0, 1) && K.numberRange(t.alpha, 0, 1);
	}
	e.is = n;
})(c ||= {});
var l;
(function(e) {
	function t(e, t) {
		return {
			range: e,
			color: t
		};
	}
	e.create = t;
	function n(e) {
		let t = e;
		return K.objectLiteral(t) && a.is(t.range) && c.is(t.color);
	}
	e.is = n;
})(l ||= {});
var u;
(function(e) {
	function t(e, t, n) {
		return {
			label: e,
			textEdit: t,
			additionalTextEdits: n
		};
	}
	e.create = t;
	function n(e) {
		let t = e;
		return K.objectLiteral(t) && K.string(t.label) && (K.undefined(t.textEdit) || y.is(t)) && (K.undefined(t.additionalTextEdits) || K.typedArray(t.additionalTextEdits, y.is));
	}
	e.is = n;
})(u ||= {});
var d;
(function(e) {
	e.Comment = "comment", e.Imports = "imports", e.Region = "region";
})(d ||= {});
var f;
(function(e) {
	function t(e, t, n, r, i, a) {
		let o = {
			startLine: e,
			endLine: t
		};
		return K.defined(n) && (o.startCharacter = n), K.defined(r) && (o.endCharacter = r), K.defined(i) && (o.kind = i), K.defined(a) && (o.collapsedText = a), o;
	}
	e.create = t;
	function n(e) {
		let t = e;
		return K.objectLiteral(t) && K.uinteger(t.startLine) && K.uinteger(t.startLine) && (K.undefined(t.startCharacter) || K.uinteger(t.startCharacter)) && (K.undefined(t.endCharacter) || K.uinteger(t.endCharacter)) && (K.undefined(t.kind) || K.string(t.kind));
	}
	e.is = n;
})(f ||= {});
var p;
(function(e) {
	function t(e, t) {
		return {
			location: e,
			message: t
		};
	}
	e.create = t;
	function n(e) {
		let t = e;
		return K.defined(t) && o.is(t.location) && K.string(t.message);
	}
	e.is = n;
})(p ||= {});
var m;
(function(e) {
	e.Error = 1, e.Warning = 2, e.Information = 3, e.Hint = 4;
})(m ||= {});
var h;
(function(e) {
	e.Unnecessary = 1, e.Deprecated = 2;
})(h ||= {});
var g;
(function(e) {
	function t(e) {
		let t = e;
		return K.objectLiteral(t) && K.string(t.href);
	}
	e.is = t;
})(g ||= {});
var _;
(function(e) {
	function t(e, t, n, r, i, a) {
		let o = {
			range: e,
			message: t
		};
		return K.defined(n) && (o.severity = n), K.defined(r) && (o.code = r), K.defined(i) && (o.source = i), K.defined(a) && (o.relatedInformation = a), o;
	}
	e.create = t;
	function n(e) {
		let t = e;
		return K.defined(t) && a.is(t.range) && K.string(t.message) && (K.number(t.severity) || K.undefined(t.severity)) && (K.integer(t.code) || K.string(t.code) || K.undefined(t.code)) && (K.undefined(t.codeDescription) || K.string(t.codeDescription?.href)) && (K.string(t.source) || K.undefined(t.source)) && (K.undefined(t.relatedInformation) || K.typedArray(t.relatedInformation, p.is));
	}
	e.is = n;
})(_ ||= {});
var v;
(function(e) {
	function t(e, t, ...n) {
		let r = {
			title: e,
			command: t
		};
		return K.defined(n) && n.length > 0 && (r.arguments = n), r;
	}
	e.create = t;
	function n(e) {
		let t = e;
		return K.defined(t) && K.string(t.title) && K.string(t.command);
	}
	e.is = n;
})(v ||= {});
var y;
(function(e) {
	function t(e, t) {
		return {
			range: e,
			newText: t
		};
	}
	e.replace = t;
	function n(e, t) {
		return {
			range: {
				start: e,
				end: e
			},
			newText: t
		};
	}
	e.insert = n;
	function r(e) {
		return {
			range: e,
			newText: ""
		};
	}
	e.del = r;
	function i(e) {
		let t = e;
		return K.objectLiteral(t) && K.string(t.newText) && a.is(t.range);
	}
	e.is = i;
})(y ||= {});
var b;
(function(e) {
	function t(e, t, n) {
		let r = { label: e };
		return t !== void 0 && (r.needsConfirmation = t), n !== void 0 && (r.description = n), r;
	}
	e.create = t;
	function n(e) {
		let t = e;
		return K.objectLiteral(t) && K.string(t.label) && (K.boolean(t.needsConfirmation) || t.needsConfirmation === void 0) && (K.string(t.description) || t.description === void 0);
	}
	e.is = n;
})(b ||= {});
var x;
(function(e) {
	function t(e) {
		let t = e;
		return K.string(t);
	}
	e.is = t;
})(x ||= {});
var ee;
(function(e) {
	function t(e, t, n) {
		return {
			range: e,
			newText: t,
			annotationId: n
		};
	}
	e.replace = t;
	function n(e, t, n) {
		return {
			range: {
				start: e,
				end: e
			},
			newText: t,
			annotationId: n
		};
	}
	e.insert = n;
	function r(e, t) {
		return {
			range: e,
			newText: "",
			annotationId: t
		};
	}
	e.del = r;
	function i(e) {
		let t = e;
		return y.is(t) && (b.is(t.annotationId) || x.is(t.annotationId));
	}
	e.is = i;
})(ee ||= {});
var S;
(function(e) {
	function t(e, t) {
		return {
			textDocument: e,
			edits: t
		};
	}
	e.create = t;
	function n(e) {
		let t = e;
		return K.defined(t) && O.is(t.textDocument) && Array.isArray(t.edits);
	}
	e.is = n;
})(S ||= {});
var C;
(function(e) {
	function t(e, t, n) {
		let r = {
			kind: "create",
			uri: e
		};
		return t !== void 0 && (t.overwrite !== void 0 || t.ignoreIfExists !== void 0) && (r.options = t), n !== void 0 && (r.annotationId = n), r;
	}
	e.create = t;
	function n(e) {
		let t = e;
		return t && t.kind === "create" && K.string(t.uri) && (t.options === void 0 || (t.options.overwrite === void 0 || K.boolean(t.options.overwrite)) && (t.options.ignoreIfExists === void 0 || K.boolean(t.options.ignoreIfExists))) && (t.annotationId === void 0 || x.is(t.annotationId));
	}
	e.is = n;
})(C ||= {});
var w;
(function(e) {
	function t(e, t, n, r) {
		let i = {
			kind: "rename",
			oldUri: e,
			newUri: t
		};
		return n !== void 0 && (n.overwrite !== void 0 || n.ignoreIfExists !== void 0) && (i.options = n), r !== void 0 && (i.annotationId = r), i;
	}
	e.create = t;
	function n(e) {
		let t = e;
		return t && t.kind === "rename" && K.string(t.oldUri) && K.string(t.newUri) && (t.options === void 0 || (t.options.overwrite === void 0 || K.boolean(t.options.overwrite)) && (t.options.ignoreIfExists === void 0 || K.boolean(t.options.ignoreIfExists))) && (t.annotationId === void 0 || x.is(t.annotationId));
	}
	e.is = n;
})(w ||= {});
var T;
(function(e) {
	function t(e, t, n) {
		let r = {
			kind: "delete",
			uri: e
		};
		return t !== void 0 && (t.recursive !== void 0 || t.ignoreIfNotExists !== void 0) && (r.options = t), n !== void 0 && (r.annotationId = n), r;
	}
	e.create = t;
	function n(e) {
		let t = e;
		return t && t.kind === "delete" && K.string(t.uri) && (t.options === void 0 || (t.options.recursive === void 0 || K.boolean(t.options.recursive)) && (t.options.ignoreIfNotExists === void 0 || K.boolean(t.options.ignoreIfNotExists))) && (t.annotationId === void 0 || x.is(t.annotationId));
	}
	e.is = n;
})(T ||= {});
var E;
(function(e) {
	function t(e) {
		let t = e;
		return t && (t.changes !== void 0 || t.documentChanges !== void 0) && (t.documentChanges === void 0 || t.documentChanges.every((e) => K.string(e.kind) ? C.is(e) || w.is(e) || T.is(e) : S.is(e)));
	}
	e.is = t;
})(E ||= {});
var te;
(function(e) {
	function t(e) {
		return { uri: e };
	}
	e.create = t;
	function n(e) {
		let t = e;
		return K.defined(t) && K.string(t.uri);
	}
	e.is = n;
})(te ||= {});
var D;
(function(e) {
	function t(e, t) {
		return {
			uri: e,
			version: t
		};
	}
	e.create = t;
	function n(e) {
		let t = e;
		return K.defined(t) && K.string(t.uri) && K.integer(t.version);
	}
	e.is = n;
})(D ||= {});
var O;
(function(e) {
	function t(e, t) {
		return {
			uri: e,
			version: t
		};
	}
	e.create = t;
	function n(e) {
		let t = e;
		return K.defined(t) && K.string(t.uri) && (t.version === null || K.integer(t.version));
	}
	e.is = n;
})(O ||= {});
var ne;
(function(e) {
	function t(e, t, n, r) {
		return {
			uri: e,
			languageId: t,
			version: n,
			text: r
		};
	}
	e.create = t;
	function n(e) {
		let t = e;
		return K.defined(t) && K.string(t.uri) && K.string(t.languageId) && K.integer(t.version) && K.string(t.text);
	}
	e.is = n;
})(ne ||= {});
var k;
(function(e) {
	e.PlainText = "plaintext", e.Markdown = "markdown";
	function t(t) {
		let n = t;
		return n === e.PlainText || n === e.Markdown;
	}
	e.is = t;
})(k ||= {});
var A;
(function(e) {
	function t(e) {
		let t = e;
		return K.objectLiteral(e) && k.is(t.kind) && K.string(t.value);
	}
	e.is = t;
})(A ||= {});
var j;
(function(e) {
	e.Text = 1, e.Method = 2, e.Function = 3, e.Constructor = 4, e.Field = 5, e.Variable = 6, e.Class = 7, e.Interface = 8, e.Module = 9, e.Property = 10, e.Unit = 11, e.Value = 12, e.Enum = 13, e.Keyword = 14, e.Snippet = 15, e.Color = 16, e.File = 17, e.Reference = 18, e.Folder = 19, e.EnumMember = 20, e.Constant = 21, e.Struct = 22, e.Event = 23, e.Operator = 24, e.TypeParameter = 25;
})(j ||= {});
var M;
(function(e) {
	e.PlainText = 1, e.Snippet = 2;
})(M ||= {});
var N;
(function(e) {
	e.Deprecated = 1;
})(N ||= {});
var re;
(function(e) {
	function t(e, t, n) {
		return {
			newText: e,
			insert: t,
			replace: n
		};
	}
	e.create = t;
	function n(e) {
		let t = e;
		return t && K.string(t.newText) && a.is(t.insert) && a.is(t.replace);
	}
	e.is = n;
})(re ||= {});
var ie;
(function(e) {
	e.asIs = 1, e.adjustIndentation = 2;
})(ie ||= {});
var ae;
(function(e) {
	function t(e) {
		let t = e;
		return t && (K.string(t.detail) || t.detail === void 0) && (K.string(t.description) || t.description === void 0);
	}
	e.is = t;
})(ae ||= {});
var P;
(function(e) {
	function t(e) {
		return { label: e };
	}
	e.create = t;
})(P ||= {});
var oe;
(function(e) {
	function t(e, t) {
		return {
			items: e || [],
			isIncomplete: !!t
		};
	}
	e.create = t;
})(oe ||= {});
var F;
(function(e) {
	function t(e) {
		return e.replace(/[\\`*_{}[\]()#+\-.!]/g, "\\$&");
	}
	e.fromPlainText = t;
	function n(e) {
		let t = e;
		return K.string(t) || K.objectLiteral(t) && K.string(t.language) && K.string(t.value);
	}
	e.is = n;
})(F ||= {});
var se;
(function(e) {
	function t(e) {
		let t = e;
		return !!t && K.objectLiteral(t) && (A.is(t.contents) || F.is(t.contents) || K.typedArray(t.contents, F.is)) && (e.range === void 0 || a.is(e.range));
	}
	e.is = t;
})(se ||= {});
var ce;
(function(e) {
	function t(e, t) {
		return t ? {
			label: e,
			documentation: t
		} : { label: e };
	}
	e.create = t;
})(ce ||= {});
var le;
(function(e) {
	function t(e, t, ...n) {
		let r = { label: e };
		return K.defined(t) && (r.documentation = t), r.parameters = K.defined(n) ? n : [], r;
	}
	e.create = t;
})(le ||= {});
var I;
(function(e) {
	e.Text = 1, e.Read = 2, e.Write = 3;
})(I ||= {});
var ue;
(function(e) {
	function t(e, t) {
		let n = { range: e };
		return K.number(t) && (n.kind = t), n;
	}
	e.create = t;
})(ue ||= {});
var L;
(function(e) {
	e.File = 1, e.Module = 2, e.Namespace = 3, e.Package = 4, e.Class = 5, e.Method = 6, e.Property = 7, e.Field = 8, e.Constructor = 9, e.Enum = 10, e.Interface = 11, e.Function = 12, e.Variable = 13, e.Constant = 14, e.String = 15, e.Number = 16, e.Boolean = 17, e.Array = 18, e.Object = 19, e.Key = 20, e.Null = 21, e.EnumMember = 22, e.Struct = 23, e.Event = 24, e.Operator = 25, e.TypeParameter = 26;
})(L ||= {});
var de;
(function(e) {
	e.Deprecated = 1;
})(de ||= {});
var R;
(function(e) {
	function t(e, t, n, r, i) {
		let a = {
			name: e,
			kind: t,
			location: {
				uri: r,
				range: n
			}
		};
		return i && (a.containerName = i), a;
	}
	e.create = t;
})(R ||= {});
var fe;
(function(e) {
	function t(e, t, n, r) {
		return r === void 0 ? {
			name: e,
			kind: t,
			location: { uri: n }
		} : {
			name: e,
			kind: t,
			location: {
				uri: n,
				range: r
			}
		};
	}
	e.create = t;
})(fe ||= {});
var z;
(function(e) {
	function t(e, t, n, r, i, a) {
		let o = {
			name: e,
			detail: t,
			kind: n,
			range: r,
			selectionRange: i
		};
		return a !== void 0 && (o.children = a), o;
	}
	e.create = t;
	function n(e) {
		let t = e;
		return t && K.string(t.name) && K.number(t.kind) && a.is(t.range) && a.is(t.selectionRange) && (t.detail === void 0 || K.string(t.detail)) && (t.deprecated === void 0 || K.boolean(t.deprecated)) && (t.children === void 0 || Array.isArray(t.children)) && (t.tags === void 0 || Array.isArray(t.tags));
	}
	e.is = n;
})(z ||= {});
var B;
(function(e) {
	e.Empty = "", e.QuickFix = "quickfix", e.Refactor = "refactor", e.RefactorExtract = "refactor.extract", e.RefactorInline = "refactor.inline", e.RefactorRewrite = "refactor.rewrite", e.Source = "source", e.SourceOrganizeImports = "source.organizeImports", e.SourceFixAll = "source.fixAll";
})(B ||= {});
var V;
(function(e) {
	e.Invoked = 1, e.Automatic = 2;
})(V ||= {});
var pe;
(function(e) {
	function t(e, t, n) {
		let r = { diagnostics: e };
		return t != null && (r.only = t), n != null && (r.triggerKind = n), r;
	}
	e.create = t;
	function n(e) {
		let t = e;
		return K.defined(t) && K.typedArray(t.diagnostics, _.is) && (t.only === void 0 || K.typedArray(t.only, K.string)) && (t.triggerKind === void 0 || t.triggerKind === V.Invoked || t.triggerKind === V.Automatic);
	}
	e.is = n;
})(pe ||= {});
var H;
(function(e) {
	function t(e, t, n) {
		let r = { title: e }, i = !0;
		return typeof t == "string" ? (i = !1, r.kind = t) : v.is(t) ? r.command = t : r.edit = t, i && n !== void 0 && (r.kind = n), r;
	}
	e.create = t;
	function n(e) {
		let t = e;
		return t && K.string(t.title) && (t.diagnostics === void 0 || K.typedArray(t.diagnostics, _.is)) && (t.kind === void 0 || K.string(t.kind)) && (t.edit !== void 0 || t.command !== void 0) && (t.command === void 0 || v.is(t.command)) && (t.isPreferred === void 0 || K.boolean(t.isPreferred)) && (t.edit === void 0 || E.is(t.edit));
	}
	e.is = n;
})(H ||= {});
var me;
(function(e) {
	function t(e, t) {
		let n = { range: e };
		return K.defined(t) && (n.data = t), n;
	}
	e.create = t;
	function n(e) {
		let t = e;
		return K.defined(t) && a.is(t.range) && (K.undefined(t.command) || v.is(t.command));
	}
	e.is = n;
})(me ||= {});
var he;
(function(e) {
	function t(e, t) {
		return {
			tabSize: e,
			insertSpaces: t
		};
	}
	e.create = t;
	function n(e) {
		let t = e;
		return K.defined(t) && K.uinteger(t.tabSize) && K.boolean(t.insertSpaces);
	}
	e.is = n;
})(he ||= {});
var ge;
(function(e) {
	function t(e, t, n) {
		return {
			range: e,
			target: t,
			data: n
		};
	}
	e.create = t;
	function n(e) {
		let t = e;
		return K.defined(t) && a.is(t.range) && (K.undefined(t.target) || K.string(t.target));
	}
	e.is = n;
})(ge ||= {});
var U;
(function(e) {
	function t(e, t) {
		return {
			range: e,
			parent: t
		};
	}
	e.create = t;
	function n(t) {
		let n = t;
		return K.objectLiteral(n) && a.is(n.range) && (n.parent === void 0 || e.is(n.parent));
	}
	e.is = n;
})(U ||= {});
var _e;
(function(e) {
	e.namespace = "namespace", e.type = "type", e.class = "class", e.enum = "enum", e.interface = "interface", e.struct = "struct", e.typeParameter = "typeParameter", e.parameter = "parameter", e.variable = "variable", e.property = "property", e.enumMember = "enumMember", e.event = "event", e.function = "function", e.method = "method", e.macro = "macro", e.keyword = "keyword", e.modifier = "modifier", e.comment = "comment", e.string = "string", e.number = "number", e.regexp = "regexp", e.operator = "operator", e.decorator = "decorator";
})(_e ||= {});
var ve;
(function(e) {
	e.declaration = "declaration", e.definition = "definition", e.readonly = "readonly", e.static = "static", e.deprecated = "deprecated", e.abstract = "abstract", e.async = "async", e.modification = "modification", e.documentation = "documentation", e.defaultLibrary = "defaultLibrary";
})(ve ||= {});
var ye;
(function(e) {
	function t(e) {
		let t = e;
		return K.objectLiteral(t) && (t.resultId === void 0 || typeof t.resultId == "string") && Array.isArray(t.data) && (t.data.length === 0 || typeof t.data[0] == "number");
	}
	e.is = t;
})(ye ||= {});
var be;
(function(e) {
	function t(e, t) {
		return {
			range: e,
			text: t
		};
	}
	e.create = t;
	function n(e) {
		let t = e;
		return t != null && a.is(t.range) && K.string(t.text);
	}
	e.is = n;
})(be ||= {});
var xe;
(function(e) {
	function t(e, t, n) {
		return {
			range: e,
			variableName: t,
			caseSensitiveLookup: n
		};
	}
	e.create = t;
	function n(e) {
		let t = e;
		return t != null && a.is(t.range) && K.boolean(t.caseSensitiveLookup) && (K.string(t.variableName) || t.variableName === void 0);
	}
	e.is = n;
})(xe ||= {});
var Se;
(function(e) {
	function t(e, t) {
		return {
			range: e,
			expression: t
		};
	}
	e.create = t;
	function n(e) {
		let t = e;
		return t != null && a.is(t.range) && (K.string(t.expression) || t.expression === void 0);
	}
	e.is = n;
})(Se ||= {});
var Ce;
(function(e) {
	function t(e, t) {
		return {
			frameId: e,
			stoppedLocation: t
		};
	}
	e.create = t;
	function n(e) {
		let t = e;
		return K.defined(t) && a.is(e.stoppedLocation);
	}
	e.is = n;
})(Ce ||= {});
var W;
(function(e) {
	e.Type = 1, e.Parameter = 2;
	function t(e) {
		return e === 1 || e === 2;
	}
	e.is = t;
})(W ||= {});
var G;
(function(e) {
	function t(e) {
		return { value: e };
	}
	e.create = t;
	function n(e) {
		let t = e;
		return K.objectLiteral(t) && (t.tooltip === void 0 || K.string(t.tooltip) || A.is(t.tooltip)) && (t.location === void 0 || o.is(t.location)) && (t.command === void 0 || v.is(t.command));
	}
	e.is = n;
})(G ||= {});
var we;
(function(e) {
	function t(e, t, n) {
		let r = {
			position: e,
			label: t
		};
		return n !== void 0 && (r.kind = n), r;
	}
	e.create = t;
	function n(e) {
		let t = e;
		return K.objectLiteral(t) && i.is(t.position) && (K.string(t.label) || K.typedArray(t.label, G.is)) && (t.kind === void 0 || W.is(t.kind)) && t.textEdits === void 0 || K.typedArray(t.textEdits, y.is) && (t.tooltip === void 0 || K.string(t.tooltip) || A.is(t.tooltip)) && (t.paddingLeft === void 0 || K.boolean(t.paddingLeft)) && (t.paddingRight === void 0 || K.boolean(t.paddingRight));
	}
	e.is = n;
})(we ||= {});
var Te;
(function(e) {
	function t(e) {
		return {
			kind: "snippet",
			value: e
		};
	}
	e.createSnippet = t;
})(Te ||= {});
var Ee;
(function(e) {
	function t(e, t, n, r) {
		return {
			insertText: e,
			filterText: t,
			range: n,
			command: r
		};
	}
	e.create = t;
})(Ee ||= {});
var De;
(function(e) {
	function t(e) {
		return { items: e };
	}
	e.create = t;
})(De ||= {});
var Oe;
(function(e) {
	e.Invoked = 0, e.Automatic = 1;
})(Oe ||= {});
var ke;
(function(e) {
	function t(e, t) {
		return {
			range: e,
			text: t
		};
	}
	e.create = t;
})(ke ||= {});
var Ae;
(function(e) {
	function t(e, t) {
		return {
			triggerKind: e,
			selectedCompletionInfo: t
		};
	}
	e.create = t;
})(Ae ||= {});
var je;
(function(e) {
	function n(e) {
		let n = e;
		return K.objectLiteral(n) && t.is(n.uri) && K.string(n.name);
	}
	e.is = n;
})(je ||= {});
var Me;
(function(e) {
	function t(e, t, n, r) {
		return new Ne(e, t, n, r);
	}
	e.create = t;
	function n(e) {
		let t = e;
		return !!(K.defined(t) && K.string(t.uri) && (K.undefined(t.languageId) || K.string(t.languageId)) && K.uinteger(t.lineCount) && K.func(t.getText) && K.func(t.positionAt) && K.func(t.offsetAt));
	}
	e.is = n;
	function r(e, t) {
		let n = e.getText(), r = i(t, (e, t) => {
			let n = e.range.start.line - t.range.start.line;
			return n === 0 ? e.range.start.character - t.range.start.character : n;
		}), a = n.length;
		for (let t = r.length - 1; t >= 0; t--) {
			let i = r[t], o = e.offsetAt(i.range.start), s = e.offsetAt(i.range.end);
			if (s <= a) n = n.substring(0, o) + i.newText + n.substring(s, n.length);
			else throw Error("Overlapping edit");
			a = o;
		}
		return n;
	}
	e.applyEdits = r;
	function i(e, t) {
		if (e.length <= 1) return e;
		let n = e.length / 2 | 0, r = e.slice(0, n), a = e.slice(n);
		i(r, t), i(a, t);
		let o = 0, s = 0, c = 0;
		for (; o < r.length && s < a.length;) t(r[o], a[s]) <= 0 ? e[c++] = r[o++] : e[c++] = a[s++];
		for (; o < r.length;) e[c++] = r[o++];
		for (; s < a.length;) e[c++] = a[s++];
		return e;
	}
})(Me ||= {});
var Ne = class {
	constructor(e, t, n, r) {
		this._uri = e, this._languageId = t, this._version = n, this._content = r, this._lineOffsets = void 0;
	}
	get uri() {
		return this._uri;
	}
	get languageId() {
		return this._languageId;
	}
	get version() {
		return this._version;
	}
	getText(e) {
		if (e) {
			let t = this.offsetAt(e.start), n = this.offsetAt(e.end);
			return this._content.substring(t, n);
		}
		return this._content;
	}
	update(e, t) {
		this._content = e.text, this._version = t, this._lineOffsets = void 0;
	}
	getLineOffsets() {
		if (this._lineOffsets === void 0) {
			let e = [], t = this._content, n = !0;
			for (let r = 0; r < t.length; r++) {
				n &&= (e.push(r), !1);
				let i = t.charAt(r);
				n = i === "\r" || i === "\n", i === "\r" && r + 1 < t.length && t.charAt(r + 1) === "\n" && r++;
			}
			n && t.length > 0 && e.push(t.length), this._lineOffsets = e;
		}
		return this._lineOffsets;
	}
	positionAt(e) {
		e = Math.max(Math.min(e, this._content.length), 0);
		let t = this.getLineOffsets(), n = 0, r = t.length;
		if (r === 0) return i.create(0, e);
		for (; n < r;) {
			let i = Math.floor((n + r) / 2);
			t[i] > e ? r = i : n = i + 1;
		}
		let a = n - 1;
		return i.create(a, e - t[a]);
	}
	offsetAt(e) {
		let t = this.getLineOffsets();
		if (e.line >= t.length) return this._content.length;
		if (e.line < 0) return 0;
		let n = t[e.line], r = e.line + 1 < t.length ? t[e.line + 1] : this._content.length;
		return Math.max(Math.min(n + e.character, r), n);
	}
	get lineCount() {
		return this.getLineOffsets().length;
	}
}, K;
(function(e) {
	let t = Object.prototype.toString;
	function n(e) {
		return e !== void 0;
	}
	e.defined = n;
	function r(e) {
		return e === void 0;
	}
	e.undefined = r;
	function i(e) {
		return e === !0 || e === !1;
	}
	e.boolean = i;
	function a(e) {
		return t.call(e) === "[object String]";
	}
	e.string = a;
	function o(e) {
		return t.call(e) === "[object Number]";
	}
	e.number = o;
	function s(e, n, r) {
		return t.call(e) === "[object Number]" && n <= e && e <= r;
	}
	e.numberRange = s;
	function c(e) {
		return t.call(e) === "[object Number]" && -2147483648 <= e && e <= 2147483647;
	}
	e.integer = c;
	function l(e) {
		return t.call(e) === "[object Number]" && 0 <= e && e <= 2147483647;
	}
	e.uinteger = l;
	function u(e) {
		return t.call(e) === "[object Function]";
	}
	e.func = u;
	function d(e) {
		return typeof e == "object" && !!e;
	}
	e.objectLiteral = d;
	function f(e, t) {
		return Array.isArray(e) && e.every(t);
	}
	e.typedArray = f;
})(K ||= {});
// node_modules/monaco-editor/esm/external/vscode-languageserver-textdocument/lib/esm/main.js
var q = class e {
	constructor(e, t, n, r) {
		this._uri = e, this._languageId = t, this._version = n, this._content = r, this._lineOffsets = void 0;
	}
	get uri() {
		return this._uri;
	}
	get languageId() {
		return this._languageId;
	}
	get version() {
		return this._version;
	}
	getText(e) {
		if (e) {
			let t = this.offsetAt(e.start), n = this.offsetAt(e.end);
			return this._content.substring(t, n);
		}
		return this._content;
	}
	update(t, n) {
		for (let n of t) if (e.isIncremental(n)) {
			let e = Z(n.range), t = this.offsetAt(e.start), r = this.offsetAt(e.end);
			this._content = this._content.substring(0, t) + n.text + this._content.substring(r, this._content.length);
			let i = Math.max(e.start.line, 0), a = Math.max(e.end.line, 0), o = this._lineOffsets, s = X(n.text, !1, t);
			if (a - i === s.length) for (let e = 0, t = s.length; e < t; e++) o[e + i + 1] = s[e];
			else s.length < 1e4 ? o.splice(i + 1, a - i, ...s) : this._lineOffsets = o = o.slice(0, i + 1).concat(s, o.slice(a + 1));
			let c = n.text.length - (r - t);
			if (c !== 0) for (let e = i + 1 + s.length, t = o.length; e < t; e++) o[e] = o[e] + c;
		} else if (e.isFull(n)) this._content = n.text, this._lineOffsets = void 0;
		else throw Error("Unknown change event received");
		this._version = n;
	}
	getLineOffsets() {
		return this._lineOffsets === void 0 && (this._lineOffsets = X(this._content, !0)), this._lineOffsets;
	}
	positionAt(e) {
		e = Math.max(Math.min(e, this._content.length), 0);
		let t = this.getLineOffsets(), n = 0, r = t.length;
		if (r === 0) return {
			line: 0,
			character: e
		};
		for (; n < r;) {
			let i = Math.floor((n + r) / 2);
			t[i] > e ? r = i : n = i + 1;
		}
		let i = n - 1;
		return {
			line: i,
			character: e - t[i]
		};
	}
	offsetAt(e) {
		let t = this.getLineOffsets();
		if (e.line >= t.length) return this._content.length;
		if (e.line < 0) return 0;
		let n = t[e.line], r = e.line + 1 < t.length ? t[e.line + 1] : this._content.length;
		return Math.max(Math.min(n + e.character, r), n);
	}
	get lineCount() {
		return this.getLineOffsets().length;
	}
	static isIncremental(e) {
		let t = e;
		return t != null && typeof t.text == "string" && t.range !== void 0 && (t.rangeLength === void 0 || typeof t.rangeLength == "number");
	}
	static isFull(e) {
		let t = e;
		return t != null && typeof t.text == "string" && t.range === void 0 && t.rangeLength === void 0;
	}
}, J;
(function(e) {
	function t(e, t, n, r) {
		return new q(e, t, n, r);
	}
	e.create = t;
	function n(e, t, n) {
		if (e instanceof q) return e.update(t, n), e;
		throw Error("TextDocument.update: document must be created by TextDocument.create");
	}
	e.update = n;
	function r(e, t) {
		let n = e.getText(), r = Y(t.map(Pe), (e, t) => {
			let n = e.range.start.line - t.range.start.line;
			return n === 0 ? e.range.start.character - t.range.start.character : n;
		}), i = 0, a = [];
		for (let t of r) {
			let r = e.offsetAt(t.range.start);
			if (r < i) throw Error("Overlapping edit");
			r > i && a.push(n.substring(i, r)), t.newText.length && a.push(t.newText), i = e.offsetAt(t.range.end);
		}
		return a.push(n.substr(i)), a.join("");
	}
	e.applyEdits = r;
})(J ||= {});
function Y(e, t) {
	if (e.length <= 1) return e;
	let n = e.length / 2 | 0, r = e.slice(0, n), i = e.slice(n);
	Y(r, t), Y(i, t);
	let a = 0, o = 0, s = 0;
	for (; a < r.length && o < i.length;) t(r[a], i[o]) <= 0 ? e[s++] = r[a++] : e[s++] = i[o++];
	for (; a < r.length;) e[s++] = r[a++];
	for (; o < i.length;) e[s++] = i[o++];
	return e;
}
function X(e, t, n = 0) {
	let r = t ? [n] : [];
	for (let t = 0; t < e.length; t++) {
		let i = e.charCodeAt(t);
		(i === 13 || i === 10) && (i === 13 && t + 1 < e.length && e.charCodeAt(t + 1) === 10 && t++, r.push(n + t + 1));
	}
	return r;
}
function Z(e) {
	let t = e.start, n = e.end;
	return t.line > n.line || t.line === n.line && t.character > n.character ? {
		start: n,
		end: t
	} : e;
}
function Pe(e) {
	let t = Z(e.range);
	return t === e.range ? e : {
		newText: e.newText,
		range: t
	};
}
// node_modules/monaco-editor/esm/external/@vscode/l10n/dist/browser.js
function Q(...e) {
	let t = e[0], n, r, i;
	if (typeof t == "string") n = t, r = t, e.splice(0, 1), i = !e || typeof e[0] != "object" ? e : e[0];
	else if (t instanceof Array) {
		let n = e.slice(1);
		if (t.length !== n.length + 1) throw Error("expected a string as the first argument to l10n.t");
		let r = t[0];
		for (let e = 1; e < t.length; e++) r += `{${e - 1}}` + t[e];
		return Q(r, ...n);
	} else r = t.message, n = r, t.comment && t.comment.length > 0 && (n += `/${Array.isArray(t.comment) ? t.comment.join("") : t.comment}`), i = t.args ?? {};
	return Ie(r, i);
}
var Fe = /{([^}]+)}/g;
function Ie(e, t) {
	return Object.keys(t).length === 0 ? e : e.replace(Fe, (e, n) => t[n] ?? e);
}
// node_modules/monaco-editor/esm/external/vscode-uri/lib/esm/index.js
var $;
(() => {
	var e = { 470: (e) => {
		function t(e) {
			if (typeof e != "string") throw TypeError("Path must be a string. Received " + JSON.stringify(e));
		}
		function n(e, t) {
			for (var n, r = "", i = 0, a = -1, o = 0, s = 0; s <= e.length; ++s) {
				if (s < e.length) n = e.charCodeAt(s);
				else {
					if (n === 47) break;
					n = 47;
				}
				if (n === 47) {
					if (a !== s - 1 && o !== 1) {
						if (a !== s - 1 && o === 2) {
							if (r.length < 2 || i !== 2 || r.charCodeAt(r.length - 1) !== 46 || r.charCodeAt(r.length - 2) !== 46) {
								if (r.length > 2) {
									var c = r.lastIndexOf("/");
									if (c !== r.length - 1) {
										c === -1 ? (r = "", i = 0) : i = (r = r.slice(0, c)).length - 1 - r.lastIndexOf("/"), a = s, o = 0;
										continue;
									}
								} else if (r.length === 2 || r.length === 1) {
									r = "", i = 0, a = s, o = 0;
									continue;
								}
							}
							t && (r.length > 0 ? r += "/.." : r = "..", i = 2);
						} else r.length > 0 ? r += "/" + e.slice(a + 1, s) : r = e.slice(a + 1, s), i = s - a - 1;
					}
					a = s, o = 0;
				} else n === 46 && o !== -1 ? ++o : o = -1;
			}
			return r;
		}
		var r = {
			resolve: function() {
				for (var e, r = "", i = !1, a = arguments.length - 1; a >= -1 && !i; a--) {
					var o;
					a >= 0 ? o = arguments[a] : (e === void 0 && (e = process.cwd()), o = e), t(o), o.length !== 0 && (r = o + "/" + r, i = o.charCodeAt(0) === 47);
				}
				return r = n(r, !i), i ? r.length > 0 ? "/" + r : "/" : r.length > 0 ? r : ".";
			},
			normalize: function(e) {
				if (t(e), e.length === 0) return ".";
				var r = e.charCodeAt(0) === 47, i = e.charCodeAt(e.length - 1) === 47;
				return (e = n(e, !r)).length !== 0 || r || (e = "."), e.length > 0 && i && (e += "/"), r ? "/" + e : e;
			},
			isAbsolute: function(e) {
				return t(e), e.length > 0 && e.charCodeAt(0) === 47;
			},
			join: function() {
				if (arguments.length === 0) return ".";
				for (var e, n = 0; n < arguments.length; ++n) {
					var i = arguments[n];
					t(i), i.length > 0 && (e === void 0 ? e = i : e += "/" + i);
				}
				return e === void 0 ? "." : r.normalize(e);
			},
			relative: function(e, n) {
				if (t(e), t(n), e === n || (e = r.resolve(e)) === (n = r.resolve(n))) return "";
				for (var i = 1; i < e.length && e.charCodeAt(i) === 47; ++i);
				for (var a = e.length, o = a - i, s = 1; s < n.length && n.charCodeAt(s) === 47; ++s);
				for (var c = n.length - s, l = o < c ? o : c, u = -1, d = 0; d <= l; ++d) {
					if (d === l) {
						if (c > l) {
							if (n.charCodeAt(s + d) === 47) return n.slice(s + d + 1);
							if (d === 0) return n.slice(s + d);
						} else o > l && (e.charCodeAt(i + d) === 47 ? u = d : d === 0 && (u = 0));
						break;
					}
					var f = e.charCodeAt(i + d);
					if (f !== n.charCodeAt(s + d)) break;
					f === 47 && (u = d);
				}
				var p = "";
				for (d = i + u + 1; d <= a; ++d) d !== a && e.charCodeAt(d) !== 47 || (p.length === 0 ? p += ".." : p += "/..");
				return p.length > 0 ? p + n.slice(s + u) : (s += u, n.charCodeAt(s) === 47 && ++s, n.slice(s));
			},
			_makeLong: function(e) {
				return e;
			},
			dirname: function(e) {
				if (t(e), e.length === 0) return ".";
				for (var n = e.charCodeAt(0), r = n === 47, i = -1, a = !0, o = e.length - 1; o >= 1; --o) if ((n = e.charCodeAt(o)) === 47) {
					if (!a) {
						i = o;
						break;
					}
				} else a = !1;
				return i === -1 ? r ? "/" : "." : r && i === 1 ? "//" : e.slice(0, i);
			},
			basename: function(e, n) {
				if (n !== void 0 && typeof n != "string") throw TypeError("\"ext\" argument must be a string");
				t(e);
				var r, i = 0, a = -1, o = !0;
				if (n !== void 0 && n.length > 0 && n.length <= e.length) {
					if (n.length === e.length && n === e) return "";
					var s = n.length - 1, c = -1;
					for (r = e.length - 1; r >= 0; --r) {
						var l = e.charCodeAt(r);
						if (l === 47) {
							if (!o) {
								i = r + 1;
								break;
							}
						} else c === -1 && (o = !1, c = r + 1), s >= 0 && (l === n.charCodeAt(s) ? --s == -1 && (a = r) : (s = -1, a = c));
					}
					return i === a ? a = c : a === -1 && (a = e.length), e.slice(i, a);
				}
				for (r = e.length - 1; r >= 0; --r) if (e.charCodeAt(r) === 47) {
					if (!o) {
						i = r + 1;
						break;
					}
				} else a === -1 && (o = !1, a = r + 1);
				return a === -1 ? "" : e.slice(i, a);
			},
			extname: function(e) {
				t(e);
				for (var n = -1, r = 0, i = -1, a = !0, o = 0, s = e.length - 1; s >= 0; --s) {
					var c = e.charCodeAt(s);
					if (c !== 47) i === -1 && (a = !1, i = s + 1), c === 46 ? n === -1 ? n = s : o !== 1 && (o = 1) : n !== -1 && (o = -1);
					else if (!a) {
						r = s + 1;
						break;
					}
				}
				return n === -1 || i === -1 || o === 0 || o === 1 && n === i - 1 && n === r + 1 ? "" : e.slice(n, i);
			},
			format: function(e) {
				if (typeof e != "object" || !e) throw TypeError("The \"pathObject\" argument must be of type Object. Received type " + typeof e);
				return function(e, t) {
					var n = t.dir || t.root, r = t.base || (t.name || "") + (t.ext || "");
					return n ? n === t.root ? n + r : n + "/" + r : r;
				}(0, e);
			},
			parse: function(e) {
				t(e);
				var n = {
					root: "",
					dir: "",
					base: "",
					ext: "",
					name: ""
				};
				if (e.length === 0) return n;
				var r, i = e.charCodeAt(0), a = i === 47;
				a ? (n.root = "/", r = 1) : r = 0;
				for (var o = -1, s = 0, c = -1, l = !0, u = e.length - 1, d = 0; u >= r; --u) if ((i = e.charCodeAt(u)) !== 47) c === -1 && (l = !1, c = u + 1), i === 46 ? o === -1 ? o = u : d !== 1 && (d = 1) : o !== -1 && (d = -1);
				else if (!l) {
					s = u + 1;
					break;
				}
				return o === -1 || c === -1 || d === 0 || d === 1 && o === c - 1 && o === s + 1 ? c !== -1 && (n.base = n.name = s === 0 && a ? e.slice(1, c) : e.slice(s, c)) : (s === 0 && a ? (n.name = e.slice(1, o), n.base = e.slice(1, c)) : (n.name = e.slice(s, o), n.base = e.slice(s, c)), n.ext = e.slice(o, c)), s > 0 ? n.dir = e.slice(0, s - 1) : a && (n.dir = "/"), n;
			},
			sep: "/",
			delimiter: ":",
			win32: null,
			posix: null
		};
		r.posix = r, e.exports = r;
	} }, t = {};
	function n(r) {
		var i = t[r];
		if (i !== void 0) return i.exports;
		var a = t[r] = { exports: {} };
		return e[r](a, a.exports, n), a.exports;
	}
	n.d = (e, t) => {
		for (var r in t) n.o(t, r) && !n.o(e, r) && Object.defineProperty(e, r, {
			enumerable: !0,
			get: t[r]
		});
	}, n.o = (e, t) => Object.prototype.hasOwnProperty.call(e, t), n.r = (e) => {
		typeof Symbol < "u" && Symbol.toStringTag && Object.defineProperty(e, Symbol.toStringTag, { value: "Module" }), Object.defineProperty(e, "__esModule", { value: !0 });
	};
	var r = {};
	(() => {
		let e;
		n.r(r), n.d(r, {
			URI: () => c,
			Utils: () => x
		}), typeof process == "object" ? e = process.platform === "win32" : typeof navigator == "object" && (e = navigator.userAgent.indexOf("Windows") >= 0);
		let t = /^\w[\w\d+.-]*$/, i = /^\//, a = /^\/\//;
		function o(e, n) {
			if (!e.scheme && n) throw Error(`[UriError]: Scheme is missing: {scheme: "", authority: "${e.authority}", path: "${e.path}", query: "${e.query}", fragment: "${e.fragment}"}`);
			if (e.scheme && !t.test(e.scheme)) throw Error("[UriError]: Scheme contains illegal characters.");
			if (e.path) {
				if (e.authority) {
					if (!i.test(e.path)) throw Error("[UriError]: If a URI contains an authority component, then the path component must either be empty or begin with a slash (\"/\") character");
				} else if (a.test(e.path)) throw Error("[UriError]: If a URI does not contain an authority component, then the path cannot begin with two slash characters (\"//\")");
			}
		}
		let s = /^(([^:/?#]+?):)?(\/\/([^/?#]*))?([^?#]*)(\?([^#]*))?(#(.*))?/;
		class c {
			static isUri(e) {
				return e instanceof c || !!e && typeof e.authority == "string" && typeof e.fragment == "string" && typeof e.path == "string" && typeof e.query == "string" && typeof e.scheme == "string" && typeof e.fsPath == "string" && typeof e.with == "function" && typeof e.toString == "function";
			}
			scheme;
			authority;
			path;
			query;
			fragment;
			constructor(e, t, n, r, i, a = !1) {
				typeof e == "object" ? (this.scheme = e.scheme || "", this.authority = e.authority || "", this.path = e.path || "", this.query = e.query || "", this.fragment = e.fragment || "") : (this.scheme = function(e, t) {
					return e || t ? e : "file";
				}(e, a), this.authority = t || "", this.path = function(e, t) {
					switch (e) {
						case "https":
						case "http":
						case "file": t ? t[0] !== "/" && (t = "/" + t) : t = "/";
					}
					return t;
				}(this.scheme, n || ""), this.query = r || "", this.fragment = i || "", o(this, a));
			}
			get fsPath() {
				return m(this);
			}
			with(e) {
				if (!e) return this;
				let { scheme: t, authority: n, path: r, query: i, fragment: a } = e;
				return t === void 0 ? t = this.scheme : t === null && (t = ""), n === void 0 ? n = this.authority : n === null && (n = ""), r === void 0 ? r = this.path : r === null && (r = ""), i === void 0 ? i = this.query : i === null && (i = ""), a === void 0 ? a = this.fragment : a === null && (a = ""), t === this.scheme && n === this.authority && r === this.path && i === this.query && a === this.fragment ? this : new u(t, n, r, i, a);
			}
			static parse(e, t = !1) {
				let n = s.exec(e);
				return n ? new u(n[2] || "", v(n[4] || ""), v(n[5] || ""), v(n[7] || ""), v(n[9] || ""), t) : new u("", "", "", "", "");
			}
			static file(t) {
				let n = "";
				if (e && (t = t.replace(/\\/g, "/")), t[0] === "/" && t[1] === "/") {
					let e = t.indexOf("/", 2);
					e === -1 ? (n = t.substring(2), t = "/") : (n = t.substring(2, e), t = t.substring(e) || "/");
				}
				return new u("file", n, t, "", "");
			}
			static from(e) {
				let t = new u(e.scheme, e.authority, e.path, e.query, e.fragment);
				return o(t, !0), t;
			}
			toString(e = !1) {
				return h(this, e);
			}
			toJSON() {
				return this;
			}
			static revive(e) {
				if (e) {
					if (e instanceof c) return e;
					{
						let t = new u(e);
						return t._formatted = e.external, t._fsPath = e._sep === l ? e.fsPath : null, t;
					}
				}
				return e;
			}
		}
		let l = e ? 1 : void 0;
		class u extends c {
			_formatted = null;
			_fsPath = null;
			get fsPath() {
				return this._fsPath ||= m(this), this._fsPath;
			}
			toString(e = !1) {
				return e ? h(this, !0) : (this._formatted ||= h(this, !1), this._formatted);
			}
			toJSON() {
				let e = { $mid: 1 };
				return this._fsPath && (e.fsPath = this._fsPath, e._sep = l), this._formatted && (e.external = this._formatted), this.path && (e.path = this.path), this.scheme && (e.scheme = this.scheme), this.authority && (e.authority = this.authority), this.query && (e.query = this.query), this.fragment && (e.fragment = this.fragment), e;
			}
		}
		let d = {
			58: "%3A",
			47: "%2F",
			63: "%3F",
			35: "%23",
			91: "%5B",
			93: "%5D",
			64: "%40",
			33: "%21",
			36: "%24",
			38: "%26",
			39: "%27",
			40: "%28",
			41: "%29",
			42: "%2A",
			43: "%2B",
			44: "%2C",
			59: "%3B",
			61: "%3D",
			32: "%20"
		};
		function f(e, t, n) {
			let r, i = -1;
			for (let a = 0; a < e.length; a++) {
				let o = e.charCodeAt(a);
				if (o >= 97 && o <= 122 || o >= 65 && o <= 90 || o >= 48 && o <= 57 || o === 45 || o === 46 || o === 95 || o === 126 || t && o === 47 || n && o === 91 || n && o === 93 || n && o === 58) i !== -1 && (r += encodeURIComponent(e.substring(i, a)), i = -1), r !== void 0 && (r += e.charAt(a));
				else {
					r === void 0 && (r = e.substr(0, a));
					let t = d[o];
					t === void 0 ? i === -1 && (i = a) : (i !== -1 && (r += encodeURIComponent(e.substring(i, a)), i = -1), r += t);
				}
			}
			return i !== -1 && (r += encodeURIComponent(e.substring(i))), r === void 0 ? e : r;
		}
		function p(e) {
			let t;
			for (let n = 0; n < e.length; n++) {
				let r = e.charCodeAt(n);
				r === 35 || r === 63 ? (t === void 0 && (t = e.substr(0, n)), t += d[r]) : t !== void 0 && (t += e[n]);
			}
			return t === void 0 ? e : t;
		}
		function m(t, n) {
			let r;
			return r = t.authority && t.path.length > 1 && t.scheme === "file" ? `//${t.authority}${t.path}` : t.path.charCodeAt(0) === 47 && (t.path.charCodeAt(1) >= 65 && t.path.charCodeAt(1) <= 90 || t.path.charCodeAt(1) >= 97 && t.path.charCodeAt(1) <= 122) && t.path.charCodeAt(2) === 58 ? t.path[1].toLowerCase() + t.path.substr(2) : t.path, e && (r = r.replace(/\//g, "\\")), r;
		}
		function h(e, t) {
			let n = t ? p : f, r = "", { scheme: i, authority: a, path: o, query: s, fragment: c } = e;
			if (i && (r += i, r += ":"), (a || i === "file") && (r += "/", r += "/"), a) {
				let e = a.indexOf("@");
				if (e !== -1) {
					let t = a.substr(0, e);
					a = a.substr(e + 1), e = t.lastIndexOf(":"), e === -1 ? r += n(t, !1, !1) : (r += n(t.substr(0, e), !1, !1), r += ":", r += n(t.substr(e + 1), !1, !0)), r += "@";
				}
				a = a.toLowerCase(), e = a.lastIndexOf(":"), e === -1 ? r += n(a, !1, !0) : (r += n(a.substr(0, e), !1, !0), r += a.substr(e));
			}
			if (o) {
				if (o.length >= 3 && o.charCodeAt(0) === 47 && o.charCodeAt(2) === 58) {
					let e = o.charCodeAt(1);
					e >= 65 && e <= 90 && (o = `/${String.fromCharCode(e + 32)}:${o.substr(3)}`);
				} else if (o.length >= 2 && o.charCodeAt(1) === 58) {
					let e = o.charCodeAt(0);
					e >= 65 && e <= 90 && (o = `${String.fromCharCode(e + 32)}:${o.substr(2)}`);
				}
				r += n(o, !0, !1);
			}
			return s && (r += "?", r += n(s, !1, !1)), c && (r += "#", r += t ? c : f(c, !1, !1)), r;
		}
		function g(e) {
			try {
				return decodeURIComponent(e);
			} catch {
				return e.length > 3 ? e.substr(0, 3) + g(e.substr(3)) : e;
			}
		}
		let _ = /(%[0-9A-Za-z][0-9A-Za-z])+/g;
		function v(e) {
			return e.match(_) ? e.replace(_, ((e) => g(e))) : e;
		}
		var y = n(470);
		let b = y.posix || y;
		var x;
		(function(e) {
			e.joinPath = function(e, ...t) {
				return e.with({ path: b.join(e.path, ...t) });
			}, e.resolvePath = function(e, ...t) {
				let n = e.path, r = !1;
				n[0] !== "/" && (n = "/" + n, r = !0);
				let i = b.resolve(n, ...t);
				return r && i[0] === "/" && !e.authority && (i = i.substring(1)), e.with({ path: i });
			}, e.dirname = function(e) {
				if (e.path.length === 0 || e.path === "/") return e;
				let t = b.dirname(e.path);
				return t.length === 1 && t.charCodeAt(0) === 46 && (t = ""), e.with({ path: t });
			}, e.basename = function(e) {
				return b.basename(e.path);
			}, e.extname = function(e) {
				return b.extname(e.path);
			};
		})(x ||= {});
	})(), $ = r;
})();
var { URI: Le, Utils: Re } = $;
export { L as C, D as E, R as S, y as T, o as _, H as a, a as b, P as c, _ as d, m as f, M as g, d as h, J as i, j as l, z as m, Re as n, B as o, I as p, Q as r, v as s, Le as t, N as u, k as v, S as w, U as x, i as y };
