namespace AndroLink.Core;

/// <summary>
/// Minimal resources.arsc reader that resolves one string resource id to its
/// default-locale value. Used to turn the manifest's <c>@string/app_name</c>
/// label reference into a human-readable app label. Handles dense and sparse
/// (Android 8+) type chunks; gracefully returns null on anything it cannot parse.
/// </summary>
public static class ArscLabelResolver
{
    private const ushort TableType = 0x0002;
    private const ushort PackageType = 0x0200;
    private const ushort TypeSpecType = 0x0202;
    private const ushort TypeTableType = 0x0201;
    private const ushort StringType = 0x0001;

    public static string? ResolveStringResource(byte[] arsc, int resourceId)
    {
        try
        {
            if (arsc.Length < 12 || U16(arsc, 0) != TableType) return null;

            int pkgId = (resourceId >> 24) & 0xFF;
            int typeId = (resourceId >> 16) & 0xFF;
            int entryId = resourceId & 0xFFFF;

            // Global value string pool sits right after the 12-byte table header.
            var valuePool = BinaryXmlParser.ReadStringPool(12, arsc);

            uint pos = (uint)(U16(arsc, 2)); // table header size
            while (pos + 8 <= (uint)arsc.Length)
            {
                ushort type = U16(arsc, (int)pos);
                uint size = U32(arsc, (int)pos + 4);
                if (size < 8 || pos + size > (uint)arsc.Length) break;

                if (type == PackageType)
                {
                    uint packageHeaderSize = U16(arsc, (int)pos + 2);
                    uint packageId = U32(arsc, (int)pos + 8);
                    if ((int)(packageId & 0xFF) == pkgId)
                    {
                        var found = SearchPackage(arsc, (int)pos, packageHeaderSize, (int)size, typeId, entryId, valuePool);
                        if (found is not null) return found;
                    }
                }
                pos += size;
            }
        }
        catch
        {
            // resource resolution is best-effort
        }
        return null;
    }

    private static string? SearchPackage(byte[] arsc, int pkgStart, uint pkgHeaderSize, int pkgSize, int typeId, int entryId, List<string> valuePool)
    {
        uint pos = (uint)pkgStart + pkgHeaderSize;
        uint pkgEnd = (uint)(pkgStart + pkgSize);

        while (pos + 8 <= pkgEnd)
        {
            ushort type = U16(arsc, (int)pos);
            uint size = U32(arsc, (int)pos + 4);
            if (size < 8 || pos + size > pkgEnd) break;

            if (type == TypeTableType)
            {
                byte chunkTypeId = arsc[(int)pos + 8];
                byte flags = arsc[(int)pos + 9];
                uint entryCount = U32(arsc, (int)pos + 12);
                uint entriesStart = U32(arsc, (int)pos + 16);

                if (chunkTypeId == typeId)
                {
                    int? offset = FindEntryOffset(arsc, (int)pos, (int)size, (int)entryCount, (int)entriesStart, entryId, (flags & 0x01) != 0);
                    if (offset is int entryOffset)
                    {
                        return ReadSimpleStringValue(arsc, (int)pos + entryOffset, valuePool);
                    }
                }
            }
            pos += size;
        }
        return null;
    }

    private static int? FindEntryOffset(byte[] arsc, int chunkStart, int chunkSize, int entryCount, int entriesStart, int entryId, bool sparse)
    {
        if (sparse)
        {
            for (int i = 0; i < entryCount; i++)
            {
                int rec = chunkStart + 20 * 0 + 0; // offsets array begins after header
                // sparse entries: pairs of (idx u16, offset u16 * 4)
                int basePos = chunkStart + HeaderSizeOf(arsc, chunkStart) + i * 4;
                if (basePos + 4 > chunkStart + chunkSize) break;
                int idx = U16(arsc, basePos);
                int off = U16(arsc, basePos + 2) * 4;
                if (idx == entryId) return entriesStart + off;
            }
            return null;
        }

        int offsetsStart = chunkStart + HeaderSizeOf(arsc, chunkStart);
        if (entryId >= entryCount) return null;
        int offPos = offsetsStart + entryId * 4;
        if (offPos + 4 > chunkStart + chunkSize) return null;
        uint offset = U32(arsc, offPos);
        if (offset == 0xFFFFFFFF) return null; // NO_ENTRY
        return entriesStart + (int)offset;
    }

    private static int HeaderSizeOf(byte[] arsc, int chunkStart) => U16(arsc, chunkStart + 2);

    private static string? ReadSimpleStringValue(byte[] arsc, int entryPos, List<string> valuePool)
    {
        if (entryPos + 8 > arsc.Length) return null;
        ushort entrySize = U16(arsc, entryPos);
        ushort entryFlags = U16(arsc, entryPos + 2);

        if ((entryFlags & 0x0001) != 0) return null; // complex/map entry - not a plain string

        int valuePos = entryPos + entrySize;
        if (valuePos + 8 > arsc.Length) return null;
        byte dataType = arsc[valuePos + 3];
        int data = (int)U32(arsc, valuePos + 4);

        if (dataType == 0x03 && data >= 0 && data < valuePool.Count) // STRING
        {
            return valuePool[data];
        }
        return null;
    }

    private static ushort U16(byte[] d, int o) => (ushort)(d[o] | (d[o + 1] << 8));

    private static uint U32(byte[] d, int o)
        => (uint)(d[o] | (d[o + 1] << 8) | (d[o + 2] << 16) | (d[o + 3] << 24));
}
