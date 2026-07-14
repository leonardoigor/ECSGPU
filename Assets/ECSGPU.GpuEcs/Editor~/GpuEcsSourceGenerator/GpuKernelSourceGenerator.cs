using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace ECSGPU.GpuEcs.SourceGenerator
{
    [Generator]
    public class GpuKernelSourceGenerator : IIncrementalGenerator
    {
        const string GpuSystemAttributeFullName = "ECSGPU.GpuEcs.GpuSystemAttribute";
        const string GpuKernelAttributeFullName = "ECSGPU.GpuEcs.GpuKernelAttribute";
        const string GpuReadWriteAttributeFullName = "ECSGPU.GpuEcs.GpuReadWriteAttribute";
        const string GpuReadOnlyAttributeFullName = "ECSGPU.GpuEcs.GpuReadOnlyAttribute";
        const string GpuComponentAttributeFullName = "ECSGPU.GpuEcs.GpuComponentAttribute";
        const string GpuComponentBufferFullName = "ECSGPU.GpuEcs.GpuComponentBuffer";
        static readonly HashSet<string> ScalarHlslKeywords = new HashSet<string>
        {
            "float", "double", "int", "uint", "bool", "short", "ushort", "long", "ulong", "byte", "sbyte"
        };

        static bool IsGpuComponentBuffer(INamedTypeSymbol namedType)
        {
            string origDef = namedType.OriginalDefinition.ToDisplayString();
            return origDef.StartsWith(GpuComponentBufferFullName) &&
                   (origDef.Contains("<") || origDef.Contains("`"));
        }

        static bool TryGetScalarHlslType(string csType, out string hlslType)
        {
            if (ScalarHlslTypeMap.TryGetValue(csType, out hlslType))
                return true;
            hlslType = null;
            return false;
        }
        const string IGpuComponentFullName = "ECSGPU.GpuEcs.IGpuComponent";

        static readonly Dictionary<string, string> HlslTypeMap = new Dictionary<string, string>
        {
            { "System.Single",  "float" },
            { "System.Int32",   "int" },
            { "System.UInt32",  "uint" },
            { "System.Boolean", "uint" },
            { "Unity.Mathematics.float2",  "float2" },
            { "Unity.Mathematics.float3",  "float3" },
            { "Unity.Mathematics.float4",  "float4" },
            { "Unity.Mathematics.int2",    "int2" },
            { "Unity.Mathematics.int3",    "int3" },
            { "Unity.Mathematics.int4",    "int4" },
            { "Unity.Mathematics.uint2",   "uint2" },
            { "Unity.Mathematics.uint3",   "uint3" },
            { "Unity.Mathematics.uint4",   "uint4" },
            { "Unity.Mathematics.quaternion", "float4" },
            { "UnityEngine.Vector2",  "float2" },
            { "UnityEngine.Vector3",  "float3" },
            { "UnityEngine.Vector4",  "float4" },
            { "UnityEngine.Color",    "float4" },
            { "UnityEngine.Quaternion", "float4" },
        };

        static readonly Dictionary<string, string> ScalarHlslTypeMap = new Dictionary<string, string>
        {
            { "System.Single",  "float" },
            { "System.Int32",   "int" },
            { "System.UInt32",  "uint" },
            { "System.Boolean", "uint" },
            { "float",  "float" },
            { "int",     "int" },
            { "uint",    "uint" },
            { "bool",    "uint" },
            { "double",  "double" },
            { "short",   "short" },
            { "ushort",  "ushort" },
            { "long",    "long" },
            { "ulong",   "ulong" },
            { "byte",    "byte" },
            { "sbyte",   "sbyte" },
        };

        static readonly Dictionary<string, string> HlslTypeMapByKeyword = new Dictionary<string, string>
        {
            { "float",  "float" },
            { "double", "double" },
            { "int",    "int" },
            { "uint",   "uint" },
            { "bool",   "uint" },
            { "short",  "short" },
            { "ushort", "ushort" },
            { "long",   "long" },
            { "ulong",  "ulong" },
            { "byte",   "byte" },
            { "sbyte",  "sbyte" },
        };

        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            var systemProvider = context.SyntaxProvider
                .ForAttributeWithMetadataName(
                    GpuSystemAttributeFullName,
                    predicate: (node, _) => node is StructDeclarationSyntax,
                    transform: (ctx, _) =>
                    {
                        try
                        {
                            return GetSystemInfo(ctx);
                        }
                        catch (System.Exception ex)
                        {
                            return null;
                        }
                    })
                .Where(static m => m != null);

            context.RegisterSourceOutput(systemProvider, (spc, info) =>
            {
                try
                {
                    GenerateAll(spc, info);
                }
                catch (System.Exception ex)
                {
                    spc.ReportDiagnostic(Diagnostic.Create(
                        new DiagnosticDescriptor(
                            "GPUGEN002",
                            "Source Generator Error",
                            "GpuKernelSourceGenerator failed: {0}",
                            "GpuEcs",
                            DiagnosticSeverity.Error,
                            true),
                        Location.None,
                        ex.ToString()));
                }
            });
        }

        SystemKernelInfo GetSystemInfo(GeneratorAttributeSyntaxContext ctx)
        {
            if (!(ctx.TargetSymbol is INamedTypeSymbol structSymbol))
                return null;

            string systemName = structSymbol.Name;
            string ns = structSymbol.ContainingNamespace?.IsGlobalNamespace == true
                ? null
                : structSymbol.ContainingNamespace?.ToDisplayString();

            int threadX = 256, threadY = 1, threadZ = 1;
            string systemKernelName = null;

            var sysAttr = structSymbol.GetAttributes()
                .FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == GpuSystemAttributeFullName);

            if (sysAttr != null)
            {
                if (sysAttr.ConstructorArguments.Length > 0)
                    systemKernelName = sysAttr.ConstructorArguments[0].Value as string;

                foreach (var named in sysAttr.NamedArguments)
                {
                    switch (named.Key)
                    {
                        case "ThreadX": threadX = (int)(named.Value.Value ?? 256); break;
                        case "ThreadY": threadY = (int)(named.Value.Value ?? 1); break;
                        case "ThreadZ": threadZ = (int)(named.Value.Value ?? 1); break;
                    }
                }
            }

            var bufferFields = new List<BufferFieldInfo>();
            var scalarFields = new List<ScalarFieldInfo>();
            var kernelMethods = new List<KernelMethodInfo>();
            var componentStructs = new List<ComponentStructInfo>();

            var allMembers = structSymbol.GetMembers();

            foreach (var member in allMembers)
            {
                if (!(member is IFieldSymbol field) || field.IsStatic || field.IsConst)
                    continue;

                bool hasReadWrite = field.GetAttributes().Any(a =>
                    a.AttributeClass?.ToDisplayString() == GpuReadWriteAttributeFullName);
                bool hasReadOnly = field.GetAttributes().Any(a =>
                    a.AttributeClass?.ToDisplayString() == GpuReadOnlyAttributeFullName);

                string fieldType = field.Type.ToDisplayString();

                if (field.Type is INamedTypeSymbol namedType2 &&
                    IsGpuComponentBuffer(namedType2))
                {
                    var componentType = namedType2.TypeArguments[0];
                    string compFullName = componentType.ToDisplayString();

                    string access = hasReadOnly ? "StructuredBuffer" : "RWStructuredBuffer";

                    bufferFields.Add(new BufferFieldInfo
                    {
                        FieldName = field.Name,
                        ComponentType = compFullName,
                        HlslComponentType = componentType.Name,
                        Access = access
                    });

                    CollectComponentStruct(componentType, componentStructs);
                }
                else if (TryGetScalarHlslType(fieldType, out string scalarHlslType))
                {
                    scalarFields.Add(new ScalarFieldInfo
                    {
                        FieldName = field.Name,
                        CsType = fieldType,
                        HlslType = scalarHlslType
                    });
                }
            }

            foreach (var method in allMembers.OfType<IMethodSymbol>())
            {
                bool isKernel = method.GetAttributes().Any(a =>
                    a.AttributeClass?.ToDisplayString() == GpuKernelAttributeFullName);

                if (!isKernel || method.Parameters.Length > 0)
                    continue;

                kernelMethods.Add(new KernelMethodInfo
                {
                    Name = method.Name,
                    Syntax = method.DeclaringSyntaxReferences[0].GetSyntax() as MethodDeclarationSyntax
                });
            }

            if (kernelMethods.Count == 0)
                return null;

            return new SystemKernelInfo
            {
                SystemName = systemName,
                Namespace = ns,
                SystemKernelName = systemKernelName ?? kernelMethods[0]?.Name ?? "Execute",
                ThreadX = threadX,
                ThreadY = threadY,
                ThreadZ = threadZ,
                BufferFields = bufferFields,
                ScalarFields = scalarFields,
                KernelMethods = kernelMethods,
                ComponentStructs = componentStructs,
                SemanticModel = ctx.SemanticModel
            };
        }

        void CollectComponentStruct(ITypeSymbol componentType, List<ComponentStructInfo> structs)
        {
            string compName = componentType.Name;
            if (structs.Any(s => s.Name == compName))
                return;

            var fields = new List<ComputeShaderEmitter.HlslFieldInfo>();
            foreach (var member in componentType.GetMembers())
            {
                if (member is IFieldSymbol f && !f.IsStatic && !f.IsConst)
                {
                    string csType = f.Type.ToDisplayString();
                    string hlslType = "float";
                    if (HlslTypeMap.TryGetValue(csType, out string mapped))
                        hlslType = mapped;
                    else if (HlslTypeMapByKeyword.TryGetValue(csType, out mapped))
                        hlslType = mapped;

                    fields.Add(new ComputeShaderEmitter.HlslFieldInfo
                    {
                        HlslType = hlslType,
                        Name = f.Name
                    });
                }
            }

            structs.Add(new ComponentStructInfo
            {
                Name = compName,
                Fields = fields
            });
        }

        void GenerateAll(SourceProductionContext context, SystemKernelInfo info)
        {
            var emitter = new ComputeShaderEmitter();

            foreach (var comp in info.ComponentStructs)
            {
                emitter.AddStruct(new ComputeShaderEmitter.HlslStructInfo
                {
                    Name = comp.Name,
                    Fields = comp.Fields
                });
            }

            foreach (var buffer in info.BufferFields)
            {
                emitter.AddBuffer(new ComputeShaderEmitter.BufferDeclInfo
                {
                    Access = buffer.Access,
                    ComponentType = buffer.HlslComponentType,
                    Name = buffer.FieldName
                });
            }

            foreach (var scalar in info.ScalarFields)
            {
                emitter.AddScalar(new ComputeShaderEmitter.ScalarDeclInfo
                {
                    HlslType = scalar.HlslType,
                    Name = scalar.FieldName
                });
            }

            var bufferNameMap = new Dictionary<string, string>();
            foreach (var buffer in info.BufferFields)
            {
                bufferNameMap[buffer.FieldName] = buffer.FieldName;
            }

            string kernelNameToUse = info.SystemKernelName;

            foreach (var kernel in info.KernelMethods)
            {
                string kernelBody = "";

                if (kernel.Syntax.Body != null)
                {
                    var transpiler = new HlslTranspiler(info.SemanticModel, bufferNameMap);
                    kernelBody = transpiler.Transpile(kernel.Syntax);
                }

                emitter.AddKernel(new ComputeShaderEmitter.KernelInfo
                {
                    Name = kernelNameToUse,
                    ThreadX = info.ThreadX,
                    ThreadY = info.ThreadY,
                    ThreadZ = info.ThreadZ,
                    Body = kernelBody
                });
            }

            string hlslSource = emitter.Emit();

            GenerateComputeShaderOutput(context, info, hlslSource, kernelNameToUse);
            GenerateDispatchOutput(context, info, bufferNameMap, kernelNameToUse);
        }

        void GenerateComputeShaderOutput(SourceProductionContext context, SystemKernelInfo info, string hlslSource, string kernelName)
        {
            var sb = new StringBuilder();
            sb.AppendLine("// <auto-generated/>");
            sb.AppendLine("// Generated by ECSGPU.GpuEcs Source Generator");
            sb.AppendLine("// DO NOT EDIT - changes will be overwritten");
            sb.AppendLine();
            sb.AppendLine("namespace ECSGPU.GpuEcs.Generated");
            sb.AppendLine("{");
            sb.AppendLine($"    internal static class {info.SystemName}_ComputeShader");
            sb.AppendLine("    {");
            sb.AppendLine("        public const string HLSL = @\"");
            foreach (var line in hlslSource.Split('\n'))
            {
                sb.AppendLine(line);
            }
            sb.AppendLine("\";");
            sb.AppendLine("    }");
            sb.AppendLine("}");

            context.AddSource($"{info.SystemName}ComputeShader.g.cs",
                SourceText.From(sb.ToString(), Encoding.UTF8));
        }

        void GenerateDispatchOutput(SourceProductionContext context, SystemKernelInfo info,
            Dictionary<string, string> bufferNameMap, string kernelName)
        {
            var sb = new StringBuilder();
            sb.AppendLine("// <auto-generated/>");
            sb.AppendLine("// Generated by ECSGPU.GpuEcs Source Generator");
            sb.AppendLine();
            sb.AppendLine("using UnityEngine;");
            sb.AppendLine();

            if (info.Namespace != null)
            {
                sb.AppendLine($"namespace {info.Namespace}");
                sb.AppendLine("{");
            }

            string indent = info.Namespace != null ? "    " : "";
            sb.AppendLine($"{indent}partial struct {info.SystemName}");
            sb.AppendLine($"{indent}{{");

            foreach (var kernel in info.KernelMethods)
            {
                string methodName = kernel.Syntax.Identifier.Text;

                sb.AppendLine($"{indent}    public void {methodName}(ComputeShader shader, GpuWorld world, int entityCount)");
                sb.AppendLine($"{indent}    {{");
                sb.AppendLine($"{indent}        int kernel = shader.FindKernel(\"{kernelName}\");");
                sb.AppendLine($"{indent}        int groupsX = (entityCount + {info.ThreadX - 1}) / {info.ThreadX};");
                sb.AppendLine($"{indent}        int groupsY = {(info.ThreadY > 1 ? $"(entityCount + {info.ThreadY - 1}) / {info.ThreadY}" : "1")};");
                sb.AppendLine($"{indent}        int groupsZ = 1;");
                sb.AppendLine();

                foreach (var buffer in info.BufferFields)
                {
                    sb.AppendLine($"{indent}        if ({buffer.FieldName} != null)");
                    sb.AppendLine($"{indent}            shader.SetBuffer(kernel, \"{buffer.FieldName}\", {buffer.FieldName}.Buffer);");
                }

                foreach (var scalar in info.ScalarFields)
                {
                    if (scalar.CsType.StartsWith("System.Boolean") || scalar.CsType.StartsWith("bool"))
                    {
                        sb.AppendLine($"{indent}        shader.SetInt(\"{scalar.FieldName}\", {scalar.FieldName} ? 1 : 0);");
                    }
                    else
                    {
                        string setMethod = GetShaderSetMethod(scalar.CsType);
                        string cast = GetScalarCast(scalar.CsType);
                        string castSuffix = GetScalarCastSuffix(scalar.CsType);
                        sb.AppendLine($"{indent}        shader.{setMethod}(\"{scalar.FieldName}\", {cast}{scalar.FieldName}{castSuffix});");
                    }
                }

                sb.AppendLine();
                sb.AppendLine($"{indent}        shader.Dispatch(kernel, groupsX, groupsY, groupsZ);");
                sb.AppendLine($"{indent}    }}");
                sb.AppendLine();
            }

            sb.AppendLine($"{indent}    public void AutoBind(GpuWorld world)");
            sb.AppendLine($"{indent}    {{");
            foreach (var buffer in info.BufferFields)
            {
                sb.AppendLine($"{indent}        if (world.HasBuffer<{buffer.ComponentType}>())");
                sb.AppendLine($"{indent}            {buffer.FieldName} = world.GetOrCreateBuffer<{buffer.ComponentType}>(world.GetCount<{buffer.ComponentType}>());");
            }
            sb.AppendLine($"{indent}    }}");

            sb.AppendLine($"{indent}}}");
            if (info.Namespace != null)
                sb.AppendLine("}");

            context.AddSource($"{info.SystemName}Dispatch.g.cs",
                SourceText.From(sb.ToString(), Encoding.UTF8));
        }

        static string GetShaderSetMethod(string csType)
        {
            switch (csType)
            {
                case "System.Int32":  case "int":    return "SetInt";
                case "System.UInt32": case "uint":   return "SetInt";
                case "System.Boolean":case "bool":   return "SetInt";
                case "System.Single": case "float":  return "SetFloat";
                case "System.Double": case "double": return "SetFloat";
                default:                              return "SetFloat";
            }
        }

        static string GetScalarCast(string csType)
        {
            switch (csType)
            {
                case "System.Boolean": case "bool":   return "(int)(";
                case "System.UInt32":  case "uint":   return "(int)";
                default:                              return "";
            }
        }

        static string GetScalarCastSuffix(string csType)
        {
            switch (csType)
            {
                case "System.Boolean": case "bool":   return " ? 1 : 0)";
                default:                              return "";
            }
        }

        class SystemKernelInfo
        {
            public string SystemName;
            public string Namespace;
            public string SystemKernelName;
            public int ThreadX, ThreadY, ThreadZ;
            public List<BufferFieldInfo> BufferFields;
            public List<ScalarFieldInfo> ScalarFields;
            public List<KernelMethodInfo> KernelMethods;
            public List<ComponentStructInfo> ComponentStructs;
            public SemanticModel SemanticModel;
        }

        class BufferFieldInfo
        {
            public string FieldName;
            public string ComponentType;
            public string HlslComponentType;
            public string Access;
        }

        class ScalarFieldInfo
        {
            public string FieldName;
            public string CsType;
            public string HlslType;
        }

        class KernelMethodInfo
        {
            public string Name;
            public MethodDeclarationSyntax Syntax;
        }

        class ComponentStructInfo
        {
            public string Name;
            public List<ComputeShaderEmitter.HlslFieldInfo> Fields;
        }
    }
}
