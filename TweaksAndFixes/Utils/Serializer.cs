using System;
using System.Collections.Generic;
using System.Collections;
using MelonLoader;
using System.Reflection;
using UnityEngine;
using System.Text;

#pragma warning disable CS8600
#pragma warning disable CS8601
#pragma warning disable CS8602
#pragma warning disable CS8603
#pragma warning disable CS8604
#pragma warning disable CS8625

namespace TweaksAndFixes
{
    public static class Serializer
    {
        private static readonly char[] _Buf = new char[1024];

        public class CSV
        {
            private static unsafe List<string> ParseLine(string input)
            {
                var lst = new List<string>();
                int len = input.Length;
                bool inQuote = false;
                bool escaped = false;
                int bufIdx = 0;
                fixed (char* pInput = input)
                {
                    for (int i = 0; i < len; ++i)
                    {
                        char c = pInput[i];
                        if (escaped)
                        {
                            escaped = false;
                            // I'm sure there's a real way to do this
                            // but this will do.
                            c = c switch
                            {
                                'a' => '\a',
                                'b' => '\b',
                                'f' => '\f',
                                'n' => '\n',
                                'r' => '\r',
                                't' => '\t',
                                'v' => '\v',
                                _ => c
                            };
                        }
                        else
                        {
                            switch (c)
                            {
                                case '\\':
                                    escaped = true;
                                    continue;

                                case '"':
                                    inQuote = !inQuote;
                                    if (!inQuote) // i.e. we were quoted before
                                    {
                                        lst.Add(new string(_Buf, 0, bufIdx));
                                        bufIdx = 0;
                                    }
                                    continue;

                                case ',':
                                    if (!inQuote)
                                    {
                                        lst.Add(new string(_Buf, 0, bufIdx));
                                        bufIdx = 0;
                                        continue;
                                    }
                                    break;
                            }
                        }
                        _Buf[bufIdx++] = c;
                    }
                    if (bufIdx > 0)
                        lst.Add(new string(_Buf, 0, bufIdx));
                }

                return lst;
            }

            public static bool Write<TColl, TItem>(TColl coll, List<string> output) where TColl : ICollection<TItem>
            {
                var tc = GetOrCreate(coll.GetType());
                if (tc == null)
                    return false;

                var header = tc.WriteHeader();
                output.Add(header);
                bool allSucceeded = true;
                foreach (var item in coll)
                {
                    bool ok = tc.Write(item, out var s);
                    if (ok)
                        output.Add(s);

                    allSucceeded &= ok;
                }

                return allSucceeded;
            }

            public static bool Read<TList, TItem>(string[] lines, TList output) where TList : IList<TItem>
            {
                var tc = GetOrCreate(typeof(TItem));
                if (tc == null)
                    return false;

                List<string> header = null;
                bool allSucceeded = true;
                foreach (var l in lines)
                {
                    if (header == null)
                    {
                        header = ParseLine(l);
                    }
                    else
                    {
                        var item = (TItem)GetNewInstance(typeof(TItem));
                        bool ok = tc.Read(item, ParseLine(l), header);
                        if (ok)
                            output.Add(item);

                        allSucceeded &= ok;
                    }
                }

                return allSucceeded;
            }

            public static bool Read<TDict, TKey, TValue>(string[] lines, TDict output, string keyName) where TDict : IDictionary<TKey, TValue>
            {
                var tc = GetOrCreate(typeof(TValue));
                if (tc == null)
                    return false;

                if (!tc._nameToField.TryGetValue(keyName, out var keyField))
                    return false;

                List<string> header = null;
                bool allSucceeded = true;
                foreach (var l in lines)
                {
                    if (header == null)
                    {
                        header = ParseLine(l);
                    }
                    else
                    {
                        var item = (TValue)GetNewInstance(typeof(TValue));
                        bool ok = tc.Read(item, ParseLine(l), header);
                        var key = keyField._fieldInfo.GetValue(item);
                        ok &= key != null;
                        if (ok)
                            output.Add((TKey)key, item);

                        allSucceeded &= ok;
                    }
                }

                return allSucceeded;
            }

            // It would be better to do these line by line. But
            // (a) this is faster, and (b) the alloc isn't too
            // bad given actual use cases.
            public static bool Write<TColl, TItem>(string path, TColl coll) where TColl : ICollection<TItem>
            {
                var lines = new List<string>();
                bool ok = Write<TColl, TItem>(coll, lines);
                File.WriteAllLines(path, lines);
                return ok;
            }


            public static bool Read<TList, TItem>(string path, TList output) where TList : IList<TItem>
            {
                var lines = File.ReadAllLines(path);
                return Read<TList, TItem>(lines, output);
            }

            public static bool Read<TDict, TKey, TValue>(string path, TDict output, string keyName) where TDict : IDictionary<TKey, TValue>
            {
                var lines = File.ReadAllLines(path);
                return Read<TDict, TKey, TValue>(lines, output, keyName);
            }

            // This is an expanded version of System.TypeCode
            public enum DataType : uint
            {
                INVALID = 0,
                ValueString,
                ValueGuid,
                ValueBool,
                ValueByte,
                ValueSByte,
                ValueChar,
                ValueDecimal,
                ValueDouble,
                ValueFloat,
                ValueInt,
                ValueUInt,
                ValueLong,
                ValueULong,
                ValueShort,
                ValueUShort,
                ValueVector2,
                ValueVector3,
                ValueVector4,
                ValueQuaternion,
                ValueMatrix4x4,
                ValueColor,
                ValueColor32,
                ValueEnum,
            }

            public class FieldData
            {
                private static readonly System.Globalization.CultureInfo _Invariant = System.Globalization.CultureInfo.InvariantCulture;

                public string _fieldName = null;
                public Type _fieldType = null;
                public FieldInfo _fieldInfo = null;
                public SerializeInfo _attrib = null;
                public DataType _dataType = DataType.INVALID;

                public FieldData(MemberInfo memberInfo, SerializeInfo attrib)
                {
                    this._attrib = attrib;

                    string pName = attrib.name;
                    _fieldName = pName != null && pName.Length > 0 ? pName : memberInfo.Name;

                    _fieldInfo = memberInfo.DeclaringType.GetField(memberInfo.Name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    _fieldType = _fieldInfo.FieldType;

                    SetDataType(true);
                }

                public bool Read(string value, object host)
                {
                    object val = ReadValue(value, _dataType, _fieldType);

                    if (val == null)
                        return false;

                    _fieldInfo.SetValue(host, val);
                    return true;
                }

                public static object ReadValue(string value, DataType dataType, Type fieldType)
                {
                    switch (dataType)
                    {
                        case DataType.ValueString:
                            return value;
                        case DataType.ValueGuid:
                            return new Guid(value);
                        case DataType.ValueBool:
                            if (bool.TryParse(value, out var b))
                                return b;
                            return null;
                        case DataType.ValueDouble:
                            if (double.TryParse(value, out var d))
                                return d;
                            return null;
                        case DataType.ValueFloat:
                            if (float.TryParse(value, out var f))
                                return f;
                            return null;
                        case DataType.ValueDecimal:
                            if (decimal.TryParse(value, out var dc))
                                return dc;
                            return null;
                        case DataType.ValueInt:
                            if (int.TryParse(value, out var i))
                                return i;
                            return null;
                        case DataType.ValueUInt:
                            if (uint.TryParse(value, out var ui))
                                return ui;
                            return null;
                        case DataType.ValueChar:
                            return value.Length > 0 ? value[0] : '\0';
                        case DataType.ValueShort:
                            if (short.TryParse(value, out var s))
                                return s;
                            return null;
                        case DataType.ValueUShort:
                            if (ushort.TryParse(value, out var us))
                                return us;
                            return null;
                        case DataType.ValueLong:
                            if (long.TryParse(value, out var l))
                                return l;
                            return null;
                        case DataType.ValueULong:
                            if (ulong.TryParse(value, out var ul))
                                return ul;
                            return null;
                        case DataType.ValueByte:
                            if (byte.TryParse(value, out var by))
                                return by;
                            return null;
                        case DataType.ValueSByte:
                            if (sbyte.TryParse(value, out var sb))
                                return sb;
                            return null;
                        case DataType.ValueEnum:
                            try
                            {
                                return Enum.Parse(fieldType, value);
                            }
                            catch
                            {
                                string[] enumNames = fieldType.GetEnumNames();
                                string defaultName = enumNames.Length > 0 ? enumNames[0] : string.Empty;
                                Debug.LogWarning($"[KSPCF] Couldn't parse value '{value}' for enum '{fieldType.Name}', default value '{defaultName}' will be used.\nValid values are {string.Join(", ", enumNames)}");
                                return null;
                            }
                        case DataType.ValueVector2:
                            return ParseVector2(value);
                        case DataType.ValueVector3:
                            return ParseVector3(value);
                        case DataType.ValueVector4:
                            return ParseVector4(value);
                        case DataType.ValueQuaternion:
                            return ParseQuaternion(value);
                        case DataType.ValueMatrix4x4:
                            return ParseMatrix4x4(value);
                        case DataType.ValueColor:
                            return ParseColor(value);
                        case DataType.ValueColor32:
                            return ParseColor32(value);
                    }
                    return null;
                }

                public bool Write(object value, out string output)
                {
                    output = WriteValue(value, _dataType);
                    if (output == null)
                        return false;

                    return true;
                }

                public static string WriteValue(object value, DataType dataType)
                {
                    switch (dataType)
                    {
                        case DataType.ValueString:
                            return (string)value;
                        case DataType.ValueGuid:
                            return ((Guid)value).ToString();
                        case DataType.ValueBool:
                            return ((bool)value).ToString(_Invariant);
                        case DataType.ValueDouble:
                            return ((double)value).ToString("G17");
                        case DataType.ValueFloat:
                            return ((float)value).ToString("G9");
                        case DataType.ValueDecimal:
                            return ((decimal)value).ToString(_Invariant);
                        case DataType.ValueInt:
                            return ((int)value).ToString(_Invariant);
                        case DataType.ValueUInt:
                            return ((uint)value).ToString(_Invariant);
                        case DataType.ValueChar:
                            return ((char)value).ToString(_Invariant);
                        case DataType.ValueShort:
                            return ((short)value).ToString(_Invariant);
                        case DataType.ValueUShort:
                            return ((ushort)value).ToString(_Invariant);
                        case DataType.ValueLong:
                            return ((long)value).ToString(_Invariant);
                        case DataType.ValueULong:
                            return ((ulong)value).ToString(_Invariant);
                        case DataType.ValueByte:
                            return ((byte)value).ToString(_Invariant);
                        case DataType.ValueSByte:
                            return ((sbyte)value).ToString(_Invariant);
                        case DataType.ValueEnum:
                            return ((System.Enum)value).ToString();
                        case DataType.ValueVector2:
                            return WriteVector((Vector2)value);
                        case DataType.ValueVector3:
                            return WriteVector((Vector3)value);
                        case DataType.ValueVector4:
                            return WriteVector((Vector4)value);
                        case DataType.ValueQuaternion:
                            return WriteQuaternion((Quaternion)value);
                        case DataType.ValueMatrix4x4:
                            return WriteMatrix4x4((Matrix4x4)value);
                        case DataType.ValueColor:
                            return WriteColor((Color)value);
                        case DataType.ValueColor32:
                            return WriteColor((Color32)value);
                    }
                    return null;
                }

                public static DataType ValueDataType(Type fieldType)
                {
                    if (!fieldType.IsValueType)
                    {
                        if (fieldType == typeof(string))
                            return DataType.ValueString;

                        return DataType.INVALID;
                    }
                    if (fieldType == typeof(Guid))
                        return DataType.ValueGuid;
                    if (fieldType == typeof(bool))
                        return DataType.ValueBool;
                    if (fieldType == typeof(byte))
                        return DataType.ValueByte;
                    if (fieldType == typeof(sbyte))
                        return DataType.ValueSByte;
                    if (fieldType == typeof(char))
                        return DataType.ValueChar;
                    if (fieldType == typeof(decimal))
                        return DataType.ValueDecimal;
                    if (fieldType == typeof(double))
                        return DataType.ValueDouble;
                    if (fieldType == typeof(float))
                        return DataType.ValueFloat;
                    if (fieldType == typeof(int))
                        return DataType.ValueInt;
                    if (fieldType == typeof(uint))
                        return DataType.ValueUInt;
                    if (fieldType == typeof(long))
                        return DataType.ValueLong;
                    if (fieldType == typeof(ulong))
                        return DataType.ValueULong;
                    if (fieldType == typeof(short))
                        return DataType.ValueShort;
                    if (fieldType == typeof(ushort))
                        return DataType.ValueUShort;
                    if (fieldType == typeof(Vector2))
                        return DataType.ValueVector2;
                    if (fieldType == typeof(Vector3))
                        return DataType.ValueVector3;
                    if (fieldType == typeof(Vector4))
                        return DataType.ValueVector4;
                    if (fieldType == typeof(Quaternion))
                        return DataType.ValueQuaternion;
                    if (fieldType == typeof(Matrix4x4))
                        return DataType.ValueMatrix4x4;
                    if (fieldType == typeof(Color))
                        return DataType.ValueColor;
                    if (fieldType == typeof(Color32))
                        return DataType.ValueColor32;
                    if (fieldType.IsEnum)
                        return DataType.ValueEnum;

                    return DataType.INVALID;
                }

                private void SetDataType(bool allowArrays)
                {
                    _dataType = ValueDataType(_fieldType);
                }
            }


            private static readonly StringBuilder _StringBuilder = new StringBuilder();
            private static readonly Dictionary<Type, CSV> cache = new Dictionary<Type, CSV>();
            //private static readonly Dictionary<Type, ConstructorInfo> _typeToConstrutor = new Dictionary<Type, ConstructorInfo>();

            private List<FieldData> _fields = new List<FieldData>();
            private Dictionary<string, FieldData> _nameToField = new Dictionary<string, FieldData>();

            public CSV(Type t)
            {
                MemberInfo[] members = t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.FlattenHierarchy);
                for (int i = 0, iC = members.Length; i < iC; ++i)
                {
                    var attrib = (SerializeInfo)members[i].GetCustomAttribute(typeof(SerializeInfo), inherit: true);
                    if (attrib == null)
                        continue;

                    var data = new FieldData(members[i], attrib);
                    if (data._dataType != DataType.INVALID)
                    {
                        _fields.Add(data);
                        _nameToField[data._fieldName] = data;
                    }
                }
            }

            public bool Read(object host, List<string> line, List<string> header)
            {
                bool allSucceeded = true;
                int count = line.Count;
                if (count != header.Count)
                    return false;

                for (int i = 0; i < count; ++i)
                {
                    if (!_nameToField.TryGetValue(header[i], out FieldData fieldItem))
                        continue;

                    allSucceeded &= fieldItem.Read(line[i], host);
                }

                return allSucceeded;
            }

            public bool Write(object obj, out string output)
            {
                int num = _fields.Count;
                bool allSucceeded = true;
                bool isNotFirst = false;

                for (int i = 0; i < num; i++)
                {
                    var fieldData = _fields[i];
                    if (!fieldData._attrib.writeable)
                        continue;

                    if (isNotFirst)
                        _StringBuilder.Append(',');
                    else
                        isNotFirst = true;

                    object value = fieldData._fieldInfo.GetValue(obj);
                    if (value == null)
                        continue;

                    bool success = fieldData.Write(value, out string val);
                    allSucceeded &= success;
                    if (success)
                        _StringBuilder.Append(val);
                }
                output = _StringBuilder.ToString();
                _StringBuilder.Clear();

                return allSucceeded;
            }

            public string WriteHeader()
            {
                int num = _fields.Count;
                bool isNotFirst = false;

                for (int i = 0; i < num; i++)
                {
                    var fieldData = _fields[i];
                    if (!fieldData._attrib.writeable)
                        continue;

                    if (isNotFirst)
                        _StringBuilder.Append(',');
                    else
                        isNotFirst = true;

                    _StringBuilder.Append(fieldData._fieldName);
                }
                var output = _StringBuilder.ToString();
                _StringBuilder.Clear();
                return output;
            }

            public static CSV GetOrCreate(Type t)
            {
                if (cache.TryGetValue(t, out var tc))
                    return tc;

                return CreateAndAdd(t);
            }

            public static CSV CreateAndAdd(Type t)
            {
                var tc = new CSV(t);
                if (tc._fields.Count == 0)
                {
                    Debug.LogError($"[TweaksAndFixes]: No Persistent fields on object of type {t.Name} that is referenced in persistent field, adding as null to TypeCache.");
                    tc = null;
                }

                cache[t] = tc;
                return tc;
            }

            public static object GetNewInstance(Type t)
            {
                //if (!_typeToConstrutor.TryGetValue(t, out var cons))
                //{
                //    cons = t.GetConstructor(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null);
                //    _typeToConstrutor[t] = cons; // yes this might be null, but we're caching that it's null
                //}
                //if (cons == null)
                //    return null;

                //return cons.Invoke(null);
                return Activator.CreateInstance(t, true);
            }
        }

        [System.AttributeUsage(System.AttributeTargets.Field | System.AttributeTargets.Property, AllowMultiple = false)]
        public class SerializeInfo : System.Attribute
        {
            public string name;
            public bool writeable;

            public SerializeInfo()
            {
                name = string.Empty;
                writeable = true;
            }
        }

        public static Vector2 ParseVector2(string val)
        {
            var data = val.Split(new char[] { ',', ' ', '\t' }, System.StringSplitOptions.RemoveEmptyEntries);

            if (data.Length != 2)
                return Vector2.zero;

            return new Vector2(float.Parse(data[0], System.Globalization.CultureInfo.InvariantCulture), float.Parse(data[1], System.Globalization.CultureInfo.InvariantCulture));
        }

        public static Vector3 ParseVector3(string val)
        {
            var data = val.Split(new char[] { ',', ' ', '\t' }, System.StringSplitOptions.RemoveEmptyEntries);

            if (data.Length != 3)
                return Vector3.zero;

            return new Vector3(float.Parse(data[0], System.Globalization.CultureInfo.InvariantCulture), float.Parse(data[1], System.Globalization.CultureInfo.InvariantCulture), float.Parse(data[2], System.Globalization.CultureInfo.InvariantCulture));
        }

        public static Vector4 ParseVector4(string val)
        {
            var data = val.Split(new char[] { ',', ' ', '\t' }, System.StringSplitOptions.RemoveEmptyEntries);

            if (data.Length != 4)
                return Vector4.zero;

            return new Vector4(float.Parse(data[0]), float.Parse(data[1]), float.Parse(data[2]), float.Parse(data[3]));
        }

        public static Quaternion ParseQuaternion(string val)
        {
            var data = val.Split(new char[] { ',', ' ', '\t' }, System.StringSplitOptions.RemoveEmptyEntries);

            if (data.Length != 4)
                return Quaternion.identity;

            return new Quaternion(float.Parse(data[0]), float.Parse(data[1]), float.Parse(data[2]), float.Parse(data[3]));
        }

        public static Matrix4x4 ParseMatrix4x4(string val)
        {
            var data = val.Split(new char[] { ',', ' ', '\t' }, System.StringSplitOptions.RemoveEmptyEntries);

            if (data.Length != 16)
                return Matrix4x4.identity;

            Matrix4x4 matrix = Matrix4x4.identity;

            matrix.m00 = float.Parse(data[0]);
            matrix.m01 = float.Parse(data[1]);
            matrix.m02 = float.Parse(data[2]);
            matrix.m03 = float.Parse(data[3]);

            matrix.m10 = float.Parse(data[4]);
            matrix.m11 = float.Parse(data[5]);
            matrix.m11 = float.Parse(data[6]);
            matrix.m12 = float.Parse(data[7]);

            matrix.m20 = float.Parse(data[8]);
            matrix.m21 = float.Parse(data[9]);
            matrix.m22 = float.Parse(data[10]);
            matrix.m23 = float.Parse(data[11]);

            matrix.m30 = float.Parse(data[12]);
            matrix.m31 = float.Parse(data[13]);
            matrix.m32 = float.Parse(data[14]);
            matrix.m33 = float.Parse(data[15]);

            return matrix;
        }

        public static Color ParseColor(string val)
        {
            var data = val.Split(new char[] { ',', ' ', '\t' }, System.StringSplitOptions.RemoveEmptyEntries);

            if (data.Length < 3 || data.Length > 4)
                return Color.white;

            if (data.Length == 3)
                return new Color(float.Parse(data[0]), float.Parse(data[1]), float.Parse(data[2]));
            else
                return new Color(float.Parse(data[0]), float.Parse(data[1]), float.Parse(data[2]), float.Parse(data[3]));
        }

        public static Color32 ParseColor32(string val)
        {
            var data = val.Split(new char[] { ',', ' ', '\t' }, System.StringSplitOptions.RemoveEmptyEntries);

            if (data.Length < 3 || data.Length > 4)
                return Color.white;

            if (data.Length == 3)
                return new Color32(byte.Parse(data[0]), byte.Parse(data[1]), byte.Parse(data[2]), 255);
            else
                return new Color32(byte.Parse(data[0]), byte.Parse(data[1]), byte.Parse(data[2]), byte.Parse(data[3]));
        }

        public static string WriteVector(Vector2 vector)
        {
            return vector.x.ToString("G9", System.Globalization.CultureInfo.InvariantCulture) + "," + vector.y.ToString("G9", System.Globalization.CultureInfo.InvariantCulture);
        }

        public static string WriteVector(Vector3 vector)
        {
            return vector.x.ToString("G9", System.Globalization.CultureInfo.InvariantCulture) + "," + vector.y.ToString("G9", System.Globalization.CultureInfo.InvariantCulture) + "," + vector.z.ToString("G9", System.Globalization.CultureInfo.InvariantCulture);
        }

        public static string WriteVector(Vector4 vector)
        {
            //if (vector == null) return "";
            return vector.x.ToString("G9", System.Globalization.CultureInfo.InvariantCulture) + "," + vector.y.ToString("G9", System.Globalization.CultureInfo.InvariantCulture) + "," + vector.z.ToString("G9", System.Globalization.CultureInfo.InvariantCulture) + "," + vector.w.ToString("G9", System.Globalization.CultureInfo.InvariantCulture);
        }

        public static string WriteQuaternion(Quaternion quaternion)
        {
            return quaternion.x.ToString("G9", System.Globalization.CultureInfo.InvariantCulture) + "," + quaternion.y.ToString("G9", System.Globalization.CultureInfo.InvariantCulture) + "," + quaternion.z.ToString("G9", System.Globalization.CultureInfo.InvariantCulture) + "," + quaternion.w.ToString("G9", System.Globalization.CultureInfo.InvariantCulture);
        }

        public static string WriteMatrix4x4(Matrix4x4 matrix)
        {
            return
                matrix.m00.ToString("G9", System.Globalization.CultureInfo.InvariantCulture) + ","
                + matrix.m01.ToString("G9", System.Globalization.CultureInfo.InvariantCulture) + ","
                + matrix.m02.ToString("G9", System.Globalization.CultureInfo.InvariantCulture) + ","
                + matrix.m03.ToString("G9", System.Globalization.CultureInfo.InvariantCulture) + ","

                + matrix.m10.ToString("G9", System.Globalization.CultureInfo.InvariantCulture) + ","
                + matrix.m11.ToString("G9", System.Globalization.CultureInfo.InvariantCulture) + ","
                + matrix.m12.ToString("G9", System.Globalization.CultureInfo.InvariantCulture) + ","
                + matrix.m13.ToString("G9", System.Globalization.CultureInfo.InvariantCulture) + ","

                + matrix.m20.ToString("G9", System.Globalization.CultureInfo.InvariantCulture) + ","
                + matrix.m21.ToString("G9", System.Globalization.CultureInfo.InvariantCulture) + ","
                + matrix.m22.ToString("G9", System.Globalization.CultureInfo.InvariantCulture) + ","
                + matrix.m23.ToString("G9", System.Globalization.CultureInfo.InvariantCulture) + ","

                + matrix.m30.ToString("G9", System.Globalization.CultureInfo.InvariantCulture) + ","
                + matrix.m31.ToString("G9", System.Globalization.CultureInfo.InvariantCulture) + ","
                + matrix.m32.ToString("G9", System.Globalization.CultureInfo.InvariantCulture) + ","
                + matrix.m33.ToString("G9", System.Globalization.CultureInfo.InvariantCulture);
        }

        public static string WriteColor(Color color)
        {
            return color.r.ToString("G9", System.Globalization.CultureInfo.InvariantCulture) + "," + color.g.ToString("G9", System.Globalization.CultureInfo.InvariantCulture) + "," + color.b.ToString("G9", System.Globalization.CultureInfo.InvariantCulture) + "," + color.a.ToString("G9", System.Globalization.CultureInfo.InvariantCulture);
        }

        public static string WriteColor(Color32 color)
        {
            return color.r.ToString(System.Globalization.CultureInfo.InvariantCulture) + "," + color.g.ToString(System.Globalization.CultureInfo.InvariantCulture) + "," + color.b.ToString(System.Globalization.CultureInfo.InvariantCulture) + "," + color.a.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
    }
}