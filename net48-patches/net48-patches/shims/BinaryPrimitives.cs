// ============================================================================
// BinaryPrimitives.cs  --  net48 polyfill for System.Buffers.Binary
// ============================================================================
// .NET Core 2.1+ provides `System.Buffers.Binary.BinaryPrimitives` for
// endian-safe reads/writes. .NET Framework 4.x does NOT ship this type.
//
// We define it here in the same namespace so existing source code using
// `BinaryPrimitives.ReadUInt16BigEndian(...)` continues to work without
// any code changes.
//
// NOTE: this version takes `byte[]` + offset (the common usage in
// ServiceLib.UdpTest), not `ReadOnlySpan<byte>`. The Span overload would
// require System.Memory package; if you need it, add it yourself.
// ============================================================================

using System.Runtime.InteropServices;

namespace System.Buffers.Binary
{
    internal static class BinaryPrimitives
    {
        public static ushort ReadUInt16BigEndian(byte[] buffer, int offset)
        {
            unchecked
            {
                return (ushort)((buffer[offset] << 8) | buffer[offset + 1]);
            }
        }

        public static ushort ReadUInt16BigEndian(ReadOnlySpan<byte> buffer)
        {
            unchecked
            {
                return (ushort)((buffer[0] << 8) | buffer[1]);
            }
        }

        public static void WriteUInt16BigEndian(byte[] buffer, ushort value)
        {
            unchecked
            {
                buffer[0] = (byte)(value >> 8);
                buffer[1] = (byte)(value & 0xFF);
            }
        }

        public static void WriteUInt16BigEndian(Span<byte> buffer, ushort value)
        {
            unchecked
            {
                buffer[0] = (byte)(value >> 8);
                buffer[1] = (byte)(value & 0xFF);
            }
        }

        public static uint ReadUInt32BigEndian(byte[] buffer, int offset)
        {
            unchecked
            {
                return ((uint)buffer[offset] << 24)
                     | ((uint)buffer[offset + 1] << 16)
                     | ((uint)buffer[offset + 2] << 8)
                     |  buffer[offset + 3];
            }
        }
    }
}
