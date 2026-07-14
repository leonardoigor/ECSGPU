using System;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;

namespace ECSGPU.GpuEcs
{
    public class GpuWorld : IDisposable
    {
        readonly Dictionary<Type, IComponentBuffer> _buffers = new Dictionary<Type, IComponentBuffer>();
        readonly Dictionary<Type, int> _entityCounts = new Dictionary<Type, int>();

        public IReadOnlyDictionary<Type, IComponentBuffer> Buffers => _buffers;
        public int BufferCount => _buffers.Count;

        public GpuComponentBuffer<T> GetOrCreateBuffer<T>(int initialCapacity = 1024) where T : struct, IGpuComponent
        {
            Type type = typeof(T);
            if (_buffers.TryGetValue(type, out IComponentBuffer existing))
            {
                return (GpuComponentBuffer<T>)existing;
            }

            var buffer = new GpuComponentBuffer<T>(initialCapacity);
            _buffers[type] = buffer;
            _entityCounts[type] = 0;
            return buffer;
        }

        public GpuComponentBuffer<T> GetBuffer<T>() where T : struct, IGpuComponent
        {
            Type type = typeof(T);
            if (_buffers.TryGetValue(type, out IComponentBuffer buffer))
            {
                return (GpuComponentBuffer<T>)buffer;
            }
            return null;
        }

        public IComponentBuffer GetBuffer(Type componentType)
        {
            _buffers.TryGetValue(componentType, out IComponentBuffer buffer);
            return buffer;
        }

        public bool HasBuffer<T>() where T : struct, IGpuComponent
        {
            return _buffers.ContainsKey(typeof(T));
        }

        public bool HasBuffer(Type componentType)
        {
            return _buffers.ContainsKey(componentType);
        }

        public int GetCount<T>() where T : struct, IGpuComponent
        {
            return _entityCounts.TryGetValue(typeof(T), out int count) ? count : 0;
        }

        public int GetCount(Type componentType)
        {
            return _entityCounts.TryGetValue(componentType, out int count) ? count : 0;
        }

        public void SetCount<T>(int count) where T : struct, IGpuComponent
        {
            _entityCounts[typeof(T)] = count;
        }

        public void SetCount(Type componentType, int count)
        {
            _entityCounts[componentType] = count;
        }

        public void SetComponentData<T>(T[] data) where T : struct, IGpuComponent
        {
            var buffer = GetOrCreateBuffer<T>(data.Length);
            buffer.Resize(data.Length);
            buffer.SetData(data);
            _entityCounts[typeof(T)] = data.Length;
        }

        public void SetComponentData<T>(NativeArray<T> data) where T : struct, IGpuComponent
        {
            var buffer = GetOrCreateBuffer<T>(data.Length);
            buffer.Resize(data.Length);
            buffer.SetData(data);
            _entityCounts[typeof(T)] = data.Length;
        }

        public void GetComponentData<T>(T[] results) where T : struct, IGpuComponent
        {
            var buffer = GetBuffer<T>();
            if (buffer != null)
            {
                buffer.GetData(results);
            }
        }

        public void GetComponentData<T>(NativeArray<T> results) where T : struct, IGpuComponent
        {
            var buffer = GetBuffer<T>();
            if (buffer != null)
            {
                buffer.GetData(results);
            }
        }

        public void BindBuffer(ComputeShader shader, int kernel, string name, Type componentType)
        {
            if (_buffers.TryGetValue(componentType, out IComponentBuffer buffer))
            {
                shader.SetBuffer(kernel, name, buffer.Buffer);
            }
        }

        public void BindBuffer<T>(ComputeShader shader, int kernel, string name) where T : struct, IGpuComponent
        {
            var buffer = GetBuffer<T>();
            if (buffer != null)
            {
                shader.SetBuffer(kernel, name, buffer.Buffer);
            }
        }

        public void SyncBuffers()
        {
            foreach (var kvp in _buffers)
            {
                int count = _entityCounts.TryGetValue(kvp.Key, out int c) ? c : 0;
                if (count > 0)
                {
                    kvp.Value.Resize(count);
                }
            }
        }

        public GpuReadbackRequest<T> ReadbackAsync<T>() where T : struct, IGpuComponent
        {
            var buffer = GetBuffer<T>();
            if (buffer == null) return default;

            var request = buffer.ReadbackAsync();
            return new GpuReadbackRequest<T>(request, buffer.Count);
        }

        public void Dispose()
        {
            foreach (var buffer in _buffers.Values)
            {
                buffer.Dispose();
            }
            _buffers.Clear();
            _entityCounts.Clear();
        }
    }
}
