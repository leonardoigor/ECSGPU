using System;
using System.Collections.Generic;

namespace MiniECS
{
    public static class EntityManager
    {
        struct EntityData { public ulong[] mask; }

        static EntityData[] entities = new EntityData[1024];
        static Dictionary<Type, object> pools = new();
        static int nextId = 1;

        static void Ensure(int id)
        {
            if (id < entities.Length) return;
            Array.Resize(ref entities, id * 2);
        }

        public static Entity CreateEntity()
        {
            int id = nextId++;
            Ensure(id);
            entities[id].mask = Array.Empty<ulong>();
            return new Entity(id);
        }

        public static ulong[] Mask(int id) => entities[id].mask;

        public static ComponentPool<T> Pool<T>()
        {
            var t = typeof(T);
            if (!pools.TryGetValue(t, out var o))
                pools[t] = o = new ComponentPool<T>();
            return (ComponentPool<T>)o;
        }

        public static IComponentPool GetPool(Type t)
        {
            if (!pools.TryGetValue(t, out var o))
            {
                var poolType = typeof(ComponentPool<>).MakeGenericType(t);
                pools[t] = o = Activator.CreateInstance(poolType);
            }
            return (IComponentPool)o;
        }

        public static void Add<T>(Entity e, T c)
        {
            Pool<T>().Add(e.Id, c);
            ref var m = ref entities[e.Id].mask;
            BitMask.EnsureSize(ref m, ComponentType<T>.Word);
            m[ComponentType<T>.Word] |= ComponentType<T>.Bit;
        }

        public static void RemoveComponent<T>(int id)
        {
            Pool<T>().Remove(id);
            ref var m = ref entities[id].mask;
            if (m != null && m.Length > ComponentType<T>.Word)
                m[ComponentType<T>.Word] &= ~ComponentType<T>.Bit;
        }

        public static bool Has<T>(int id)
        {
            if (id <= 0 || id >= entities.Length) return false;
            var m = entities[id].mask;
            if (m == null) return false;
            return m.Length > ComponentType<T>.Word &&
                   (m[ComponentType<T>.Word] & ComponentType<T>.Bit) != 0;
        }

        public static Query<T> Query<T>() => new Query<T>();
        public static Query<T1, T2> Query<T1, T2>() => new Query<T1, T2>();
        public static Query<T1, T2, T3> Query<T1, T2, T3>() => new Query<T1, T2, T3>();
        public static Query<T1, T2, T3, T4> Query<T1, T2, T3, T4>() => new Query<T1, T2, T3, T4>();
        public static Query<T1, T2, T3, T4, T5> Query<T1, T2, T3, T4, T5>() => new Query<T1, T2, T3, T4, T5>();
        public static Query<T1, T2, T3, T4, T5, T6> Query<T1, T2, T3, T4, T5, T6>() => new Query<T1, T2, T3, T4, T5, T6>();
        public static Query<T1, T2, T3, T4, T5, T6, T7> Query<T1, T2, T3, T4, T5, T6, T7>() => new Query<T1, T2, T3, T4, T5, T6, T7>();

        public static Dictionary<Type, int> GetPoolEntityCounts()
        {
            var result = new Dictionary<Type, int>();
            foreach (var kvp in pools)
            {
                var pool = (IComponentPool)kvp.Value;
                result[kvp.Key] = pool.GetEntities().Count;
            }
            return result;
        }

        public static IEnumerable<KeyValuePair<Type, IComponentPool>> GetAllPools()
        {
            foreach (var kvp in pools)
                yield return new KeyValuePair<Type, IComponentPool>(kvp.Key, (IComponentPool)kvp.Value);
        }

        public static IEnumerable<int> GetActiveEntityIds()
        {
            for (int i = 1; i < nextId; i++)
            {
                var mask = entities[i].mask;
                if (mask == null) continue;
                bool hasAny = false;
                for (int j = 0; j < mask.Length; j++)
                    if (mask[j] != 0) { hasAny = true; break; }
                if (hasAny) yield return i;
            }
        }

        public static int GetActiveEntityCount()
        {
            int count = 0;
            for (int i = 1; i < nextId; i++)
            {
                var mask = entities[i].mask;
                if (mask == null) continue;
                for (int j = 0; j < mask.Length; j++)
                    if (mask[j] != 0) { count++; break; }
            }
            return count;
        }

        public static void Reset()
        {
            entities = new EntityData[1024];
            pools = new Dictionary<Type, object>();
            nextId = 1;
        }
    }
}
