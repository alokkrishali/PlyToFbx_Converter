using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace PlyToFbx
{
    public sealed class PcdFileReader : IPointCloudReader
    {
        private enum DataFormat
        {
            Ascii,
            Binary,
            BinaryCompressed
        }

        private sealed class Field
        {
            public string Name;
            public int Size;
            public char Type;
            public int Count;
            public int Offset;
        }

        public PlyMeshData Read(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentException("A PCD file path is required.", nameof(filePath));
            if (!File.Exists(filePath))
                throw new FileNotFoundException("The PCD file could not be found.", filePath);

            using (FileStream stream = File.OpenRead(filePath))
            {
                List<Field> fields = ReadHeader(stream, out int pointCount, out int recordSize, out DataFormat format);
                Field xField = FindField(fields, "x");
                Field yField = FindField(fields, "y");
                Field zField = FindField(fields, "z");
                if (xField == null || yField == null || zField == null)
                    throw new InvalidDataException("The PCD header must define x, y, and z fields.");

                Vector3[] vertices = format == DataFormat.Ascii
                    ? ReadAscii(stream, fields, pointCount, xField, yField, zField)
                    : format == DataFormat.Binary
                        ? ReadBinary(stream, fields, pointCount, recordSize, xField, yField, zField)
                        : ReadCompressed(stream, fields, pointCount, recordSize, xField, yField, zField);

                return new PlyMeshData(vertices, Array.Empty<int>());
            }
        }

        private static List<Field> ReadHeader(Stream stream, out int pointCount, out int recordSize, out DataFormat format)
        {
            pointCount = -1;
            recordSize = 0;
            format = DataFormat.Ascii;
            List<string> names = null;
            List<int> sizes = null;
            List<char> types = null;
            List<int> counts = null;
            bool foundData = false;

            while (true)
            {
                string line = ReadLine(stream);
                if (line == null)
                    break;
                string[] parts = line.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 0 || parts[0].StartsWith("#", StringComparison.Ordinal))
                    continue;

                switch (parts[0].ToUpperInvariant())
                {
                    case "FIELDS":
                    case "FIELD":
                        names = new List<string>(parts);
                        names.RemoveAt(0);
                        break;
                    case "SIZE":
                        sizes = ParseInts(parts);
                        break;
                    case "TYPE":
                        types = new List<char>();
                        for (int index = 1; index < parts.Length; index++)
                            types.Add(char.ToUpperInvariant(parts[index][0]));
                        break;
                    case "COUNT":
                        counts = ParseInts(parts);
                        break;
                    case "WIDTH":
                        if (parts.Length > 1 && pointCount < 0)
                            pointCount = ParseNonNegativeInt(parts[1], "WIDTH");
                        break;
                    case "HEIGHT":
                        if (parts.Length > 1)
                        {
                            int height = ParseNonNegativeInt(parts[1], "HEIGHT");
                            int width = pointCount < 0 ? 0 : pointCount;
                            pointCount = checked(width * height);
                        }
                        break;
                    case "POINTS":
                        if (parts.Length > 1)
                            pointCount = ParseNonNegativeInt(parts[1], "POINTS");
                        break;
                    case "DATA":
                        if (parts.Length != 2)
                            throw new InvalidDataException("The PCD DATA declaration is invalid.");
                        switch (parts[1].ToLowerInvariant())
                        {
                            case "ascii": format = DataFormat.Ascii; break;
                            case "binary": format = DataFormat.Binary; break;
                            case "binary_compressed": format = DataFormat.BinaryCompressed; break;
                            default: throw new InvalidDataException("Unsupported PCD DATA format: " + parts[1]);
                        }
                        foundData = true;
                        break;
                }

                if (foundData)
                    break;
            }

            if (!foundData || names == null || sizes == null || types == null)
                throw new InvalidDataException("The PCD header is incomplete.");
            if (pointCount < 0)
                throw new InvalidDataException("The PCD header must define WIDTH and HEIGHT or POINTS.");
            if (sizes.Count != names.Count || types.Count != names.Count)
                throw new InvalidDataException("The PCD FIELDS, SIZE, and TYPE entries do not match.");
            if (counts == null)
            {
                counts = new List<int>();
                for (int index = 0; index < names.Count; index++)
                    counts.Add(1);
            }
            if (counts.Count != names.Count)
                throw new InvalidDataException("The PCD COUNT entries do not match FIELDS.");

            List<Field> fields = new List<Field>(names.Count);
            foreach (int index in Range(names.Count))
            {
                if (sizes[index] <= 0 || counts[index] <= 0)
                    throw new InvalidDataException("PCD field sizes and counts must be positive.");
                if (types[index] != 'F' && types[index] != 'I' && types[index] != 'U')
                    throw new InvalidDataException("Unsupported PCD field type: " + types[index]);
                if (types[index] == 'F' && sizes[index] != 4 && sizes[index] != 8)
                    throw new InvalidDataException("PCD floating-point fields must use 4-byte or 8-byte values.");
                if (types[index] != 'F' && sizes[index] != 1 && sizes[index] != 2 && sizes[index] != 4)
                    throw new InvalidDataException("PCD integer fields must use 1-, 2-, or 4-byte values.");

                fields.Add(new Field
                {
                    Name = names[index],
                    Size = sizes[index],
                    Type = types[index],
                    Count = counts[index],
                    Offset = recordSize
                });
                recordSize = checked(recordSize + sizes[index] * counts[index]);
            }

            if (pointCount > 0 && recordSize <= 0)
                throw new InvalidDataException("The PCD record size is invalid.");
            return fields;
        }

        private static Vector3[] ReadAscii(Stream stream, List<Field> fields, int pointCount, Field x, Field y, Field z)
        {
            Vector3[] vertices = new Vector3[pointCount];
            using (StreamReader reader = new StreamReader(stream, Encoding.ASCII, false, 1024, true))
            {
                int xIndex = ValueIndex(fields, x);
                int yIndex = ValueIndex(fields, y);
                int zIndex = ValueIndex(fields, z);
                for (int point = 0; point < pointCount; point++)
                {
                    string line = reader.ReadLine();
                    while (line != null && string.IsNullOrWhiteSpace(line))
                        line = reader.ReadLine();
                    if (line == null)
                        throw new InvalidDataException("The PCD data ended before all points were read.");
                    string[] values = line.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
                    if (values.Length < ValueCount(fields))
                        throw new InvalidDataException("A PCD point record contains too few values.");
                    vertices[point] = new Vector3(
                        ParseFloat(values[xIndex]), ParseFloat(values[yIndex]), ParseFloat(values[zIndex]));
                }
            }
            return vertices;
        }

        private static Vector3[] ReadBinary(Stream stream, List<Field> fields, int pointCount, int recordSize, Field x, Field y, Field z)
        {
            byte[] bytes = ReadExactly(stream, checked(pointCount * recordSize));
            Vector3[] vertices = new Vector3[pointCount];
            for (int point = 0; point < pointCount; point++)
            {
                int offset = point * recordSize;
                vertices[point] = new Vector3(
                    ReadFieldValue(bytes, offset + x.Offset, x),
                    ReadFieldValue(bytes, offset + y.Offset, y),
                    ReadFieldValue(bytes, offset + z.Offset, z));
            }
            return vertices;
        }

        private static Vector3[] ReadCompressed(Stream stream, List<Field> fields, int pointCount, int recordSize, Field x, Field y, Field z)
        {
            using (BinaryReader reader = new BinaryReader(stream, Encoding.ASCII, true))
            {
                int compressedSize = reader.ReadInt32();
                int uncompressedSize = reader.ReadInt32();
                int expectedSize = checked(pointCount * recordSize);
                if (compressedSize < 0 || uncompressedSize != expectedSize)
                    throw new InvalidDataException("The PCD compressed data size does not match the header.");
                byte[] compressed = ReadExactly(stream, compressedSize);
                byte[] bytes = DecompressLzf(compressed, uncompressedSize);
                Vector3[] vertices = new Vector3[pointCount];
                int fieldOffset = 0;
                for (int fieldIndex = 0; fieldIndex < fields.Count; fieldIndex++)
                {
                    Field field = fields[fieldIndex];
                    int fieldBytes = checked(field.Size * field.Count * pointCount);
                    if (field == x || field == y || field == z)
                    {
                        for (int point = 0; point < pointCount; point++)
                        {
                            float value = ReadFieldValue(bytes, fieldOffset + point * field.Size * field.Count, field);
                            Vector3 position = vertices[point];
                            if (field == x) position.x = value;
                            else if (field == y) position.y = value;
                            else position.z = value;
                            vertices[point] = position;
                        }
                    }
                    fieldOffset += fieldBytes;
                }
                return vertices;
            }
        }

        private static byte[] DecompressLzf(byte[] input, int outputLength)
        {
            byte[] output = new byte[outputLength];
            int inputIndex = 0;
            int outputIndex = 0;
            while (inputIndex < input.Length)
            {
                int control = input[inputIndex++];
                if (control < 32)
                {
                    int length = control + 1;
                    if (inputIndex + length > input.Length || outputIndex + length > output.Length)
                        throw new InvalidDataException("The PCD LZF stream contains an invalid literal run.");
                    Buffer.BlockCopy(input, inputIndex, output, outputIndex, length);
                    inputIndex += length;
                    outputIndex += length;
                }
                else
                {
                    int length = control >> 5;
                    int reference = outputIndex - ((control & 0x1f) << 8) - 1;
                    if (length == 7)
                    {
                        if (inputIndex >= input.Length)
                            throw new InvalidDataException("The PCD LZF stream is truncated.");
                        length += input[inputIndex++];
                    }
                    if (inputIndex >= input.Length)
                        throw new InvalidDataException("The PCD LZF stream is truncated.");
                    reference -= input[inputIndex++];
                    length += 2;
                    if (reference < 0 || outputIndex + length > output.Length)
                        throw new InvalidDataException("The PCD LZF stream contains an invalid back-reference.");
                    for (int index = 0; index < length; index++)
                        output[outputIndex++] = output[reference++];
                }
            }

            if (outputIndex != output.Length)
                throw new InvalidDataException("The PCD LZF stream did not produce the expected data size.");
            return output;
        }

        private static float ReadFieldValue(byte[] bytes, int offset, Field field)
        {
            if (field.Count != 1)
                throw new InvalidDataException("The PCD x, y, and z fields must have COUNT 1.");
            if (field.Type == 'F')
            {
                if (field.Size == 4)
                {
                    byte[] value = new byte[4];
                    Buffer.BlockCopy(bytes, offset, value, 0, 4);
                    if (!BitConverter.IsLittleEndian) Array.Reverse(value);
                    return BitConverter.ToSingle(value, 0);
                }
                byte[] doubleValue = new byte[8];
                Buffer.BlockCopy(bytes, offset, doubleValue, 0, 8);
                if (!BitConverter.IsLittleEndian) Array.Reverse(doubleValue);
                return (float)BitConverter.ToDouble(doubleValue, 0);
            }

            long integer = ReadInteger(bytes, offset, field.Size, field.Type == 'I');
            return integer;
        }

        private static long ReadInteger(byte[] bytes, int offset, int size, bool signed)
        {
            if (size == 1)
                return signed ? (sbyte)bytes[offset] : bytes[offset];
            byte[] value = new byte[4];
            Buffer.BlockCopy(bytes, offset, value, 0, size);
            if (!BitConverter.IsLittleEndian) Array.Reverse(value, 0, size);
            if (signed)
            {
                if (size == 2) return BitConverter.ToInt16(value, 0);
                return BitConverter.ToInt32(value, 0);
            }
            if (size == 2) return BitConverter.ToUInt16(value, 0);
            return BitConverter.ToUInt32(value, 0);
        }

        private static byte[] ReadExactly(Stream stream, int length)
        {
            byte[] result = new byte[length];
            int offset = 0;
            while (offset < length)
            {
                int read = stream.Read(result, offset, length - offset);
                if (read == 0)
                    throw new EndOfStreamException("The PCD file ended before all point data was read.");
                offset += read;
            }
            return result;
        }

        private static List<int> ParseInts(string[] parts)
        {
            List<int> values = new List<int>(parts.Length - 1);
            for (int index = 1; index < parts.Length; index++)
                values.Add(ParseNonNegativeInt(parts[index], parts[0]));
            return values;
        }

        private static int ParseNonNegativeInt(string value, string field)
        {
            if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int result) || result < 0)
                throw new InvalidDataException("The PCD " + field + " value is invalid.");
            return result;
        }

        private static float ParseFloat(string value)
        {
            if (!float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float result))
                throw new InvalidDataException("The PCD data contains an invalid numeric value.");
            return result;
        }

        private static Field FindField(List<Field> fields, string name)
        {
            return fields.Find(field => string.Equals(field.Name, name, StringComparison.OrdinalIgnoreCase));
        }

        private static int ValueCount(List<Field> fields)
        {
            int count = 0;
            foreach (Field field in fields)
                count += field.Count;
            return count;
        }

        private static int ValueIndex(List<Field> fields, Field target)
        {
            int index = 0;
            foreach (Field field in fields)
            {
                if (field == target)
                    return index;
                index += field.Count;
            }
            throw new InvalidDataException("A required PCD field was not found.");
        }

        private static string ReadLine(Stream stream)
        {
            StringBuilder line = new StringBuilder();
            int value;
            while ((value = stream.ReadByte()) >= 0)
            {
                if (value == '\n')
                    return line.ToString().TrimEnd('\r');
                line.Append((char)value);
            }
            return line.Length == 0 ? null : line.ToString().TrimEnd('\r');
        }

        private static IEnumerable<int> Range(int count)
        {
            for (int index = 0; index < count; index++)
                yield return index;
        }
    }
}
