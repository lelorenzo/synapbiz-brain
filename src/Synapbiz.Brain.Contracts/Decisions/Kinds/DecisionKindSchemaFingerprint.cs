using System.Security.Cryptography;
using System.Text.Json;

namespace Nuvora.Decisions.Contracts.Kinds;

/// <summary>Culture-independent identity of a decision kind's exact schema; not a pack compatibility check.</summary>
public static class DecisionKindSchemaFingerprint
{
    /// <summary>Returns a SHA-256 hexadecimal fingerprint of the canonical schema.</summary>
    public static string Compute(DecisionKindDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        using var buffer = new MemoryStream();
        using (var json = new Utf8JsonWriter(buffer))
        {
            json.WriteStartObject();
            json.WriteString("key", descriptor.Key.Value);
            json.WriteNumber("schemaVersion", descriptor.SchemaVersion);
            json.WriteStartArray("facts");
            foreach (var fact in descriptor.Facts.OrderBy(f => f.Name, StringComparer.Ordinal))
            {
                json.WriteStartObject();
                json.WriteString("name", fact.Name);
                json.WriteString("type", fact.Type.ToString());
                json.WriteBoolean("nullable", fact.Nullable);
                json.WriteStartArray("allowedValues");
                foreach (var value in (fact.AllowedValues ?? []).Order(StringComparer.Ordinal)) { json.WriteStringValue(value); }
                json.WriteEndArray();
                json.WriteString("composedFromKind", fact.ComposedFromKind?.Value);
                WriteLabel(json, fact.LabelSource);
                json.WriteString("catalogFactId", fact.CatalogFactId);
                json.WriteEndObject();
            }
            json.WriteEndArray();
            json.WriteStartArray("outcomeFields");
            foreach (var field in descriptor.OutcomeFields.OrderBy(f => f.Name, StringComparer.Ordinal))
            {
                json.WriteStartObject();
                json.WriteString("name", field.Name);
                json.WriteString("type", field.Type.ToString());
                json.WriteBoolean("nullable", field.Nullable);
                WriteLabel(json, field.LabelSource);
                json.WriteEndObject();
            }
            json.WriteEndArray();
            json.WriteEndObject();
        }
        return Convert.ToHexStringLower(SHA256.HashData(buffer.GetBuffer().AsSpan(0, checked((int)buffer.Length))));
    }

    private static void WriteLabel(Utf8JsonWriter json, ReferenceLabelSource? source)
    {
        if (source is null) { json.WriteNull("labelSource"); return; }
        json.WriteStartObject("labelSource");
        json.WriteString("ownerModule", source.OwnerModule);
        json.WriteString("entityType", source.EntityType);
        json.WriteEndObject();
    }
}

