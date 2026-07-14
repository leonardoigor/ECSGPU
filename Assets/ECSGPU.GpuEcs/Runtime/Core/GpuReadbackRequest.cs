using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;

namespace ECSGPU.GpuEcs
{
    public struct GpuReadbackRequest<T> where T : struct
    {
        AsyncGPUReadbackRequest _request;
        int _count;
        bool _created;

        public bool IsDone => _created && _request.done;
        public bool HasError => _created && _request.hasError;
        public int Count => _count;

        public GpuReadbackRequest(AsyncGPUReadbackRequest request, int count)
        {
            _request = request;
            _count = count;
            _created = true;
        }

        public NativeArray<T> GetData()
        {
            if (!_created || !IsDone || HasError)
                return default;

            return _request.GetData<T>();
        }

        public bool TryGetData(out NativeArray<T> data)
        {
            data = default;
            if (!_created || !IsDone || HasError)
                return false;

            data = _request.GetData<T>();
            return true;
        }
    }
}
