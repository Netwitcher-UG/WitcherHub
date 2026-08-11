// Accept German decimal notation in client-side validation.
//
// jQuery Validate's built-in "number" and "range" rules only understand
// invariant notation, so a German user typing "0,00" or "1.234,56" was blocked
// in the browser before the request ever reached the server. The server-side
// counterpart is FlexibleDecimalModelBinder; keep the two in sync.
(function () {
    "use strict";

    if (typeof window.jQuery === "undefined" || typeof window.jQuery.validator === "undefined") {
        return;
    }

    var $ = window.jQuery;

    // "1234.56" | "1,234.56" | "1234,56" | "1.234,56" -> Number, or NaN.
    function parseFlexible(value) {
        if (typeof value === "number") {
            return value;
        }

        if (typeof value !== "string") {
            return NaN;
        }

        var text = value.trim().replace(/[€\s ]/g, "");
        if (text === "") {
            return NaN;
        }

        var lastComma = text.lastIndexOf(",");
        var lastDot = text.lastIndexOf(".");

        if (lastComma >= 0 && lastDot >= 0) {
            // The separator that appears last is the decimal separator.
            text = lastComma > lastDot
                ? text.replace(/\./g, "").replace(",", ".")
                : text.replace(/,/g, "");
        } else if (lastComma >= 0) {
            var isThousands = text.indexOf(",") === lastComma && text.length - lastComma - 1 === 3;
            text = isThousands ? text.replace(/,/g, "") : text.replace(",", ".");
        }

        return /^-?\d*\.?\d+$/.test(text) ? parseFloat(text) : NaN;
    }

    window.witcherhubParseDecimal = parseFlexible;

    $.validator.methods.number = function (value, element) {
        return this.optional(element) || !isNaN(parseFlexible(value));
    };

    $.validator.methods.range = function (value, element, param) {
        if (this.optional(element)) {
            return true;
        }

        var parsed = parseFlexible(value);
        return !isNaN(parsed) && parsed >= param[0] && parsed <= param[1];
    };

    $.validator.methods.min = function (value, element, param) {
        return this.optional(element) || parseFlexible(value) >= param;
    };

    $.validator.methods.max = function (value, element, param) {
        return this.optional(element) || parseFlexible(value) <= param;
    };
})();
