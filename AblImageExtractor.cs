using System.Buffers.Binary;
using SharpCompress.Compressors.LZMA;

namespace QuestStack;

internal static class AblImageExtractor
{
    private static readonly byte[] LzmaSectionGuid =
        new Guid("ee4e5898-3914-4259-9d6e-dc7bd79403cf").ToByteArray();

    private const int CommonSectionHeaderSize = 4;
    private const int GuidDefinedHeaderSize = 24;
    private const int LzmaHeaderSize = 13;
    private const int MaximumDecompressedSize = 64 * 1024 * 1024;

    public static IEnumerable<byte[]> DecompressGuidedLzmaSections(byte[] image)
    {
        for (int guidOffset = CommonSectionHeaderSize;
             guidOffset <= image.Length - LzmaSectionGuid.Length;
             guidOffset++)
        {
            if (!MatchesAt(image, guidOffset, LzmaSectionGuid))
                continue;

            int sectionOffset = guidOffset - CommonSectionHeaderSize;
            int sectionSize = ReadUInt24LittleEndian(image, sectionOffset);
            if (image[sectionOffset + 3] != 0x02 || sectionSize == 0xffffff)
                continue;

            if (sectionSize < GuidDefinedHeaderSize || sectionOffset + sectionSize > image.Length)
                continue;

            ushort dataOffset = BinaryPrimitives.ReadUInt16LittleEndian(image.AsSpan(guidOffset + 16, 2));
            if (dataOffset < GuidDefinedHeaderSize || dataOffset >= sectionSize)
                continue;

            int lzmaOffset = sectionOffset + dataOffset;
            int lzmaLength = sectionSize - dataOffset;
            byte[]? decompressed = TryDecompressLzma(image, lzmaOffset, lzmaLength);
            if (decompressed != null)
                yield return decompressed;
        }
    }

    private static byte[]? TryDecompressLzma(byte[] source, int offset, int length)
    {
        if (length <= LzmaHeaderSize)
            return null;

        byte[] properties = source.AsSpan(offset, 5).ToArray();
        ulong declaredSize = BinaryPrimitives.ReadUInt64LittleEndian(source.AsSpan(offset + 5, 8));
        if (declaredSize == 0 || declaredSize > MaximumDecompressedSize)
            return null;

        int compressedOffset = offset + LzmaHeaderSize;
        int compressedLength = length - LzmaHeaderSize;

        try
        {
            using var input = new MemoryStream(source, compressedOffset, compressedLength, writable: false);
            using LzmaStream decoder = LzmaStream.Create(
                properties,
                input,
                compressedLength,
                checked((long)declaredSize));
            using var output = new MemoryStream(checked((int)declaredSize));
            decoder.CopyTo(output);

            if ((ulong)output.Length != declaredSize)
                return null;

            return output.ToArray();
        }
        catch
        {
            return null;
        }
    }

    private static bool MatchesAt(byte[] source, int offset, byte[] expected)
    {
        if (offset < 0 || offset + expected.Length > source.Length)
            return false;

        for (int index = 0; index < expected.Length; index++)
        {
            if (source[offset + index] != expected[index])
                return false;
        }

        return true;
    }

    private static int ReadUInt24LittleEndian(byte[] source, int offset)
    {
        return source[offset] |
               (source[offset + 1] << 8) |
               (source[offset + 2] << 16);
    }
}
