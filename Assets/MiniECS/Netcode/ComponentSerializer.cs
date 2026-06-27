using System;
using System.Collections.Generic;
using System.Reflection;
using Unity.Netcode;
using UnityEngine;

namespace MiniECS.Netcode
{
    public static class ComponentSerializer
    {
        static readonly Dictionary<Type, short> _typeToIndex = new();
        static readonly Dictionary<short, Type> _indexToType = new();
        static readonly Dictionary<Type, FieldInfo[]> _typeFields = new();
        static readonly Dictionary<Type, Action<FastBufferWriter, object>> _writers = new();
        static readonly Dictionary<Type, Func<FastBufferReader, object>> _readers = new();

        static bool _initialized;

        public static void Initialize()
        {
            if (_initialized) return;
            _initialized = true;

            short index = 0;

            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (asm.IsDynamic) continue;

                Type[] types;
                try { types = asm.GetExportedTypes(); }
                catch { continue; }

                foreach (var type in types)
                {
                    if (!type.IsValueType) continue;
                    if (!type.IsDefined(typeof(SyncAttribute), false)) continue;
                    if (!typeof(IComponentData).IsAssignableFrom(type)) continue;
                    if (_typeToIndex.ContainsKey(type)) continue;

                    _typeToIndex[type] = index;
                    _indexToType[index] = type;
                    _typeFields[type] = type.GetFields(BindingFlags.Public | BindingFlags.Instance);

                    _writers[type] = BuildWriter(type);
                    _readers[type] = BuildReader(type);

                    index++;
                }
            }
        }

        public static IReadOnlyDictionary<Type, short> TypeToIndex => _typeToIndex;
        public static IReadOnlyDictionary<short, Type> IndexToType => _indexToType;

        public static int SyncTypeCount => _typeToIndex.Count;

        static Action<FastBufferWriter, object> BuildWriter(Type type)
        {
            var fields = _typeFields[type];
            return (writer, obj) =>
            {
                writer.WriteByte((byte)fields.Length);
                foreach (var f in fields)
                    WriteField(writer, f.FieldType, f.GetValue(obj));
            };
        }

        static Func<FastBufferReader, object> BuildReader(Type type)
        {
            var fields = _typeFields[type];
            return (reader) =>
            {
                object boxed = Activator.CreateInstance(type);
                reader.ReadByte(out _); // consume field count
                foreach (var f in fields)
                    f.SetValue(boxed, ReadField(reader, f.FieldType));
                return boxed;
            };
        }

        // ── Serialize Entity ──────────────────────────────────────────

        public static void WriteEntity(FastBufferWriter writer, int entityId)
        {
            writer.WriteValueSafe(entityId);

            var comps = CollectSyncComponents(entityId);
            writer.WriteByte((byte)comps.Count);

            foreach (var (type, data) in comps)
            {
                writer.WriteValueSafe(_typeToIndex[type]);
                _writers[type](writer, data);
            }
        }

        public static (int entityId, List<(short typeIndex, object data)> components) ReadEntity(
            FastBufferReader reader)
        {
            reader.ReadValueSafe(out int entityId);
            reader.ReadByte(out byte compCount);
            var list = new List<(short, object)>(compCount);

            for (int i = 0; i < compCount; i++)
            {
                reader.ReadValueSafe(out short typeIndex);
                var type = _indexToType[typeIndex];
                var data = _readers[type](reader);
                list.Add((typeIndex, data));
            }

            return (entityId, list);
        }

        // ── Helpers ───────────────────────────────────────────────────

        static List<(Type type, object data)> CollectSyncComponents(int entityId)
        {
            var result = new List<(Type, object)>();
            foreach (var kvp in EntityManager.GetAllPools())
            {
                var type = kvp.Key;
                if (!_typeToIndex.ContainsKey(type)) continue;
                if (!kvp.Value.Has(entityId)) continue;

                int idx = kvp.Value.GetLookup()[entityId];
                object val = kvp.Value.GetComponents().GetValue(idx);
                result.Add((type, val));
            }
            return result;
        }

        static void WriteField(FastBufferWriter writer, Type type, object val)
        {
            if (type == typeof(int))         { writer.WriteValueSafe((int)val); return; }
            if (type == typeof(uint))        { writer.WriteValueSafe((uint)val); return; }
            if (type == typeof(float))       { writer.WriteValueSafe((float)val); return; }
            if (type == typeof(double))      { writer.WriteValueSafe((double)val); return; }
            if (type == typeof(bool))        { writer.WriteValueSafe((bool)val); return; }
            if (type == typeof(byte))        { writer.WriteValueSafe((byte)val); return; }
            if (type == typeof(sbyte))       { writer.WriteValueSafe((sbyte)val); return; }
            if (type == typeof(short))       { writer.WriteValueSafe((short)val); return; }
            if (type == typeof(ushort))      { writer.WriteValueSafe((ushort)val); return; }
            if (type == typeof(long))        { writer.WriteValueSafe((long)val); return; }
            if (type == typeof(ulong))       { writer.WriteValueSafe((ulong)val); return; }
            if (type == typeof(Vector2))     { writer.WriteValueSafe((Vector2)val); return; }
            if (type == typeof(Vector3))     { writer.WriteValueSafe((Vector3)val); return; }
            if (type == typeof(Vector2Int))  { writer.WriteValueSafe((Vector2Int)val); return; }
            if (type == typeof(Vector3Int))  { writer.WriteValueSafe((Vector3Int)val); return; }
            if (type == typeof(Quaternion))  { writer.WriteValueSafe((Quaternion)val); return; }
            if (type == typeof(Color))       { writer.WriteValueSafe((Color)val); return; }
            if (type == typeof(string))      { writer.WriteValueSafe((string)val ?? ""); return; }
            if (type == typeof(vector2))     { var v = (vector2)val; writer.WriteValueSafe(v.x); writer.WriteValueSafe(v.y); return; }
            if (type == typeof(vector3))     { var v = (vector3)val; writer.WriteValueSafe(v.x); writer.WriteValueSafe(v.y); writer.WriteValueSafe(v.z); return; }

            if (type.IsEnum)
            {
                WriteField(writer, Enum.GetUnderlyingType(type), val);
                return;
            }

            throw new ArgumentException($"Unsupported field type for sync: {type}");
        }

        static object ReadField(FastBufferReader reader, Type type)
        {
            if (type == typeof(int))         { reader.ReadValueSafe(out int v); return v; }
            if (type == typeof(uint))        { reader.ReadValueSafe(out uint v); return v; }
            if (type == typeof(float))       { reader.ReadValueSafe(out float v); return v; }
            if (type == typeof(double))      { reader.ReadValueSafe(out double v); return v; }
            if (type == typeof(bool))        { reader.ReadValueSafe(out bool v); return v; }
            if (type == typeof(byte))        { reader.ReadValueSafe(out byte v); return v; }
            if (type == typeof(sbyte))       { reader.ReadValueSafe(out sbyte v); return v; }
            if (type == typeof(short))       { reader.ReadValueSafe(out short v); return v; }
            if (type == typeof(ushort))      { reader.ReadValueSafe(out ushort v); return v; }
            if (type == typeof(long))        { reader.ReadValueSafe(out long v); return v; }
            if (type == typeof(ulong))       { reader.ReadValueSafe(out ulong v); return v; }
            if (type == typeof(Vector2))     { reader.ReadValueSafe(out Vector2 v); return v; }
            if (type == typeof(Vector3))     { reader.ReadValueSafe(out Vector3 v); return v; }
            if (type == typeof(Vector2Int))  { reader.ReadValueSafe(out Vector2Int v); return v; }
            if (type == typeof(Vector3Int))  { reader.ReadValueSafe(out Vector3Int v); return v; }
            if (type == typeof(Quaternion))  { reader.ReadValueSafe(out Quaternion v); return v; }
            if (type == typeof(Color))       { reader.ReadValueSafe(out Color v); return v; }
            if (type == typeof(string))      { reader.ReadValueSafe(out string v); return v; }
            if (type == typeof(vector2))     { reader.ReadValueSafe(out float vx); reader.ReadValueSafe(out float vy); return new vector2(vx, vy); }
            if (type == typeof(vector3))     { reader.ReadValueSafe(out float vx); reader.ReadValueSafe(out float vy); reader.ReadValueSafe(out float vz); return new vector3(vx, vy, vz); }

            if (type.IsEnum)
            {
                var underlying = Enum.GetUnderlyingType(type);
                var val = ReadField(reader, underlying);
                return Enum.ToObject(type, val);
            }

            throw new ArgumentException($"Unsupported field type for sync: {type}");
        }
    }
}
