using Microsoft.AspNetCore.Mvc.ModelBinding;
using System.Globalization;

namespace WitcherHub.Configuration.ModelBinding
{
    /// <summary>
    /// Parses decimal input in both German and invariant notation.
    ///
    /// The request culture defaults to "en", so the stock binder rejected values
    /// typed by German users ("0,00", "1.234,56") on every money and quantity
    /// field — the reported "Base Price 0,00 is invalid" defect. Rather than
    /// switching the whole app to de-DE (which would break the JavaScript that
    /// posts invariant numbers), accept either form and normalise here.
    /// </summary>
    public sealed class FlexibleDecimalModelBinder : IModelBinder
    {
        private const char NonBreakingSpace = ' ';
        private const char NarrowNoBreakSpace = ' ';

        public Task BindModelAsync(ModelBindingContext bindingContext)
        {
            ArgumentNullException.ThrowIfNull(bindingContext);

            var modelName = bindingContext.ModelName;
            var valueProviderResult = bindingContext.ValueProvider.GetValue(modelName);

            if (valueProviderResult == ValueProviderResult.None)
                return Task.CompletedTask;

            bindingContext.ModelState.SetModelValue(modelName, valueProviderResult);

            var raw = valueProviderResult.FirstValue;
            var underlyingType = Nullable.GetUnderlyingType(bindingContext.ModelType) ?? bindingContext.ModelType;
            var isNullable = underlyingType != bindingContext.ModelType;

            if (string.IsNullOrWhiteSpace(raw))
            {
                if (isNullable)
                {
                    bindingContext.Result = ModelBindingResult.Success(null);
                    return Task.CompletedTask;
                }

                bindingContext.ModelState.TryAddModelError(modelName, "A value is required.");
                return Task.CompletedTask;
            }

            if (TryParse(raw, out var parsed))
            {
                bindingContext.Result = ModelBindingResult.Success(
                    underlyingType == typeof(double) ? (double)parsed : parsed);
                return Task.CompletedTask;
            }

            bindingContext.ModelState.TryAddModelError(
                modelName,
                $"'{raw}' is not a valid number. Use 1234.56 or 1.234,56.");

            return Task.CompletedTask;
        }

        /// <summary>
        /// Accepts "1234.56", "1,234.56", "1234,56" and "1.234,56".
        ///
        /// The input is normalised to invariant form and structurally validated
        /// rather than handed to a lenient <c>decimal.TryParse</c>: the framework
        /// parser accepts malformed grouping such as "12,34,56" and silently reads
        /// it as 123456, which is exactly the kind of wrong number a price field
        /// must never accept.
        /// </summary>
        internal static bool TryParse(string raw, out decimal value)
        {
            value = 0m;

            // Strip currency symbols and the spaces users paste in (including the
            // non-breaking and narrow no-break spaces that Word and Excel emit).
            var text = raw.Trim()
                          .Replace("€", "", StringComparison.Ordinal)
                          .Replace(" ", "", StringComparison.Ordinal)
                          .Replace(NonBreakingSpace.ToString(), "", StringComparison.Ordinal)
                          .Replace(NarrowNoBreakSpace.ToString(), "", StringComparison.Ordinal);

            if (text.Length == 0)
                return false;

            var negative = false;
            if (text[0] is '-' or '+')
            {
                negative = text[0] == '-';
                text = text[1..];
            }

            if (text.Length == 0)
                return false;

            var lastComma = text.LastIndexOf(',');
            var lastDot = text.LastIndexOf('.');

            // Whichever separator appears last is the decimal separator. When only
            // one kind is present, a single occurrence followed by exactly three
            // digits is read as grouping ("1,234" -> 1234), otherwise as decimal.
            char? decimalSeparator;

            if (lastComma >= 0 && lastDot >= 0)
                decimalSeparator = lastComma > lastDot ? ',' : '.';
            else if (lastComma >= 0)
                decimalSeparator = IsGroupingOnly(text, ',') ? null : ',';
            else if (lastDot >= 0)
                decimalSeparator = IsGroupingOnly(text, '.') ? null : '.';
            else
                decimalSeparator = null;

            string integerPart;
            string fractionPart;

            if (decimalSeparator is char separator)
            {
                var splitAt = text.LastIndexOf(separator);
                integerPart = text[..splitAt];
                fractionPart = text[(splitAt + 1)..];

                if (fractionPart.Length == 0 || !fractionPart.All(char.IsAsciiDigit))
                    return false;
            }
            else
            {
                integerPart = text;
                fractionPart = "";
            }

            var groupSeparator = decimalSeparator == ',' ? '.' : ',';
            if (!TryNormaliseIntegerPart(integerPart, groupSeparator, out var digits))
                return false;

            var normalised = fractionPart.Length > 0 ? $"{digits}.{fractionPart}" : digits;

            if (!decimal.TryParse(
                    normalised,
                    NumberStyles.AllowDecimalPoint,
                    CultureInfo.InvariantCulture,
                    out value))
            {
                return false;
            }

            if (negative)
                value = -value;

            return true;
        }

        /// <summary>
        /// True when the separator only ever appears at a valid thousands boundary,
        /// meaning the value has no decimal part at all.
        /// </summary>
        private static bool IsGroupingOnly(string text, char separator) =>
            TryNormaliseIntegerPart(text, separator, out _)
            && text.Length - text.LastIndexOf(separator) - 1 == 3;

        /// <summary>
        /// Validates grouping and returns the bare digits. Accepts either no group
        /// separator at all, or 1-3 leading digits followed by groups of exactly three.
        /// </summary>
        private static bool TryNormaliseIntegerPart(string part, char groupSeparator, out string digits)
        {
            digits = "";

            if (part.Length == 0)
            {
                digits = "0";
                return true;
            }

            if (part.All(char.IsAsciiDigit))
            {
                digits = part;
                return true;
            }

            var groups = part.Split(groupSeparator);

            if (groups.Length < 2)
                return false;

            if (groups[0].Length is < 1 or > 3 || !groups[0].All(char.IsAsciiDigit))
                return false;

            for (var i = 1; i < groups.Length; i++)
            {
                if (groups[i].Length != 3 || !groups[i].All(char.IsAsciiDigit))
                    return false;
            }

            digits = string.Concat(groups);
            return true;
        }
    }
}
