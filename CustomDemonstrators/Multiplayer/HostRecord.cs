using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Xml.Serialization;
using CustomDemonstrators.Api;
using CustomDemonstrators.Config;
using CustomDemonstrators.Saves;
using Newtonsoft.Json.Linq;

namespace CustomDemonstrators.Multiplayer;

// Everything a host's game state was built from that clients need to be aware of
internal sealed class HostRecord
{
    private const string SettingsField = "settings";
    private const string SaveField = "save";
    private const string OwnedField = "ownedCarTypes";

    internal Settings Settings = new();

    internal JObject Save = [];

    internal int[] OwnedCarTypes = [];

    private static string? _builtFrom;

    internal static void NoteWorldBuilding() => _builtFrom = Serialize(Main.Settings);

    internal static HostRecord Capture()
    {
        var settings = Deserialize(_builtFrom ?? Serialize(Main.Settings));

        // Summon prices are the one setting that applies unguarded 
        settings.SummonPriceOverrides = Main.Settings.SummonPriceOverrides;

        var save = new JObject();
        foreach (var property in SaveState.Data()?.GetJsonObject().Properties() ?? [])
        {
            if (property.Name.StartsWith(SaveRecord.KeyPrefix, StringComparison.Ordinal))
                save[property.Name] = property.Value.DeepClone();
        }

        return new HostRecord
        {
            Settings = settings,
            Save = save,
            OwnedCarTypes = [.. GarageOwnership.OwnedCarTypes()],
        };
    }

    internal byte[] ToBytes()
    {
        var json = new JObject
        {
            [SettingsField] = Serialize(Settings),
            [SaveField] = Save,
            [OwnedField] = new JArray(OwnedCarTypes),
        };

        using var buffer = new MemoryStream();
        using (var gzip = new GZipStream(buffer, CompressionMode.Compress))
        {
            var bytes = Encoding.UTF8.GetBytes(json.ToString(Newtonsoft.Json.Formatting.None));
            gzip.Write(bytes, 0, bytes.Length);
        }
        return buffer.ToArray();
    }

    internal static HostRecord FromBytes(byte[] data)
    {
        using var gzip = new GZipStream(new MemoryStream(data), CompressionMode.Decompress);
        using var reader = new StreamReader(gzip, Encoding.UTF8);
        var json = JObject.Parse(reader.ReadToEnd());

        return new HostRecord
        {
            Settings = Deserialize((string?)json[SettingsField] ?? ""),
            Save = json[SaveField] as JObject ?? [],
            OwnedCarTypes = json[OwnedField]?.Values<int>().ToArray() ?? [],
        };
    }

    // The same XML the settings are saved to disk as
    private static string Serialize(Settings settings)
    {
        using var writer = new StringWriter();
        new XmlSerializer(typeof(Settings)).Serialize(writer, settings);
        return writer.ToString();
    }

    private static Settings Deserialize(string xml)
    {
        using var reader = new StringReader(xml);
        return (Settings)new XmlSerializer(typeof(Settings)).Deserialize(reader);
    }
}
