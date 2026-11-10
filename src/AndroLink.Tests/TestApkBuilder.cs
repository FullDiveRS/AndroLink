using System.IO.Compression;
using System.Text;

namespace AndroLink.Tests;

/// <summary>
/// Builds a tiny but *structurally valid* APK in memory: binary XML manifest
/// (AXML) plus a minimal resources.arsc that maps @string/app_name to a label.
/// Exercises BinaryXmlParser and ArscLabelResolver end-to-end.
/// </summary>
public static class TestApkBuilder
{
    public const string Package = "com.androlink.testapp";
    public const string Label = "AndroLink Test App";
    public const string VersionName = "1.2.3";
    public const int VersionCode = 7;
    public const int MinSdk = 24;
    public const uint LabelResourceId = 0x7F0E0000;

    public static string WriteTestApk()
    {
        var path = Path.Combine(Path.GetTempPath(), $"al-test-{Guid.NewGuid():N}.apk");
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);

        var manifestEntry = zip.CreateEntry("AndroidManifest.xml");
        using (var s = manifestEntry.Open()) s.Write(BuildManifest());

        var arscEntry = zip.CreateEntry("resources.arsc");
        using (var s = arscEntry.Open()) s.Write(BuildResources());

        using (var s = zip.CreateEntry("classes.dex").Open()) { /* empty is fine */ }

        return path;
    }

    // ------------------------------------------------------------------ AXML

    private static byte[] BuildManifest()
    {
        var strings = new List<string>
        {
            "manifest",                 // 0
            "package",                  // 1
            Package,                    // 2
            "versionCode",              // 3
            "versionName",              // 4
            VersionName,                // 5
            "uses-sdk",                 // 6
            "minSdkVersion",            // 7
            "targetSdkVersion",         // 8
            "uses-permission",          // 9
            "name",                     // 10
            "android.permission.INTERNET", // 11
            "application",              // 12
            "label",                    // 13
            "@string/app_name",         // 14
        };

        var body = new List<byte>();
        var pool = StringPool(strings);

        body.AddRange(pool);

        // <manifest package=... versionCode=7 versionName=1.2.3>
        body.AddRange(StartElement("manifest", new List<Attr>
        {
            Attr.Str("package", 2),
            Attr.Int("versionCode", VersionCode),
            Attr.Str("versionName", 5),
        }));
        // <uses-sdk minSdkVersion=24 targetSdkVersion=33/>
        body.AddRange(StartElement("uses-sdk", new List<Attr>
        {
            Attr.Int("minSdkVersion", MinSdk),
            Attr.Int("targetSdkVersion", 33),
        }));
        body.AddRange(EndElement("uses-sdk"));
        // <uses-permission android:name="...INTERNET"/>
        body.AddRange(StartElement("uses-permission", new List<Attr> { Attr.Str("name", 11) }));
        body.AddRange(EndElement("uses-permission"));
        // <application label="@string/app_name"/>
        body.AddRange(StartElement("application", new List<Attr> { Attr.Ref("label", LabelResourceId) }));
        body.AddRange(EndElement("application"));
        body.AddRange(EndElement("manifest"));

        var data = new List<byte>();
        var total = 8 + body.Count;
        PutU16(data, 0x0003);      // RES_XML_TYPE
        PutU16(data, 8);           // header size
        PutU32(data, (uint)total);
        data.AddRange(body);
        return data.ToArray();
    }

    private readonly struct Attr
    {
        public string Name { get; init; }
        public byte DataType { get; init; }
        public int Data { get; init; }
        public int RawIndex { get; init; }

        public static Attr Str(string name, int stringIndex) => new()
        {
            Name = name, DataType = 0x03, Data = stringIndex, RawIndex = stringIndex,
        };
        public static Attr Int(string name, int value) => new()
        {
            Name = name, DataType = 0x10, Data = value, RawIndex = -1,
        };
        public static Attr Ref(string name, uint resourceId) => new()
        {
            Name = name, DataType = 0x01, Data = (int)resourceId, RawIndex = -1,
        };
    }

    private static int IndexOf(IReadOnlyList<string> strings, string value)
    {
        for (var i = 0; i < strings.Count; i++)
        {
            if (strings[i] == value) return i;
        }
        return -1;
    }

    private static byte[] StartElement(string name, List<Attr> attributes)
    {
        // Name indices refer to the manifest string pool built in BuildManifest.
        var nameIndex = name switch
        {
            "manifest" => 0,
            "uses-sdk" => 6,
            "uses-permission" => 9,
            "application" => 12,
            _ => throw new ArgumentException(name),
        };

        var data = new List<byte>();
        PutU16(data, 0x0102);      // START_ELEMENT
        PutU16(data, 16);          // header size
        PutU32(data, (uint)(16 + 20 + attributes.Count * 20));
        PutU32(data, 1);           // line number
        PutU32(data, 0xFFFFFFFF);  // comment
        PutU32(data, 0xFFFFFFFF);  // ns
        PutU32(data, (uint)nameIndex);
        PutU16(data, 20);          // attributeStart
        PutU16(data, 20);          // attributeSize
        PutU16(data, (ushort)attributes.Count);
        PutU16(data, 0);           // idIndex
        PutU16(data, 0);           // classIndex
        PutU16(data, 0);           // styleIndex

        foreach (var attr in attributes)
        {
            PutU32(data, 0xFFFFFFFF);                       // ns
            PutU32(data, (uint)IndexOf(StringsFor(attr.Name), attr.Name)); // name
            PutU32(data, (uint)attr.RawIndex);              // rawValue
            PutU16(data, 8);                                // typed value size
            data.Add(0);                                    // res0
            data.Add(attr.DataType);
            PutU32(data, (uint)attr.Data);
        }
        return data.ToArray();
    }

    // Attribute names all live in one pool; map through a static dictionary.
    private static readonly IReadOnlyList<string> StringsForNames = new[]
    {
        "manifest", "package", Package, "versionCode", "versionName", VersionName,
        "uses-sdk", "minSdkVersion", "targetSdkVersion", "uses-permission", "name",
        "android.permission.INTERNET", "application", "label", "@string/app_name",
    };

    private static IReadOnlyList<string> StringsFor(string name) => StringsForNames;

    private static int NameIndex(string name) => StringsForNames.ToList().IndexOf(name);

    private static byte[] EndElement(string name)
    {
        var data = new List<byte>();
        PutU16(data, 0x0103);      // END_ELEMENT
        PutU16(data, 16);
        PutU32(data, 24);
        PutU32(data, 1);           // line
        PutU32(data, 0xFFFFFFFF);  // comment
        PutU32(data, 0xFFFFFFFF);  // ns
        PutU32(data, (uint)NameIndex(name));
        return data.ToArray();
    }

    // The StartElement helper resolves names through this shared pool list.
    private static readonly List<string> ManifestStrings = new()
    {
        "manifest", "package", Package, "versionCode", "versionName", VersionName,
        "uses-sdk", "minSdkVersion", "targetSdkVersion", "uses-permission", "name",
        "android.permission.INTERNET", "application", "label", "@string/app_name",
    };

    private static byte[] StringPool(IReadOnlyList<string> strings)
    {
        var offsets = new List<uint>();
        var stringsData = new List<byte>();
        foreach (var s in strings)
        {
            offsets.Add((uint)stringsData.Count);
            PutU16(stringsData, (ushort)s.Length);
            foreach (var ch in s) PutU16(stringsData, ch);
            PutU16(stringsData, 0);
            while (stringsData.Count % 4 != 0) stringsData.Add(0);
        }
        while ((28 + strings.Count * 4 + stringsData.Count) % 4 != 0) stringsData.Add(0);

        var stringsStart = 28 + strings.Count * 4;
        var data = new List<byte>();
        PutU16(data, 0x0001);                              // RES_STRING_POOL_TYPE
        PutU16(data, 28);                                  // header size
        PutU32(data, (uint)(stringsStart + stringsData.Count));
        PutU32(data, (uint)strings.Count);
        PutU32(data, 0);                                   // styleCount
        PutU32(data, 0);                                   // flags: UTF-16
        PutU32(data, (uint)stringsStart);
        PutU32(data, 0);                                   // stylesStart
        foreach (var offset in offsets) PutU32(data, offset);
        data.AddRange(stringsData);
        return data.ToArray();
    }

    // -------------------------------------------------------- resources.arsc

    private static byte[] BuildResources()
    {
        // Global value pool: index 0 = label
        var valuePool = StringPool(new[] { Label });

        // Package: headerSize 288, then typeStrings pool ("string"), keyStrings pool ("app_name"),
        // then a typeSpec + a dense type chunk with entry 0 -> STRING value pool index 0.
        var typeStrings = StringPool(new[] { "string" });
        var keyStrings = StringPool(new[] { "app_name" });

        var typeSpec = new List<byte>();
        PutU16(typeSpec, 0x0202);          // TYPE_SPEC
        PutU16(typeSpec, 16);
        PutU32(typeSpec, 20);              // size = 16 + 1 * 4
        typeSpec.Add(0x0E);                // type id (string = 14)
        typeSpec.Add(0);                   // res0
        PutU16(typeSpec, 0);               // res1
        PutU32(typeSpec, 1);               // entryCount
        PutU32(typeSpec, 0);               // flags for entry 0

        // Type chunk: dense, one entry, STRING value.
        var typeHeaderSize = 28;           // 8 + id/res/reserved(4) + entryCount(4) + entriesStart(4) + config(8)
        var entryCount = 1;
        var entriesStart = typeHeaderSize + entryCount * 4;
        var entry = new List<byte>();
        PutU16(entry, 8);                  // entry size
        PutU16(entry, 0);                  // flags: simple
        PutU32(entry, 0);                  // key index
        PutU16(entry, 8);                  // value size
        entry.Add(0);                      // res0
        entry.Add(0x03);                   // STRING
        PutU32(entry, 0);                  // global value pool index 0

        var typeChunkSize = entriesStart + entry.Count;
        var type = new List<byte>();
        PutU16(type, 0x0201);              // TYPE
        PutU16(type, (ushort)typeHeaderSize);
        PutU32(type, (uint)typeChunkSize);
        type.Add(0x0E);                    // type id
        type.Add(0);                       // flags: dense
        PutU16(type, 0);                   // reserved
        PutU32(type, (uint)entryCount);
        PutU32(type, (uint)entriesStart);
        // config: size 8, all default
        PutU32(type, 8);
        PutU32(type, 0);
        PutU32(type, 0);                   // entry 0 offset (relative to entriesStart)
        type.AddRange(entry);

        var typeStringsOffset = 288;
        var keyStringsOffset = typeStringsOffset + typeStrings.Length;

        var packageSize = keyStringsOffset + keyStrings.Length + typeSpec.Count + type.Count;

        var package = new List<byte>();
        PutU16(package, 0x0200);           // RES_TABLE_PACKAGE_TYPE
        PutU16(package, 288);              // header size
        PutU32(package, (uint)packageSize);
        PutU32(package, 0x7F);             // package id
        var nameBytes = Encoding.Unicode.GetBytes("test");
        package.AddRange(nameBytes);
        while (package.Count < 12 + 256) package.Add(0); // pad name (128 UTF-16 chars)
        PutU32(package, (uint)typeStringsOffset);
        PutU32(package, 1);                // lastPublicType
        PutU32(package, (uint)keyStringsOffset);
        PutU32(package, 1);                // lastPublicKey
        while (package.Count < 288) package.Add(0);
        package.AddRange(typeStrings);
        package.AddRange(keyStrings);
        package.AddRange(typeSpec);
        package.AddRange(type);

        var tableSize = 12 + valuePool.Length + package.Count;
        var table = new List<byte>();
        PutU16(table, 0x0002);             // RES_TABLE_TYPE
        PutU16(table, 12);                 // header size
        PutU32(table, (uint)tableSize);
        PutU32(table, 1);                  // packageCount
        table.AddRange(valuePool);
        table.AddRange(package);
        return table.ToArray();
    }

    private static void PutU16(List<byte> list, ushort value)
    {
        list.Add((byte)value);
        list.Add((byte)(value >> 8));
    }

    private static void PutU32(List<byte> list, uint value)
    {
        list.Add((byte)value);
        list.Add((byte)(value >> 8));
        list.Add((byte)(value >> 16));
        list.Add((byte)(value >> 24));
    }
}
