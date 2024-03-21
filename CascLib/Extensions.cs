using System;
using System.Collections.Generic;
using System.IO;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace CASCLib
{
    public static class Extensions
    {
        public static int ReadInt32BE(this BinaryReader reader)
        {
            int val = reader.ReadInt32();
            int ret = (val >> 24 & 0xFF) << 0;
            ret |= (val >> 16 & 0xFF) << 8;
            ret |= (val >> 8 & 0xFF) << 16;
            ret |= (val >> 0 & 0xFF) << 24;
            return ret;
        }

        public static long ReadInt40BE(this BinaryReader reader)
        {
            byte[] val = reader.ReadBytes(5);
            return val[4] | val[3] << 8 | val[2] << 16 | val[1] << 24 | val[0] << 32;
        }

        public static void Skip(this BinaryReader reader, int bytes)
        {
            reader.BaseStream.Position += bytes;
        }

        public static ushort ReadUInt16BE(this BinaryReader reader)
        {
            byte[] val = reader.ReadBytes(2);
            return (ushort)(val[1] | val[0] << 8);
        }

        public static uint ReadUInt32BE(this BinaryReader reader)
        {
            byte[] val = reader.ReadBytes(4);
            return (uint)(val[3] | val[2] << 8 | val[1] << 16 | val[0] << 24);
        }

        public static Action<T, V> GetSetter<T, V>(this FieldInfo fieldInfo)
        {
            var paramExpression = Expression.Parameter(typeof(T));
            var fieldExpression = Expression.Field(paramExpression, fieldInfo);
            var valueExpression = Expression.Parameter(fieldInfo.FieldType);
            var assignExpression = Expression.Assign(fieldExpression, valueExpression);

            return Expression.Lambda<Action<T, V>>(assignExpression, paramExpression, valueExpression).Compile();
        }

        public static Func<T, V> GetGetter<T, V>(this FieldInfo fieldInfo)
        {
            var paramExpression = Expression.Parameter(typeof(T));
            var fieldExpression = Expression.Field(paramExpression, fieldInfo);

            return Expression.Lambda<Func<T, V>>(fieldExpression, paramExpression).Compile();
        }

        public static T Read<T>(this BinaryReader reader) where T : unmanaged
        {
            byte[] result = reader.ReadBytes(Unsafe.SizeOf<T>());

            return Unsafe.ReadUnaligned<T>(ref result[0]);
        }

        public static T[] ReadArray<T>(this BinaryReader reader) where T : unmanaged
        {
            int numBytes = (int)reader.ReadInt64();

            byte[] source = reader.ReadBytes(numBytes);

            if (source.Length != numBytes)
                throw new Exception("source.Length != numBytes");

            reader.BaseStream.Position += (0 - numBytes) & 0x07;

            return source.CopyTo<T>();
        }

        public static T[] ReadArray<T>(this BinaryReader reader, int size) where T : unmanaged
        {
            int numBytes = Unsafe.SizeOf<T>() * size;

            byte[] source = reader.ReadBytes(numBytes);

            if (source.Length != numBytes)
                throw new Exception("source.Length != numBytes");

            return source.CopyTo<T>();
        }

        public static unsafe T[] CopyTo<T>(this byte[] src) where T : unmanaged
        {
            //T[] result = new T[src.Length / Unsafe.SizeOf<T>()];

            //if (src.Length > 0)
            //    Unsafe.CopyBlockUnaligned(Unsafe.AsPointer(ref result[0]), Unsafe.AsPointer(ref src[0]), (uint)src.Length);

            //return result;

            Span<T> result = MemoryMarshal.Cast<byte, T>(src);
            return result.ToArray();
        }

        public static short ReadInt16BE(this BinaryReader reader)
        {
            byte[] val = reader.ReadBytes(2);
            return (short)(val[1] | val[0] << 8);
        }

        public static void CopyBytes(this Stream input, Stream output, int bytes)
        {
            byte[] buffer = new byte[0x1000];
            int read;
            while (bytes > 0 && (read = input.Read(buffer, 0, Math.Min(buffer.Length, bytes))) > 0)
            {
                output.Write(buffer, 0, read);
                bytes -= read;
            }
        }

        public static void CopyBytesFromPos(this Stream input, Stream output, int offset, int bytes)
        {
            byte[] buffer = new byte[0x1000];
            int read;
            int pos = 0;
            while (pos < offset && (read = input.Read(buffer, 0, Math.Min(buffer.Length, offset - pos))) > 0)
            {
                pos += read;
            }
            while (bytes > 0 && (read = input.Read(buffer, 0, Math.Min(buffer.Length, bytes))) > 0)
            {
                output.Write(buffer, 0, read);
                bytes -= read;
            }
        }

        public static void CopyToStream(this Stream src, Stream dst, long len, BackgroundWorkerEx progressReporter = null)
        {
            long done = 0;

#if NET6_0_OR_GREATER
            Span<byte> buf = stackalloc byte[0x1000];
#else
            byte[] buf = new byte[0x1000];
#endif
            int count;
            do
            {
                if (progressReporter != null && progressReporter.CancellationPending)
                    return;
#if NET6_0_OR_GREATER
                count = src.Read(buf);
                dst.Write(buf.Slice(0, count));
#else
                count = src.Read(buf, 0, buf.Length);
                dst.Write(buf, 0, count);
#endif
                done += count;

                progressReporter?.ReportProgress((int)(done / (float)len * 100));
            } while (count > 0);
        }

        public static void ExtractToFile(this Stream input, string path, string name)
        {
            string fullPath = Path.Combine(path, name);
            string dir = Path.GetDirectoryName(fullPath);

            DirectoryInfo dirInfo = new DirectoryInfo(dir);
            if (!dirInfo.Exists)
                dirInfo.Create();

            using (var fileStream = File.Open(fullPath, FileMode.Create))
            {
                input.Position = 0;
                input.CopyTo(fileStream);
            }
        }

        public static string ToHexString(this byte[] data)
        {
#if NET6_0_OR_GREATER
            return Convert.ToHexString(data);
#else
            if (data == null)
                throw new ArgumentNullException(nameof(data));
            if (data.Length == 0)
                return string.Empty;
            if (data.Length > int.MaxValue / 2)
                throw new ArgumentOutOfRangeException(nameof(data), "SR.ArgumentOutOfRange_InputTooLarge");
            return HexConverter.ToString(data, HexConverter.Casing.Upper);
#endif
        }

        public static bool EqualsTo(this in MD5Hash key, byte[] array)
        {
            if (array.Length != 16)
                return false;

            ref MD5Hash other = ref Unsafe.As<byte, MD5Hash>(ref array[0]);

            if (key.lowPart != other.lowPart || key.highPart != other.highPart)
                return false;

            return true;
        }

        public static bool EqualsTo9(this in MD5Hash key, byte[] array)
        {
            if (array.Length != 16)
                return false;

            ref MD5Hash other = ref Unsafe.As<byte, MD5Hash>(ref array[0]);

            return EqualsTo9(key, other);
        }

        public static bool EqualsTo9(this in MD5Hash key, in MD5Hash other)
        {
            if (key.lowPart != other.lowPart)
                return false;

            if ((key.highPart & 0xFF) != (other.highPart & 0xFF))
                return false;

            return true;
        }

        public static bool EqualsTo(this in MD5Hash key, in MD5Hash other)
        {
            return key.lowPart == other.lowPart && key.highPart == other.highPart;
        }

        public static unsafe string ToHexString(this in MD5Hash key)
        {
#if NET6_0
            ref MD5Hash md5ref = ref Unsafe.AsRef(in key);
            var md5Span = MemoryMarshal.CreateReadOnlySpan(ref md5ref, 1);
            var span = MemoryMarshal.AsBytes(md5Span);
            return Convert.ToHexString(span);
#elif NET7_0_OR_GREATER
            var md5Span = new ReadOnlySpan<MD5Hash>(in key);
            var span = MemoryMarshal.AsBytes(md5Span);
            return Convert.ToHexString(span);
#else
            byte[] array = new byte[16];
            fixed (byte* aptr = array)
            {
                *(MD5Hash*)aptr = key;
            }
            return array.ToHexString();
#endif
        }

        public static MD5Hash ToMD5(this byte[] array)
        {
            if (array.Length != 16)
                throw new ArgumentException("array size != 16", nameof(array));

            return Unsafe.As<byte, MD5Hash>(ref array[0]);
        }

        public static MD5Hash ToMD5(this Span<byte> array)
        {
            if (array.Length != 16)
                throw new ArgumentException("array size != 16", nameof(array));

            return Unsafe.As<byte, MD5Hash>(ref array[0]);
        }
    }

    public static class CStringExtensions
    {
        /// <summary> Reads the NULL terminated string from 
        /// the current stream and advances the current position of the stream by string length + 1.
        /// <seealso cref="BinaryReader.ReadString"/>
        /// </summary>
        public static string ReadCString(this BinaryReader reader)
        {
            return reader.ReadCString(Encoding.UTF8);
        }

        /// <summary> Reads the NULL terminated string from 
        /// the current stream and advances the current position of the stream by string length + 1.
        /// <seealso cref="BinaryReader.ReadString"/>
        /// </summary>
        public static string ReadCString(this BinaryReader reader, Encoding encoding)
        {
            var bytes = new List<byte>();
            byte b;
            while ((b = reader.ReadByte()) != 0)
                bytes.Add(b);
            return encoding.GetString(bytes.ToArray());
        }

        public static void WriteCString(this BinaryWriter writer, string str)
        {
            var bytes = Encoding.UTF8.GetBytes(str);
            writer.Write(bytes);
            writer.Write((byte)0);
        }

        public static byte[] FromHexString(this string str)
        {
#if NET6_0_OR_GREATER
            return Convert.FromHexString(str);
#else
            if (str == null)
                throw new ArgumentNullException(nameof(str));
            if (str.Length == 0)
                return Array.Empty<byte>();
            if ((uint)str.Length % 2 != 0)
                throw new FormatException("SR.Format_BadHexLength");

            byte[] result = new byte[str.Length >> 1];

            if (!HexConverter.TryDecodeFromUtf16(str, result))
                throw new FormatException("SR.Format_BadHexChar");

            return result;
#endif
        }
    }
}
