using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace WitcherHub.Application.Common.ConfigSchema
{
    public sealed record ConfigFieldError(string Field, string Message);

    /// <summary>
    /// Lite JSON-Schema support (subset) matching your UI schema builder:
    /// - type: object
    /// - properties
    /// - required
    /// - additionalProperties
    /// - default
    /// - enum
    /// - minimum/maximum
    /// - minLength/maxLength
    /// </summary>
    public static class ConfigSchemaLite
    {
        public static JsonDocument ApplyDefaults(JsonDocument? schemaDoc, JsonDocument? configDoc)
        {
            configDoc ??= JsonDocument.Parse("{}");
            if (schemaDoc is null) return configDoc;

            var schema = schemaDoc.RootElement;
            if (schema.ValueKind != JsonValueKind.Object) return configDoc;

            if (!schema.TryGetProperty("properties", out var propsEl) || propsEl.ValueKind != JsonValueKind.Object)
                return configDoc;

            JsonObject cfgObj;
            if (configDoc.RootElement.ValueKind == JsonValueKind.Object)
            {
                cfgObj = JsonNode.Parse(configDoc.RootElement.GetRawText()) as JsonObject ?? new JsonObject();
            }
            else
            {
                cfgObj = new JsonObject();
            }

            foreach (var p in propsEl.EnumerateObject())
            {
                var key = p.Name;
                var def = p.Value;

                if (cfgObj.ContainsKey(key)) continue;

                if (def.ValueKind == JsonValueKind.Object && def.TryGetProperty("default", out var dflt))
                {
                    cfgObj[key] = JsonNode.Parse(dflt.GetRawText());
                }
            }

            return JsonSerializer.SerializeToDocument(cfgObj);
        }

        public static List<ConfigFieldError> Validate(JsonDocument? schemaDoc, JsonDocument? configDoc)
        {
            var errors = new List<ConfigFieldError>();
            if (schemaDoc is null) return errors;

            configDoc ??= JsonDocument.Parse("{}");

            var schema = schemaDoc.RootElement;
            if (schema.ValueKind != JsonValueKind.Object)
            {
                errors.Add(new("schema", "Schema must be a JSON object."));
                return errors;
            }

            if (schema.TryGetProperty("type", out var typeEl) &&
                typeEl.ValueKind == JsonValueKind.String &&
                !string.Equals(typeEl.GetString(), "object", StringComparison.OrdinalIgnoreCase))
            {
                errors.Add(new("schema.type", "Only schema type 'object' is supported."));
                return errors;
            }

            var required = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (schema.TryGetProperty("required", out var reqEl) && reqEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var r in reqEl.EnumerateArray())
                    if (r.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(r.GetString()))
                        required.Add(r.GetString()!);
            }

            var props = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
            if (schema.TryGetProperty("properties", out var propsEl) && propsEl.ValueKind == JsonValueKind.Object)
            {
                foreach (var p in propsEl.EnumerateObject())
                    props[p.Name] = p.Value;
            }

            var allowAdditional = true;
            if (schema.TryGetProperty("additionalProperties", out var apEl))
            {
                if (apEl.ValueKind == JsonValueKind.False) allowAdditional = false;
            }

            if (configDoc.RootElement.ValueKind != JsonValueKind.Object)
            {
                errors.Add(new("config", "Config must be a JSON object."));
                return errors;
            }

            foreach (var r in required)
            {
                if (!TryGetPropertyIgnoreCase(configDoc.RootElement, r, out var val) ||
                    val.ValueKind == JsonValueKind.Null)
                {
                    errors.Add(new(r, "Field is required."));
                }
            }

            foreach (var p in configDoc.RootElement.EnumerateObject())
            {
                if (!allowAdditional && !props.ContainsKey(p.Name))
                {
                    errors.Add(new(p.Name, "Field is not allowed (additionalProperties=false)."));
                }
            }

            foreach (var defKvp in props)
            {
                var key = defKvp.Key;
                var def = defKvp.Value;

                if (!TryGetPropertyIgnoreCase(configDoc.RootElement, key, out var val) ||
                    val.ValueKind == JsonValueKind.Undefined ||
                    val.ValueKind == JsonValueKind.Null)
                {
                    continue;
                }

                if (def.ValueKind != JsonValueKind.Object) continue;

                if (def.TryGetProperty("enum", out var enumEl) && enumEl.ValueKind == JsonValueKind.Array)
                {
                    var raw = val.GetRawText();
                    var ok = enumEl.EnumerateArray().Any(e => e.GetRawText() == raw);
                    if (!ok)
                    {
                        errors.Add(new(key, "Value must be one of the allowed enum values."));
                        continue;
                    }
                }

                string? t = null;
                if (def.TryGetProperty("type", out var tEl))
                {
                    if (tEl.ValueKind == JsonValueKind.String) t = tEl.GetString();
                }

                if (string.Equals(t, "boolean", StringComparison.OrdinalIgnoreCase))
                {
                    if (val.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                        errors.Add(new(key, "Must be a boolean."));
                    continue;
                }

                if (string.Equals(t, "string", StringComparison.OrdinalIgnoreCase) || t is null)
                {
                    if (val.ValueKind != JsonValueKind.String)
                    {
                        errors.Add(new(key, "Must be a string."));
                        continue;
                    }

                    var s = val.GetString() ?? "";
                    if (def.TryGetProperty("minLength", out var minLenEl) && minLenEl.TryGetInt32(out var minLen) && s.Length < minLen)
                        errors.Add(new(key, $"Length must be >= {minLen}."));

                    if (def.TryGetProperty("maxLength", out var maxLenEl) && maxLenEl.TryGetInt32(out var maxLen) && s.Length > maxLen)
                        errors.Add(new(key, $"Length must be <= {maxLen}."));

                    continue;
                }

                if (string.Equals(t, "integer", StringComparison.OrdinalIgnoreCase))
                {
                    if (val.ValueKind != JsonValueKind.Number || !val.TryGetInt64(out var i))
                    {
                        errors.Add(new(key, "Must be an integer."));
                        continue;
                    }

                    if (def.TryGetProperty("minimum", out var minEl) && TryGetDecimal(minEl, out var min) && i < (long)min)
                        errors.Add(new(key, $"Must be >= {min}."));

                    if (def.TryGetProperty("maximum", out var maxEl) && TryGetDecimal(maxEl, out var max) && i > (long)max)
                        errors.Add(new(key, $"Must be <= {max}."));

                    continue;
                }

                if (string.Equals(t, "number", StringComparison.OrdinalIgnoreCase))
                {
                    if (val.ValueKind != JsonValueKind.Number || !TryGetDecimal(val, out var n))
                    {
                        errors.Add(new(key, "Must be a number."));
                        continue;
                    }

                    if (def.TryGetProperty("minimum", out var minEl) && TryGetDecimal(minEl, out var min) && n < min)
                        errors.Add(new(key, $"Must be >= {min}."));

                    if (def.TryGetProperty("maximum", out var maxEl) && TryGetDecimal(maxEl, out var max) && n > max)
                        errors.Add(new(key, $"Must be <= {max}."));

                    continue;
                }
            }

            return errors;
        }

        private static bool TryGetPropertyIgnoreCase(JsonElement obj, string name, out JsonElement value)
        {
            foreach (var p in obj.EnumerateObject())
            {
                if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    value = p.Value;
                    return true;
                }
            }
            value = default;
            return false;
        }

        private static bool TryGetDecimal(JsonElement el, out decimal val)
        {
            val = 0m;
            if (el.ValueKind != JsonValueKind.Number) return false;
            if (el.TryGetDecimal(out var d)) { val = d; return true; }
            if (el.TryGetDouble(out var dd)) { val = (decimal)dd; return true; }
            return false;
        }
    }
}
