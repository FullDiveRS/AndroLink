namespace AndroLink.Core;

using System.Text;

/// <summary>
/// Parser for Android binary XML (AXML) - the compiled format of
/// AndroidManifest.xml inside an APK. Extracts a lightweight element tree with
/// typed attribute values.
/// </summary>
public sealed class BinaryXmlParser
{
    public const ushort ResXmlType = 0x0003;
    public const ushort ResStringPoolType = 0x0001;
    public const ushort ResXmlStartElementType = 0x0102;
    public const ushort ResXmlEndElementType = 0x0103;

    public sealed class AxValue
    {
        public int DataType { get; init; }
        public int Data { get; init; }
        public string? Raw { get; init; }
        public string? Str { get; init; }

        /// <summary>Plain integer value (INT_DEC / INT_HEX).</summary>
        public int IntValue => DataType is 0x10 or 0x11 ? Data : 0;
        public bool BoolValue => DataType == 0x12 && Data != 0;
        /// <summary>True when the value is a resource reference such as @string/app_name.</summary>
        public bool IsReference => DataType == 0x01;
        public string? Text => Str ?? Raw;
    }

    public sealed class AxElement
    {
        public string Name { get; init; } = "";
        public Dictionary<string, AxValue> Attributes { get; } = new(StringComparer.OrdinalIgnoreCase);
        public List<AxElement> Children { get; } = new();

        public AxValue? Attr(string name) => Attributes.TryGetValue(name, out var v) ? v : null;
        public IEnumerable<AxElement> ChildrenNamed(string name) => Children.Where(c => c.Name == name);
        public AxElement? FirstChild(string name) => Children.FirstOrDefault(c => c.Name == name);
    }

    private readonly byte[] _data;
    private List<string> _strings = new();

    private BinaryXmlParser(byte[] data) => _data = data;

    public static AxElement Parse(byte[] data)
    {
        var parser = new BinaryXmlParser(data);
        return parser.ParseRoot();
    }

    private AxElement ParseRoot()
    {
        ushort fileType = U16(0);
        if (fileType != ResXmlType)
            throw new InvalidDataException($"not binary XML (chunk type 0x{fileType:X4})");

        uint pos = U16(2); // file header size (8)
        var stack = new Stack<AxElement>();
        AxElement? root = null;

        while (pos + 8 <= (uint)_data.Length)
        {
            ushort type = U16((int)pos);
            uint size = U32((int)pos + 4);
            if (size < 8 || pos + size > (uint)_data.Length) break;

            switch (type)
            {
                case ResStringPoolType:
                    _strings = ReadStringPool((int)pos, _data);
                    break;

                case ResXmlStartElementType:
                {
                    int nameIdx = (int)U32((int)pos + 20);
                    ushort attrStart = U16((int)pos + 24);
                    ushort attrSize = U16((int)pos + 26);
                    ushort attrCount = U16((int)pos + 28);

                    var element = new AxElement { Name = StringAt(nameIdx) ?? "?" };
                    for (int i = 0; i < attrCount; i++)
                    {
                        int a = (int)pos + 16 + attrStart + i * attrSize;
                        if (a + 20 > _data.Length) break;
                        int attrNameIdx = (int)U32(a + 4);
                        int rawValueIdx = (int)U32(a + 8);
                        byte dataType = _data[a + 15];
                        int data = (int)U32(a + 16);

                        var value = new AxValue
                        {
                            DataType = dataType,
                            Data = data,
                            Raw = rawValueIdx >= 0 ? StringAt(rawValueIdx) : null,
                            Str = dataType == 0x03 ? StringAt(data) : null,
                        };
                        element.Attributes[StringAt(attrNameIdx) ?? $"attr{i}"] = value;
                    }

                    if (stack.Count == 0)
                    {
                        root = element;
                    }
                    else
                    {
                        stack.Peek().Children.Add(element);
                    }
                    stack.Push(element);
                    break;
                }

                case ResXmlEndElementType:
                    if (stack.Count > 0) stack.Pop();
                    break;
            }

            pos += size;
        }

        return root ?? throw new InvalidDataException("binary XML contained no elements");
    }

    private string? StringAt(int index)
        => index >= 0 && index < _strings.Count ? _strings[index] : null;

    private ushort U16(int offset) => (ushort)(_data[offset] | (_data[offset + 1] << 8));

    private uint U32(int offset)
        => (uint)(_data[offset] | (_data[offset + 1] << 8) | (_data[offset + 2] << 16) | (_data[offset + 3] << 24));

    /// <summary>Decodes a RES_STRING_POOL_TYPE chunk (both UTF-16 and UTF-8 encodings).</summary>
    internal static List<string> ReadStringPool(int chunkStart, byte[] data)
    {
        var result = new List<string>();
        if (chunkStart + 28 > data.Length) return result;

        uint stringCount = U32(data, chunkStart + 8);
        uint flags = U32(data, chunkStart + 16);
        uint stringsStart = U32(data, chunkStart + 20);
        bool utf8 = (flags & 0x100) != 0;

        for (uint i = 0; i < stringCount; i++)
        {
            uint offsetPos = (uint)chunkStart + 28 + i * 4;
            if (offsetPos + 4 > (uint)data.Length) break;
            uint offset = U32(data, (int)offsetPos);
            uint at = (uint)chunkStart + stringsStart + offset;
            if (at >= (uint)data.Length) { result.Add(""); continue; }

            result.Add(utf8 ? ReadUtf8String(data, at) : ReadUtf16String(data, at));
        }
        return result;
    }

    private static string ReadUtf8String(byte[] data, uint at)
    {
        uint pos = at;
        // UTF-8 pool: u16 char count (as u8 or u8-pair), u8 byte length, then bytes, NUL-terminated
        int charLen = data[pos++];
        if ((charLen & 0x80) != 0) charLen = ((charLen & 0x7F) << 8) | data[pos++];
        int byteLen = data[pos++];
        if ((byteLen & 0x80) != 0) byteLen = ((byteLen & 0x7F) << 8) | data[pos++];

        int take = Math.Min(byteLen, data.Length - (int)pos);
        return Encoding.UTF8.GetString(data, (int)pos, Math.Max(0, take));
    }

    private static string ReadUtf16String(byte[] data, uint at)
    {
        uint pos = at;
        int len = U16(data, (int)pos);
        pos += 2;
        if ((len & 0x8000) != 0)
        {
            len = ((len & 0x7FFF) << 16) | (U16(data, (int)pos));
            pos += 2;
        }

        int take = Math.Min(len * 2, data.Length - (int)pos);
        return Encoding.Unicode.GetString(data, (int)pos, Math.Max(0, take));
    }

    internal static ushort U16(byte[] d, int o) => (ushort)(d[o] | (d[o + 1] << 8));

    internal static uint U32(byte[] data, int offset)
        => (uint)(data[offset] | (data[offset + 1] << 8) | (data[offset + 2] << 16) | (data[offset + 3] << 24));
}
