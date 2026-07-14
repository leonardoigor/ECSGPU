using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.InteropServices;
using Unity.Mathematics;
using UnityEngine;

namespace ECSGPU.GpuEcs
{
    public static class GpuTypeMapper
    {
        struct TypeMapping
        {
            public string HlslType;
            public int Stride;
        }

        static readonly Dictionary<Type, TypeMapping> TypeMap = new Dictionary<Type, TypeMapping>
        {
            { typeof(float),  new TypeMapping { HlslType = "float",  Stride = 4  } },
            { typeof(float2), new TypeMapping { HlslType = "float2", Stride = 8  } },
            { typeof(float3), new TypeMapping { HlslType = "float3", Stride = 12 } },
            { typeof(float4), new TypeMapping { HlslType = "float4", Stride = 16 } },
            { typeof(int),    new TypeMapping { HlslType = "int",    Stride = 4  } },
            { typeof(int2),   new TypeMapping { HlslType = "int2",   Stride = 8  } },
            { typeof(int3),   new TypeMapping { HlslType = "int3",   Stride = 12 } },
            { typeof(int4),   new TypeMapping { HlslType = "int4",   Stride = 16 } },
            { typeof(uint),   new TypeMapping { HlslType = "uint",   Stride = 4  } },
            { typeof(uint2),  new TypeMapping { HlslType = "uint2",  Stride = 8  } },
            { typeof(uint3),  new TypeMapping { HlslType = "uint3",  Stride = 12 } },
            { typeof(uint4),  new TypeMapping { HlslType = "uint4",  Stride = 16 } },
            { typeof(bool),   new TypeMapping { HlslType = "uint",   Stride = 4  } },
            { typeof(Color),  new TypeMapping { HlslType = "float4", Stride = 16 } },
            { typeof(Vector2), new TypeMapping { HlslType = "float2", Stride = 8 } },
            { typeof(Vector3), new TypeMapping { HlslType = "float3", Stride = 12 } },
            { typeof(Vector4), new TypeMapping { HlslType = "float4", Stride = 16 } },
            { typeof(Matrix4x4), new TypeMapping { HlslType = "float4x4", Stride = 64 } },
            { typeof(Quaternion), new TypeMapping { HlslType = "float4", Stride = 16 } },
        };

        public static bool TryGetMapping(Type csType, out string hlslType, out int stride)
        {
            if (TypeMap.TryGetValue(csType, out var mapping))
            {
                hlslType = mapping.HlslType;
                stride = mapping.Stride;
                return true;
            }

            hlslType = null;
            stride = 0;
            return false;
        }

        public static string GetHlslType(Type csType)
        {
            return TryGetMapping(csType, out string hlsl, out _) ? hlsl : null;
        }

        public static int GetStride(Type csType)
        {
            return TryGetMapping(csType, out _, out int stride) ? stride : 0;
        }

        public static int ComputeStructStride(Type structType)
        {
            var fields = structType.GetFields(BindingFlags.Public | BindingFlags.Instance);
            int totalStride = 0;

            foreach (var field in fields)
            {
                if (!TryGetMapping(field.FieldType, out _, out int fieldStride))
                {
                    Debug.LogWarning($"[GpuTypeMapper] Cannot map field '{field.Name}' of type '{field.FieldType.Name}' to HLSL.");
                    return -1;
                }
                totalStride += fieldStride;
            }

            return totalStride;
        }

        public static bool ValidateStruct(Type structType)
        {
            var fields = structType.GetFields(BindingFlags.Public | BindingFlags.Instance);
            foreach (var field in fields)
            {
                if (!TryGetMapping(field.FieldType, out _, out _))
                {
                    Debug.LogWarning($"[GpuTypeMapper] {structType.Name}.{field.Name} ({field.FieldType.Name}) has no HLSL mapping.");
                    return false;
                }
            }
            return true;
        }

        public static string GenerateHlslStruct(Type structType, string hlslName = null)
        {
            string name = hlslName ?? structType.Name;
            var fields = structType.GetFields(BindingFlags.Public | BindingFlags.Instance);

            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"struct {name}");
            sb.AppendLine("{");

            foreach (var field in fields)
            {
                if (TryGetMapping(field.FieldType, out string hlslType, out _))
                {
                    sb.AppendLine($"    {hlslType} {field.Name};");
                }
            }

            sb.AppendLine("};");
            return sb.ToString();
        }
    }
}
