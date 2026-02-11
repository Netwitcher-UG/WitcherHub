using Esprima;
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace WitcherHub.Application.Validators.Services
{
    public static class JsExprGuard
    {
        // منع كلمات/تراكيب تتحول "statements" أو خطرة
        private static readonly Regex Banned =
            new(@"\b(for|while|do|function|class|new|return|throw|try|catch|switch|import|export|debugger|eval)\b",
                RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public static bool TryParseExpression(string expr, out string? error)
        {
            error = null;

            if (string.IsNullOrWhiteSpace(expr))
            {
                error = "Expression cannot be empty.";
                return false;
            }

            // نرفض blocks/semicolon لتجنب statements
            if (Banned.IsMatch(expr) || expr.Contains("{") || expr.Contains("}") || expr.Contains(";"))
            {
                error = "Expression contains unsupported tokens (statements/blocks are not allowed).";
                return false;
            }

            try
            {
                // لفّها بين أقواس عشان تعتبر Expression
                var parser = new JavaScriptParser();
                parser.ParseScript($"({expr})");
                return true;
            }
            catch (ParserException ex)
            {
                error = ex.Description;
                return false;
            }
        }

        // Discount عندك واضح أنه رقم literal مثل 0.10
        public static bool TryParseDecimalLiteral(string expr, out decimal value)
        {
            value = 0m;
            if (string.IsNullOrWhiteSpace(expr)) return false;

            return decimal.TryParse(
                expr.Trim(),
                System.Globalization.NumberStyles.Number,
                System.Globalization.CultureInfo.InvariantCulture,
                out value
            );
        }
    }
}
