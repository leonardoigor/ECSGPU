using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ECSGPU.GpuEcs.SourceGenerator
{
    public class ComputeShaderEmitter
    {
        readonly List<KernelInfo> _kernels = new List<KernelInfo>();
        readonly List<HlslStructInfo> _structs = new List<HlslStructInfo>();
        readonly List<BufferDeclInfo> _buffers = new List<BufferDeclInfo>();
        readonly List<ScalarDeclInfo> _scalars = new List<ScalarDeclInfo>();

        public void AddKernel(KernelInfo kernel)
        {
            _kernels.Add(kernel);
        }

        public void AddStruct(HlslStructInfo s)
        {
            if (!_structs.Any(existing => existing.Name == s.Name))
                _structs.Add(s);
        }

        public void AddBuffer(BufferDeclInfo buffer)
        {
            if (!_buffers.Any(b => b.Name == buffer.Name))
                _buffers.Add(buffer);
        }

        public void AddScalar(ScalarDeclInfo scalar)
        {
            if (!_scalars.Any(s => s.Name == scalar.Name))
                _scalars.Add(scalar);
        }

        public string Emit()
        {
            var sb = new StringBuilder();

            foreach (var kernel in _kernels)
            {
                sb.AppendLine($"#pragma kernel {kernel.Name}");
            }
            sb.AppendLine();

            foreach (var s in _structs)
            {
                sb.AppendLine($"struct {s.Name}");
                sb.AppendLine("{");
                foreach (var field in s.Fields)
                {
                    sb.AppendLine($"    {field.HlslType} {field.Name};");
                }
                sb.AppendLine("};");
                sb.AppendLine();
            }

            foreach (var buffer in _buffers)
            {
                sb.AppendLine($"{buffer.Access}<{buffer.ComponentType}> {buffer.Name};");
            }
            if (_buffers.Count > 0)
                sb.AppendLine();

            foreach (var scalar in _scalars)
            {
                sb.AppendLine($"{scalar.HlslType} {scalar.Name};");
            }
            if (_scalars.Count > 0)
                sb.AppendLine();

            foreach (var kernel in _kernels)
            {
                sb.AppendLine($"[numthreads({kernel.ThreadX}, {kernel.ThreadY}, {kernel.ThreadZ})]");
                sb.AppendLine($"void {kernel.Name}(uint3 id : SV_DispatchThreadID)");
                sb.Append(kernel.Body);
                sb.AppendLine();
            }

            return sb.ToString();
        }

        public struct KernelInfo
        {
            public string Name;
            public int ThreadX;
            public int ThreadY;
            public int ThreadZ;
            public string Body;
        }

        public struct HlslStructInfo
        {
            public string Name;
            public List<HlslFieldInfo> Fields;
        }

        public struct HlslFieldInfo
        {
            public string HlslType;
            public string Name;
        }

        public struct BufferDeclInfo
        {
            public string Access;
            public string ComponentType;
            public string Name;
        }

        public struct ScalarDeclInfo
        {
            public string HlslType;
            public string Name;
        }
    }
}
