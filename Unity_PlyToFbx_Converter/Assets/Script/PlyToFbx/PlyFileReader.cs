using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace PlyToFbx
{
    public sealed class PlyFileReader : IPointCloudReader
    {
        private enum PlyFormat
        {
            Ascii,
            BinaryLittleEndian
        }

        private sealed class PlyProperty
        {
            public string Name;
            public string ValueType;
            public bool IsList;
            public string CountType;
            public string ItemType;
        }

        private sealed class PlyElement
        {
            public string Name;
            public int Count;
            public readonly List<PlyProperty> Properties = new List<PlyProperty>();
        }

        public PlyMeshData Read(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentException("A PLY file path is required.", nameof(filePath));
            if (!File.Exists(filePath))
                throw new FileNotFoundException("The PLY file could not be found.", filePath);

            using (FileStream stream = File.OpenRead(filePath))
            {
                List<PlyElement> elements = ReadHeader(stream, out PlyFormat format);
                List<Vector3> vertices = new List<Vector3>();
                List<int> triangles = new List<int>();

                if (format == PlyFormat.Ascii)
                {
                    using (StreamReader reader = new StreamReader(stream, Encoding.ASCII, false, 1024, true))
                        ReadAsciiBody(reader, elements, vertices, triangles);
                }
                else
                {
                    using (BinaryReader reader = new BinaryReader(stream, Encoding.ASCII, true))
                        ReadBinaryBody(reader, elements, vertices, triangles);
                }

                if (vertices.Count == 0)
                    throw new InvalidDataException("The PLY file contains no vertices.");

                return new PlyMeshData(vertices.ToArray(), triangles.ToArray());
            }
        }

        private static List<PlyElement> ReadHeader(Stream stream, out PlyFormat format)
        {
            string firstLine = ReadHeaderLine(stream);
            if (!string.Equals(firstLine, "ply", StringComparison.Ordinal))
                throw new InvalidDataException("The file does not begin with a valid PLY header.");

            format = PlyFormat.Ascii;
            bool formatFound = false;
            bool headerEnded = false;
            PlyElement currentElement = null;
            List<PlyElement> elements = new List<PlyElement>();

            while (true)
            {
                string line = ReadHeaderLine(stream);
                if (line == null)
                    break;

                string[] parts = line.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 0 || parts[0] == "comment" || parts[0] == "obj_info")
                    continue;

                if (parts[0] == "format")
                {
                    if (parts.Length < 3 || parts[2] != "1.0")
                        throw new InvalidDataException("Only PLY format version 1.0 is supported.");
                    if (parts[1] == "ascii")
                        format = PlyFormat.Ascii;
                    else if (parts[1] == "binary_little_endian")
                        format = PlyFormat.BinaryLittleEndian;
                    else
                        throw new InvalidDataException("Only ASCII and binary little-endian PLY files are supported.");
                    formatFound = true;
                }
                else if (parts[0] == "element")
                {
                    if (parts.Length != 3 || !int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out int count) || count < 0)
                        throw new InvalidDataException("The PLY header contains an invalid element declaration.");
                    currentElement = new PlyElement { Name = parts[1], Count = count };
                    elements.Add(currentElement);
                }
                else if (parts[0] == "property")
                {
                    if (currentElement == null)
                        throw new InvalidDataException("A PLY property was declared before its element.");

                    PlyProperty property;
                    if (parts.Length == 3)
                    {
                        property = new PlyProperty { ValueType = parts[1], Name = parts[2] };
                        ValidateScalarType(property.ValueType);
                    }
                    else if (parts.Length == 5 && parts[1] == "list")
                    {
                        property = new PlyProperty
                        {
                            IsList = true,
                            CountType = parts[2],
                            ItemType = parts[3],
                            Name = parts[4]
                        };
                        ValidateScalarType(property.CountType);
                        ValidateScalarType(property.ItemType);
                    }
                    else
                    {
                        throw new InvalidDataException("The PLY header contains an invalid property declaration.");
                    }
                    currentElement.Properties.Add(property);
                }
                else if (parts[0] == "end_header")
                {
                    headerEnded = true;
                    break;
                }
            }

            if (!headerEnded || !formatFound)
                throw new InvalidDataException("The PLY header is incomplete or has no format declaration.");

            return elements;
        }

        private static void ReadAsciiBody(TextReader reader, List<PlyElement> elements, List<Vector3> vertices, List<int> triangles)
        {
            foreach (PlyElement element in elements)
            {
                for (int row = 0; row < element.Count; row++)
                {
                    double x = 0;
                    double y = 0;
                    double z = 0;
                    bool hasX = false;
                    bool hasY = false;
                    bool hasZ = false;
                    List<int> faceIndices = null;

                    foreach (PlyProperty property in element.Properties)
                    {
                        if (property.IsList)
                        {
                            int count = ReadCount(ReadAsciiValue(reader), property.CountType);
                            bool isFaceIndices = element.Name == "face" &&
                                (property.Name == "vertex_indices" || property.Name == "vertex_index");
                            if (isFaceIndices)
                                faceIndices = new List<int>(count);

                            for (int index = 0; index < count; index++)
                            {
                                double value = ReadAsciiValue(reader);
                                if (isFaceIndices)
                                    faceIndices.Add(ToIndex(value));
                            }
                        }
                        else
                        {
                            double value = ReadAsciiValue(reader);
                            if (element.Name != "vertex")
                                continue;

                            switch (property.Name)
                            {
                                case "x": x = value; hasX = true; break;
                                case "y": y = value; hasY = true; break;
                                case "z": z = value; hasZ = true; break;
                            }
                        }
                    }

                    if (element.Name == "vertex")
                    {
                        if (!hasX || !hasY || !hasZ)
                            throw new InvalidDataException("The PLY vertex element must define scalar x, y, and z properties.");
                        vertices.Add(new Vector3((float)x, (float)y, (float)z));
                    }
                    else if (element.Name == "face")
                    {
                        AddFace(faceIndices, vertices.Count, triangles);
                    }
                }
            }
        }

        private static void ReadBinaryBody(BinaryReader reader, List<PlyElement> elements, List<Vector3> vertices, List<int> triangles)
        {
            foreach (PlyElement element in elements)
            {
                for (int row = 0; row < element.Count; row++)
                {
                    double x = 0;
                    double y = 0;
                    double z = 0;
                    bool hasX = false;
                    bool hasY = false;
                    bool hasZ = false;
                    List<int> faceIndices = null;

                    foreach (PlyProperty property in element.Properties)
                    {
                        if (property.IsList)
                        {
                            int count = ReadCount(ReadBinaryValue(reader, property.CountType), property.CountType);
                            bool isFaceIndices = element.Name == "face" &&
                                (property.Name == "vertex_indices" || property.Name == "vertex_index");
                            if (isFaceIndices)
                                faceIndices = new List<int>(count);

                            for (int index = 0; index < count; index++)
                            {
                                double value = ReadBinaryValue(reader, property.ItemType);
                                if (isFaceIndices)
                                    faceIndices.Add(ToIndex(value));
                            }
                        }
                        else
                        {
                            double value = ReadBinaryValue(reader, property.ValueType);
                            if (element.Name != "vertex")
                                continue;

                            switch (property.Name)
                            {
                                case "x": x = value; hasX = true; break;
                                case "y": y = value; hasY = true; break;
                                case "z": z = value; hasZ = true; break;
                            }
                        }
                    }

                    if (element.Name == "vertex")
                    {
                        if (!hasX || !hasY || !hasZ)
                            throw new InvalidDataException("The PLY vertex element must define scalar x, y, and z properties.");
                        vertices.Add(new Vector3((float)x, (float)y, (float)z));
                    }
                    else if (element.Name == "face")
                    {
                        AddFace(faceIndices, vertices.Count, triangles);
                    }
                }
            }
        }

        private static void AddFace(List<int> indices, int vertexCount, List<int> triangles)
        {
            if (indices == null || indices.Count < 3)
                throw new InvalidDataException("Each PLY face must contain at least three vertex indices.");

            for (int index = 0; index < indices.Count; index++)
            {
                if (indices[index] < 0 || indices[index] >= vertexCount)
                    throw new InvalidDataException("A PLY face contains an out-of-range vertex index.");
            }

            for (int index = 1; index < indices.Count - 1; index++)
            {
                triangles.Add(indices[0]);
                triangles.Add(indices[index]);
                triangles.Add(indices[index + 1]);
            }
        }

        private static string ReadHeaderLine(Stream stream)
        {
            StringBuilder line = new StringBuilder();
            int next;
            while ((next = stream.ReadByte()) >= 0)
            {
                if (next == '\n')
                    return line.ToString().TrimEnd('\r');
                line.Append((char)next);
            }
            return line.Length == 0 ? null : line.ToString().TrimEnd('\r');
        }

        private static double ReadAsciiValue(TextReader reader)
        {
            StringBuilder token = new StringBuilder();
            int next;
            do
            {
                next = reader.Read();
            } while (next >= 0 && char.IsWhiteSpace((char)next));

            while (next >= 0 && !char.IsWhiteSpace((char)next))
            {
                token.Append((char)next);
                next = reader.Read();
            }

            if (token.Length == 0 || !double.TryParse(token.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
                throw new InvalidDataException("The PLY data contains an invalid or missing numeric value.");
            return value;
        }

        private static double ReadBinaryValue(BinaryReader reader, string type)
        {
            switch (NormalizeType(type))
            {
                case "int8": return reader.ReadSByte();
                case "uint8": return reader.ReadByte();
                case "int16": return reader.ReadInt16();
                case "uint16": return reader.ReadUInt16();
                case "int32": return reader.ReadInt32();
                case "uint32": return reader.ReadUInt32();
                case "float32": return reader.ReadSingle();
                case "float64": return reader.ReadDouble();
                default: throw new InvalidDataException("The PLY file uses an unsupported scalar type: " + type);
            }
        }

        private static int ReadCount(double value, string type)
        {
            if (value < 0 || value > int.MaxValue || value != Math.Truncate(value))
                throw new InvalidDataException("A PLY list has an invalid item count.");
            return (int)value;
        }

        private static int ToIndex(double value)
        {
            if (value < 0 || value > int.MaxValue || value != Math.Truncate(value))
                throw new InvalidDataException("A PLY face contains an invalid vertex index.");
            return (int)value;
        }

        private static void ValidateScalarType(string type)
        {
            switch (NormalizeType(type))
            {
                case "int8": case "uint8": case "int16": case "uint16":
                case "int32": case "uint32": case "float32": case "float64": return;
                default: throw new InvalidDataException("The PLY file uses an unsupported scalar type: " + type);
            }
        }

        private static string NormalizeType(string type)
        {
            switch (type)
            {
                case "char": case "int8": return "int8";
                case "uchar": case "uint8": return "uint8";
                case "short": case "int16": return "int16";
                case "ushort": case "uint16": return "uint16";
                case "int": case "int32": return "int32";
                case "uint": case "uint32": return "uint32";
                case "float": case "float32": return "float32";
                case "double": case "float64": return "float64";
                default: return type;
            }
        }
    }
}
