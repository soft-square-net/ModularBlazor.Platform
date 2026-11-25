import { t as e } from "./shared-z2HCP1zm.js";
import { C as t, T as n, _ as r, b as i, c as a, d as o, f as s, g as c, h as l, i as u, l as d, r as f, t as p, v as m, x as h, y as g } from "./shared-BGgqrgXE.js";
// node_modules/monaco-editor/esm/external/jsonc-parser/lib/esm/impl/scanner.js
function _(e, t = !1) {
	let n = e.length, r = 0, i = "", a = 0, o = 16, s = 0, c = 0, l = 0, u = 0, d = 0;
	function f(t, n) {
		let i = 0, a = 0;
		for (; i < t;) {
			let t = e.charCodeAt(r);
			if (t >= 48 && t <= 57) a = a * 16 + t - 48;
			else if (t >= 65 && t <= 70) a = a * 16 + t - 65 + 10;
			else if (t >= 97 && t <= 102) a = a * 16 + t - 97 + 10;
			else break;
			r++, i++;
		}
		return i < t && (a = -1), a;
	}
	function p(e) {
		r = e, i = "", a = 0, o = 16, d = 0;
	}
	function m() {
		let t = r;
		if (e.charCodeAt(r) === 48) r++;
		else for (r++; r < e.length && b(e.charCodeAt(r));) r++;
		if (r < e.length && e.charCodeAt(r) === 46) {
			if (r++, r < e.length && b(e.charCodeAt(r))) for (r++; r < e.length && b(e.charCodeAt(r));) r++;
			else return d = 3, e.substring(t, r);
		}
		let n = r;
		if (r < e.length && (e.charCodeAt(r) === 69 || e.charCodeAt(r) === 101)) {
			if (r++, (r < e.length && e.charCodeAt(r) === 43 || e.charCodeAt(r) === 45) && r++, r < e.length && b(e.charCodeAt(r))) {
				for (r++; r < e.length && b(e.charCodeAt(r));) r++;
				n = r;
			} else d = 3;
		}
		return e.substring(t, n);
	}
	function h() {
		let t = "", i = r;
		for (;;) {
			if (r >= n) {
				t += e.substring(i, r), d = 2;
				break;
			}
			let a = e.charCodeAt(r);
			if (a === 34) {
				t += e.substring(i, r), r++;
				break;
			}
			if (a === 92) {
				if (t += e.substring(i, r), r++, r >= n) {
					d = 2;
					break;
				}
				switch (e.charCodeAt(r++)) {
					case 34:
						t += "\"";
						break;
					case 92:
						t += "\\";
						break;
					case 47:
						t += "/";
						break;
					case 98:
						t += "\b";
						break;
					case 102:
						t += "\f";
						break;
					case 110:
						t += "\n";
						break;
					case 114:
						t += "\r";
						break;
					case 116:
						t += "	";
						break;
					case 117:
						let e = f(4);
						e >= 0 ? t += String.fromCharCode(e) : d = 4;
						break;
					default: d = 5;
				}
				i = r;
			} else {
				if (a >= 0 && a <= 31) {
					if (y(a)) {
						t += e.substring(i, r), d = 2;
						break;
					}
					d = 6;
				}
				r++;
			}
		}
		return t;
	}
	function g() {
		if (i = "", d = 0, a = r, c = s, u = l, r >= n) return a = n, o = 17;
		let t = e.charCodeAt(r);
		if (v(t)) {
			do
				r++, i += String.fromCharCode(t), t = e.charCodeAt(r);
			while (v(t));
			return o = 15;
		}
		if (y(t)) return r++, i += String.fromCharCode(t), t === 13 && e.charCodeAt(r) === 10 && (r++, i += "\n"), s++, l = r, o = 14;
		switch (t) {
			case 123: return r++, o = 1;
			case 125: return r++, o = 2;
			case 91: return r++, o = 3;
			case 93: return r++, o = 4;
			case 58: return r++, o = 6;
			case 44: return r++, o = 5;
			case 34: return r++, i = h(), o = 10;
			case 47:
				let c = r - 1;
				if (e.charCodeAt(r + 1) === 47) {
					for (r += 2; r < n && !y(e.charCodeAt(r));) r++;
					return i = e.substring(c, r), o = 12;
				}
				if (e.charCodeAt(r + 1) === 42) {
					r += 2;
					let t = n - 1, a = !1;
					for (; r < t;) {
						let t = e.charCodeAt(r);
						if (t === 42 && e.charCodeAt(r + 1) === 47) {
							r += 2, a = !0;
							break;
						}
						r++, y(t) && (t === 13 && e.charCodeAt(r) === 10 && r++, s++, l = r);
					}
					return a || (r++, d = 1), i = e.substring(c, r), o = 13;
				}
				return i += String.fromCharCode(t), r++, o = 16;
			case 45: if (i += String.fromCharCode(t), r++, r === n || !b(e.charCodeAt(r))) return o = 16;
			case 48:
			case 49:
			case 50:
			case 51:
			case 52:
			case 53:
			case 54:
			case 55:
			case 56:
			case 57: return i += m(), o = 11;
			default:
				for (; r < n && _(t);) r++, t = e.charCodeAt(r);
				if (a !== r) {
					switch (i = e.substring(a, r), i) {
						case "true": return o = 8;
						case "false": return o = 9;
						case "null": return o = 7;
					}
					return o = 16;
				}
				return i += String.fromCharCode(t), r++, o = 16;
		}
	}
	function _(e) {
		if (v(e) || y(e)) return !1;
		switch (e) {
			case 125:
			case 93:
			case 123:
			case 91:
			case 34:
			case 58:
			case 44:
			case 47: return !1;
		}
		return !0;
	}
	function x() {
		let e;
		do
			e = g();
		while (e >= 12 && e <= 15);
		return e;
	}
	return {
		setPosition: p,
		getPosition: () => r,
		scan: t ? x : g,
		getToken: () => o,
		getTokenValue: () => i,
		getTokenOffset: () => a,
		getTokenLength: () => r - a,
		getTokenStartLine: () => c,
		getTokenStartCharacter: () => a - u,
		getTokenError: () => d
	};
}
function v(e) {
	return e === 32 || e === 9;
}
function y(e) {
	return e === 10 || e === 13;
}
function b(e) {
	return e >= 48 && e <= 57;
}
var x;
(function(e) {
	e[e.lineFeed = 10] = "lineFeed", e[e.carriageReturn = 13] = "carriageReturn", e[e.space = 32] = "space", e[e._0 = 48] = "_0", e[e._1 = 49] = "_1", e[e._2 = 50] = "_2", e[e._3 = 51] = "_3", e[e._4 = 52] = "_4", e[e._5 = 53] = "_5", e[e._6 = 54] = "_6", e[e._7 = 55] = "_7", e[e._8 = 56] = "_8", e[e._9 = 57] = "_9", e[e.a = 97] = "a", e[e.b = 98] = "b", e[e.c = 99] = "c", e[e.d = 100] = "d", e[e.e = 101] = "e", e[e.f = 102] = "f", e[e.g = 103] = "g", e[e.h = 104] = "h", e[e.i = 105] = "i", e[e.j = 106] = "j", e[e.k = 107] = "k", e[e.l = 108] = "l", e[e.m = 109] = "m", e[e.n = 110] = "n", e[e.o = 111] = "o", e[e.p = 112] = "p", e[e.q = 113] = "q", e[e.r = 114] = "r", e[e.s = 115] = "s", e[e.t = 116] = "t", e[e.u = 117] = "u", e[e.v = 118] = "v", e[e.w = 119] = "w", e[e.x = 120] = "x", e[e.y = 121] = "y", e[e.z = 122] = "z", e[e.A = 65] = "A", e[e.B = 66] = "B", e[e.C = 67] = "C", e[e.D = 68] = "D", e[e.E = 69] = "E", e[e.F = 70] = "F", e[e.G = 71] = "G", e[e.H = 72] = "H", e[e.I = 73] = "I", e[e.J = 74] = "J", e[e.K = 75] = "K", e[e.L = 76] = "L", e[e.M = 77] = "M", e[e.N = 78] = "N", e[e.O = 79] = "O", e[e.P = 80] = "P", e[e.Q = 81] = "Q", e[e.R = 82] = "R", e[e.S = 83] = "S", e[e.T = 84] = "T", e[e.U = 85] = "U", e[e.V = 86] = "V", e[e.W = 87] = "W", e[e.X = 88] = "X", e[e.Y = 89] = "Y", e[e.Z = 90] = "Z", e[e.asterisk = 42] = "asterisk", e[e.backslash = 92] = "backslash", e[e.closeBrace = 125] = "closeBrace", e[e.closeBracket = 93] = "closeBracket", e[e.colon = 58] = "colon", e[e.comma = 44] = "comma", e[e.dot = 46] = "dot", e[e.doubleQuote = 34] = "doubleQuote", e[e.minus = 45] = "minus", e[e.openBrace = 123] = "openBrace", e[e.openBracket = 91] = "openBracket", e[e.plus = 43] = "plus", e[e.slash = 47] = "slash", e[e.formFeed = 12] = "formFeed", e[e.tab = 9] = "tab";
})(x ||= {});
// node_modules/monaco-editor/esm/external/jsonc-parser/lib/esm/impl/string-intern.js
var S = Array(20).fill(0).map((e, t) => " ".repeat(t)), C = 200, w = {
	" ": {
		"\n": Array(C).fill(0).map((e, t) => "\n" + " ".repeat(t)),
		"\r": Array(C).fill(0).map((e, t) => "\r" + " ".repeat(t)),
		"\r\n": Array(C).fill(0).map((e, t) => "\r\n" + " ".repeat(t))
	},
	"	": {
		"\n": Array(C).fill(0).map((e, t) => "\n" + "	".repeat(t)),
		"\r": Array(C).fill(0).map((e, t) => "\r" + "	".repeat(t)),
		"\r\n": Array(C).fill(0).map((e, t) => "\r\n" + "	".repeat(t))
	}
}, ee = [
	"\n",
	"\r",
	"\r\n"
];
// node_modules/monaco-editor/esm/external/jsonc-parser/lib/esm/impl/format.js
function te(e, t, n) {
	let r, i, a, o, s;
	if (t) {
		for (o = t.offset, s = o + t.length, a = o; a > 0 && !re(e, a - 1);) a--;
		let c = s;
		for (; c < e.length && !re(e, c);) c++;
		i = e.substring(a, c), r = E(i, n);
	} else i = e, r = 0, a = 0, o = 0, s = e.length;
	let c = ne(n, e), l = ee.includes(c), u = 0, d = 0, f;
	f = n.insertSpaces ? S[n.tabSize || 4] ?? T(S[1], n.tabSize || 4) : "	";
	let p = f === "	" ? "	" : " ", m = _(i, !1), h = !1;
	function g() {
		if (u > 1) return T(c, u) + T(f, r + d);
		let e = f.length * (r + d);
		return !l || e > w[p][c].length ? c + T(f, r + d) : e <= 0 ? c : w[p][c][e];
	}
	function v() {
		let e = m.scan();
		for (u = 0; e === 15 || e === 14;) e === 14 && n.keepLines ? u += 1 : e === 14 && (u = 1), e = m.scan();
		return h = e === 16 || m.getTokenError() !== 0, e;
	}
	let y = [];
	function b(n, r, i) {
		!h && (!t || r < s && i > o) && e.substring(r, i) !== n && y.push({
			offset: r,
			length: i - r,
			content: n
		});
	}
	let x = v();
	if (n.keepLines && u > 0 && b(T(c, u), 0, 0), x !== 17) {
		let e = m.getTokenOffset() + a;
		b(f.length * r < 20 && n.insertSpaces ? S[f.length * r] : T(f, r), a, e);
	}
	for (; x !== 17;) {
		let e = m.getTokenOffset() + m.getTokenLength() + a, t = v(), r = "", i = !1;
		for (; u === 0 && (t === 12 || t === 13);) {
			let n = m.getTokenOffset() + a;
			b(S[1], e, n), e = m.getTokenOffset() + m.getTokenLength() + a, i = t === 12, r = i ? g() : "", t = v();
		}
		if (t === 2) x !== 1 && d--, n.keepLines && u > 0 || !n.keepLines && x !== 1 ? r = g() : n.keepLines && (r = S[1]);
		else if (t === 4) x !== 3 && d--, n.keepLines && u > 0 || !n.keepLines && x !== 3 ? r = g() : n.keepLines && (r = S[1]);
		else {
			switch (x) {
				case 3:
				case 1:
					d++, r = n.keepLines && u > 0 || !n.keepLines ? g() : S[1];
					break;
				case 5:
					r = n.keepLines && u > 0 || !n.keepLines ? g() : S[1];
					break;
				case 12:
					r = g();
					break;
				case 13:
					u > 0 ? r = g() : i || (r = S[1]);
					break;
				case 6:
					n.keepLines && u > 0 ? r = g() : i || (r = S[1]);
					break;
				case 10:
					n.keepLines && u > 0 ? r = g() : t === 6 && !i && (r = "");
					break;
				case 7:
				case 8:
				case 9:
				case 11:
				case 2:
				case 4:
					n.keepLines && u > 0 ? r = g() : (t === 12 || t === 13) && !i ? r = S[1] : t !== 5 && t !== 17 && (h = !0);
					break;
				case 16: h = !0;
			}
			u > 0 && (t === 12 || t === 13) && (r = g());
		}
		t === 17 && (r = n.keepLines && u > 0 ? g() : n.insertFinalNewline ? c : "");
		let o = m.getTokenOffset() + a;
		b(r, e, o), x = t;
	}
	return y;
}
function T(e, t) {
	let n = "";
	for (let r = 0; r < t; r++) n += e;
	return n;
}
function E(e, t) {
	let n = 0, r = 0, i = t.tabSize || 4;
	for (; n < e.length;) {
		let t = e.charAt(n);
		if (t === S[1]) r++;
		else if (t === "	") r += i;
		else break;
		n++;
	}
	return Math.floor(r / i);
}
function ne(e, t) {
	for (let e = 0; e < t.length; e++) {
		let n = t.charAt(e);
		if (n === "\r") return e + 1 < t.length && t.charAt(e + 1) === "\n" ? "\r\n" : "\r";
		if (n === "\n") return "\n";
	}
	return e && e.eol || "\n";
}
function re(e, t) {
	return "\r\n".indexOf(e.charAt(t)) !== -1;
}
// node_modules/monaco-editor/esm/external/jsonc-parser/lib/esm/impl/parser.js
var D;
(function(e) {
	e.DEFAULT = { allowTrailingComma: !1 };
})(D ||= {});
function ie(e, t = [], n = D.DEFAULT) {
	let r = null, i = [], a = [];
	function o(e) {
		Array.isArray(i) ? i.push(e) : r !== null && (i[r] = e);
	}
	return le(e, {
		onObjectBegin: () => {
			let e = {};
			o(e), a.push(i), i = e, r = null;
		},
		onObjectProperty: (e) => {
			r = e;
		},
		onObjectEnd: () => {
			i = a.pop();
		},
		onArrayBegin: () => {
			let e = [];
			o(e), a.push(i), i = e, r = null;
		},
		onArrayEnd: () => {
			i = a.pop();
		},
		onLiteralValue: o,
		onError: (e, n, r) => {
			t.push({
				error: e,
				offset: n,
				length: r
			});
		}
	}, n), i[0];
}
function ae(e) {
	if (!e.parent || !e.parent.children) return [];
	let t = ae(e.parent);
	if (e.parent.type === "property") {
		let n = e.parent.children[0].value;
		t.push(n);
	} else if (e.parent.type === "array") {
		let n = e.parent.children.indexOf(e);
		n !== -1 && t.push(n);
	}
	return t;
}
function oe(e) {
	switch (e.type) {
		case "array": return e.children.map(oe);
		case "object":
			let t = Object.create(null);
			for (let n of e.children) {
				let e = n.children[1];
				e && (t[n.children[0].value] = oe(e));
			}
			return t;
		case "null":
		case "string":
		case "number":
		case "boolean": return e.value;
		default: return;
	}
}
function se(e, t, n = !1) {
	return t >= e.offset && t < e.offset + e.length || n && t === e.offset + e.length;
}
function ce(e, t, n = !1) {
	if (se(e, t, n)) {
		let r = e.children;
		if (Array.isArray(r)) for (let e = 0; e < r.length && r[e].offset <= t; e++) {
			let i = ce(r[e], t, n);
			if (i) return i;
		}
		return e;
	}
}
function le(e, t, n = D.DEFAULT) {
	let r = _(e, !1), i = [];
	function a(e) {
		return e ? () => e(r.getTokenOffset(), r.getTokenLength(), r.getTokenStartLine(), r.getTokenStartCharacter()) : () => !0;
	}
	function o(e) {
		return e ? () => e(r.getTokenOffset(), r.getTokenLength(), r.getTokenStartLine(), r.getTokenStartCharacter(), () => i.slice()) : () => !0;
	}
	function s(e) {
		return e ? (t) => e(t, r.getTokenOffset(), r.getTokenLength(), r.getTokenStartLine(), r.getTokenStartCharacter()) : () => !0;
	}
	function c(e) {
		return e ? (t) => e(t, r.getTokenOffset(), r.getTokenLength(), r.getTokenStartLine(), r.getTokenStartCharacter(), () => i.slice()) : () => !0;
	}
	let l = o(t.onObjectBegin), u = c(t.onObjectProperty), d = a(t.onObjectEnd), f = o(t.onArrayBegin), p = a(t.onArrayEnd), m = c(t.onLiteralValue), h = s(t.onSeparator), g = a(t.onComment), v = s(t.onError), y = n && n.disallowComments, b = n && n.allowTrailingComma;
	function x() {
		for (;;) {
			let e = r.scan();
			switch (r.getTokenError()) {
				case 4:
					S(14);
					break;
				case 5:
					S(15);
					break;
				case 3:
					S(13);
					break;
				case 1:
					y || S(11);
					break;
				case 2:
					S(12);
					break;
				case 6: S(16);
			}
			switch (e) {
				case 12:
				case 13:
					y ? S(10) : g();
					break;
				case 16:
					S(1);
					break;
				case 15:
				case 14: break;
				default: return e;
			}
		}
	}
	function S(e, t = [], n = []) {
		if (v(e), t.length + n.length > 0) {
			let e = r.getToken();
			for (; e !== 17;) {
				if (t.indexOf(e) !== -1) {
					x();
					break;
				}
				if (n.indexOf(e) !== -1) break;
				e = x();
			}
		}
	}
	function C(e) {
		let t = r.getTokenValue();
		return e ? m(t) : (u(t), i.push(t)), x(), !0;
	}
	function w() {
		switch (r.getToken()) {
			case 11:
				let e = r.getTokenValue(), t = Number(e);
				isNaN(t) && (S(2), t = 0), m(t);
				break;
			case 7:
				m(null);
				break;
			case 8:
				m(!0);
				break;
			case 9:
				m(!1);
				break;
			default: return !1;
		}
		return x(), !0;
	}
	function ee() {
		return r.getToken() === 10 ? (C(!1), r.getToken() === 6 ? (h(":"), x(), E() || S(4, [], [2, 5])) : S(5, [], [2, 5]), i.pop(), !0) : (S(3, [], [2, 5]), !1);
	}
	function te() {
		l(), x();
		let e = !1;
		for (; r.getToken() !== 2 && r.getToken() !== 17;) {
			if (r.getToken() === 5) {
				if (e || S(4, [], []), h(","), x(), r.getToken() === 2 && b) break;
			} else e && S(6, [], []);
			ee() || S(4, [], [2, 5]), e = !0;
		}
		return d(), r.getToken() === 2 ? x() : S(7, [2], []), !0;
	}
	function T() {
		f(), x();
		let e = !0, t = !1;
		for (; r.getToken() !== 4 && r.getToken() !== 17;) {
			if (r.getToken() === 5) {
				if (t || S(4, [], []), h(","), x(), r.getToken() === 4 && b) break;
			} else t && S(6, [], []);
			e ? (i.push(0), e = !1) : i[i.length - 1]++, E() || S(4, [], [4, 5]), t = !0;
		}
		return p(), e || i.pop(), r.getToken() === 4 ? x() : S(8, [4], []), !0;
	}
	function E() {
		switch (r.getToken()) {
			case 3: return T();
			case 1: return te();
			case 10: return C(!0);
			default: return w();
		}
	}
	return x(), r.getToken() === 17 ? n.allowEmptyContent ? !0 : (S(4, [], []), !1) : E() ? (r.getToken() !== 17 && S(9, [], []), !0) : (S(4, [], []), !1);
}
// node_modules/monaco-editor/esm/external/jsonc-parser/lib/esm/main.js
var O = _, ue;
(function(e) {
	e[e.None = 0] = "None", e[e.UnexpectedEndOfComment = 1] = "UnexpectedEndOfComment", e[e.UnexpectedEndOfString = 2] = "UnexpectedEndOfString", e[e.UnexpectedEndOfNumber = 3] = "UnexpectedEndOfNumber", e[e.InvalidUnicode = 4] = "InvalidUnicode", e[e.InvalidEscapeCharacter = 5] = "InvalidEscapeCharacter", e[e.InvalidCharacter = 6] = "InvalidCharacter";
})(ue ||= {});
var de;
(function(e) {
	e[e.OpenBraceToken = 1] = "OpenBraceToken", e[e.CloseBraceToken = 2] = "CloseBraceToken", e[e.OpenBracketToken = 3] = "OpenBracketToken", e[e.CloseBracketToken = 4] = "CloseBracketToken", e[e.CommaToken = 5] = "CommaToken", e[e.ColonToken = 6] = "ColonToken", e[e.NullKeyword = 7] = "NullKeyword", e[e.TrueKeyword = 8] = "TrueKeyword", e[e.FalseKeyword = 9] = "FalseKeyword", e[e.StringLiteral = 10] = "StringLiteral", e[e.NumericLiteral = 11] = "NumericLiteral", e[e.LineCommentTrivia = 12] = "LineCommentTrivia", e[e.BlockCommentTrivia = 13] = "BlockCommentTrivia", e[e.LineBreakTrivia = 14] = "LineBreakTrivia", e[e.Trivia = 15] = "Trivia", e[e.Unknown = 16] = "Unknown", e[e.EOF = 17] = "EOF";
})(de ||= {});
var fe = ie, pe = ce, me = ae, he = oe, ge;
(function(e) {
	e[e.InvalidSymbol = 1] = "InvalidSymbol", e[e.InvalidNumberFormat = 2] = "InvalidNumberFormat", e[e.PropertyNameExpected = 3] = "PropertyNameExpected", e[e.ValueExpected = 4] = "ValueExpected", e[e.ColonExpected = 5] = "ColonExpected", e[e.CommaExpected = 6] = "CommaExpected", e[e.CloseBraceExpected = 7] = "CloseBraceExpected", e[e.CloseBracketExpected = 8] = "CloseBracketExpected", e[e.EndOfFileExpected = 9] = "EndOfFileExpected", e[e.InvalidCommentToken = 10] = "InvalidCommentToken", e[e.UnexpectedEndOfComment = 11] = "UnexpectedEndOfComment", e[e.UnexpectedEndOfString = 12] = "UnexpectedEndOfString", e[e.UnexpectedEndOfNumber = 13] = "UnexpectedEndOfNumber", e[e.InvalidUnicode = 14] = "InvalidUnicode", e[e.InvalidEscapeCharacter = 15] = "InvalidEscapeCharacter", e[e.InvalidCharacter = 16] = "InvalidCharacter";
})(ge ||= {});
function _e(e, t, n) {
	return te(e, t, n);
}
// node_modules/monaco-editor/esm/external/vscode-json-languageservice/lib/esm/utils/objects.js
function k(e, t) {
	if (e === t) return !0;
	if (e == null || t == null || typeof e != typeof t || typeof e != "object" || Array.isArray(e) !== Array.isArray(t)) return !1;
	let n, r;
	if (Array.isArray(e)) {
		if (e.length !== t.length) return !1;
		for (n = 0; n < e.length; n++) if (!k(e[n], t[n])) return !1;
	} else {
		let i = [];
		for (r in e) i.push(r);
		i.sort();
		let a = [];
		for (r in t) a.push(r);
		if (a.sort(), !k(i, a)) return !1;
		for (n = 0; n < i.length; n++) if (!k(e[i[n]], t[i[n]])) return !1;
	}
	return !0;
}
function A(e) {
	return typeof e == "number";
}
function j(e) {
	return e !== void 0;
}
function M(e) {
	return typeof e == "boolean";
}
function ve(e) {
	return typeof e == "string";
}
function N(e) {
	return typeof e == "object" && !!e && !Array.isArray(e);
}
// node_modules/monaco-editor/esm/external/vscode-json-languageservice/lib/esm/utils/strings.js
function ye(e, t) {
	if (e.length < t.length) return !1;
	for (let n = 0; n < t.length; n++) if (e[n] !== t[n]) return !1;
	return !0;
}
function P(e, t) {
	let n = e.length - t.length;
	return n > 0 ? e.lastIndexOf(t) === n : n === 0 && e === t;
}
function F(e) {
	let t = "";
	ye(e, "(?i)") && (e = e.substring(4), t = "i");
	try {
		return new RegExp(e, t + "u");
	} catch {
		try {
			return new RegExp(e, t);
		} catch {
			return;
		}
	}
}
function be(e) {
	let t = 0;
	for (let n = 0; n < e.length; n++) {
		t++;
		let r = e.charCodeAt(n);
		55296 <= r && r <= 56319 && n++;
	}
	return t;
}
// node_modules/monaco-editor/esm/external/vscode-json-languageservice/lib/esm/jsonLanguageTypes.js
var I;
(function(e) {
	e[e.Undefined = 0] = "Undefined", e[e.EnumValueMismatch = 1] = "EnumValueMismatch", e[e.Deprecated = 2] = "Deprecated", e[e.UnexpectedEndOfComment = 257] = "UnexpectedEndOfComment", e[e.UnexpectedEndOfString = 258] = "UnexpectedEndOfString", e[e.UnexpectedEndOfNumber = 259] = "UnexpectedEndOfNumber", e[e.InvalidUnicode = 260] = "InvalidUnicode", e[e.InvalidEscapeCharacter = 261] = "InvalidEscapeCharacter", e[e.InvalidCharacter = 262] = "InvalidCharacter", e[e.PropertyExpected = 513] = "PropertyExpected", e[e.CommaExpected = 514] = "CommaExpected", e[e.ColonExpected = 515] = "ColonExpected", e[e.ValueExpected = 516] = "ValueExpected", e[e.CommaOrCloseBacketExpected = 517] = "CommaOrCloseBacketExpected", e[e.CommaOrCloseBraceExpected = 518] = "CommaOrCloseBraceExpected", e[e.TrailingComma = 519] = "TrailingComma", e[e.DuplicateKey = 520] = "DuplicateKey", e[e.CommentNotPermitted = 521] = "CommentNotPermitted", e[e.PropertyKeysMustBeDoublequoted = 528] = "PropertyKeysMustBeDoublequoted", e[e.SchemaResolveError = 768] = "SchemaResolveError", e[e.SchemaUnsupportedFeature = 769] = "SchemaUnsupportedFeature";
})(I ||= {});
var L;
(function(e) {
	e[e.v3 = 3] = "v3", e[e.v4 = 4] = "v4", e[e.v6 = 6] = "v6", e[e.v7 = 7] = "v7", e[e.v2019_09 = 19] = "v2019_09", e[e.v2020_12 = 20] = "v2020_12";
})(L ||= {});
var xe;
(function(e) {
	e.LATEST = { textDocument: { completion: { completionItem: {
		documentationFormat: [m.Markdown, m.PlainText],
		commitCharactersSupport: !0,
		labelDetailsSupport: !0
	} } } };
})(xe ||= {});
// node_modules/monaco-editor/esm/external/vscode-json-languageservice/lib/esm/parser/jsonParser.js
var Se = {
	"color-hex": {
		errorMessage: f("Invalid color format. Use #RGB, #RGBA, #RRGGBB or #RRGGBBAA."),
		pattern: /^#([0-9A-Fa-f]{3,4}|([0-9A-Fa-f]{2}){3,4})$/
	},
	"date-time": {
		errorMessage: f("String is not a RFC3339 date-time."),
		pattern: /^(\d{4})-(0[1-9]|1[0-2])-(0[1-9]|[12][0-9]|3[01])T([01][0-9]|2[0-3]):([0-5][0-9]):([0-5][0-9]|60)(\.[0-9]+)?(Z|(\+|-)([01][0-9]|2[0-3]):([0-5][0-9]))$/i
	},
	date: {
		errorMessage: f("String is not a RFC3339 date."),
		pattern: /^(\d{4})-(0[1-9]|1[0-2])-(0[1-9]|[12][0-9]|3[01])$/i
	},
	time: {
		errorMessage: f("String is not a RFC3339 time."),
		pattern: /^([01][0-9]|2[0-3]):([0-5][0-9]):([0-5][0-9]|60)(\.[0-9]+)?(Z|(\+|-)([01][0-9]|2[0-3]):([0-5][0-9]))$/i
	},
	email: {
		errorMessage: f("String is not an e-mail address."),
		pattern: /^(([^<>()\[\]\\.,;:\s@"]+(\.[^<>()\[\]\\.,;:\s@"]+)*)|(".+"))@((\[[0-9]{1,3}\.[0-9]{1,3}\.[0-9]{1,3}\.[0-9]{1,3}])|(([a-zA-Z0-9-]+\.)+[a-zA-Z]{2,}))$/
	},
	hostname: {
		errorMessage: f("String is not a hostname."),
		pattern: /^(?=.{1,253}\.?$)[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?(?:\.[a-z0-9](?:[-0-9a-z]{0,61}[0-9a-z])?)*\.?$/i
	},
	ipv4: {
		errorMessage: f("String is not an IPv4 address."),
		pattern: /^(?:(?:25[0-5]|2[0-4]\d|1\d\d|[1-9]?\d)\.){3}(?:25[0-5]|2[0-4]\d|1\d\d|[1-9]?\d)$/
	},
	ipv6: {
		errorMessage: f("String is not an IPv6 address."),
		pattern: /^((([0-9a-f]{1,4}:){7}([0-9a-f]{1,4}|:))|(([0-9a-f]{1,4}:){6}(:[0-9a-f]{1,4}|((25[0-5]|2[0-4]\d|1\d\d|[1-9]?\d)(\.(25[0-5]|2[0-4]\d|1\d\d|[1-9]?\d)){3})|:))|(([0-9a-f]{1,4}:){5}(((:[0-9a-f]{1,4}){1,2})|:((25[0-5]|2[0-4]\d|1\d\d|[1-9]?\d)(\.(25[0-5]|2[0-4]\d|1\d\d|[1-9]?\d)){3})|:))|(([0-9a-f]{1,4}:){4}(((:[0-9a-f]{1,4}){1,3})|((:[0-9a-f]{1,4})?:((25[0-5]|2[0-4]\d|1\d\d|[1-9]?\d)(\.(25[0-5]|2[0-4]\d|1\d\d|[1-9]?\d)){3}))|:))|(([0-9a-f]{1,4}:){3}(((:[0-9a-f]{1,4}){1,4})|((:[0-9a-f]{1,4}){0,2}:((25[0-5]|2[0-4]\d|1\d\d|[1-9]?\d)(\.(25[0-5]|2[0-4]\d|1\d\d|[1-9]?\d)){3}))|:))|(([0-9a-f]{1,4}:){2}(((:[0-9a-f]{1,4}){1,5})|((:[0-9a-f]{1,4}){0,3}:((25[0-5]|2[0-4]\d|1\d\d|[1-9]?\d)(\.(25[0-5]|2[0-4]\d|1\d\d|[1-9]?\d)){3}))|:))|(([0-9a-f]{1,4}:){1}(((:[0-9a-f]{1,4}){1,6})|((:[0-9a-f]{1,4}){0,4}:((25[0-5]|2[0-4]\d|1\d\d|[1-9]?\d)(\.(25[0-5]|2[0-4]\d|1\d\d|[1-9]?\d)){3}))|:))|(:(((:[0-9a-f]{1,4}){1,7})|((:[0-9a-f]{1,4}){0,5}:((25[0-5]|2[0-4]\d|1\d\d|[1-9]?\d)(\.(25[0-5]|2[0-4]\d|1\d\d|[1-9]?\d)){3}))|:)))$/i
	}
}, R = class {
	constructor(e, t, n = 0) {
		this.offset = t, this.length = n, this.parent = e;
	}
	get children() {
		return [];
	}
	toString() {
		return "type: " + this.type + " (" + this.offset + "/" + this.length + ")" + (this.parent ? " parent: {" + this.parent.toString() + "}" : "");
	}
}, Ce = class extends R {
	constructor(e, t) {
		super(e, t), this.type = "null", this.value = null;
	}
}, we = class extends R {
	constructor(e, t, n) {
		super(e, n), this.type = "boolean", this.value = t;
	}
}, Te = class extends R {
	constructor(e, t) {
		super(e, t), this.type = "array", this.items = [];
	}
	get children() {
		return this.items;
	}
}, Ee = class extends R {
	constructor(e, t) {
		super(e, t), this.type = "number", this.isInteger = !0, this.value = NaN;
	}
}, z = class extends R {
	constructor(e, t, n) {
		super(e, t, n), this.type = "string", this.value = "";
	}
}, De = class extends R {
	constructor(e, t, n) {
		super(e, t), this.type = "property", this.colonOffset = -1, this.keyNode = n;
	}
	get children() {
		return this.valueNode ? [this.keyNode, this.valueNode] : [this.keyNode];
	}
}, Oe = class extends R {
	constructor(e, t) {
		super(e, t), this.type = "object", this.properties = [];
	}
	get children() {
		return this.properties;
	}
};
function B(e) {
	return M(e) ? e ? {} : { not: {} } : e;
}
var ke;
(function(e) {
	e[e.Key = 0] = "Key", e[e.Enum = 1] = "Enum";
})(ke ||= {});
var Ae = {
	"http://json-schema.org/draft-03/schema#": L.v3,
	"http://json-schema.org/draft-04/schema#": L.v4,
	"http://json-schema.org/draft-06/schema#": L.v6,
	"http://json-schema.org/draft-07/schema#": L.v7,
	"https://json-schema.org/draft/2019-09/schema": L.v2019_09,
	"https://json-schema.org/draft/2020-12/schema": L.v2020_12
}, je = class {
	constructor(e) {
		this.schemaDraft = e;
	}
}, Me = class e {
	constructor(e = -1, t) {
		this.focusOffset = e, this.exclude = t, this.schemas = [];
	}
	add(e) {
		this.schemas.push(e);
	}
	merge(e) {
		Array.prototype.push.apply(this.schemas, e.schemas);
	}
	include(e) {
		return (this.focusOffset === -1 || Pe(e, this.focusOffset)) && e !== this.exclude;
	}
	newSub() {
		return new e(-1, this.exclude);
	}
}, V = class {
	constructor() {}
	get schemas() {
		return [];
	}
	add(e) {}
	merge(e) {}
	include(e) {
		return !0;
	}
	newSub() {
		return this;
	}
};
V.instance = new V();
var H = class {
	constructor() {
		this.problems = [], this.propertiesMatches = 0, this.processedProperties = /* @__PURE__ */ new Set(), this.propertiesValueMatches = 0, this.primaryValueMatches = 0, this.enumValueMatch = !1, this.enumValues = void 0;
	}
	hasProblems() {
		return !!this.problems.length;
	}
	merge(e) {
		this.problems = this.problems.concat(e.problems), this.propertiesMatches += e.propertiesMatches, this.propertiesValueMatches += e.propertiesValueMatches, this.mergeProcessedProperties(e);
	}
	mergeEnumValues(e) {
		if (!this.enumValueMatch && !e.enumValueMatch && this.enumValues && e.enumValues) {
			this.enumValues = this.enumValues.concat(e.enumValues);
			for (let e of this.problems) e.code === I.EnumValueMismatch && (e.message = f("Value is not accepted. Valid values: {0}.", this.enumValues.map((e) => JSON.stringify(e)).join(", ")));
		}
	}
	mergePropertyMatch(e) {
		this.problems = this.problems.concat(e.problems), this.propertiesMatches++, (e.enumValueMatch || !e.hasProblems() && e.propertiesMatches) && this.propertiesValueMatches++, e.enumValueMatch && e.enumValues && e.enumValues.length === 1 && this.primaryValueMatches++;
	}
	mergeProcessedProperties(e) {
		e.processedProperties.forEach((e) => this.processedProperties.add(e));
	}
	compare(e) {
		let t = this.hasProblems();
		return t === e.hasProblems() ? this.enumValueMatch === e.enumValueMatch ? this.primaryValueMatches === e.primaryValueMatches ? this.propertiesValueMatches === e.propertiesValueMatches ? this.propertiesMatches - e.propertiesMatches : this.propertiesValueMatches - e.propertiesValueMatches : this.primaryValueMatches - e.primaryValueMatches : e.enumValueMatch ? -1 : 1 : t ? -1 : 1;
	}
};
function Ne(e, t = []) {
	return new Fe(e, t, []);
}
function U(e) {
	return he(e);
}
function W(e) {
	return me(e);
}
function Pe(e, t, n = !1) {
	return t >= e.offset && t < e.offset + e.length || n && t === e.offset + e.length;
}
var Fe = class {
	constructor(e, t = [], n = []) {
		this.root = e, this.syntaxErrors = t, this.comments = n;
	}
	getNodeFromOffset(e, t = !1) {
		if (this.root) return pe(this.root, e, t);
	}
	visit(e) {
		if (this.root) {
			let t = (n) => {
				let r = e(n), i = n.children;
				if (Array.isArray(i)) for (let e = 0; e < i.length && r; e++) r = t(i[e]);
				return r;
			};
			t(this.root);
		}
	}
	validate(e, t, n = s.Warning, r) {
		if (this.root && t) {
			let a = new H();
			return G(this.root, t, a, V.instance, new je(r ?? Ie(t))), a.problems.map((t) => {
				let r = i.create(e.positionAt(t.location.offset), e.positionAt(t.location.offset + t.location.length));
				return o.create(r, t.message, t.severity ?? n, t.code);
			});
		}
	}
	getMatchingSchemas(e, t = -1, n) {
		if (this.root && e) {
			let r = new Me(t, n), i = new je(Ie(e));
			return G(this.root, e, new H(), r, i), r.schemas;
		}
		return [];
	}
};
function Ie(e, t = L.v2020_12) {
	let n = e.$schema;
	return n ? Ae[n] ?? t : t;
}
function G(e, t, n, r, i) {
	if (!e || !r.include(e)) return;
	if (e.type === "property") return G(e.valueNode, t, n, r, i);
	let a = e;
	switch (o(), a.type) {
		case "object":
			d(a);
			break;
		case "array":
			u(a);
			break;
		case "string":
			l(a);
			break;
		case "number": c(a);
	}
	r.add({
		node: a,
		schema: t
	});
	function o() {
		function e(e) {
			return a.type === e || e === "integer" && a.type === "number" && a.isInteger;
		}
		if (Array.isArray(t.type) ? t.type.some(e) || n.problems.push({
			location: {
				offset: a.offset,
				length: a.length
			},
			message: t.errorMessage || f("Incorrect type. Expected one of {0}.", t.type.join(", "))
		}) : t.type && (e(t.type) || n.problems.push({
			location: {
				offset: a.offset,
				length: a.length
			},
			message: t.errorMessage || f("Incorrect type. Expected \"{0}\".", t.type)
		})), Array.isArray(t.allOf)) for (let e of t.allOf) {
			let t = new H(), o = r.newSub();
			G(a, B(e), t, o, i), n.merge(t), r.merge(o);
		}
		let o = B(t.not);
		if (o) {
			let e = new H(), s = r.newSub();
			G(a, o, e, s, i), e.hasProblems() || n.problems.push({
				location: {
					offset: a.offset,
					length: a.length
				},
				message: t.errorMessage || f("Matches a schema that is not allowed.")
			});
			for (let e of s.schemas) e.inverted = !e.inverted, r.add(e);
		}
		let c = (e, t) => {
			let o = [], s;
			for (let n of e) {
				let e = B(n), c = new H(), l = r.newSub();
				if (G(a, e, c, l, i), c.hasProblems() || o.push(e), !s) s = {
					schema: e,
					validationResult: c,
					matchingSchemas: l
				};
				else if (!t && !c.hasProblems() && !s.validationResult.hasProblems()) s.matchingSchemas.merge(l), s.validationResult.propertiesMatches += c.propertiesMatches, s.validationResult.propertiesValueMatches += c.propertiesValueMatches, s.validationResult.mergeProcessedProperties(c);
				else {
					let t = c.compare(s.validationResult);
					t > 0 ? s = {
						schema: e,
						validationResult: c,
						matchingSchemas: l
					} : t === 0 && (s.matchingSchemas.merge(l), s.validationResult.mergeEnumValues(c));
				}
			}
			return o.length > 1 && t && n.problems.push({
				location: {
					offset: a.offset,
					length: 1
				},
				message: f("Matches multiple schemas when only one must validate.")
			}), s && (n.merge(s.validationResult), r.merge(s.matchingSchemas)), o.length;
		};
		Array.isArray(t.anyOf) && c(t.anyOf, !1), Array.isArray(t.oneOf) && c(t.oneOf, !0);
		let l = (e) => {
			let t = new H(), o = r.newSub();
			G(a, B(e), t, o, i), n.merge(t), r.merge(o);
		}, u = (e, t, o) => {
			let s = B(e), c = new H(), u = r.newSub();
			G(a, s, c, u, i), r.merge(u), n.mergeProcessedProperties(c), c.hasProblems() ? o && l(o) : t && l(t);
		}, d = B(t.if);
		if (d && u(d, B(t.then), B(t.else)), Array.isArray(t.enum)) {
			let e = U(a), r = !1;
			for (let n of t.enum) if (k(e, n)) {
				r = !0;
				break;
			}
			n.enumValues = t.enum, n.enumValueMatch = r, r || n.problems.push({
				location: {
					offset: a.offset,
					length: a.length
				},
				code: I.EnumValueMismatch,
				message: t.errorMessage || f("Value is not accepted. Valid values: {0}.", t.enum.map((e) => JSON.stringify(e)).join(", "))
			});
		}
		j(t.const) && (k(U(a), t.const) ? n.enumValueMatch = !0 : (n.problems.push({
			location: {
				offset: a.offset,
				length: a.length
			},
			code: I.EnumValueMismatch,
			message: t.errorMessage || f("Value must be {0}.", JSON.stringify(t.const))
		}), n.enumValueMatch = !1), n.enumValues = [t.const]);
		let p = t.deprecationMessage;
		if (p || t.deprecated) {
			p ||= f("Value is deprecated");
			let e = a.parent?.type === "property" ? a.parent : a;
			n.problems.push({
				location: {
					offset: e.offset,
					length: e.length
				},
				severity: s.Warning,
				message: p,
				code: I.Deprecated
			});
		}
	}
	function c(e) {
		let r = e.value;
		function i(e) {
			let t = /^(-?\d+)(?:\.(\d+))?(?:e([-+]\d+))?$/.exec(e.toString());
			return t && {
				value: Number(t[1] + (t[2] || "")),
				multiplier: (t[2]?.length || 0) - (parseInt(t[3]) || 0)
			};
		}
		if (A(t.multipleOf)) {
			let a = -1;
			if (Number.isInteger(t.multipleOf)) a = r % t.multipleOf;
			else {
				let e = i(t.multipleOf), n = i(r);
				if (e && n) {
					let t = 10 ** Math.abs(n.multiplier - e.multiplier);
					n.multiplier < e.multiplier ? n.value *= t : e.value *= t, a = n.value % e.value;
				}
			}
			a !== 0 && n.problems.push({
				location: {
					offset: e.offset,
					length: e.length
				},
				message: f("Value is not divisible by {0}.", t.multipleOf)
			});
		}
		function a(e, t) {
			if (A(t)) return t;
			if (M(t) && t) return e;
		}
		function o(e, t) {
			if (!M(t) || !t) return e;
		}
		let s = a(t.minimum, t.exclusiveMinimum);
		A(s) && r <= s && n.problems.push({
			location: {
				offset: e.offset,
				length: e.length
			},
			message: f("Value is below the exclusive minimum of {0}.", s)
		});
		let c = a(t.maximum, t.exclusiveMaximum);
		A(c) && r >= c && n.problems.push({
			location: {
				offset: e.offset,
				length: e.length
			},
			message: f("Value is above the exclusive maximum of {0}.", c)
		});
		let l = o(t.minimum, t.exclusiveMinimum);
		A(l) && r < l && n.problems.push({
			location: {
				offset: e.offset,
				length: e.length
			},
			message: f("Value is below the minimum of {0}.", l)
		});
		let u = o(t.maximum, t.exclusiveMaximum);
		A(u) && r > u && n.problems.push({
			location: {
				offset: e.offset,
				length: e.length
			},
			message: f("Value is above the maximum of {0}.", u)
		});
	}
	function l(e) {
		if (A(t.minLength) && be(e.value) < t.minLength && n.problems.push({
			location: {
				offset: e.offset,
				length: e.length
			},
			message: f("String is shorter than the minimum length of {0}.", t.minLength)
		}), A(t.maxLength) && be(e.value) > t.maxLength && n.problems.push({
			location: {
				offset: e.offset,
				length: e.length
			},
			message: f("String is longer than the maximum length of {0}.", t.maxLength)
		}), ve(t.pattern) && (F(t.pattern)?.test(e.value) || n.problems.push({
			location: {
				offset: e.offset,
				length: e.length
			},
			message: t.patternErrorMessage || t.errorMessage || f("String does not match the pattern of \"{0}\".", t.pattern)
		})), t.format) switch (t.format) {
			case "uri":
			case "uri-reference":
				{
					let r;
					if (!e.value) r = f("URI expected.");
					else {
						let n = /^(([^:/?#]+?):)?(\/\/([^/?#]*))?([^?#]*)(\?([^#]*))?(#(.*))?/.exec(e.value);
						n ? !n[2] && t.format === "uri" && (r = f("URI with a scheme is expected.")) : r = f("URI is expected.");
					}
					r && n.problems.push({
						location: {
							offset: e.offset,
							length: e.length
						},
						message: t.patternErrorMessage || t.errorMessage || f("String is not a URI: {0}", r)
					});
				}
				break;
			case "color-hex":
			case "date-time":
			case "date":
			case "time":
			case "email":
			case "hostname":
			case "ipv4":
			case "ipv6":
				let r = Se[t.format];
				e.value && r.pattern.exec(e.value) || n.problems.push({
					location: {
						offset: e.offset,
						length: e.length
					},
					message: t.patternErrorMessage || t.errorMessage || r.errorMessage
				});
		}
	}
	function u(e) {
		let a, o;
		i.schemaDraft >= L.v2020_12 ? (a = t.prefixItems, o = Array.isArray(t.items) ? void 0 : t.items) : (a = Array.isArray(t.items) ? t.items : void 0, o = Array.isArray(t.items) ? t.additionalItems : t.items);
		let s = 0;
		if (a !== void 0) {
			let t = Math.min(a.length, e.items.length);
			for (; s < t; s++) {
				let t = a[s], o = B(t), c = new H(), l = e.items[s];
				l && (G(l, o, c, r, i), n.mergePropertyMatch(c)), n.processedProperties.add(String(s));
			}
		}
		if (o !== void 0 && s < e.items.length) {
			if (typeof o == "boolean") for (o === !1 && n.problems.push({
				location: {
					offset: e.offset,
					length: e.length
				},
				message: f("Array has too many items according to schema. Expected {0} or fewer.", s)
			}); s < e.items.length; s++) n.processedProperties.add(String(s)), n.propertiesValueMatches++;
			else for (; s < e.items.length; s++) {
				let t = new H();
				G(e.items[s], o, t, r, i), n.mergePropertyMatch(t), n.processedProperties.add(String(s));
			}
		}
		let c = B(t.contains);
		if (c) {
			let r = 0;
			for (let t = 0; t < e.items.length; t++) {
				let a = e.items[t], o = new H();
				G(a, c, o, V.instance, i), o.hasProblems() || (r++, i.schemaDraft >= L.v2020_12 && n.processedProperties.add(String(t)));
			}
			r === 0 && !A(t.minContains) && n.problems.push({
				location: {
					offset: e.offset,
					length: e.length
				},
				message: t.errorMessage || f("Array does not contain required item.")
			}), A(t.minContains) && r < t.minContains && n.problems.push({
				location: {
					offset: e.offset,
					length: e.length
				},
				message: t.errorMessage || f("Array has too few items that match the contains contraint. Expected {0} or more.", t.minContains)
			}), A(t.maxContains) && r > t.maxContains && n.problems.push({
				location: {
					offset: e.offset,
					length: e.length
				},
				message: t.errorMessage || f("Array has too many items that match the contains contraint. Expected {0} or less.", t.maxContains)
			});
		}
		let l = t.unevaluatedItems;
		if (l !== void 0) for (let a = 0; a < e.items.length; a++) {
			if (!n.processedProperties.has(String(a))) {
				if (l === !1) n.problems.push({
					location: {
						offset: e.offset,
						length: e.length
					},
					message: f("Item does not match any validation rule from the array.")
				});
				else {
					let o = new H();
					G(e.items[a], t.unevaluatedItems, o, r, i), n.mergePropertyMatch(o);
				}
			}
			n.processedProperties.add(String(a)), n.propertiesValueMatches++;
		}
		if (A(t.minItems) && e.items.length < t.minItems && n.problems.push({
			location: {
				offset: e.offset,
				length: e.length
			},
			message: f("Array has too few items. Expected {0} or more.", t.minItems)
		}), A(t.maxItems) && e.items.length > t.maxItems && n.problems.push({
			location: {
				offset: e.offset,
				length: e.length
			},
			message: f("Array has too many items. Expected {0} or fewer.", t.maxItems)
		}), t.uniqueItems === !0) {
			let t = U(e);
			function r() {
				for (let e = 0; e < t.length - 1; e++) {
					let n = t[e];
					for (let r = e + 1; r < t.length; r++) if (k(n, t[r])) return !0;
				}
				return !1;
			}
			r() && n.problems.push({
				location: {
					offset: e.offset,
					length: e.length
				},
				message: f("Array has duplicate items.")
			});
		}
	}
	function d(e) {
		let a = Object.create(null), o = /* @__PURE__ */ new Set();
		for (let t of e.properties) {
			let e = t.keyNode.value;
			a[e] = t.valueNode, o.add(e);
		}
		if (Array.isArray(t.required)) {
			for (let r of t.required) if (!a[r]) {
				let t = e.parent && e.parent.type === "property" && e.parent.keyNode, i = t ? {
					offset: t.offset,
					length: t.length
				} : {
					offset: e.offset,
					length: 1
				};
				n.problems.push({
					location: i,
					message: f("Missing property \"{0}\".", r)
				});
			}
		}
		let s = (e) => {
			o.delete(e), n.processedProperties.add(e);
		};
		if (t.properties) for (let e of Object.keys(t.properties)) {
			s(e);
			let o = t.properties[e], c = a[e];
			if (c) {
				if (M(o)) {
					if (o) n.propertiesMatches++, n.propertiesValueMatches++;
					else {
						let r = c.parent;
						n.problems.push({
							location: {
								offset: r.keyNode.offset,
								length: r.keyNode.length
							},
							message: t.errorMessage || f("Property {0} is not allowed.", e)
						});
					}
				} else {
					let e = new H();
					G(c, o, e, r, i), n.mergePropertyMatch(e);
				}
			}
		}
		if (t.patternProperties) for (let e of Object.keys(t.patternProperties)) {
			let c = F(e);
			if (c) {
				let l = [];
				for (let s of o) if (c.test(s)) {
					l.push(s);
					let o = a[s];
					if (o) {
						let a = t.patternProperties[e];
						if (M(a)) {
							if (a) n.propertiesMatches++, n.propertiesValueMatches++;
							else {
								let e = o.parent;
								n.problems.push({
									location: {
										offset: e.keyNode.offset,
										length: e.keyNode.length
									},
									message: t.errorMessage || f("Property {0} is not allowed.", s)
								});
							}
						} else {
							let e = new H();
							G(o, a, e, r, i), n.mergePropertyMatch(e);
						}
					}
				}
				l.forEach(s);
			}
		}
		let c = t.additionalProperties;
		if (c !== void 0) for (let e of o) {
			s(e);
			let o = a[e];
			if (o) {
				if (c === !1) {
					let r = o.parent;
					n.problems.push({
						location: {
							offset: r.keyNode.offset,
							length: r.keyNode.length
						},
						message: t.errorMessage || f("Property {0} is not allowed.", e)
					});
				} else if (c !== !0) {
					let e = new H();
					G(o, c, e, r, i), n.mergePropertyMatch(e);
				}
			}
		}
		let l = t.unevaluatedProperties;
		if (l !== void 0) {
			let e = [];
			for (let s of o) if (!n.processedProperties.has(s)) {
				e.push(s);
				let o = a[s];
				if (o) {
					if (l === !1) {
						let e = o.parent;
						n.problems.push({
							location: {
								offset: e.keyNode.offset,
								length: e.keyNode.length
							},
							message: t.errorMessage || f("Property {0} is not allowed.", s)
						});
					} else if (l !== !0) {
						let e = new H();
						G(o, l, e, r, i), n.mergePropertyMatch(e);
					}
				}
			}
			e.forEach(s);
		}
		if (A(t.maxProperties) && e.properties.length > t.maxProperties && n.problems.push({
			location: {
				offset: e.offset,
				length: e.length
			},
			message: f("Object has more properties than limit of {0}.", t.maxProperties)
		}), A(t.minProperties) && e.properties.length < t.minProperties && n.problems.push({
			location: {
				offset: e.offset,
				length: e.length
			},
			message: f("Object has fewer properties than the required number of {0}", t.minProperties)
		}), t.dependentRequired) for (let e in t.dependentRequired) {
			let n = a[e], r = t.dependentRequired[e];
			n && Array.isArray(r) && d(e, r);
		}
		if (t.dependentSchemas) for (let e in t.dependentSchemas) {
			let n = a[e], r = t.dependentSchemas[e];
			n && N(r) && d(e, r);
		}
		if (t.dependencies) for (let e in t.dependencies) a[e] && d(e, t.dependencies[e]);
		let u = B(t.propertyNames);
		if (u) for (let t of e.properties) {
			let e = t.keyNode;
			e && G(e, u, n, V.instance, i);
		}
		function d(t, o) {
			if (Array.isArray(o)) for (let r of o) a[r] ? n.propertiesValueMatches++ : n.problems.push({
				location: {
					offset: e.offset,
					length: e.length
				},
				message: f("Object is missing property {0} required by property {1}.", r, t)
			});
			else {
				let t = B(o);
				if (t) {
					let a = new H();
					G(e, t, a, r, i), n.mergePropertyMatch(a);
				}
			}
		}
	}
}
function Le(e, t) {
	let n = [], r = -1, a = e.getText(), c = O(a, !1), l = t && t.collectComments ? [] : void 0;
	function u() {
		for (;;) {
			let t = c.scan();
			switch (m(), t) {
				case 12:
				case 13:
					Array.isArray(l) && l.push(i.create(e.positionAt(c.getTokenOffset()), e.positionAt(c.getTokenOffset() + c.getTokenLength())));
					break;
				case 15:
				case 14: break;
				default: return t;
			}
		}
	}
	function d(t, a, c, l, u = s.Error) {
		if (n.length === 0 || c !== r) {
			let s = i.create(e.positionAt(c), e.positionAt(l));
			n.push(o.create(s, t, u, a, e.languageId)), r = c;
		}
	}
	function p(e, t, n = void 0, r = [], i = []) {
		let o = c.getTokenOffset(), s = c.getTokenOffset() + c.getTokenLength();
		if (o === s && o > 0) {
			for (o--; o > 0 && /\s/.test(a.charAt(o));) o--;
			s = o + 1;
		}
		if (d(e, t, o, s), n && h(n, !1), r.length + i.length > 0) {
			let e = c.getToken();
			for (; e !== 17;) {
				if (r.indexOf(e) !== -1) {
					u();
					break;
				}
				if (i.indexOf(e) !== -1) break;
				e = u();
			}
		}
		return n;
	}
	function m() {
		switch (c.getTokenError()) {
			case 4: return p(f("Invalid unicode sequence in string."), I.InvalidUnicode), !0;
			case 5: return p(f("Invalid escape character in string."), I.InvalidEscapeCharacter), !0;
			case 3: return p(f("Unexpected end of number."), I.UnexpectedEndOfNumber), !0;
			case 1: return p(f("Unexpected end of comment."), I.UnexpectedEndOfComment), !0;
			case 2: return p(f("Unexpected end of string."), I.UnexpectedEndOfString), !0;
			case 6: return p(f("Invalid characters in string. Control characters must be escaped."), I.InvalidCharacter), !0;
		}
		return !1;
	}
	function h(e, t) {
		return e.length = c.getTokenOffset() + c.getTokenLength() - e.offset, t && u(), e;
	}
	function g(e) {
		if (c.getToken() !== 3) return;
		let t = new Te(e, c.getTokenOffset());
		u();
		let n = !1;
		for (; c.getToken() !== 4 && c.getToken() !== 17;) {
			if (c.getToken() === 5) {
				n || p(f("Value expected"), I.ValueExpected);
				let e = c.getTokenOffset();
				if (u(), c.getToken() === 4) {
					n && d(f("Trailing comma"), I.TrailingComma, e, e + 1);
					continue;
				}
			} else n && p(f("Expected comma"), I.CommaExpected);
			let e = C(t);
			e ? t.items.push(e) : p(f("Value expected"), I.ValueExpected, void 0, [], [4, 5]), n = !0;
		}
		return c.getToken() === 4 ? h(t, !0) : p(f("Expected comma or closing bracket"), I.CommaOrCloseBacketExpected, t);
	}
	let _ = new z(void 0, 0, 0);
	function v(t, n) {
		let r = new De(t, c.getTokenOffset(), _), i = b(r);
		if (!i) {
			if (c.getToken() === 16) {
				p(f("Property keys must be doublequoted"), I.PropertyKeysMustBeDoublequoted);
				let e = new z(r, c.getTokenOffset(), c.getTokenLength());
				e.value = c.getTokenValue(), i = e, u();
			} else return;
		}
		if (r.keyNode = i, i.value !== "//") {
			let e = n[i.value];
			e ? (d(f("Duplicate object key"), I.DuplicateKey, r.keyNode.offset, r.keyNode.offset + r.keyNode.length, s.Warning), N(e) && d(f("Duplicate object key"), I.DuplicateKey, e.keyNode.offset, e.keyNode.offset + e.keyNode.length, s.Warning), n[i.value] = !0) : n[i.value] = r;
		}
		if (c.getToken() === 6) r.colonOffset = c.getTokenOffset(), u();
		else if (p(f("Colon expected"), I.ColonExpected), c.getToken() === 10 && e.positionAt(i.offset + i.length).line < e.positionAt(c.getTokenOffset()).line) return r.length = i.length, r;
		let a = C(r);
		return a ? (r.valueNode = a, r.length = a.offset + a.length - r.offset, r) : p(f("Value expected"), I.ValueExpected, r, [], [2, 5]);
	}
	function y(e) {
		if (c.getToken() !== 1) return;
		let t = new Oe(e, c.getTokenOffset()), n = Object.create(null);
		u();
		let r = !1;
		for (; c.getToken() !== 2 && c.getToken() !== 17;) {
			if (c.getToken() === 5) {
				r || p(f("Property expected"), I.PropertyExpected);
				let e = c.getTokenOffset();
				if (u(), c.getToken() === 2) {
					r && d(f("Trailing comma"), I.TrailingComma, e, e + 1);
					continue;
				}
			} else r && p(f("Expected comma"), I.CommaExpected);
			let e = v(t, n);
			e ? t.properties.push(e) : p(f("Property expected"), I.PropertyExpected, void 0, [], [2, 5]), r = !0;
		}
		return c.getToken() === 2 ? h(t, !0) : p(f("Expected comma or closing brace"), I.CommaOrCloseBraceExpected, t);
	}
	function b(e) {
		if (c.getToken() !== 10) return;
		let t = new z(e, c.getTokenOffset());
		return t.value = c.getTokenValue(), h(t, !0);
	}
	function x(e) {
		if (c.getToken() !== 11) return;
		let t = new Ee(e, c.getTokenOffset());
		if (c.getTokenError() === 0) {
			let e = c.getTokenValue();
			try {
				let n = JSON.parse(e);
				if (!A(n)) return p(f("Invalid number format."), I.Undefined, t);
				t.value = n;
			} catch {
				return p(f("Invalid number format."), I.Undefined, t);
			}
			t.isInteger = e.indexOf(".") === -1;
		}
		return h(t, !0);
	}
	function S(e) {
		switch (c.getToken()) {
			case 7: return h(new Ce(e, c.getTokenOffset()), !0);
			case 8: return h(new we(e, !0, c.getTokenOffset()), !0);
			case 9: return h(new we(e, !1, c.getTokenOffset()), !0);
			default: return;
		}
	}
	function C(e) {
		return g(e) || y(e) || b(e) || x(e) || S(e);
	}
	let w;
	return u() !== 17 && (w = C(w), w ? c.getToken() !== 17 && p(f("End of file expected."), I.Undefined) : p(f("Expected a JSON object, array or literal."), I.Undefined)), new Fe(w, n, l);
}
// node_modules/monaco-editor/esm/external/vscode-json-languageservice/lib/esm/utils/json.js
function Re(e, t, n) {
	if (typeof e == "object" && e) {
		let r = t + "	";
		if (Array.isArray(e)) {
			if (e.length === 0) return "[]";
			let i = "[\n";
			for (let t = 0; t < e.length; t++) i += r + Re(e[t], r, n), t < e.length - 1 && (i += ","), i += "\n";
			return i += t + "]", i;
		}
		{
			let i = Object.keys(e);
			if (i.length === 0) return "{}";
			let a = "{\n";
			for (let t = 0; t < i.length; t++) {
				let o = i[t];
				a += r + JSON.stringify(o) + ": " + Re(e[o], r, n), t < i.length - 1 && (a += ","), a += "\n";
			}
			return a += t + "}", a;
		}
	}
	return n(e);
}
// node_modules/monaco-editor/esm/external/vscode-json-languageservice/lib/esm/services/jsonCompletion.js
var ze = class {
	constructor(e, t = [], n = Promise, r = {}) {
		this.schemaService = e, this.contributions = t, this.promiseConstructor = n, this.clientCapabilities = r;
	}
	doResolve(e) {
		for (let t = this.contributions.length - 1; t >= 0; t--) {
			let n = this.contributions[t].resolveCompletion;
			if (n) {
				let t = n(e);
				if (t) return t;
			}
		}
		return this.promiseConstructor.resolve(e);
	}
	doComplete(e, t, r) {
		let o = {
			items: [],
			isIncomplete: !1
		}, s = e.getText(), l = e.offsetAt(t), u = r.getNodeFromOffset(l, !0);
		if (this.isInComment(e, u ? u.offset : 0, l)) return Promise.resolve(o);
		if (u && l === u.offset + u.length && l > 0) {
			let e = s[l - 1];
			(u.type === "object" && e === "}" || u.type === "array" && e === "]") && (u = u.parent);
		}
		let f = this.getCurrentWord(e, l), p;
		if (u && (u.type === "string" || u.type === "number" || u.type === "boolean" || u.type === "null")) p = i.create(e.positionAt(u.offset), e.positionAt(u.offset + u.length));
		else {
			let n = l - f.length;
			n > 0 && s[n - 1] === "\"" && n--, p = i.create(e.positionAt(n), t);
		}
		let m = /* @__PURE__ */ new Map(), h = {
			add: (e) => {
				let t = e.label, r = m.get(t);
				if (r) r.documentation ||= e.documentation, r.detail ||= e.detail, r.labelDetails ||= e.labelDetails;
				else {
					if (t = t.replace(/[\n]/g, "↵"), t.length > 60) {
						let e = t.substr(0, 57).trim() + "...";
						m.has(e) || (t = e);
					}
					e.textEdit = n.replace(p, e.insertText), e.label = t, m.set(t, e), o.items.push(e);
				}
			},
			setAsIncomplete: () => {
				o.isIncomplete = !0;
			},
			error: (e) => {
				console.error(e);
			},
			getNumberOfProposals: () => o.items.length
		};
		return this.schemaService.getSchemaForResource(e.uri, r).then((t) => {
			let n = [], i = !0, g = "", _;
			if (u && u.type === "string") {
				let e = u.parent;
				e && e.type === "property" && e.keyNode === u && (i = !e.valueNode, _ = e, g = s.substr(u.offset + 1, u.length - 2), e && (u = e.parent));
			}
			if (u && u.type === "object") {
				if (u.offset === l) return o;
				u.properties.forEach((e) => {
					_ && _ === e || m.set(e.keyNode.value, a.create("__"));
				});
				let v = "";
				i && (v = this.evaluateSeparatorAfter(e, e.offsetAt(p.end))), t ? this.getPropertyCompletions(t, r, u, i, v, h) : this.getSchemaLessPropertyCompletions(r, u, g, h);
				let y = W(u);
				this.contributions.forEach((t) => {
					let r = t.collectPropertyCompletions(e.uri, y, f, i, v === "", h);
					r && n.push(r);
				}), !t && f.length > 0 && s.charAt(l - f.length - 1) !== "\"" && (h.add({
					kind: d.Property,
					label: this.getLabelForValue(f),
					insertText: this.getInsertTextForProperty(f, void 0, !1, v),
					insertTextFormat: c.Snippet,
					documentation: ""
				}), h.setAsIncomplete());
			}
			let v = {};
			return t ? this.getValueCompletions(t, r, u, l, e, h, v) : this.getSchemaLessValueCompletions(r, u, l, e, h), this.contributions.length > 0 && this.getContributedValueCompletions(r, u, l, e, h, n), this.promiseConstructor.all(n).then(() => {
				if (h.getNumberOfProposals() === 0) {
					let t = l;
					u && (u.type === "string" || u.type === "number" || u.type === "boolean" || u.type === "null") && (t = u.offset + u.length);
					let n = this.evaluateSeparatorAfter(e, t);
					this.addFillerValueCompletions(v, n, h);
				}
				return o;
			});
		});
	}
	getPropertyCompletions(e, t, n, r, i, a) {
		t.getMatchingSchemas(e.schema, n.offset).forEach((e) => {
			if (e.node === n && !e.inverted) {
				let t = e.schema.properties;
				t && Object.keys(t).forEach((e) => {
					let n = t[e];
					if (typeof n == "object" && !n.deprecationMessage && !n.doNotSuggest) {
						let t = {
							kind: d.Property,
							label: e,
							insertText: this.getInsertTextForProperty(e, n, r, i),
							insertTextFormat: c.Snippet,
							filterText: this.getFilterTextForValue(e),
							documentation: this.fromMarkup(n.markdownDescription) || n.description || ""
						};
						n.suggestSortText !== void 0 && (t.sortText = n.suggestSortText), t.insertText && P(t.insertText, `$1${i}`) && (t.command = {
							title: "Suggest",
							command: "editor.action.triggerSuggest"
						}), a.add(t);
					}
				});
				let n = e.schema.propertyNames;
				if (typeof n == "object" && !n.deprecationMessage && !n.doNotSuggest) {
					let e = (e, t = void 0) => {
						let o = {
							kind: d.Property,
							label: e,
							insertText: this.getInsertTextForProperty(e, void 0, r, i),
							insertTextFormat: c.Snippet,
							filterText: this.getFilterTextForValue(e),
							documentation: t || this.fromMarkup(n.markdownDescription) || n.description || ""
						};
						n.suggestSortText !== void 0 && (o.sortText = n.suggestSortText), o.insertText && P(o.insertText, `$1${i}`) && (o.command = {
							title: "Suggest",
							command: "editor.action.triggerSuggest"
						}), a.add(o);
					};
					if (n.enum) for (let t = 0; t < n.enum.length; t++) {
						let r;
						n.markdownEnumDescriptions && t < n.markdownEnumDescriptions.length ? r = this.fromMarkup(n.markdownEnumDescriptions[t]) : n.enumDescriptions && t < n.enumDescriptions.length && (r = n.enumDescriptions[t]), e(n.enum[t], r);
					}
					n.const && e(n.const);
				}
			}
		});
	}
	getSchemaLessPropertyCompletions(e, t, n, r) {
		let i = (e) => {
			e.properties.forEach((e) => {
				let t = e.keyNode.value;
				r.add({
					kind: d.Property,
					label: t,
					insertText: this.getInsertTextForValue(t, ""),
					insertTextFormat: c.Snippet,
					filterText: this.getFilterTextForValue(t),
					documentation: ""
				});
			});
		};
		if (t.parent) {
			if (t.parent.type === "property") {
				let n = t.parent.keyNode.value;
				e.visit((e) => (e.type === "property" && e !== t.parent && e.keyNode.value === n && e.valueNode && e.valueNode.type === "object" && i(e.valueNode), !0));
			} else t.parent.type === "array" && t.parent.items.forEach((e) => {
				e.type === "object" && e !== t && i(e);
			});
		} else t.type === "object" && r.add({
			kind: d.Property,
			label: "$schema",
			insertText: this.getInsertTextForProperty("$schema", void 0, !0, ""),
			insertTextFormat: c.Snippet,
			documentation: "",
			filterText: this.getFilterTextForValue("$schema")
		});
	}
	getSchemaLessValueCompletions(e, t, n, r, i) {
		let a = n;
		if (t && (t.type === "string" || t.type === "number" || t.type === "boolean" || t.type === "null") && (a = t.offset + t.length, t = t.parent), !t) {
			i.add({
				kind: this.getSuggestionKind("object"),
				label: "Empty object",
				insertText: this.getInsertTextForValue({}, ""),
				insertTextFormat: c.Snippet,
				documentation: ""
			}), i.add({
				kind: this.getSuggestionKind("array"),
				label: "Empty array",
				insertText: this.getInsertTextForValue([], ""),
				insertTextFormat: c.Snippet,
				documentation: ""
			});
			return;
		}
		let o = this.evaluateSeparatorAfter(r, a), s = (e) => {
			e.parent && !Pe(e.parent, n, !0) && i.add({
				kind: this.getSuggestionKind(e.type),
				label: this.getLabelTextForMatchingNode(e, r),
				insertText: this.getInsertTextForMatchingNode(e, r, o),
				insertTextFormat: c.Snippet,
				documentation: ""
			}), e.type === "boolean" && this.addBooleanValueCompletion(!e.value, o, i);
		};
		if (t.type === "property" && n > (t.colonOffset || 0)) {
			let r = t.valueNode;
			if (r && (n > r.offset + r.length || r.type === "object" || r.type === "array")) return;
			let a = t.keyNode.value;
			e.visit((e) => (e.type === "property" && e.keyNode.value === a && e.valueNode && s(e.valueNode), !0)), a === "$schema" && t.parent && !t.parent.parent && this.addDollarSchemaCompletions(o, i);
		}
		if (t.type === "array") {
			if (t.parent && t.parent.type === "property") {
				let n = t.parent.keyNode.value;
				e.visit((e) => (e.type === "property" && e.keyNode.value === n && e.valueNode && e.valueNode.type === "array" && e.valueNode.items.forEach(s), !0));
			} else t.items.forEach(s);
		}
	}
	getValueCompletions(e, t, n, r, i, a, o) {
		let s = r, c, l;
		if (n && (n.type === "string" || n.type === "number" || n.type === "boolean" || n.type === "null") && (s = n.offset + n.length, l = n, n = n.parent), !n) this.addSchemaValueCompletions(e.schema, "", a, o);
		else {
			if (n.type === "property" && r > (n.colonOffset || 0)) {
				let e = n.valueNode;
				if (e && r > e.offset + e.length) return;
				c = n.keyNode.value, n = n.parent;
			}
			if (n && (c !== void 0 || n.type === "array")) {
				let u = this.evaluateSeparatorAfter(i, s), d = t.getMatchingSchemas(e.schema, n.offset, l);
				for (let e of d) if (e.node === n && !e.inverted && e.schema) {
					if (n.type === "array" && e.schema.items) {
						let t = a;
						if (e.schema.uniqueItems) {
							let e = /* @__PURE__ */ new Set();
							n.children.forEach((t) => {
								t.type !== "array" && t.type !== "object" && e.add(this.getLabelForValue(U(t)));
							}), t = {
								...a,
								add(t) {
									e.has(t.label) || a.add(t);
								}
							};
						}
						if (Array.isArray(e.schema.items)) {
							let a = this.findItemAtOffset(n, i, r);
							a < e.schema.items.length && this.addSchemaValueCompletions(e.schema.items[a], u, t, o);
						} else this.addSchemaValueCompletions(e.schema.items, u, t, o);
					}
					if (c !== void 0) {
						let t = !1;
						if (e.schema.properties) {
							let n = e.schema.properties[c];
							n && (t = !0, this.addSchemaValueCompletions(n, u, a, o));
						}
						if (e.schema.patternProperties && !t) {
							for (let n of Object.keys(e.schema.patternProperties)) if (F(n)?.test(c)) {
								t = !0;
								let r = e.schema.patternProperties[n];
								this.addSchemaValueCompletions(r, u, a, o);
							}
						}
						if (e.schema.additionalProperties && !t) {
							let t = e.schema.additionalProperties;
							this.addSchemaValueCompletions(t, u, a, o);
						}
					}
				}
				c === "$schema" && !n.parent && this.addDollarSchemaCompletions(u, a), o.boolean && (this.addBooleanValueCompletion(!0, u, a), this.addBooleanValueCompletion(!1, u, a)), o.null && this.addNullValueCompletion(u, a);
			}
		}
	}
	getContributedValueCompletions(e, t, n, r, i, a) {
		if (!t) this.contributions.forEach((e) => {
			let t = e.collectDefaultCompletions(r.uri, i);
			t && a.push(t);
		});
		else if ((t.type === "string" || t.type === "number" || t.type === "boolean" || t.type === "null") && (t = t.parent), t && t.type === "property" && n > (t.colonOffset || 0)) {
			let e = t.keyNode.value, o = t.valueNode;
			if ((!o || n <= o.offset + o.length) && t.parent) {
				let n = W(t.parent);
				this.contributions.forEach((t) => {
					let o = t.collectValueCompletions(r.uri, n, e, i);
					o && a.push(o);
				});
			}
		}
	}
	addSchemaValueCompletions(e, t, n, r) {
		typeof e == "object" && (this.addEnumValueCompletions(e, t, n), this.addDefaultValueCompletions(e, t, n), this.collectTypes(e, r), Array.isArray(e.allOf) && e.allOf.forEach((e) => this.addSchemaValueCompletions(e, t, n, r)), Array.isArray(e.anyOf) && e.anyOf.forEach((e) => this.addSchemaValueCompletions(e, t, n, r)), Array.isArray(e.oneOf) && e.oneOf.forEach((e) => this.addSchemaValueCompletions(e, t, n, r)));
	}
	addDefaultValueCompletions(e, t, n, r = 0) {
		let i = !1;
		if (j(e.default)) {
			let a = e.type, o = e.default;
			for (let e = r; e > 0; e--) o = [o], a = "array";
			let s = {
				kind: this.getSuggestionKind(a),
				label: this.getLabelForValue(o),
				insertText: this.getInsertTextForValue(o, t),
				insertTextFormat: c.Snippet
			};
			this.doesSupportsLabelDetails() ? s.labelDetails = { description: f("Default value") } : s.detail = f("Default value"), n.add(s), i = !0;
		}
		Array.isArray(e.examples) && e.examples.forEach((a) => {
			let o = e.type, s = a;
			for (let e = r; e > 0; e--) s = [s], o = "array";
			n.add({
				kind: this.getSuggestionKind(o),
				label: this.getLabelForValue(s),
				insertText: this.getInsertTextForValue(s, t),
				insertTextFormat: c.Snippet
			}), i = !0;
		}), Array.isArray(e.defaultSnippets) && e.defaultSnippets.forEach((a) => {
			let o = e.type, s = a.body, l = a.label, u, d;
			if (j(s)) {
				e.type;
				for (let e = r; e > 0; e--) s = [s];
				u = this.getInsertTextForSnippetValue(s, t), d = this.getFilterTextForSnippetValue(s), l ||= this.getLabelForSnippetValue(s);
			} else if (typeof a.bodyText == "string") {
				let e = "", n = "", i = "";
				for (let t = r; t > 0; t--) e = e + i + "[\n", n = n + "\n" + i + "]", i += "	", o = "array";
				u = e + i + a.bodyText.split("\n").join("\n" + i) + n + t, l ||= u, d = u.replace(/[\n]/g, "");
			} else return;
			n.add({
				kind: this.getSuggestionKind(o),
				label: l,
				documentation: this.fromMarkup(a.markdownDescription) || a.description,
				insertText: u,
				insertTextFormat: c.Snippet,
				filterText: d
			}), i = !0;
		}), !i && typeof e.items == "object" && !Array.isArray(e.items) && r < 5 && this.addDefaultValueCompletions(e.items, t, n, r + 1);
	}
	addEnumValueCompletions(e, t, n) {
		if (j(e.const) && n.add({
			kind: this.getSuggestionKind(e.type),
			label: this.getLabelForValue(e.const),
			insertText: this.getInsertTextForValue(e.const, t),
			insertTextFormat: c.Snippet,
			documentation: this.fromMarkup(e.markdownDescription) || e.description
		}), Array.isArray(e.enum)) for (let r = 0, i = e.enum.length; r < i; r++) {
			let i = e.enum[r], a = this.fromMarkup(e.markdownDescription) || e.description;
			e.markdownEnumDescriptions && r < e.markdownEnumDescriptions.length && this.doesSupportMarkdown() ? a = this.fromMarkup(e.markdownEnumDescriptions[r]) : e.enumDescriptions && r < e.enumDescriptions.length && (a = e.enumDescriptions[r]), n.add({
				kind: this.getSuggestionKind(e.type),
				label: this.getLabelForValue(i),
				insertText: this.getInsertTextForValue(i, t),
				insertTextFormat: c.Snippet,
				documentation: a
			});
		}
	}
	collectTypes(e, t) {
		if (Array.isArray(e.enum) || j(e.const)) return;
		let n = e.type;
		Array.isArray(n) ? n.forEach((e) => t[e] = !0) : n && (t[n] = !0);
	}
	addFillerValueCompletions(e, t, n) {
		e.object && n.add({
			kind: this.getSuggestionKind("object"),
			label: "{}",
			insertText: this.getInsertTextForGuessedValue({}, t),
			insertTextFormat: c.Snippet,
			detail: f("New object"),
			documentation: ""
		}), e.array && n.add({
			kind: this.getSuggestionKind("array"),
			label: "[]",
			insertText: this.getInsertTextForGuessedValue([], t),
			insertTextFormat: c.Snippet,
			detail: f("New array"),
			documentation: ""
		});
	}
	addBooleanValueCompletion(e, t, n) {
		n.add({
			kind: this.getSuggestionKind("boolean"),
			label: e ? "true" : "false",
			insertText: this.getInsertTextForValue(e, t),
			insertTextFormat: c.Snippet,
			documentation: ""
		});
	}
	addNullValueCompletion(e, t) {
		t.add({
			kind: this.getSuggestionKind("null"),
			label: "null",
			insertText: "null" + e,
			insertTextFormat: c.Snippet,
			documentation: ""
		});
	}
	addDollarSchemaCompletions(e, t) {
		this.schemaService.getRegisteredSchemaIds((e) => e === "http" || e === "https").forEach((n) => {
			n.startsWith("http://json-schema.org/draft-") && (n += "#"), t.add({
				kind: d.Module,
				label: this.getLabelForValue(n),
				filterText: this.getFilterTextForValue(n),
				insertText: this.getInsertTextForValue(n, e),
				insertTextFormat: c.Snippet,
				documentation: ""
			});
		});
	}
	getLabelForValue(e) {
		return JSON.stringify(e);
	}
	getValueFromLabel(e) {
		return JSON.parse(e);
	}
	getFilterTextForValue(e) {
		return JSON.stringify(e);
	}
	getFilterTextForSnippetValue(e) {
		return JSON.stringify(e).replace(/\$\{\d+:([^}]+)\}|\$\d+/g, "$1");
	}
	getLabelForSnippetValue(e) {
		return JSON.stringify(e).replace(/\$\{\d+:([^}]+)\}|\$\d+/g, "$1");
	}
	getInsertTextForPlainText(e) {
		return e.replace(/[\\\$\}]/g, "\\$&");
	}
	getInsertTextForValue(e, t) {
		let n = JSON.stringify(e, null, "	");
		return n === "{}" ? "{$1}" + t : n === "[]" ? "[$1]" + t : this.getInsertTextForPlainText(n + t);
	}
	getInsertTextForSnippetValue(e, t) {
		return Re(e, "", (e) => typeof e == "string" && e[0] === "^" ? e.substr(1) : JSON.stringify(e)) + t;
	}
	getInsertTextForGuessedValue(e, t) {
		switch (typeof e) {
			case "object": return e === null ? "${1:null}" + t : this.getInsertTextForValue(e, t);
			case "string":
				let n = JSON.stringify(e);
				return n = n.substr(1, n.length - 2), n = this.getInsertTextForPlainText(n), "\"${1:" + n + "}\"" + t;
			case "number":
			case "boolean": return "${1:" + JSON.stringify(e) + "}" + t;
		}
		return this.getInsertTextForValue(e, t);
	}
	getSuggestionKind(e) {
		if (Array.isArray(e)) {
			let t = e;
			e = t.length > 0 ? t[0] : void 0;
		}
		if (!e) return d.Value;
		switch (e) {
			case "string": return d.Value;
			case "object": return d.Module;
			case "property": return d.Property;
			default: return d.Value;
		}
	}
	getLabelTextForMatchingNode(e, t) {
		switch (e.type) {
			case "array": return "[]";
			case "object": return "{}";
			default: return t.getText().substr(e.offset, e.length);
		}
	}
	getInsertTextForMatchingNode(e, t, n) {
		switch (e.type) {
			case "array": return this.getInsertTextForValue([], n);
			case "object": return this.getInsertTextForValue({}, n);
			default:
				let r = t.getText().substr(e.offset, e.length) + n;
				return this.getInsertTextForPlainText(r);
		}
	}
	getInsertTextForProperty(e, t, n, r) {
		let i = this.getInsertTextForValue(e, "");
		if (!n) return i;
		let a = i + ": ", o, s = 0;
		if (t) {
			if (Array.isArray(t.defaultSnippets)) {
				if (t.defaultSnippets.length === 1) {
					let e = t.defaultSnippets[0].body;
					j(e) && (o = this.getInsertTextForSnippetValue(e, ""));
				}
				s += t.defaultSnippets.length;
			}
			if (t.enum && (!o && t.enum.length === 1 && (o = this.getInsertTextForGuessedValue(t.enum[0], "")), s += t.enum.length), j(t.const) && (o ||= this.getInsertTextForGuessedValue(t.const, ""), s++), j(t.default) && (o ||= this.getInsertTextForGuessedValue(t.default, ""), s++), Array.isArray(t.examples) && t.examples.length && (o ||= this.getInsertTextForGuessedValue(t.examples[0], ""), s += t.examples.length), s === 0) {
				let e = Array.isArray(t.type) ? t.type[0] : t.type;
				switch (e || (t.properties ? e = "object" : t.items && (e = "array")), e) {
					case "boolean":
						o = "$1";
						break;
					case "string":
						o = "\"$1\"";
						break;
					case "object":
						o = "{$1}";
						break;
					case "array":
						o = "[$1]";
						break;
					case "number":
					case "integer":
						o = "${1:0}";
						break;
					case "null":
						o = "${1:null}";
						break;
					default: return i;
				}
			}
		}
		return (!o || s > 1) && (o = "$1"), a + o + r;
	}
	getCurrentWord(e, t) {
		let n = t - 1, r = e.getText();
		for (; n >= 0 && " 	\n\r\v\":{[,]}".indexOf(r.charAt(n)) === -1;) n--;
		return r.substring(n + 1, t);
	}
	evaluateSeparatorAfter(e, t) {
		let n = O(e.getText(), !0);
		switch (n.setPosition(t), n.scan()) {
			case 5:
			case 2:
			case 4:
			case 17: return "";
			default: return ",";
		}
	}
	findItemAtOffset(e, t, n) {
		let r = O(t.getText(), !0), i = e.items;
		for (let e = i.length - 1; e >= 0; e--) {
			let t = i[e];
			if (n > t.offset + t.length) return r.setPosition(t.offset + t.length), r.scan() === 5 && n >= r.getTokenOffset() + r.getTokenLength() ? e + 1 : e;
			if (n >= t.offset) return e;
		}
		return 0;
	}
	isInComment(e, t, n) {
		let r = O(e.getText(), !1);
		r.setPosition(t);
		let i = r.scan();
		for (; i !== 17 && r.getTokenOffset() + r.getTokenLength() < n;) i = r.scan();
		return (i === 12 || i === 13) && r.getTokenOffset() <= n;
	}
	fromMarkup(e) {
		if (e && this.doesSupportMarkdown()) return {
			kind: m.Markdown,
			value: e
		};
	}
	doesSupportMarkdown() {
		if (!j(this.supportsMarkdown)) {
			let e = this.clientCapabilities.textDocument?.completion?.completionItem?.documentationFormat;
			this.supportsMarkdown = Array.isArray(e) && e.indexOf(m.Markdown) !== -1;
		}
		return this.supportsMarkdown;
	}
	doesSupportsCommitCharacters() {
		return j(this.supportsCommitCharacters) || (this.labelDetailsSupport = this.clientCapabilities.textDocument?.completion?.completionItem?.commitCharactersSupport), this.supportsCommitCharacters;
	}
	doesSupportsLabelDetails() {
		return j(this.labelDetailsSupport) || (this.labelDetailsSupport = this.clientCapabilities.textDocument?.completion?.completionItem?.labelDetailsSupport), this.labelDetailsSupport;
	}
}, Be = class {
	constructor(e, t = [], n) {
		this.schemaService = e, this.contributions = t, this.promise = n || Promise;
	}
	doHover(e, t, n) {
		let r = e.offsetAt(t), a = n.getNodeFromOffset(r);
		if (!a || (a.type === "object" || a.type === "array") && r > a.offset + 1 && r < a.offset + a.length - 1) return this.promise.resolve(null);
		let o = a;
		if (a.type === "string") {
			let e = a.parent;
			if (e && e.type === "property" && e.keyNode === a && (a = e.valueNode, !a)) return this.promise.resolve(null);
		}
		let s = i.create(e.positionAt(o.offset), e.positionAt(o.offset + o.length)), c = (e) => ({
			contents: e,
			range: s
		}), l = W(a);
		for (let t = this.contributions.length - 1; t >= 0; t--) {
			let n = this.contributions[t].getInfoContribution(e.uri, l);
			if (n) return n.then((e) => c(e));
		}
		return this.schemaService.getSchemaForResource(e.uri, n).then((e) => {
			if (e && a) {
				let t = n.getMatchingSchemas(e.schema, a.offset), r, i, o, s;
				t.every((e) => {
					if (e.node === a && !e.inverted && e.schema && (r ||= e.schema.title, i = i || e.schema.markdownDescription || Ve(e.schema.description), e.schema.enum)) {
						let t = e.schema.enum.indexOf(U(a));
						e.schema.markdownEnumDescriptions ? o = e.schema.markdownEnumDescriptions[t] : e.schema.enumDescriptions && (o = Ve(e.schema.enumDescriptions[t])), o && (s = e.schema.enum[t], typeof s != "string" && (s = JSON.stringify(s)));
					}
					return !0;
				});
				let l = "";
				return r && (l = Ve(r)), i && (l.length > 0 && (l += "\n\n"), l += i), o && (l.length > 0 && (l += "\n\n"), l += `\`${He(s)}\`: ${o}`), c([l]);
			}
			return null;
		});
	}
};
function Ve(e) {
	if (e) return e.replace(/([^\n\r])(\r?\n)([^\n\r])/gm, "$1\n\n$3").replace(/[\\`*_{}[\]()#+\-.!]/g, "\\$&");
}
function He(e) {
	return e.indexOf("`") === -1 ? e : "`` " + e + " ``";
}
// node_modules/monaco-editor/esm/external/vscode-json-languageservice/lib/esm/services/jsonValidation.js
var Ue = class {
	constructor(e, t) {
		this.jsonSchemaService = e, this.promise = t, this.validationEnabled = !0;
	}
	configure(e) {
		e && (this.validationEnabled = e.validate !== !1, this.commentSeverity = e.allowComments ? void 0 : s.Error);
	}
	doValidation(e, t, n, r) {
		if (!this.validationEnabled) return this.promise.resolve([]);
		let a = [], c = {}, l = (e) => {
			let t = e.range.start.line + " " + e.range.start.character + " " + e.message;
			c[t] || (c[t] = !0, a.push(e));
		}, u = (r) => {
			let c = n?.trailingCommas ? K(n.trailingCommas) : s.Error, u = n?.comments ? K(n.comments) : this.commentSeverity, d = n?.schemaValidation ? K(n.schemaValidation) : s.Warning, p = n?.schemaRequest ? K(n.schemaRequest) : s.Warning;
			if (r) {
				let a = (n, r) => {
					if (t.root && p) {
						let a = t.root, s = a.type === "object" ? a.properties[0] : void 0;
						if (s && s.keyNode.value === "$schema") {
							let t = s.valueNode || s, a = i.create(e.positionAt(t.offset), e.positionAt(t.offset + t.length));
							l(o.create(a, n, p, r));
						} else {
							let t = i.create(e.positionAt(a.offset), e.positionAt(a.offset + 1));
							l(o.create(t, n, p, r));
						}
					}
				};
				if (r.errors.length) a(r.errors[0], I.SchemaResolveError);
				else if (d) {
					for (let e of r.warnings) a(e, I.SchemaUnsupportedFeature);
					let i = t.validate(e, r.schema, d, n?.schemaDraft);
					i && i.forEach(l);
				}
				Ge(r.schema) && (u = void 0), Ke(r.schema) && (c = void 0);
			}
			for (let e of t.syntaxErrors) {
				if (e.code === I.TrailingComma) {
					if (typeof c != "number") continue;
					e.severity = c;
				}
				l(e);
			}
			if (typeof u == "number") {
				let e = f("Comments are not permitted in JSON.");
				t.comments.forEach((t) => {
					l(o.create(t, e, u, I.CommentNotPermitted));
				});
			}
			return a;
		};
		if (r) {
			let e = r.id || "schemaservice://untitled/" + We++;
			return this.jsonSchemaService.registerExternalSchema({
				uri: e,
				schema: r
			}).getResolvedSchema().then((e) => u(e));
		}
		return this.jsonSchemaService.getSchemaForResource(e.uri, t).then((e) => u(e));
	}
	getLanguageStatus(e, t) {
		return { schemas: this.jsonSchemaService.getSchemaURIsForResource(e.uri, t) };
	}
}, We = 0;
function Ge(e) {
	if (e && typeof e == "object") {
		if (M(e.allowComments)) return e.allowComments;
		if (e.allOf) for (let t of e.allOf) {
			let e = Ge(t);
			if (M(e)) return e;
		}
	}
}
function Ke(e) {
	if (e && typeof e == "object") {
		if (M(e.allowTrailingCommas)) return e.allowTrailingCommas;
		let t = e;
		if (M(t.allowsTrailingCommas)) return t.allowsTrailingCommas;
		if (e.allOf) for (let t of e.allOf) {
			let e = Ke(t);
			if (M(e)) return e;
		}
	}
}
function K(e) {
	switch (e) {
		case "error": return s.Error;
		case "warning": return s.Warning;
		case "ignore": return;
	}
}
// node_modules/monaco-editor/esm/external/vscode-json-languageservice/lib/esm/utils/colors.js
var qe = 48, Je = 57, Ye = 97, Xe = 102;
function q(e) {
	return e < qe ? 0 : e <= Je ? e - qe : (e < Ye && (e += 32), e >= Ye && e <= Xe ? e - Ye + 10 : 0);
}
function Ze(e) {
	if (e[0] === "#") switch (e.length) {
		case 4: return {
			red: q(e.charCodeAt(1)) * 17 / 255,
			green: q(e.charCodeAt(2)) * 17 / 255,
			blue: q(e.charCodeAt(3)) * 17 / 255,
			alpha: 1
		};
		case 5: return {
			red: q(e.charCodeAt(1)) * 17 / 255,
			green: q(e.charCodeAt(2)) * 17 / 255,
			blue: q(e.charCodeAt(3)) * 17 / 255,
			alpha: q(e.charCodeAt(4)) * 17 / 255
		};
		case 7: return {
			red: (q(e.charCodeAt(1)) * 16 + q(e.charCodeAt(2))) / 255,
			green: (q(e.charCodeAt(3)) * 16 + q(e.charCodeAt(4))) / 255,
			blue: (q(e.charCodeAt(5)) * 16 + q(e.charCodeAt(6))) / 255,
			alpha: 1
		};
		case 9: return {
			red: (q(e.charCodeAt(1)) * 16 + q(e.charCodeAt(2))) / 255,
			green: (q(e.charCodeAt(3)) * 16 + q(e.charCodeAt(4))) / 255,
			blue: (q(e.charCodeAt(5)) * 16 + q(e.charCodeAt(6))) / 255,
			alpha: (q(e.charCodeAt(7)) * 16 + q(e.charCodeAt(8))) / 255
		};
	}
}
// node_modules/monaco-editor/esm/external/vscode-json-languageservice/lib/esm/services/jsonDocumentSymbols.js
var Qe = class {
	constructor(e) {
		this.schemaService = e;
	}
	findDocumentSymbols(e, n, i = { resultLimit: Number.MAX_VALUE }) {
		let a = n.root;
		if (!a) return [];
		let o = i.resultLimit || Number.MAX_VALUE, s = e.uri;
		if ((s === "vscode://defaultsettings/keybindings.json" || P(s.toLowerCase(), "/user/keybindings.json")) && a.type === "array") {
			let n = [];
			for (let c of a.items) if (c.type === "object") {
				for (let a of c.properties) if (a.keyNode.value === "key" && a.valueNode) {
					let l = r.create(e.uri, J(e, c));
					if (n.push({
						name: $e(a.valueNode),
						kind: t.Function,
						location: l
					}), o--, o <= 0) return i && i.onResultLimitExceeded && i.onResultLimitExceeded(s), n;
				}
			}
			return n;
		}
		let c = [{
			node: a,
			containerName: ""
		}], l = 0, u = !1, d = [], f = (t, n) => {
			t.type === "array" ? t.items.forEach((e) => {
				e && c.push({
					node: e,
					containerName: n
				});
			}) : t.type === "object" && t.properties.forEach((t) => {
				let i = t.valueNode;
				if (i) {
					if (o > 0) {
						o--;
						let a = r.create(e.uri, J(e, t)), s = n ? n + "." + t.keyNode.value : t.keyNode.value;
						d.push({
							name: this.getKeyLabel(t),
							kind: this.getSymbolKind(i.type),
							location: a,
							containerName: n
						}), c.push({
							node: i,
							containerName: s
						});
					} else u = !0;
				}
			});
		};
		for (; l < c.length;) {
			let e = c[l++];
			f(e.node, e.containerName);
		}
		return u && i && i.onResultLimitExceeded && i.onResultLimitExceeded(s), d;
	}
	findDocumentSymbols2(e, n, r = { resultLimit: Number.MAX_VALUE }) {
		let i = n.root;
		if (!i) return [];
		let a = r.resultLimit || Number.MAX_VALUE, o = e.uri;
		if ((o === "vscode://defaultsettings/keybindings.json" || P(o.toLowerCase(), "/user/keybindings.json")) && i.type === "array") {
			let n = [];
			for (let s of i.items) if (s.type === "object") {
				for (let i of s.properties) if (i.keyNode.value === "key" && i.valueNode) {
					let c = J(e, s), l = J(e, i.keyNode);
					if (n.push({
						name: $e(i.valueNode),
						kind: t.Function,
						range: c,
						selectionRange: l
					}), a--, a <= 0) return r && r.onResultLimitExceeded && r.onResultLimitExceeded(o), n;
				}
			}
			return n;
		}
		let s = [], c = [{
			node: i,
			result: s
		}], l = 0, u = !1, d = (t, n) => {
			t.type === "array" ? t.items.forEach((t, r) => {
				if (t) {
					if (a > 0) {
						a--;
						let i = J(e, t), o = i, s = {
							name: String(r),
							kind: this.getSymbolKind(t.type),
							range: i,
							selectionRange: o,
							children: []
						};
						n.push(s), c.push({
							result: s.children,
							node: t
						});
					} else u = !0;
				}
			}) : t.type === "object" && t.properties.forEach((t) => {
				let r = t.valueNode;
				if (r) {
					if (a > 0) {
						a--;
						let i = J(e, t), o = J(e, t.keyNode), s = [], l = {
							name: this.getKeyLabel(t),
							kind: this.getSymbolKind(r.type),
							range: i,
							selectionRange: o,
							children: s,
							detail: this.getDetail(r)
						};
						n.push(l), c.push({
							result: s,
							node: r
						});
					} else u = !0;
				}
			});
		};
		for (; l < c.length;) {
			let e = c[l++];
			d(e.node, e.result);
		}
		return u && r && r.onResultLimitExceeded && r.onResultLimitExceeded(o), s;
	}
	getSymbolKind(e) {
		switch (e) {
			case "object": return t.Module;
			case "string": return t.String;
			case "number": return t.Number;
			case "array": return t.Array;
			case "boolean": return t.Boolean;
			default: return t.Variable;
		}
	}
	getKeyLabel(e) {
		let t = e.keyNode.value;
		return t &&= t.replace(/[\n]/g, "↵"), t && t.trim() ? t : `"${t}"`;
	}
	getDetail(e) {
		if (e) {
			if (e.type === "boolean" || e.type === "number" || e.type === "null" || e.type === "string") return String(e.value);
			if (e.type === "array") return e.children.length ? void 0 : "[]";
			if (e.type === "object") return e.children.length ? void 0 : "{}";
		}
	}
	findDocumentColors(e, t, n) {
		return this.schemaService.getSchemaForResource(e.uri, t).then((r) => {
			let i = [];
			if (r) {
				let a = n && typeof n.resultLimit == "number" ? n.resultLimit : Number.MAX_VALUE, o = t.getMatchingSchemas(r.schema), s = {};
				for (let t of o) if (!t.inverted && t.schema && (t.schema.format === "color" || t.schema.format === "color-hex") && t.node && t.node.type === "string") {
					let r = String(t.node.offset);
					if (!s[r]) {
						let o = Ze(U(t.node));
						if (o) {
							let n = J(e, t.node);
							i.push({
								color: o,
								range: n
							});
						}
						if (s[r] = !0, a--, a <= 0) return n && n.onResultLimitExceeded && n.onResultLimitExceeded(e.uri), i;
					}
				}
			}
			return i;
		});
	}
	getColorPresentations(e, t, r, i) {
		let a = [], o = Math.round(r.red * 255), s = Math.round(r.green * 255), c = Math.round(r.blue * 255);
		function l(e) {
			let t = e.toString(16);
			return t.length === 2 ? t : "0" + t;
		}
		let u;
		return u = r.alpha === 1 ? `#${l(o)}${l(s)}${l(c)}` : `#${l(o)}${l(s)}${l(c)}${l(Math.round(r.alpha * 255))}`, a.push({
			label: u,
			textEdit: n.replace(i, JSON.stringify(u))
		}), a;
	}
};
function J(e, t) {
	return i.create(e.positionAt(t.offset), e.positionAt(t.offset + t.length));
}
function $e(e) {
	return U(e) || f("<empty>");
}
// node_modules/monaco-editor/esm/external/vscode-json-languageservice/lib/esm/services/configuration.js
var et = {
	schemaAssociations: [],
	schemas: {
		"http://json-schema.org/draft-04/schema#": {
			$schema: "http://json-schema.org/draft-04/schema#",
			definitions: {
				schemaArray: {
					type: "array",
					minItems: 1,
					items: { $ref: "#" }
				},
				positiveInteger: {
					type: "integer",
					minimum: 0
				},
				positiveIntegerDefault0: { allOf: [{ $ref: "#/definitions/positiveInteger" }, { default: 0 }] },
				simpleTypes: {
					type: "string",
					enum: [
						"array",
						"boolean",
						"integer",
						"null",
						"number",
						"object",
						"string"
					]
				},
				stringArray: {
					type: "array",
					items: { type: "string" },
					minItems: 1,
					uniqueItems: !0
				}
			},
			type: "object",
			properties: {
				id: {
					type: "string",
					format: "uri"
				},
				$schema: {
					type: "string",
					format: "uri"
				},
				title: { type: "string" },
				description: { type: "string" },
				default: {},
				multipleOf: {
					type: "number",
					minimum: 0,
					exclusiveMinimum: !0
				},
				maximum: { type: "number" },
				exclusiveMaximum: {
					type: "boolean",
					default: !1
				},
				minimum: { type: "number" },
				exclusiveMinimum: {
					type: "boolean",
					default: !1
				},
				maxLength: { allOf: [{ $ref: "#/definitions/positiveInteger" }] },
				minLength: { allOf: [{ $ref: "#/definitions/positiveIntegerDefault0" }] },
				pattern: {
					type: "string",
					format: "regex"
				},
				additionalItems: {
					anyOf: [{ type: "boolean" }, { $ref: "#" }],
					default: {}
				},
				items: {
					anyOf: [{ $ref: "#" }, { $ref: "#/definitions/schemaArray" }],
					default: {}
				},
				maxItems: { allOf: [{ $ref: "#/definitions/positiveInteger" }] },
				minItems: { allOf: [{ $ref: "#/definitions/positiveIntegerDefault0" }] },
				uniqueItems: {
					type: "boolean",
					default: !1
				},
				maxProperties: { allOf: [{ $ref: "#/definitions/positiveInteger" }] },
				minProperties: { allOf: [{ $ref: "#/definitions/positiveIntegerDefault0" }] },
				required: { allOf: [{ $ref: "#/definitions/stringArray" }] },
				additionalProperties: {
					anyOf: [{ type: "boolean" }, { $ref: "#" }],
					default: {}
				},
				definitions: {
					type: "object",
					additionalProperties: { $ref: "#" },
					default: {}
				},
				properties: {
					type: "object",
					additionalProperties: { $ref: "#" },
					default: {}
				},
				patternProperties: {
					type: "object",
					additionalProperties: { $ref: "#" },
					default: {}
				},
				dependencies: {
					type: "object",
					additionalProperties: { anyOf: [{ $ref: "#" }, { $ref: "#/definitions/stringArray" }] }
				},
				enum: {
					type: "array",
					minItems: 1,
					uniqueItems: !0
				},
				type: { anyOf: [{ $ref: "#/definitions/simpleTypes" }, {
					type: "array",
					items: { $ref: "#/definitions/simpleTypes" },
					minItems: 1,
					uniqueItems: !0
				}] },
				format: { anyOf: [{
					type: "string",
					enum: [
						"date-time",
						"uri",
						"email",
						"hostname",
						"ipv4",
						"ipv6",
						"regex"
					]
				}, { type: "string" }] },
				allOf: { allOf: [{ $ref: "#/definitions/schemaArray" }] },
				anyOf: { allOf: [{ $ref: "#/definitions/schemaArray" }] },
				oneOf: { allOf: [{ $ref: "#/definitions/schemaArray" }] },
				not: { allOf: [{ $ref: "#" }] }
			},
			dependencies: {
				exclusiveMaximum: ["maximum"],
				exclusiveMinimum: ["minimum"]
			},
			default: {}
		},
		"http://json-schema.org/draft-07/schema#": {
			definitions: {
				schemaArray: {
					type: "array",
					minItems: 1,
					items: { $ref: "#" }
				},
				nonNegativeInteger: {
					type: "integer",
					minimum: 0
				},
				nonNegativeIntegerDefault0: { allOf: [{ $ref: "#/definitions/nonNegativeInteger" }, { default: 0 }] },
				simpleTypes: { enum: [
					"array",
					"boolean",
					"integer",
					"null",
					"number",
					"object",
					"string"
				] },
				stringArray: {
					type: "array",
					items: { type: "string" },
					uniqueItems: !0,
					default: []
				}
			},
			type: ["object", "boolean"],
			properties: {
				$id: {
					type: "string",
					format: "uri-reference"
				},
				$schema: {
					type: "string",
					format: "uri"
				},
				$ref: {
					type: "string",
					format: "uri-reference"
				},
				$comment: { type: "string" },
				title: { type: "string" },
				description: { type: "string" },
				default: !0,
				readOnly: {
					type: "boolean",
					default: !1
				},
				examples: {
					type: "array",
					items: !0
				},
				multipleOf: {
					type: "number",
					exclusiveMinimum: 0
				},
				maximum: { type: "number" },
				exclusiveMaximum: { type: "number" },
				minimum: { type: "number" },
				exclusiveMinimum: { type: "number" },
				maxLength: { $ref: "#/definitions/nonNegativeInteger" },
				minLength: { $ref: "#/definitions/nonNegativeIntegerDefault0" },
				pattern: {
					type: "string",
					format: "regex"
				},
				additionalItems: { $ref: "#" },
				items: {
					anyOf: [{ $ref: "#" }, { $ref: "#/definitions/schemaArray" }],
					default: !0
				},
				maxItems: { $ref: "#/definitions/nonNegativeInteger" },
				minItems: { $ref: "#/definitions/nonNegativeIntegerDefault0" },
				uniqueItems: {
					type: "boolean",
					default: !1
				},
				contains: { $ref: "#" },
				maxProperties: { $ref: "#/definitions/nonNegativeInteger" },
				minProperties: { $ref: "#/definitions/nonNegativeIntegerDefault0" },
				required: { $ref: "#/definitions/stringArray" },
				additionalProperties: { $ref: "#" },
				definitions: {
					type: "object",
					additionalProperties: { $ref: "#" },
					default: {}
				},
				properties: {
					type: "object",
					additionalProperties: { $ref: "#" },
					default: {}
				},
				patternProperties: {
					type: "object",
					additionalProperties: { $ref: "#" },
					propertyNames: { format: "regex" },
					default: {}
				},
				dependencies: {
					type: "object",
					additionalProperties: { anyOf: [{ $ref: "#" }, { $ref: "#/definitions/stringArray" }] }
				},
				propertyNames: { $ref: "#" },
				const: !0,
				enum: {
					type: "array",
					items: !0,
					minItems: 1,
					uniqueItems: !0
				},
				type: { anyOf: [{ $ref: "#/definitions/simpleTypes" }, {
					type: "array",
					items: { $ref: "#/definitions/simpleTypes" },
					minItems: 1,
					uniqueItems: !0
				}] },
				format: { type: "string" },
				contentMediaType: { type: "string" },
				contentEncoding: { type: "string" },
				if: { $ref: "#" },
				then: { $ref: "#" },
				else: { $ref: "#" },
				allOf: { $ref: "#/definitions/schemaArray" },
				anyOf: { $ref: "#/definitions/schemaArray" },
				oneOf: { $ref: "#/definitions/schemaArray" },
				not: { $ref: "#" }
			},
			default: !0
		}
	}
}, tt = {
	id: f("A unique identifier for the schema."),
	$schema: f("The schema to verify this document against."),
	title: f("A descriptive title of the element."),
	description: f("A long description of the element. Used in hover menus and suggestions."),
	default: f("A default value. Used by suggestions."),
	multipleOf: f("A number that should cleanly divide the current value (i.e. have no remainder)."),
	maximum: f("The maximum numerical value, inclusive by default."),
	exclusiveMaximum: f("Makes the maximum property exclusive."),
	minimum: f("The minimum numerical value, inclusive by default."),
	exclusiveMinimum: f("Makes the minimum property exclusive."),
	maxLength: f("The maximum length of a string."),
	minLength: f("The minimum length of a string."),
	pattern: f("A regular expression to match the string against. It is not implicitly anchored."),
	additionalItems: f("For arrays, only when items is set as an array. If it is a schema, then this schema validates items after the ones specified by the items array. If it is false, then additional items will cause validation to fail."),
	items: f("For arrays. Can either be a schema to validate every element against or an array of schemas to validate each item against in order (the first schema will validate the first element, the second schema will validate the second element, and so on."),
	maxItems: f("The maximum number of items that can be inside an array. Inclusive."),
	minItems: f("The minimum number of items that can be inside an array. Inclusive."),
	uniqueItems: f("If all of the items in the array must be unique. Defaults to false."),
	maxProperties: f("The maximum number of properties an object can have. Inclusive."),
	minProperties: f("The minimum number of properties an object can have. Inclusive."),
	required: f("An array of strings that lists the names of all properties required on this object."),
	additionalProperties: f("Either a schema or a boolean. If a schema, then used to validate all properties not matched by 'properties' or 'patternProperties'. If false, then any properties not matched by either will cause this schema to fail."),
	definitions: f("Not used for validation. Place subschemas here that you wish to reference inline with $ref."),
	properties: f("A map of property names to schemas for each property."),
	patternProperties: f("A map of regular expressions on property names to schemas for matching properties."),
	dependencies: f("A map of property names to either an array of property names or a schema. An array of property names means the property named in the key depends on the properties in the array being present in the object in order to be valid. If the value is a schema, then the schema is only applied to the object if the property in the key exists on the object."),
	enum: f("The set of literal values that are valid."),
	type: f("Either a string of one of the basic schema types (number, integer, null, array, object, boolean, string) or an array of strings specifying a subset of those types."),
	format: f("Describes the format expected for the value."),
	allOf: f("An array of schemas, all of which must match."),
	anyOf: f("An array of schemas, where at least one must match."),
	oneOf: f("An array of schemas, exactly one of which must match."),
	not: f("A schema which must not match."),
	$id: f("A unique identifier for the schema."),
	$ref: f("Reference a definition hosted on any location."),
	$comment: f("Comments from schema authors to readers or maintainers of the schema."),
	readOnly: f("Indicates that the value of the instance is managed exclusively by the owning authority."),
	examples: f("Sample JSON values associated with a particular schema, for the purpose of illustrating usage."),
	contains: f("An array instance is valid against \"contains\" if at least one of its elements is valid against the given schema."),
	propertyNames: f("If the instance is an object, this keyword validates if every property name in the instance validates against the provided schema."),
	const: f("An instance validates successfully against this keyword if its value is equal to the value of the keyword."),
	contentMediaType: f("Describes the media type of a string property."),
	contentEncoding: f("Describes the content encoding of a string property."),
	if: f("The validation outcome of the \"if\" subschema controls which of the \"then\" or \"else\" keywords are evaluated."),
	then: f("The \"if\" subschema is used for validation when the \"if\" subschema succeeds."),
	else: f("The \"else\" subschema is used for validation when the \"if\" subschema fails.")
};
for (let e in et.schemas) {
	let t = et.schemas[e];
	for (let e in t.properties) {
		let n = t.properties[e];
		typeof n == "boolean" && (n = t.properties[e] = {});
		let r = tt[e];
		r && (n.description = r);
	}
}
// node_modules/monaco-editor/esm/external/vscode-json-languageservice/lib/esm/utils/glob.js
function nt(e, t) {
	if (typeof e != "string") throw TypeError("Expected a string");
	let n = String(e), r = "", i = !!t, a = !!t, o = !1, s = t && typeof t.flags == "string" ? t.flags : "", c;
	for (let e = 0, t = n.length; e < t; e++) switch (c = n[e], c) {
		case "/":
		case "$":
		case "^":
		case "+":
		case ".":
		case "(":
		case ")":
		case "=":
		case "!":
		case "|":
			r += "\\" + c;
			break;
		case "?": if (i) {
			r += ".";
			break;
		}
		case "[":
		case "]": if (i) {
			r += c;
			break;
		}
		case "{": if (i) {
			o = !0, r += "(";
			break;
		}
		case "}": if (i) {
			o = !1, r += ")";
			break;
		}
		case ",":
			if (o) {
				r += "|";
				break;
			}
			r += "\\" + c;
			break;
		case "*":
			let t = n[e - 1], s = 1;
			for (; n[e + 1] === "*";) s++, e++;
			let l = n[e + 1];
			a ? s > 1 && (t === "/" || t === void 0 || t === "{" || t === ",") && (l === "/" || l === void 0 || l === "," || l === "}") ? (l === "/" ? e++ : t === "/" && r.endsWith("\\/") && (r = r.substr(0, r.length - 2)), r += "((?:[^/]*(?:/|$))*)") : r += "([^/]*)" : r += ".*";
			break;
		default: r += c;
	}
	return s && ~s.indexOf("g") || (r = "^" + r + "$"), new RegExp(r, s);
}
// node_modules/monaco-editor/esm/external/vscode-json-languageservice/lib/esm/services/jsonSchemaService.js
var rt = "!", it = "/", at = class {
	constructor(e, t, n) {
		this.folderUri = t, this.uris = n, this.globWrappers = [];
		try {
			for (let t of e) {
				let e = t[0] !== rt;
				e || (t = t.substring(1)), t.length > 0 && (t[0] === it && (t = t.substring(1)), this.globWrappers.push({
					regexp: nt("**/" + t, {
						extended: !0,
						globstar: !0
					}),
					include: e
				}));
			}
			t && (t = ut(t), t.endsWith("/") || (t += "/"), this.folderUri = t);
		} catch {
			this.globWrappers.length = 0, this.uris = [];
		}
	}
	matchesPattern(e) {
		if (this.folderUri && !e.startsWith(this.folderUri)) return !1;
		let t = !1;
		for (let { regexp: n, include: r } of this.globWrappers) n.test(e) && (t = r);
		return t;
	}
	getURIs() {
		return this.uris;
	}
}, ot = class {
	constructor(e, t, n) {
		this.service = e, this.uri = t, this.dependencies = /* @__PURE__ */ new Set(), this.anchors = void 0, n && (this.unresolvedSchema = this.service.promise.resolve(new Y(n)));
	}
	getUnresolvedSchema() {
		return this.unresolvedSchema ||= this.service.loadSchema(this.uri), this.unresolvedSchema;
	}
	getResolvedSchema() {
		return this.resolvedSchema ||= this.getUnresolvedSchema().then((e) => this.service.resolveSchemaContent(e, this)), this.resolvedSchema;
	}
	clearSchema() {
		let e = !!this.unresolvedSchema;
		return this.resolvedSchema = void 0, this.unresolvedSchema = void 0, this.dependencies.clear(), this.anchors = void 0, e;
	}
}, Y = class {
	constructor(e, t = []) {
		this.schema = e, this.errors = t;
	}
}, st = class {
	constructor(e, t = [], n = [], r) {
		this.schema = e, this.errors = t, this.warnings = n, this.schemaDraft = r;
	}
	getSection(e) {
		let t = this.getSectionRecursive(e, this.schema);
		if (t) return B(t);
	}
	getSectionRecursive(e, t) {
		if (!t || typeof t == "boolean" || e.length === 0) return t;
		let n = e.shift();
		if (t.properties && typeof t.properties[n]) return this.getSectionRecursive(e, t.properties[n]);
		if (t.patternProperties) {
			for (let r of Object.keys(t.patternProperties)) if (F(r)?.test(n)) return this.getSectionRecursive(e, t.patternProperties[r]);
		} else if (typeof t.additionalProperties == "object") return this.getSectionRecursive(e, t.additionalProperties);
		else if (n.match("[0-9]+")) {
			if (Array.isArray(t.items)) {
				let r = parseInt(n, 10);
				if (!isNaN(r) && t.items[r]) return this.getSectionRecursive(e, t.items[r]);
			} else if (t.items) return this.getSectionRecursive(e, t.items);
		}
	}
}, ct = class {
	constructor(e, t, n) {
		this.contextService = t, this.requestService = e, this.promiseConstructor = n || Promise, this.callOnDispose = [], this.contributionSchemas = {}, this.contributionAssociations = [], this.schemasById = {}, this.filePatternAssociations = [], this.registeredSchemasIds = {};
	}
	getRegisteredSchemaIds(e) {
		return Object.keys(this.registeredSchemasIds).filter((t) => {
			let n = p.parse(t).scheme;
			return n !== "schemaservice" && (!e || e(n));
		});
	}
	get promise() {
		return this.promiseConstructor;
	}
	dispose() {
		for (; this.callOnDispose.length > 0;) this.callOnDispose.pop()();
	}
	onResourceChange(e) {
		this.cachedSchemaForResource = void 0;
		let t = !1;
		e = X(e);
		let n = [e], r = Object.keys(this.schemasById).map((e) => this.schemasById[e]);
		for (; n.length;) {
			let e = n.pop();
			for (let i = 0; i < r.length; i++) {
				let a = r[i];
				a && (a.uri === e || a.dependencies.has(e)) && (a.uri !== e && n.push(a.uri), a.clearSchema() && (t = !0), r[i] = void 0);
			}
		}
		return t;
	}
	setSchemaContributions(e) {
		if (e.schemas) {
			let t = e.schemas;
			for (let e in t) {
				let n = X(e);
				this.contributionSchemas[n] = this.addSchemaHandle(n, t[e]);
			}
		}
		if (Array.isArray(e.schemaAssociations)) {
			let t = e.schemaAssociations;
			for (let e of t) {
				let t = e.uris.map(X), n = this.addFilePatternAssociation(e.pattern, e.folderUri, t);
				this.contributionAssociations.push(n);
			}
		}
	}
	addSchemaHandle(e, t) {
		let n = new ot(this, e, t);
		return this.schemasById[e] = n, n;
	}
	getOrAddSchemaHandle(e, t) {
		return this.schemasById[e] || this.addSchemaHandle(e, t);
	}
	addFilePatternAssociation(e, t, n) {
		let r = new at(e, t, n);
		return this.filePatternAssociations.push(r), r;
	}
	registerExternalSchema(e) {
		let t = X(e.uri);
		return this.registeredSchemasIds[t] = !0, this.cachedSchemaForResource = void 0, e.fileMatch && e.fileMatch.length && this.addFilePatternAssociation(e.fileMatch, e.folderUri, [t]), e.schema ? this.addSchemaHandle(t, e.schema) : this.getOrAddSchemaHandle(t);
	}
	clearExternalSchemas() {
		this.schemasById = {}, this.filePatternAssociations = [], this.registeredSchemasIds = {}, this.cachedSchemaForResource = void 0;
		for (let e in this.contributionSchemas) this.schemasById[e] = this.contributionSchemas[e], this.registeredSchemasIds[e] = !0;
		for (let e of this.contributionAssociations) this.filePatternAssociations.push(e);
	}
	getResolvedSchema(e) {
		let t = X(e), n = this.schemasById[t];
		return n ? n.getResolvedSchema() : this.promise.resolve(void 0);
	}
	loadSchema(e) {
		if (!this.requestService) {
			let t = f("Unable to load schema from '{0}'. No schema request service available", Z(e));
			return this.promise.resolve(new Y({}, [t]));
		}
		return e.startsWith("http://json-schema.org/") && (e = "https" + e.substring(4)), this.requestService(e).then((t) => {
			if (!t) return new Y({}, [f("Unable to load schema from '{0}': No content.", Z(e))]);
			let n = [];
			t.charCodeAt(0) === 65279 && (n.push(f("Problem reading content from '{0}': UTF-8 with BOM detected, only UTF 8 is allowed.", Z(e))), t = t.trimStart());
			let r = {}, i = [];
			return r = fe(t, i), i.length && n.push(f("Unable to parse content from '{0}': Parse error at offset {1}.", Z(e), i[0].offset)), new Y(r, n);
		}, (t) => {
			let n = t.toString(), r = t.toString().split("Error: ");
			return r.length > 1 && (n = r[1]), P(n, ".") && (n = n.substr(0, n.length - 1)), new Y({}, [f("Unable to load schema from '{0}': {1}.", Z(e), n)]);
		});
	}
	resolveSchemaContent(e, t) {
		let n = e.errors.slice(0), r = e.schema, i = r.$schema ? X(r.$schema) : void 0;
		if (i === "http://json-schema.org/draft-03/schema") return this.promise.resolve(new st({}, [f("Draft-03 schemas are not supported.")], [], i));
		let a = /* @__PURE__ */ new Set(), o = this.contextService, s = (e, t) => {
			t = decodeURIComponent(t);
			let n = e;
			return t[0] === "/" && (t = t.substring(1)), t.split("/").some((e) => (e = e.replace(/~1/g, "/").replace(/~0/g, "~"), n = n[e], !n)), n;
		}, c = (e, t, n) => (t.anchors ||= m(e), t.anchors.get(n)), l = (e, t) => {
			for (let n in t) t.hasOwnProperty(n) && n !== "id" && n !== "$id" && (e[n] = t[n]);
		}, u = (e, t, r, i) => {
			let a;
			a = i === void 0 || i.length === 0 ? t : i.charAt(0) === "/" ? s(t, i) : c(t, r, i), a ? l(e, a) : n.push(f("$ref '{0}' in '{1}' can not be resolved.", i || "", r.uri));
		}, d = (e, t, r, i) => {
			o && !/^[A-Za-z][A-Za-z0-9+\-.+]*:\/\/.*/.test(t) && (t = o.resolveRelativePath(t, i.uri)), t = X(t);
			let a = this.getOrAddSchemaHandle(t);
			return a.getUnresolvedSchema().then((o) => {
				if (i.dependencies.add(t), o.errors.length) {
					let e = r ? t + "#" + r : t;
					n.push(f("Problems loading reference '{0}': {1}", e, o.errors[0]));
				}
				return u(e, o.schema, a, r), p(e, o.schema, a);
			});
		}, p = (e, t, n) => {
			let r = [];
			return this.traverseNodes(e, (e) => {
				let i = /* @__PURE__ */ new Set();
				for (; e.$ref;) {
					let a = e.$ref, o = a.split("#", 2);
					if (delete e.$ref, o[0].length > 0) {
						r.push(d(e, o[0], o[1], n));
						return;
					}
					if (!i.has(a)) {
						let r = o[1];
						u(e, t, n, r), i.add(a);
					}
				}
				e.$recursiveRef && a.add("$recursiveRef"), e.$dynamicRef && a.add("$dynamicRef");
			}), this.promise.all(r);
		}, m = (e) => {
			let t = /* @__PURE__ */ new Map();
			return this.traverseNodes(e, (e) => {
				let r = e.$id || e.id, i = ve(r) && r.charAt(0) === "#" ? r.substring(1) : e.$anchor;
				i && (t.has(i) ? n.push(f("Duplicate anchor declaration: '{0}'", i)) : t.set(i, e)), e.$recursiveAnchor && a.add("$recursiveAnchor"), e.$dynamicAnchor && a.add("$dynamicAnchor");
			}), t;
		};
		return p(r, r, t).then((e) => {
			let t = [];
			return a.size && t.push(f("The schema uses meta-schema features ({0}) that are not yet supported by the validator.", Array.from(a.keys()).join(", "))), new st(r, n, t, i);
		});
	}
	traverseNodes(e, t) {
		if (!e || typeof e != "object") return Promise.resolve(null);
		let n = /* @__PURE__ */ new Set(), r = (...e) => {
			for (let t of e) N(t) && s.push(t);
		}, i = (...e) => {
			for (let t of e) if (N(t)) for (let e in t) {
				let n = t[e];
				N(n) && s.push(n);
			}
		}, a = (...e) => {
			for (let t of e) if (Array.isArray(t)) for (let e of t) N(e) && s.push(e);
		}, o = (e) => {
			if (Array.isArray(e)) for (let t of e) N(t) && s.push(t);
			else N(e) && s.push(e);
		}, s = [e], c = s.pop();
		for (; c;) n.has(c) || (n.add(c), t(c), r(c.additionalItems, c.additionalProperties, c.not, c.contains, c.propertyNames, c.if, c.then, c.else, c.unevaluatedItems, c.unevaluatedProperties), i(c.definitions, c.$defs, c.properties, c.patternProperties, c.dependencies, c.dependentSchemas), a(c.anyOf, c.allOf, c.oneOf, c.prefixItems), o(c.items)), c = s.pop();
	}
	getSchemaFromProperty(e, t) {
		if (t.root?.type === "object") {
			for (let n of t.root.properties) if (n.keyNode.value === "$schema" && n.valueNode?.type === "string") {
				let t = n.valueNode.value;
				return this.contextService && !/^\w[\w\d+.-]*:/.test(t) && (t = this.contextService.resolveRelativePath(t, e)), t;
			}
		}
	}
	getAssociatedSchemas(e) {
		let t = Object.create(null), n = [], r = ut(e);
		for (let e of this.filePatternAssociations) if (e.matchesPattern(r)) for (let r of e.getURIs()) t[r] || (n.push(r), t[r] = !0);
		return n;
	}
	getSchemaURIsForResource(e, t) {
		let n = t && this.getSchemaFromProperty(e, t);
		return n ? [n] : this.getAssociatedSchemas(e);
	}
	getSchemaForResource(e, t) {
		if (t) {
			let n = this.getSchemaFromProperty(e, t);
			if (n) {
				let e = X(n);
				return this.getOrAddSchemaHandle(e).getResolvedSchema();
			}
		}
		if (this.cachedSchemaForResource && this.cachedSchemaForResource.resource === e) return this.cachedSchemaForResource.resolvedSchema;
		let n = this.getAssociatedSchemas(e), r = n.length > 0 ? this.createCombinedSchema(e, n).getResolvedSchema() : this.promise.resolve(void 0);
		return this.cachedSchemaForResource = {
			resource: e,
			resolvedSchema: r
		}, r;
	}
	createCombinedSchema(e, t) {
		if (t.length === 1) return this.getOrAddSchemaHandle(t[0]);
		{
			let n = "schemaservice://combinedSchema/" + encodeURIComponent(e), r = { allOf: t.map((e) => ({ $ref: e })) };
			return this.addSchemaHandle(n, r);
		}
	}
	getMatchingSchemas(e, t, n) {
		if (n) {
			let e = n.id || "schemaservice://untitled/matchingSchemas/" + lt++;
			return this.addSchemaHandle(e, n).getResolvedSchema().then((e) => t.getMatchingSchemas(e.schema).filter((e) => !e.inverted));
		}
		return this.getSchemaForResource(e.uri, t).then((e) => e ? t.getMatchingSchemas(e.schema).filter((e) => !e.inverted) : []);
	}
}, lt = 0;
function X(e) {
	try {
		return p.parse(e).toString(!0);
	} catch {
		return e;
	}
}
function ut(e) {
	try {
		return p.parse(e).with({
			fragment: null,
			query: null
		}).toString(!0);
	} catch {
		return e;
	}
}
function Z(e) {
	try {
		let t = p.parse(e);
		if (t.scheme === "file") return t.fsPath;
	} catch {}
	return e;
}
// node_modules/monaco-editor/esm/external/vscode-json-languageservice/lib/esm/services/jsonFolding.js
function dt(e, t) {
	let n = [], r = [], i = [], a = -1, o = O(e.getText(), !1), s = o.scan();
	function c(e) {
		n.push(e), r.push(i.length);
	}
	for (; s !== 17;) {
		switch (s) {
			case 1:
			case 3: {
				let t = e.positionAt(o.getTokenOffset()).line, n = {
					startLine: t,
					endLine: t,
					kind: s === 1 ? "object" : "array"
				};
				i.push(n);
				break;
			}
			case 2:
			case 4: {
				let t = s === 2 ? "object" : "array";
				if (i.length > 0 && i[i.length - 1].kind === t) {
					let t = i.pop(), n = e.positionAt(o.getTokenOffset()).line;
					t && n > t.startLine + 1 && a !== t.startLine && (t.endLine = n - 1, c(t), a = t.startLine);
				}
				break;
			}
			case 13: {
				let t = e.positionAt(o.getTokenOffset()).line, n = e.positionAt(o.getTokenOffset() + o.getTokenLength()).line;
				o.getTokenError() === 1 && t + 1 < e.lineCount ? o.setPosition(e.offsetAt(g.create(t + 1, 0))) : t < n && (c({
					startLine: t,
					endLine: n,
					kind: l.Comment
				}), a = t);
				break;
			}
			case 12: {
				let t = e.getText().substr(o.getTokenOffset(), o.getTokenLength()).match(/^\/\/\s*#(region\b)|(endregion\b)/);
				if (t) {
					let n = e.positionAt(o.getTokenOffset()).line;
					if (t[1]) {
						let e = {
							startLine: n,
							endLine: n,
							kind: l.Region
						};
						i.push(e);
					} else {
						let e = i.length - 1;
						for (; e >= 0 && i[e].kind !== l.Region;) e--;
						if (e >= 0) {
							let t = i[e];
							i.length = e, n > t.startLine && a !== t.startLine && (t.endLine = n, c(t), a = t.startLine);
						}
					}
				}
				break;
			}
		}
		s = o.scan();
	}
	let u = t && t.rangeLimit;
	if (typeof u != "number" || n.length <= u) return n;
	t && t.onRangeLimitExceeded && t.onRangeLimitExceeded(e.uri);
	let d = [];
	for (let e of r) e < 30 && (d[e] = (d[e] || 0) + 1);
	let f = 0, p = 0;
	for (let e = 0; e < d.length; e++) {
		let t = d[e];
		if (t) {
			if (t + f > u) {
				p = e;
				break;
			}
			f += t;
		}
	}
	let m = [];
	for (let e = 0; e < n.length; e++) {
		let t = r[e];
		typeof t == "number" && (t < p || t === p && f++ < u) && m.push(n[e]);
	}
	return m;
}
// node_modules/monaco-editor/esm/external/vscode-json-languageservice/lib/esm/services/jsonSelectionRanges.js
function ft(e, t, n) {
	function r(t) {
		let r = e.offsetAt(t), o = n.getNodeFromOffset(r, !0), c = [];
		for (; o;) {
			switch (o.type) {
				case "string":
				case "object":
				case "array":
					let e = o.offset + 1, t = o.offset + o.length - 1;
					e < t && r >= e && r <= t && c.push(a(e, t)), c.push(a(o.offset, o.offset + o.length));
					break;
				case "number":
				case "boolean":
				case "null":
				case "property": c.push(a(o.offset, o.offset + o.length));
			}
			if (o.type === "property" || o.parent && o.parent.type === "array") {
				let e = s(o.offset + o.length, 5);
				e !== -1 && c.push(a(o.offset, e));
			}
			o = o.parent;
		}
		let l;
		for (let e = c.length - 1; e >= 0; e--) l = h.create(c[e], l);
		return l ||= h.create(i.create(t, t)), l;
	}
	function a(t, n) {
		return i.create(e.positionAt(t), e.positionAt(n));
	}
	let o = O(e.getText(), !0);
	function s(e, t) {
		return o.setPosition(e), o.scan() === t ? o.getTokenOffset() + o.getTokenLength() : -1;
	}
	return t.map(r);
}
// node_modules/monaco-editor/esm/external/vscode-json-languageservice/lib/esm/utils/format.js
function pt(e, t, r) {
	let a;
	if (r) {
		let t = e.offsetAt(r.start);
		a = {
			offset: t,
			length: e.offsetAt(r.end) - t
		};
	}
	let o = {
		tabSize: t ? t.tabSize : 4,
		insertSpaces: t?.insertSpaces === !0,
		insertFinalNewline: t?.insertFinalNewline === !0,
		eol: "\n",
		keepLines: t?.keepLines === !0
	};
	return _e(e.getText(), a, o).map((t) => n.replace(i.create(e.positionAt(t.offset), e.positionAt(t.offset + t.length)), t.content));
}
// node_modules/monaco-editor/esm/external/vscode-json-languageservice/lib/esm/utils/propertyTree.js
var Q;
(function(e) {
	e[e.Object = 0] = "Object", e[e.Array = 1] = "Array";
})(Q ||= {});
var $ = class {
	constructor(e, t) {
		this.propertyName = e ?? "", this.beginningLineNumber = t, this.childrenProperties = [], this.lastProperty = !1, this.noKeyName = !1;
	}
	addChildProperty(e) {
		if (e.parent = this, this.childrenProperties.length > 0) {
			let t = 0;
			t = e.noKeyName ? this.childrenProperties.length : ht(this.childrenProperties, e, mt), t < 0 && (t = t * -1 - 1), this.childrenProperties.splice(t, 0, e);
		} else this.childrenProperties.push(e);
		return e;
	}
};
function mt(e, t) {
	let n = e.propertyName.toLowerCase(), r = t.propertyName.toLowerCase();
	return n < r ? -1 : +(n > r);
}
function ht(e, t, n) {
	let r = t.propertyName.toLowerCase(), i = e[0].propertyName.toLowerCase(), a = e[e.length - 1].propertyName.toLowerCase();
	if (r < i) return 0;
	if (r > a) return e.length;
	let o = 0, s = e.length - 1;
	for (; o <= s;) {
		let r = s + o >> 1, i = n(t, e[r]);
		if (i > 0) o = r + 1;
		else if (i < 0) s = r - 1;
		else return r;
	}
	return -o - 1;
}
// node_modules/monaco-editor/esm/external/vscode-json-languageservice/lib/esm/utils/sort.js
function gt(e, t) {
	let r = {
		...t,
		keepLines: !1
	}, a = u.applyEdits(e, pt(e, r, void 0)), o = u.create("test://test.json", "json", 0, a), s = vt(o, _t(o)), c = pt(s, r, void 0), l = u.applyEdits(s, c);
	return [n.replace(i.create(g.create(0, 0), e.positionAt(e.getText().length)), l)];
}
function _t(e) {
	let t = O(e.getText(), !1), n = new $(), r = n, a = n, o = n, s, c = 0, l = 0, u, d, f = -1, p = -1, m = 0, h = 0, _ = [], v = !1, y = !1;
	for (; (s = t.scan()) !== 17;) {
		if (v === !0 && s !== 14 && s !== 15 && s !== 12 && s !== 13 && a.endLineNumber === void 0) {
			let e = t.getTokenStartLine();
			d === 2 || d === 4 ? o.endLineNumber = e - 1 : a.endLineNumber = e - 1, m = e, v = !1;
		}
		if (y === !0 && s !== 14 && s !== 15 && s !== 12 && s !== 13 && (m = t.getTokenStartLine(), y = !1), t.getTokenStartLine() !== c) {
			for (let n = c; n < t.getTokenStartLine(); n++) {
				let t = e.getText(i.create(g.create(n, 0), g.create(n + 1, 0))).length;
				l += t;
			}
			c = t.getTokenStartLine();
		}
		switch (s) {
			case 10:
				if (u === void 0 || u === 1 || u === 5 && _[_.length - 1] === Q.Object) {
					let e = new $(t.getTokenValue(), m);
					o = a, a = r.addChildProperty(e);
				}
				break;
			case 3:
				if (n.beginningLineNumber === void 0 && (n.beginningLineNumber = t.getTokenStartLine()), _[_.length - 1] === Q.Object) r = a;
				else if (_[_.length - 1] === Q.Array) {
					let e = new $(t.getTokenValue(), m);
					e.noKeyName = !0, o = a, a = r.addChildProperty(e), r = a;
				}
				_.push(Q.Array), a.type = Q.Array, m = t.getTokenStartLine(), m++;
				break;
			case 1:
				if (n.beginningLineNumber === void 0) n.beginningLineNumber = t.getTokenStartLine();
				else if (_[_.length - 1] === Q.Array) {
					let e = new $(t.getTokenValue(), m);
					e.noKeyName = !0, o = a, a = r.addChildProperty(e);
				}
				a.type = Q.Object, _.push(Q.Object), r = a, m = t.getTokenStartLine(), m++;
				break;
			case 4:
				h = t.getTokenStartLine(), _.pop(), a.endLineNumber === void 0 && (u === 2 || u === 4) && (a.endLineNumber = h - 1, a.lastProperty = !0, a.lineWhereToAddComma = f, a.indexWhereToAddComa = p, o = a, a = a ? a.parent : void 0, r = a), n.endLineNumber = h, m = h + 1;
				break;
			case 2:
				h = t.getTokenStartLine(), _.pop(), u !== 1 && (a.endLineNumber === void 0 && (a.endLineNumber = h - 1, a.lastProperty = !0, a.lineWhereToAddComma = f, a.indexWhereToAddComa = p), o = a, a = a ? a.parent : void 0, r = a), n.endLineNumber = t.getTokenStartLine(), m = h + 1;
				break;
			case 5:
				h = t.getTokenStartLine(), a.endLineNumber === void 0 && (_[_.length - 1] === Q.Object || _[_.length - 1] === Q.Array && (u === 2 || u === 4)) && (a.endLineNumber = h, a.commaIndex = t.getTokenOffset() - l, a.commaLine = h), (u === 2 || u === 4) && (o = a, a = a ? a.parent : void 0, r = a), m = h + 1;
				break;
			case 13: u !== 5 || f !== t.getTokenStartLine() || (_[_.length - 1] !== Q.Array || d !== 2 && d !== 4) && _[_.length - 1] !== Q.Object || (_[_.length - 1] !== Q.Array || d !== 2 && d !== 4) && _[_.length - 1] !== Q.Object || (a.endLineNumber = void 0, v = !0), (u === 1 || u === 3) && f === t.getTokenStartLine() && (y = !0);
		}
		s !== 14 && s !== 13 && s !== 12 && s !== 15 && (d = u, u = s, f = t.getTokenStartLine(), p = t.getTokenOffset() + t.getTokenLength() - l);
	}
	return n;
}
function vt(e, t) {
	if (t.childrenProperties.length === 0) return e;
	let n = u.create("test://test.json", "json", 0, e.getText()), r = [];
	for (yt(r, t, t.beginningLineNumber); r.length > 0;) {
		let t = r.shift(), a = t.propertyTreeArray, o = t.beginningLineNumber;
		for (let t = 0; t < a.length; t++) {
			let s = a[t], c = i.create(g.create(s.beginningLineNumber, 0), g.create(s.endLineNumber + 1, 0)), l = e.getText(c), d = u.create("test://test.json", "json", 0, l);
			if (s.lastProperty === !0 && t !== a.length - 1) {
				let e = s.lineWhereToAddComma - s.beginningLineNumber, t = s.indexWhereToAddComa, n = {
					range: i.create(g.create(e, t), g.create(e, t)),
					text: ","
				};
				u.update(d, [n], 1);
			} else if (s.lastProperty === !1 && t === a.length - 1) {
				let e = s.commaIndex, t = s.commaLine - s.beginningLineNumber, n = {
					range: i.create(g.create(t, e), g.create(t, e + 1)),
					text: ""
				};
				u.update(d, [n], 1);
			}
			let f = s.endLineNumber - s.beginningLineNumber + 1, p = {
				range: i.create(g.create(o, 0), g.create(o + f, 0)),
				text: d.getText()
			};
			u.update(n, [p], 1), yt(r, s, o), o += f;
		}
	}
	return n;
}
function yt(e, t, n) {
	if (t.childrenProperties.length !== 0) {
		if (t.type === Q.Object) {
			let r = Infinity;
			for (let e of t.childrenProperties) e.beginningLineNumber < r && (r = e.beginningLineNumber);
			let i = r - t.beginningLineNumber;
			n += i, e.push(new xt(n, t.childrenProperties));
		} else t.type === Q.Array && bt(e, t, n);
	}
}
function bt(e, t, n) {
	for (let r of t.childrenProperties) {
		if (r.type === Q.Object) {
			let i = Infinity;
			for (let e of r.childrenProperties) e.beginningLineNumber < i && (i = e.beginningLineNumber);
			let a = i - r.beginningLineNumber;
			e.push(new xt(n + r.beginningLineNumber - t.beginningLineNumber + a, r.childrenProperties));
		}
		r.type === Q.Array && bt(e, r, n + r.beginningLineNumber - t.beginningLineNumber);
	}
}
var xt = class {
	constructor(e, t) {
		this.beginningLineNumber = e, this.propertyTreeArray = t;
	}
};
// node_modules/monaco-editor/esm/external/vscode-json-languageservice/lib/esm/services/jsonLinks.js
function St(e, t) {
	let n = [];
	return t.visit((r) => {
		if (r.type === "property" && r.keyNode.value === "$ref" && r.valueNode?.type === "string") {
			let i = r.valueNode.value, a = wt(t, i);
			if (a) {
				let t = e.positionAt(a.offset);
				n.push({
					target: `${e.uri}#${t.line + 1},${t.character + 1}`,
					range: Ct(e, r.valueNode)
				});
			}
		}
		return !0;
	}), Promise.resolve(n);
}
function Ct(e, t) {
	return i.create(e.positionAt(t.offset + 1), e.positionAt(t.offset + t.length - 1));
}
function wt(e, t) {
	let n = Et(t);
	return n ? Tt(n, e.root) : null;
}
function Tt(e, t) {
	if (!t) return null;
	if (e.length === 0) return t;
	let n = e.shift();
	if (t && t.type === "object") {
		let r = t.properties.find((e) => e.keyNode.value === n);
		return r ? Tt(e, r.valueNode) : null;
	}
	if (t && t.type === "array" && n.match(/^(0|[1-9][0-9]*)$/)) {
		let r = Number.parseInt(n), i = t.items[r];
		return i ? Tt(e, i) : null;
	}
	return null;
}
function Et(e) {
	return e === "#" ? [] : e[0] !== "#" || e[1] !== "/" ? null : e.substring(2).split(/\//).map(Dt);
}
function Dt(e) {
	return e.replace(/~1/g, "/").replace(/~0/g, "~");
}
// node_modules/monaco-editor/esm/external/vscode-json-languageservice/lib/esm/jsonLanguageService.js
function Ot(e) {
	let t = e.promiseConstructor || Promise, n = new ct(e.schemaRequestService, e.workspaceContext, t);
	n.setSchemaContributions(et);
	let r = new ze(n, e.contributions, t, e.clientCapabilities), i = new Be(n, e.contributions, t), a = new Qe(n), o = new Ue(n, t);
	return {
		configure: (e) => {
			n.clearExternalSchemas(), e.schemas?.forEach(n.registerExternalSchema.bind(n)), o.configure(e);
		},
		resetSchema: (e) => n.onResourceChange(e),
		doValidation: o.doValidation.bind(o),
		getLanguageStatus: o.getLanguageStatus.bind(o),
		parseJSONDocument: (e) => Le(e, { collectComments: !0 }),
		newJSONDocument: (e, t) => Ne(e, t),
		getMatchingSchemas: n.getMatchingSchemas.bind(n),
		doResolve: r.doResolve.bind(r),
		doComplete: r.doComplete.bind(r),
		findDocumentSymbols: a.findDocumentSymbols.bind(a),
		findDocumentSymbols2: a.findDocumentSymbols2.bind(a),
		findDocumentColors: a.findDocumentColors.bind(a),
		getColorPresentations: a.getColorPresentations.bind(a),
		doHover: i.doHover.bind(i),
		getFoldingRanges: dt,
		getSelectionRanges: ft,
		findDefinition: () => Promise.resolve([]),
		findLinks: St,
		format: (e, t, n) => pt(e, n, t),
		sort: (e, t) => gt(e, t)
	};
}
// node_modules/monaco-editor/esm/vs/languages/features/json/jsonWorker.js
var kt;
typeof fetch < "u" && (kt = function(e) {
	return fetch(e).then((e) => e.text());
});
var At = class {
	constructor(e, t) {
		this._ctx = e, this._languageSettings = t.languageSettings, this._languageId = t.languageId, this._languageService = Ot({
			workspaceContext: { resolveRelativePath: (e, t) => Pt(t.substr(0, t.lastIndexOf("/") + 1), e) },
			schemaRequestService: t.enableSchemaRequest ? kt : void 0,
			clientCapabilities: xe.LATEST
		}), this._languageService.configure(this._languageSettings);
	}
	async doValidation(e) {
		let t = this._getTextDocument(e);
		if (t) {
			let e = this._languageService.parseJSONDocument(t);
			return this._languageService.doValidation(t, e, this._languageSettings);
		}
		return Promise.resolve([]);
	}
	async doComplete(e, t) {
		let n = this._getTextDocument(e);
		if (!n) return null;
		let r = this._languageService.parseJSONDocument(n);
		return this._languageService.doComplete(n, t, r);
	}
	async doResolve(e) {
		return this._languageService.doResolve(e);
	}
	async doHover(e, t) {
		let n = this._getTextDocument(e);
		if (!n) return null;
		let r = this._languageService.parseJSONDocument(n);
		return this._languageService.doHover(n, t, r);
	}
	async format(e, t, n) {
		let r = this._getTextDocument(e);
		if (!r) return [];
		let i = this._languageService.format(r, t, n);
		return Promise.resolve(i);
	}
	async resetSchema(e) {
		return Promise.resolve(this._languageService.resetSchema(e));
	}
	async findDocumentSymbols(e) {
		let t = this._getTextDocument(e);
		if (!t) return [];
		let n = this._languageService.parseJSONDocument(t), r = this._languageService.findDocumentSymbols2(t, n);
		return Promise.resolve(r);
	}
	async findDocumentColors(e) {
		let t = this._getTextDocument(e);
		if (!t) return [];
		let n = this._languageService.parseJSONDocument(t), r = this._languageService.findDocumentColors(t, n);
		return Promise.resolve(r);
	}
	async getColorPresentations(e, t, n) {
		let r = this._getTextDocument(e);
		if (!r) return [];
		let i = this._languageService.parseJSONDocument(r), a = this._languageService.getColorPresentations(r, i, t, n);
		return Promise.resolve(a);
	}
	async getFoldingRanges(e, t) {
		let n = this._getTextDocument(e);
		if (!n) return [];
		let r = this._languageService.getFoldingRanges(n, t);
		return Promise.resolve(r);
	}
	async getSelectionRanges(e, t) {
		let n = this._getTextDocument(e);
		if (!n) return [];
		let r = this._languageService.parseJSONDocument(n), i = this._languageService.getSelectionRanges(n, t, r);
		return Promise.resolve(i);
	}
	async parseJSONDocument(e) {
		let t = this._getTextDocument(e);
		if (!t) return null;
		let n = this._languageService.parseJSONDocument(t);
		return Promise.resolve(n);
	}
	async getMatchingSchemas(e) {
		let t = this._getTextDocument(e);
		if (!t) return [];
		let n = this._languageService.parseJSONDocument(t);
		return Promise.resolve(this._languageService.getMatchingSchemas(t, n));
	}
	_getTextDocument(e) {
		let t = this._ctx.getMirrorModels();
		for (let n of t) if (n.uri.toString() === e) return u.create(e, this._languageId, n.version, n.getValue());
		return null;
	}
}, jt = 47, Mt = 46;
function Nt(e) {
	return e.charCodeAt(0) === jt;
}
function Pt(e, t) {
	if (Nt(t)) {
		let n = p.parse(e), r = t.split("/");
		return n.with({ path: Ft(r) }).toString();
	}
	return It(e, t);
}
function Ft(e) {
	let t = [];
	for (let n of e) n.length === 0 || n.length === 1 && n.charCodeAt(0) === Mt || (n.length === 2 && n.charCodeAt(0) === Mt && n.charCodeAt(1) === Mt ? t.pop() : t.push(n));
	e.length > 1 && e[e.length - 1].length === 0 && t.push("");
	let n = t.join("/");
	return e[0].length === 0 && (n = "/" + n), n;
}
function It(e, ...t) {
	let n = p.parse(e), r = n.path.split("/");
	for (let e of t) r.push(...e.split("/"));
	return n.with({ path: Ft(r) }).toString();
}
// node_modules/monaco-editor/esm/vs/languages/features/json/json.worker.js
self.onmessage = () => {
	e((e, t) => new At(e, t));
};
