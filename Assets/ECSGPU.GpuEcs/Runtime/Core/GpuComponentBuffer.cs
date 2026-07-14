using System;
using System.Runtime.InteropServices;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;

namespace ECSGPU.GpuEcs
{
    public interface IComponentBuffer : IDisposable
    {
        int Count { get; }
        int Stride { get; }
        ComputeBuffer Buffer { get; }
        Type ComponentType { get; }
        void Resize(int count);
        void SetDataRaw(Array data);
        void GetDataRaw(Array results);
    }

    public class GpuComponentBuffer<T> : IComponentBuffer where T : struct, IGpuComponent
    {
        ComputeBuffer _buffer;
        int _count;
        int _stride;

        public int Count => _count;
        public int Stride => _stride;
        public ComputeBuffer Buffer => _buffer;
        public Type ComponentType => typeof(T);

        public T this[int index]
        {
            get
            {
                throw new InvalidOperationException(
                    "GpuComponentBuffer indexer can only be used in [GpuKernel] methods. " +
                    "These methods are transpiled to HLSL and never executed on CPU.");
            }
            set
            {
                throw new InvalidOperationException(
                    "GpuComponentBuffer indexer can only be used in [GpuKernel] methods. " +
                    "These methods are transpiled to HLSL and never executed on CPU.");
            }
        }

        public T this[uint index]
        {
            get
            {
                throw new InvalidOperationException(
                    "GpuComponentBuffer indexer can only be used in [GpuKernel] methods. " +
                    "These methods are transpiled to HLSL and never executed on CPU.");
            }
            set
            {
                throw new InvalidOperationException(
                    "GpuComponentBuffer indexer can only be used in [GpuKernel] methods. " +
                    "These methods are transpiled to HLSL and never executed on CPU.");
            }
        }

        public GpuComponentBuffer(int initialCapacity = 1024)
        {
            _stride = Marshal.SizeOf<T>();
            _count = 0;
            _buffer = new ComputeBuffer(Mathf.Max(1, initialCapacity), _stride, ComputeBufferType.Default);
        }

        public void Resize(int count)
        {
            if (count <= _buffer.count)
            {
                _count = count;
                return;
            }

            int newCapacity = Mathf.Max(count, _buffer.count * 2);
            var newBuffer = new ComputeBuffer(newCapacity, _stride, ComputeBufferType.Default);

            if (_count > 0 && _buffer != null)
            {
                var temp = new T[_count];
                _buffer.GetData(temp, 0, 0, _count);
                newBuffer.SetData(temp, 0, 0, _count);
            }

            _buffer?.Release();
            _buffer = newBuffer;
            _count = count;
        }

        public void SetData(T[] data)
        {
            int count = Mathf.Min(data.Length, _buffer.count);
            _buffer.SetData(data, 0, 0, count);
            _count = count;
        }

        public void SetData(NativeArray<T> data)
        {
            int count = Mathf.Min(data.Length, _buffer.count);
            _buffer.SetData(data);
            _count = count;
        }

        public void SetSubData(T[] data, int startIndex)
        {
            _buffer.SetData(data, startIndex, 0, data.Length);
        }

        public void SetSubData(NativeArray<T> data, int startIndex)
        {
            _buffer.SetData(data);
        }

        public void GetData(T[] results)
        {
            _buffer.GetData(results, 0, 0, Mathf.Min(_count, results.Length));
        }

        public void GetData(NativeArray<T> results)
        {
            var temp = new T[Mathf.Min(_count, results.Length)];
            _buffer.GetData(temp, 0, 0, temp.Length);
            results.CopyFrom(temp);
        }

        public void GetData(T[] results, int startIndex, int count)
        {
            _buffer.GetData(results, startIndex, 0, count);
        }

        public AsyncGPUReadbackRequest ReadbackAsync(int count = -1)
        {
            int readCount = count > 0 ? count : _count;
            int sizeInBytes = readCount * _stride;
            return AsyncGPUReadback.Request(_buffer, sizeInBytes, 0);
        }

        public void SetDataRaw(Array data)
        {
            if (data is T[] typed)
                SetData(typed);
        }

        public void GetDataRaw(Array results)
        {
            if (results is T[] typed)
                GetData(typed);
        }

        public void Dispose()
        {
            if (_buffer != null)
            {
                _buffer.Release();
                _buffer = null;
            }
            _count = 0;
        }
    }
}
