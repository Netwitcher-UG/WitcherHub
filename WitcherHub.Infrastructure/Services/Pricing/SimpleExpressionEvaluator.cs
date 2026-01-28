using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace WitcherHub.Infrastructure.Services.Pricing
{

    internal static class SimpleExpressionEvaluator
    {
        public static bool EvalBool(string expr, Dictionary<string, object?> vars)
            => Convert.ToBoolean(Eval(expr, vars), CultureInfo.InvariantCulture);

        public static decimal EvalDecimal(string expr, Dictionary<string, object?> vars)
            => Convert.ToDecimal(Eval(expr, vars), CultureInfo.InvariantCulture);

        // --------- Minimal parser (recursive descent) ----------
        private enum TokType { Number, Ident, String, Op, LParen, RParen, LBrack, RBrack, End }
        private readonly record struct Tok(TokType Type, string Text);

        private sealed class Lexer
        {
            private readonly string _s;
            private int _i;

            public Lexer(string s) => _s = s;

            public Tok Next()
            {
                while (_i < _s.Length && char.IsWhiteSpace(_s[_i])) _i++;
                if (_i >= _s.Length) return new Tok(TokType.End, "");

                char c = _s[_i];

                if (char.IsDigit(c) || c == '.')
                {
                    int start = _i++;
                    while (_i < _s.Length && (char.IsDigit(_s[_i]) || _s[_i] == '.')) _i++;
                    return new Tok(TokType.Number, _s[start.._i]);
                }

                if (char.IsLetter(c) || c == '_')
                {
                    int start = _i++;
                    while (_i < _s.Length && (char.IsLetterOrDigit(_s[_i]) || _s[_i] == '_')) _i++;
                    return new Tok(TokType.Ident, _s[start.._i]);
                }

                if (c == '"' || c == '\'')
                {
                    char q = c;
                    _i++;
                    int start = _i;
                    while (_i < _s.Length && _s[_i] != q) _i++;
                    var text = _s[start.._i];
                    if (_i < _s.Length) _i++;
                    return new Tok(TokType.String, text);
                }

                _i++;
                return c switch
                {
                    '(' => new Tok(TokType.LParen, "("),
                    ')' => new Tok(TokType.RParen, ")"),
                    '[' => new Tok(TokType.LBrack, "["),
                    ']' => new Tok(TokType.RBrack, "]"),
                    _ => new Tok(TokType.Op, ReadOp(c))
                };

                string ReadOp(char first)
                {
                    // support: >= <= == != && ||
                    if (_i < _s.Length)
                    {
                        char n = _s[_i];
                        if ((first == '>' || first == '<' || first == '=' || first == '!') && n == '=')
                        {
                            _i++;
                            return $"{first}=";
                        }
                        if (first == '&' && n == '&') { _i++; return "&&"; }
                        if (first == '|' && n == '|') { _i++; return "||"; }
                    }
                    return first.ToString();
                }
            }
        }

        private sealed class Parser
        {
            private readonly Lexer _lex;
            private Tok _cur;
            private readonly Dictionary<string, object?> _vars;

            public Parser(string expr, Dictionary<string, object?> vars)
            {
                _lex = new Lexer(expr);
                _vars = vars;
                _cur = _lex.Next();
            }

            private void Eat(TokType t)
            {
                if (_cur.Type != t) throw new InvalidOperationException($"Unexpected token: {_cur.Type} {_cur.Text}");
                _cur = _lex.Next();
            }

            public object EvalExpr() => ParseOr();

            private object ParseOr()
            {
                var left = ParseAnd();
                while (_cur.Type == TokType.Op && _cur.Text == "||")
                {
                    Eat(TokType.Op);
                    var right = ParseAnd();
                    left = ToBool(left) || ToBool(right);
                }
                return left;
            }

            private object ParseAnd()
            {
                var left = ParseEquality();
                while (_cur.Type == TokType.Op && _cur.Text == "&&")
                {
                    Eat(TokType.Op);
                    var right = ParseEquality();
                    left = ToBool(left) && ToBool(right);
                }
                return left;
            }

            private object ParseEquality()
            {
                var left = ParseRel();
                while (_cur.Type == TokType.Op && (_cur.Text == "==" || _cur.Text == "!="))
                {
                    var op = _cur.Text;
                    Eat(TokType.Op);
                    var right = ParseRel();
                    bool eq = Equals(Norm(left), Norm(right));
                    left = op == "==" ? eq : !eq;
                }
                return left;
            }

            private object ParseRel()
            {
                var left = ParseAdd();
                while (_cur.Type == TokType.Op && (_cur.Text is ">" or ">=" or "<" or "<="))
                {
                    var op = _cur.Text;
                    Eat(TokType.Op);
                    var right = ParseAdd();

                    var a = ToDec(left);
                    var b = ToDec(right);

                    left = op switch
                    {
                        ">" => a > b,
                        ">=" => a >= b,
                        "<" => a < b,
                        "<=" => a <= b,
                        _ => false
                    };
                }
                return left;
            }

            private object ParseAdd()
            {
                var left = ParseMul();
                while (_cur.Type == TokType.Op && (_cur.Text == "+" || _cur.Text == "-"))
                {
                    var op = _cur.Text;
                    Eat(TokType.Op);
                    var right = ParseMul();
                    left = op == "+" ? ToDec(left) + ToDec(right) : ToDec(left) - ToDec(right);
                }
                return left;
            }

            private object ParseMul()
            {
                var left = ParseUnary();
                while (_cur.Type == TokType.Op && (_cur.Text == "*" || _cur.Text == "/"))
                {
                    var op = _cur.Text;
                    Eat(TokType.Op);
                    var right = ParseUnary();
                    left = op == "*" ? ToDec(left) * ToDec(right) : ToDec(left) / ToDec(right);
                }
                return left;
            }

            private object ParseUnary()
            {
                if (_cur.Type == TokType.Op && _cur.Text == "-")
                {
                    Eat(TokType.Op);
                    return -ToDec(ParseUnary());
                }
                return ParsePrimary();
            }

            private object ParsePrimary()
            {
                if (_cur.Type == TokType.Number)
                {
                    var n = decimal.Parse(_cur.Text, CultureInfo.InvariantCulture);
                    Eat(TokType.Number);
                    return n;
                }

                if (_cur.Type == TokType.Ident)
                {
                    var ident = _cur.Text;
                    Eat(TokType.Ident);

                    // true/false
                    if (string.Equals(ident, "true", StringComparison.OrdinalIgnoreCase)) return true;
                    if (string.Equals(ident, "false", StringComparison.OrdinalIgnoreCase)) return false;

                    // params["x"]
                    if (ident == "params" && _cur.Type == TokType.LBrack)
                    {
                        Eat(TokType.LBrack);
                        if (_cur.Type != TokType.String) throw new InvalidOperationException("params[...] expects string key");
                        var key = _cur.Text;
                        Eat(TokType.String);
                        Eat(TokType.RBrack);
                        return _vars.TryGetValue(key, out var v) ? v ?? 0m : 0m;
                    }

                    // plain variable: qty/base/total...
                    return _vars.TryGetValue(ident, out var val) ? val ?? 0m : 0m;
                }

                if (_cur.Type == TokType.LParen)
                {
                    Eat(TokType.LParen);
                    var v = ParseOr();
                    Eat(TokType.RParen);
                    return v;
                }

                throw new InvalidOperationException($"Unexpected token: {_cur.Type} {_cur.Text}");
            }

            private static object? Norm(object v)
            {
                if (v is JsonElement je)
                {
                    return je.ValueKind switch
                    {
                        JsonValueKind.Number => je.TryGetDecimal(out var d) ? d : 0m,
                        JsonValueKind.True => true,
                        JsonValueKind.False => false,
                        JsonValueKind.String => je.GetString(),
                        _ => null
                    };
                }
                return v;
            }

            private static bool ToBool(object v) => v switch
            {
                bool b => b,
                decimal d => d != 0,
                int i => i != 0,
                _ => Convert.ToBoolean(v, CultureInfo.InvariantCulture)
            };

            private static decimal ToDec(object v) => v switch
            {
                decimal d => d,
                int i => i,
                long l => l,
                double db => (decimal)db,
                bool b => b ? 1m : 0m,
                JsonElement je when je.ValueKind == JsonValueKind.Number && je.TryGetDecimal(out var d) => d,
                _ => Convert.ToDecimal(v, CultureInfo.InvariantCulture)
            };
        }

        private static object Eval(string expr, Dictionary<string, object?> vars)
            => new Parser(expr, vars).EvalExpr();
    }
}
