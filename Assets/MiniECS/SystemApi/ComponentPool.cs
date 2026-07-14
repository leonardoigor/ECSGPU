using System;
using System.Collections.Generic;

namespace MiniECS
{
    public interface IComponentPool
    {
        Array GetComponents();
        List<int> GetEntities();
        Dictionary<int, int> GetLookup();
        bool Has(int id);
        int GetIndex(int id);
    }

    public sealed class ComponentPool<T> : IComponentPool
    {
        public readonly List<int> entities = new();
        public T[] components = new T[128];
        public Dictionary<int, int> lookup = new();

        public int[] sparse = new int[128];
        public int[] denseEntity = new int[128];
        public int count;

        public Array GetComponents() => components;
        public List<int> GetEntities() => entities;
        public Dictionary<int, int> GetLookup() => lookup;

        void EnsureSparse(int id)
        {
            if (id >= sparse.Length)
            {
                int oldLen = sparse.Length;
                int newLen = System.Math.Max(id + 1, sparse.Length * 2);
                Array.Resize(ref sparse, newLen);
                Array.Resize(ref denseEntity, newLen);
                for (int j = oldLen; j < newLen; j++)
                    sparse[j] = -1;
            }
        }

        public void Add(int id, T c)
        {
            if (lookup.TryGetValue(id, out int existing))
            {
                components[existing] = c;
                return;
            }

            EnsureSparse(id);

            if (count >= components.Length)
                Array.Resize(ref components, components.Length * 2);
            if (count >= denseEntity.Length)
                Array.Resize(ref denseEntity, denseEntity.Length * 2);

            sparse[id] = count;
            denseEntity[count] = id;
            lookup[id] = count;
            entities.Add(id);

            components[count] = c;
            count++;
        }

        public void Remove(int id)
        {
            if (!lookup.TryGetValue(id, out int i)) return;
            int last = count - 1;

            if (i != last)
            {
                entities[i] = entities[last];
                components[i] = components[last];
                denseEntity[i] = denseEntity[last];
                lookup[entities[i]] = i;
                sparse[entities[i]] = i;
            }

            sparse[id] = -1;
            entities.RemoveAt(last);
            components[last] = default;
            denseEntity[last] = 0;
            lookup.Remove(id);
            count--;
        }

        public ref T Get(int id) => ref components[sparse[id]];

        public bool Has(int id)
        {
            if (id < 0 || id >= sparse.Length) return false;
            int idx = sparse[id];
            if (idx < 0 || idx >= count) return false;
            return denseEntity[idx] == id;
        }

        public int GetIndex(int id) => sparse[id];
    }
}
