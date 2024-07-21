using System.Reflection;
using UnityEngine;
using System.Text;
using Il2Cpp;

#pragma warning disable CS8600
#pragma warning disable CS8601
#pragma warning disable CS8602
#pragma warning disable CS8603
#pragma warning disable CS8604
#pragma warning disable CS8618
#pragma warning disable CS8625

namespace TweaksAndFixes
{
    public static class Serializer
    {
        public class CSV
        {
            private static readonly char[] _Buf = new char[1024];

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
                                        ++i; // skip the comma. If this is the end
                                        // of the line this is still safe because the
                                        // loop will terminate at len+1 instead of len.
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
                var tc = GetOrCreate(typeof(TItem));
                if (tc == null)
                    return false;

                var header = tc.WriteHeader();
                output.Add(header);
                bool allSucceeded = true;
                foreach (var item in coll)
                {
                    bool ok = tc.WriteType(item, out var s);
                    if (ok)
                        output.Add(s);

                    allSucceeded &= ok;
                }

                return allSucceeded;
            }

            public static bool Read<TList, TItem>(string[] lines, TList output) where TList : IList<TItem>
            {
                int lLen = lines.Length;
                if (lLen < 2)
                    return false;

                bool create = output.Count == 0;
                if (!create && output.Count != lLen - 1)
                    return false;

                var tc = GetOrCreate(typeof(TItem));
                if (tc == null)
                    return false;

                List<string> header = ParseLine(lines[0]);
                bool allSucceeded = true;
                for (int i = 1; i < lLen; ++i)
                {
                    var line = ParseLine(lines[i]);
                    if (create)
                    {
                        var item = (TItem)GetNewInstance(typeof(TItem));
                        allSucceeded &= tc.ReadType(item, line, header);
                        output.Add(item);
                    }
                    else
                    {
                        var item = output[i - 1];
                        allSucceeded &= tc.ReadType(item, line, header);
                        output[i - 1] = item; // if valuetype, we need to do this
                    }
                }

                return allSucceeded;
            }

            public static bool Read<TDict, TKey, TValue>(string[] lines, TDict output, string keyName) where TDict : IDictionary<TKey, TValue>
            {
                int lLen = lines.Length;
                if (lLen < 2)
                    return false;

                var tc = GetOrCreate(typeof(TValue));
                if (tc == null)
                    return false;

                if (!tc._nameToField.TryGetValue(keyName, out var keyField))
                    return false;

                bool create = output.Count == 0;

                List<string> header = ParseLine(lines[0]);
                int keyIdx = header.IndexOf(keyName);
                if (keyIdx < 0)
                    return false;

                bool allSucceeded = true;
                for (int i = 1; i < lLen; ++i)
                {
                    var line = ParseLine(lines[i]);

                    var keyObj = keyField.ReadValue(line[keyIdx]);
                    if (keyObj == null)
                    {
                        // we can't insert with null key
                        allSucceeded = false;
                        continue;
                    }
                    var key = (TKey)keyObj;

                    if (!create && output.TryGetValue(key, out var existing))
                    {
                        allSucceeded &= tc.ReadType(existing, line, header);
                        output[key] = existing; // if valuetype, we need to do this
                        continue;
                    }

                    // in the not-create case, we know the key isn't found
                    // so no need to test.
                    if (create && output.ContainsKey(key))
                    {
                        Debug.LogError("[TweaksAndFixes] CSV: Tried to add object to dictionary with duplicate key " + key.ToString());
                        continue;
                    }

                    var item = (TValue)GetNewInstance(typeof(TValue));
                    allSucceeded &= tc.ReadType(item, ParseLine(lines[i]), header);
                    output.Add(key, item);
                }

                return allSucceeded;
            }

            // It would be better to do these line by line. But
            // (a) this is faster, and (b) the alloc isn't too
            // bad given actual use cases.
            public static bool Write<TColl, TItem>(TColl coll, string path) where TColl : ICollection<TItem>
            {
                var lines = new List<string>();
                bool ok = Write<TColl, TItem>(coll, lines);
                File.WriteAllLines(path, lines);
                return ok;
            }


            public static bool Read<TList, TItem>(TList output, string path) where TList : IList<TItem>
            {
                var lines = File.ReadAllLines(path);
                return Read<TList, TItem>(lines, output);
            }

            public static bool Read<TDict, TKey, TValue>(TDict output, string keyName, string path) where TDict : IDictionary<TKey, TValue>
            {
                var lines = File.ReadAllLines(path);
                return Read<TDict, TKey, TValue>(lines, output, keyName);
            }


            private static readonly string _TempTextAssetName = "tafTempTA";

            private static TextAsset __TempTextAsset = null;
            private static TextAsset _TempTextAsset
            {
                get
                {
                    if (__TempTextAsset == null)
                    {
                        __TempTextAsset = new TextAsset(Il2CppInterop.Runtime.IL2CPP.il2cpp_object_new(Il2CppInterop.Runtime.Il2CppClassPointerStore<TextAsset>.NativeClassPtr));
                        Util.resCache[_TempTextAssetName] = __TempTextAsset;
                    }
                    return __TempTextAsset;
                }
            }

            private static GameData.LoadInfo __TempLoadInfo = null;
            private static GameData.LoadInfo _TempLoadInfo
            {
                get
                {
                    if (__TempLoadInfo == null)
                    {
                        __TempLoadInfo = new GameData.LoadInfo();
                        __TempLoadInfo.forceLocal = true;
                        __TempLoadInfo.name = _TempTextAssetName;
                    }
                    return __TempLoadInfo;
                }
            }

            // If this takes an existing collection, it must be updated BEFORE PostProcess runs.
            public static Il2CppSystem.Collections.Generic.Dictionary<string, T> ProcessCSV<T>(string text, bool fillCustom, Il2CppSystem.Collections.Generic.Dictionary<string, T> existing = null) where T : BaseData
            {
                TextAsset.Internal_CreateInstance(_TempTextAsset, text);
                var newDict = G.GameData.ProcessCsv<T>(_TempLoadInfo, fillCustom);
                if (existing == null)
                    return newDict;

                int lastID = 0;
                float lastOrder = 0f;
                foreach (var item in existing.Values)
                {
                    if (item.Id > lastID)
                        lastID = item.Id;
                    if (item.order > lastOrder)
                        lastOrder = item.order;
                }

                foreach (var kvp in newDict)
                {
                    if (existing.TryGetValue(kvp.Key, out var oldData))
                    {
                        kvp.Value.order = oldData.order;
                        kvp.Value.Id = oldData.Id;
                    }
                    else
                    {
                        kvp.Value.order = ++lastOrder;
                        kvp.value.Id = ++lastID;
                    }
                    existing[kvp.Key] = kvp.Value;
                }
                return existing;
            }

            // If this takes an existing collection, it must be updated BEFORE PostProcess runs.
            public static Il2CppSystem.Collections.Generic.Dictionary<string, T> ProcessCSV<T>(bool fillCustom, string path, Il2CppSystem.Collections.Generic.Dictionary<string, T> existing = null) where T : BaseData
                => ProcessCSV<T>(File.ReadAllText(path), fillCustom, existing);

            static readonly Dictionary<string, int> _IndexCache = new Dictionary<string, int>();
            // If this takes an existing collection, it must be updated BEFORE PostProcess runs.
            public static Il2CppSystem.Collections.Generic.List<T> ProcessCSVToList<T>(string text, bool fillCustom, Il2CppSystem.Collections.Generic.List<T> existing = null) where T : BaseData
            {
                TextAsset.Internal_CreateInstance(_TempTextAsset, text);
                var list = new Il2CppSystem.Collections.Generic.List<T>();
                G.GameData.ProcessCsv<T>(_TempLoadInfo, list, null, fillCustom);
                if (existing == null)
                    return list;

                // Find last ID and cache indices
                int lastID = 0;
                float lastOrder = 0f;
                for (int i = existing.Count; i-- > 0;)
                {
                    var item = existing[i];
                    _IndexCache[item.name] = i;
                    if (item.Id > lastID)
                        lastID = item.Id;
                    if (item.order > lastOrder)
                        lastOrder = item.order;
                }

                foreach (var item in list)
                {
                    if (_IndexCache.TryGetValue(item.name, out var i))
                    {
                        var oldData = existing[i];
                        item.order = oldData.order;
                        item.Id = oldData.Id;
                        existing[i] = item;
                    }
                    else
                    {
                        item.order = ++lastOrder;
                        item.Id = ++lastID;
                        existing.Add(item);
                    }
                }
                _IndexCache.Clear();
                return existing;
            }

            // If this takes an existing collection, it must be updated BEFORE PostProcess runs.
            public static Il2CppSystem.Collections.Generic.List<T> ProcessCSVToList<T>(bool fillCustom, string path, Il2CppSystem.Collections.Generic.List<T> existing = null) where T : BaseData
                => ProcessCSVToList<T>(File.ReadAllText(path), fillCustom);

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
                public Field _attrib = null;
                public DataType _dataType = DataType.INVALID;

                public FieldData(MemberInfo memberInfo, Field attrib)
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
                    if (_dataType != DataType.ValueString && value.Length == 0)
                        return true;

                    object val = ReadValue(value, _dataType, _fieldType);

                    if (val == null)
                    {
                        Debug.LogError($"[TweaksAndFixes] CSV: Failed to parse {value} to type {_dataType} on field type {_fieldType}");
                        return false;
                    }

                    _fieldInfo.SetValue(host, val);
                    return true;
                }

                public object ReadValue(string value)
                    => ReadValue(value, _dataType, _fieldType);

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
                                Debug.LogWarning($"[TweaksAndFixes] CSV: Couldn't parse value '{value}' for enum '{fieldType.Name}', default value '{defaultName}' will be used.\nValid values are {string.Join(", ", enumNames)}");
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

                public unsafe bool Write(object value, out string output)
                {
                    output = WriteValue(value, _dataType);
                    if (output == null)
                        return false;

                    if (_dataType != DataType.ValueString)
                    {
                        if (_dataType >= DataType.ValueVector2 && _dataType <= DataType.ValueColor32)
                            output = "\"" + output + "\"";

                        return true;
                    }

                    int count = 0;
                    int len = output.Length;
                    fixed (char *pszO = output)
                    {
                        for (int i = len; i-- > 0;)
                        {
                            char c = pszO[i];
                            switch (c)
                            {

                                case '\a':
                                case '\b':
                                case '\f':
                                case '\n':
                                case '\r':
                                case '\t':
                                case '\v':
                                case '"':
                                    ++count;
                                    break;
                            }
                        }
                    }
                    if (count == 0)
                        return true;

                    string oldStr = output;
                    output = new string('x', count + len + 2);
                    fixed (char* pszNew = output)
                    {
                        fixed (char* pszOld = oldStr)
                        {
                            int j = 0;
                            pszNew[j++] = '"';
                            for (int i = 0; i < len; ++i)
                            {
                                char c = pszOld[i];
                                char c2 = c switch
                                {
                                    '\a' => 'a',
                                    '\b' => 'b',
                                    '\f' => 'f',
                                    '\n' => 'n',
                                    '\r' => 'r',
                                    '\t' => 't',
                                    '\v' => 'v',
                                    _ => c
                                };
                                if(c2 != c || c == '"')
                                    pszNew[j++] = '\\';
                                pszNew[j++] = c2;
                            }
                            pszNew[j] = '"';
                        }
                    }

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
            bool _isPostProcess;

            public CSV(Type t)
            {
                MemberInfo[] members = t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.FlattenHierarchy);
                for (int i = 0, iC = members.Length; i < iC; ++i)
                {
                    var attrib = (Field)members[i].GetCustomAttribute(typeof(Field), inherit: true);
                    if (attrib == null)
                        continue;

                    var data = new FieldData(members[i], attrib);
                    if (data._dataType != DataType.INVALID)
                    {
                        _fields.Add(data);
                        _nameToField[data._fieldName] = data;
                    }
                }

                _isPostProcess = typeof(IPostProcess).IsAssignableFrom(t);
            }

            public bool ReadType(object host, List<string> line, List<string> header)
            {
                bool allSucceeded = true;
                int count = line.Count;
                if (count != header.Count)
                {
                    Debug.LogError($"[TweaksAndFixes] CSV: Count mismatch between header line: {header.Count} and line: {count}");
                    return false;
                }

                for (int i = 0; i < count; ++i)
                {
                    if (!_nameToField.TryGetValue(header[i], out FieldData fieldItem))
                        continue;

                    allSucceeded &= fieldItem.Read(line[i], host);
                }

                if (_isPostProcess && host is IPostProcess ipp)
                    ipp.PostProcess();

                return allSucceeded;
            }

            public bool WriteType(object obj, out string output)
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
                    Debug.LogError($"[TweaksAndFixes] CSV: No serializing fields on object of type {t.Name} that is referenced in persistent field, adding as null to TypeCache.");
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

            internal static string Test()
            {
                string basePath = Path.Combine(Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location), "Tests");
                if (!Directory.Exists(basePath))
                    Directory.CreateDirectory(basePath);

                string pathL = Path.Combine(basePath, "testL.csv");
                string pathD = Path.Combine(basePath, "testD.csv");

                List<CSVTest> list = new List<CSVTest>();
                Dictionary<string, CSVTest> dict = new Dictionary<string, CSVTest>();
                for (int i = 0; i < 500; ++i)
                {
                    var t = new CSVTest();
                    t.name = "item" + i;
                    t.x = 0.5f + i * 0.1f;
                    t.y = 1000f + i * -0.5f;
                    t.SetVec(new Vector2(i * 100f, i));
                    t.SetGuid();
                    t.untouched = i;
                    t.test = false;
                    list.Add(t);
                    dict.Add(t.name, t);
                }

                Serializer.CSV.Write<List<CSVTest>, CSVTest>(list, pathL);
                Serializer.CSV.Write<Dictionary<string, CSVTest>.ValueCollection, CSVTest>(dict.Values, pathD);

                List<CSVTest> list2 = new List<CSVTest>();
                Serializer.CSV.Read<List<CSVTest>, CSVTest>(list2, pathL);

                Dictionary<string, CSVTest> dict2 = new Dictionary<string, CSVTest>();
                Serializer.CSV.Read<Dictionary<string, CSVTest>, string, CSVTest>(dict2, "name", pathD);
                if (list2.Count == 0 || dict2.Count == 0)
                    return "Count zero";

                for (int i = 0; i < 500; ++i)
                {
                    if (list[i].name != list2[i].name)
                        return $"Name mismatch on list: {list[i].name} vs {list2[i].name}";
                    if (!list2[i].test)
                        return "Test is false";
                    if (list[i].x != list2[i].x)
                        return $"x mismatch: {list[i].x} vs {list2[i].x}";
                    if (!dict2.TryGetValue("item" + i, out var itm))
                        return "Failed to find item" + i;
                    if (list[i].y != itm.y)
                        return $"y mismatch: {list[i].y} vs {itm.y}";
                    if (list2[i].untouched != 0)
                        return "Untouched is nonzero: " + list2[i].untouched;

                    list[i].x = 0;
                    itm.y = 0;
                }
                Serializer.CSV.Read<List<CSVTest>, CSVTest>(list, pathL);
                Serializer.CSV.Read<Dictionary<string, CSVTest>, string, CSVTest>(dict2, "name", pathL);
                for (int i = 0; i < 500; ++i)
                    if (list[i].x == 0 || dict2["item" + i].y == 0)
                        return $"x is {list[i].x} or y is {dict2["item" + i].y}";

                return "Yes";
            }
            public static void TestNative()
            {
                var dictA = Serializer.CSV.ProcessCSV<PartData>(false, Path.Combine(Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location), "Tests", "test1.csv"));
                var dictB = Serializer.CSV.ProcessCSV<PartData>(false, Path.Combine(Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location), "Tests", "test2.csv"));
                foreach (var v in dictA.Values)
                    Debug.Log($"{v.name}: {v.Id} / {v.order}");
                foreach (var v in dictB.Values)
                    Debug.Log($"{v.name}: {v.Id} / {v.order}");

                int id = 0;
                foreach (var v in G.GameData.parts.Values)
                {
                    id = v.Id;
                }
                ++id;

                foreach (var v in dictB.Values)
                {
                    v.Id = id++;
                    v.order = v.Id;
                    G.GameData.parts.Add(v.name, v);
                }
            }

            public static void TestNativePost()
            {
                foreach (var v in G.GameData.parts.Values)
                {
                    if (v.name.StartsWith("xbb"))
                    {
                        List<string> t = new List<string>();
                        foreach (var kvp in v.paramx)
                            t.Add(kvp.Key + "(" + Il2CppSystem.String.Join(";", kvp.Value.ToArray()) + ")");

                        Debug.Log($"Found {v.name}: {v.Id}. Params {v.param}: {string.Join("/", t)}");
                        break;
                    }
                }
            }


            private class CSVTest
            {
                [Serializer.Field]
                public string name;
                [Serializer.Field]
                public float x;
                [Serializer.Field]
                public float y;
                [Serializer.Field]
                private Vector2 vec;
                [Serializer.Field]
                System.Guid guid;

                public int untouched = 0;

                [Serializer.Field(writeable = false)]
                public bool test = true;


                public void SetVec(Vector2 v) { vec = v; }
                public void SetGuid() { guid = System.Guid.NewGuid(); }
            }
        }

        [System.AttributeUsage(System.AttributeTargets.Field | System.AttributeTargets.Property, AllowMultiple = false)]
        public class Field : System.Attribute
        {
            public string name;
            public bool writeable;

            public Field()
            {
                name = string.Empty;
                writeable = true;
            }
        }

        public interface IPostProcess
        {
            public void PostProcess();
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