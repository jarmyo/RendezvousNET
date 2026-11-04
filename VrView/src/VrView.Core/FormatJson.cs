using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace VrView.Core;

/// <summary>
/// Readable JSON form of a <see cref="VideoFormat"/>, e.g.
/// <c>{ "projection": "180", "eyes": "sbs", "coverage": [200, 150], "nadir": 30 }</c>.
/// Only values that differ from the standard are written. The compact string of version 1 files
/// ("Dome180,SideBySide,0,190,h=200") is still accepted.
/// </summary>
public sealed class VideoFormatJsonConverter : JsonConverter<VideoFormat>
{
    public static string ProjectionName(Projection p) => p switch
    {
        Projection.Flat => "flat",
        Projection.Dome180 => "180",
        Projection.Sphere360 => "360",
        Projection.Fisheye => "fisheye",
        Projection.Eac => "eac",
        Projection.Cubemap => "cubemap",
        Projection.Mesh => "mesh",
        _ => "flat",
    };

    private static Projection? ParseProjection(string? s) => s?.ToLowerInvariant() switch
    {
        "flat" or "plano" or "2d" => Projection.Flat,
        "180" or "dome180" or "vr180" => Projection.Dome180,
        "360" or "sphere360" => Projection.Sphere360,
        "fisheye" => Projection.Fisheye,
        "eac" => Projection.Eac,
        "cubemap" => Projection.Cubemap,
        "mesh" => Projection.Mesh,
        _ => null,
    };

    private static string EyesName(StereoMode s) => s switch
    {
        StereoMode.SideBySide => "sbs",
        StereoMode.TopBottom => "tb",
        _ => "mono",
    };

    private static StereoMode? ParseEyes(string? s) => s?.ToLowerInvariant() switch
    {
        "mono" or "2d" => StereoMode.Mono,
        "sbs" or "lr" or "sidebyside" => StereoMode.SideBySide,
        "tb" or "ou" or "topbottom" => StereoMode.TopBottom,
        _ => null,
    };

    private static string LensName(FisheyeLens l) => l switch
    {
        FisheyeLens.Equisolid => "equisolid",
        FisheyeLens.Stereographic => "stereographic",
        _ => "equidistant",
    };

    public override VideoFormat Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
            return VideoFormat.TryParse(reader.GetString()) ?? VideoFormat.Default;
        if (reader.TokenType != JsonTokenType.StartObject) throw new JsonException("Format expected.");

        var f = VideoFormat.Default;
        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            if (reader.TokenType != JsonTokenType.PropertyName) throw new JsonException();
            string name = reader.GetString()!.ToLowerInvariant();
            reader.Read();
            switch (name)
            {
                case "projection": f = f with { Projection = ParseProjection(ReadText(ref reader)) ?? f.Projection }; break;
                case "eyes": f = f with { Stereo = ParseEyes(ReadText(ref reader)) ?? f.Stereo }; break;
                case "swap": f = f with { SwapEyes = reader.TokenType == JsonTokenType.True }; break;
                case "fov": f = f with { FisheyeFov = Math.Clamp(ReadFloat(ref reader, 190), 90, 360) }; break;
                case "lens":
                    f = f with
                    {
                        Lens = ReadText(ref reader)?.ToLowerInvariant() switch
                        {
                            "equisolid" => FisheyeLens.Equisolid,
                            "stereographic" => FisheyeLens.Stereographic,
                            _ => FisheyeLens.Equidistant,
                        },
                    };
                    break;
                case "coverage":
                    {
                        var (h, v) = ReadPair(ref reader);
                        f = f with { HFov = Math.Clamp(h, 0, 360), VFov = Math.Clamp(v, 0, 180) };
                        break;
                    }
                case "center":
                    {
                        var (x, y) = ReadPair(ref reader);
                        f = f with { CenterX = Math.Clamp(x, -0.25f, 0.25f), CenterY = Math.Clamp(y, -0.25f, 0.25f) };
                        break;
                    }
                case "radius": f = f with { RadiusScale = Math.Clamp(ReadFloat(ref reader, 1), 0.5f, 1.5f) }; break;
                case "nadir": f = f with { NadirDeg = Math.Clamp(ReadFloat(ref reader, 0), 0, 90) }; break;
                default: reader.Skip(); break;
            }
        }
        // A standard coverage is stored as 0 so the format compares equal to the untuned one.
        if (f.HFov > 0 && MathF.Abs(f.HFov - f.StandardHFov) < 0.01f) f = f with { HFov = 0 };
        if (f.VFov > 0 && MathF.Abs(f.VFov - 180f) < 0.01f) f = f with { VFov = 0 };
        return f;
    }

    public override void Write(Utf8JsonWriter writer, VideoFormat f, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteString("projection", ProjectionName(f.Projection));
        writer.WriteString("eyes", EyesName(f.Stereo));
        if (f.SwapEyes) writer.WriteBoolean("swap", true);
        if (f.Projection == Projection.Fisheye)
        {
            writer.WriteNumber("fov", Round(f.FisheyeFov, 2));
            if (f.Lens != FisheyeLens.Equidistant) writer.WriteString("lens", LensName(f.Lens));
            if (f.CenterX != 0 || f.CenterY != 0) WritePair(writer, "center", Round(f.CenterX, 4), Round(f.CenterY, 4));
            if (f.RadiusScale != 1f) writer.WriteNumber("radius", Round(f.RadiusScale, 3));
        }
        if (f.HFov > 0 || f.VFov > 0) WritePair(writer, "coverage", Round(f.EquirectHFov, 2), Round(f.EquirectVFov, 2));
        if (f.NadirDeg > 0) writer.WriteNumber("nadir", Round(f.NadirDeg, 2));
        writer.WriteEndObject();
    }

    private static double Round(float v, int digits) => Math.Round((double)v, digits);

    private static void WritePair(Utf8JsonWriter w, string name, double a, double b)
    {
        w.WriteStartArray(name);
        w.WriteNumberValue(a);
        w.WriteNumberValue(b);
        w.WriteEndArray();
    }

    private static string? ReadText(ref Utf8JsonReader r) => r.TokenType switch
    {
        JsonTokenType.String => r.GetString(),
        JsonTokenType.Number => r.GetDouble().ToString(CultureInfo.InvariantCulture),
        _ => null,
    };

    private static float ReadFloat(ref Utf8JsonReader r, float fallback)
    {
        if (r.TokenType == JsonTokenType.Number) return (float)r.GetDouble();
        if (r.TokenType == JsonTokenType.String &&
            float.TryParse(r.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var v)) return v;
        r.Skip();
        return fallback;
    }

    private static (float A, float B) ReadPair(ref Utf8JsonReader r)
    {
        if (r.TokenType != JsonTokenType.StartArray) { r.Skip(); return (0, 0); }
        var values = new List<float>();
        while (r.Read() && r.TokenType != JsonTokenType.EndArray)
            values.Add(ReadFloat(ref r, 0));
        return (values.Count > 0 ? values[0] : 0, values.Count > 1 ? values[1] : 0);
    }
}

/// <summary>Accepts a bare number (version 1) or <c>{ "at": ms, "name": "..." }</c>.</summary>
public sealed class SpotEntryJsonConverter : JsonConverter<SpotEntry>
{
    public override SpotEntry Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Number) return new SpotEntry { At = reader.GetInt64() };
        if (reader.TokenType != JsonTokenType.StartObject) throw new JsonException("Spot expected.");
        var spot = new SpotEntry();
        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            string name = reader.GetString()!;
            reader.Read();
            if (name == "at" && reader.TokenType == JsonTokenType.Number) spot.At = reader.GetInt64();
            else if (name == "name" && reader.TokenType == JsonTokenType.String) spot.Name = reader.GetString();
            else reader.Skip();
        }
        return spot;
    }

    public override void Write(Utf8JsonWriter writer, SpotEntry value, JsonSerializerOptions options)
    {
        if (string.IsNullOrEmpty(value.Name))
        {
            writer.WriteNumberValue(value.At);
            return;
        }
        writer.WriteStartObject();
        writer.WriteNumber("at", value.At);
        writer.WriteString("name", value.Name);
        writer.WriteEndObject();
    }
}

/// <summary>Accepts the format string of version 1 or <c>{ "profile": "...", "format": {...} }</c>.</summary>
public sealed class FolderEntryJsonConverter : JsonConverter<FolderEntry>
{
    public override FolderEntry Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String) return new FolderEntry { Format = VideoFormat.TryParse(reader.GetString()) };
        if (reader.TokenType != JsonTokenType.StartObject) throw new JsonException("Folder expected.");
        var entry = new FolderEntry();
        var formatConverter = new VideoFormatJsonConverter();
        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            string name = reader.GetString()!;
            reader.Read();
            if (name == "profile" && reader.TokenType == JsonTokenType.String) entry.Profile = reader.GetString();
            else if (name == "format" && reader.TokenType is JsonTokenType.StartObject or JsonTokenType.String)
                entry.Format = formatConverter.Read(ref reader, typeof(VideoFormat), options);
            else reader.Skip();
        }
        return entry;
    }

    public override void Write(Utf8JsonWriter writer, FolderEntry value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        if (value.Profile is { Length: > 0 } p) writer.WriteString("profile", p);
        if (value.Format is { } f)
        {
            writer.WritePropertyName("format");
            new VideoFormatJsonConverter().Write(writer, f, options);
        }
        writer.WriteEndObject();
    }
}
