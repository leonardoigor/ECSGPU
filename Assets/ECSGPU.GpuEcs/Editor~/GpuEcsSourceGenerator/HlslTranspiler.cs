using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ECSGPU.GpuEcs.SourceGenerator
{
    public class HlslTranspiler : CSharpSyntaxRewriter
    {
        readonly SemanticModel _semanticModel;
        readonly StringBuilder _output;
        readonly Dictionary<string, string> _bufferNames;
        int _indent;

        static readonly Dictionary<string, string> TypeMap = new Dictionary<string, string>
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

        static readonly Dictionary<string, string> MathMethodMap = new Dictionary<string, string>
        {
            { "rsqrt",      "rsqrt" },
            { "rcp",        "rcp" },
            { "normalize",  "normalize" },
            { "length",     "length" },
            { "cross",      "cross" },
            { "dot",        "dot" },
            { "abs",        "abs" },
            { "sign",       "sign" },
            { "floor",      "floor" },
            { "ceil",       "ceil" },
            { "round",      "round" },
            { "frac",       "frac" },
            { "saturate",   "saturate" },
            { "clamp",      "clamp" },
            { "lerp",       "lerp" },
            { "step",       "step" },
            { "smoothstep", "smoothstep" },
            { "sin",        "sin" },
            { "cos",        "cos" },
            { "tan",        "tan" },
            { "asin",       "asin" },
            { "acos",       "acos" },
            { "atan",       "atan" },
            { "atan2",      "atan2" },
            { "sinh",       "sinh" },
            { "cosh",       "cosh" },
            { "tanh",       "tanh" },
            { "exp",        "exp" },
            { "exp2",       "exp2" },
            { "log",        "log" },
            { "log2",       "log2" },
            { "pow",        "pow" },
            { "sqrt",       "sqrt" },
            { "mad",        "mad" },
            { "modf",       "modf" },
            { "frexp",      "frexp" },
            { "ldexp",      "ldexp" },
            { "asuint",     "asuint" },
            { "asfloat",    "asfloat" },
            { "asint",      "asint" },
        };

        static readonly Dictionary<string, string> MathPropertyMap = new Dictionary<string, string>
        {
            { "Epsilon", "FLT_EPSILON" },
        };

        static readonly HashSet<string> FloatConstructors = new HashSet<string>
        {
            "Unity.Mathematics.float2",
            "Unity.Mathematics.float3",
            "Unity.Mathematics.float4",
            "Unity.Mathematics.int2",
            "Unity.Mathematics.int3",
            "Unity.Mathematics.int4",
            "Unity.Mathematics.uint2",
            "Unity.Mathematics.uint3",
            "Unity.Mathematics.uint4",
        };

        public HlslTranspiler(SemanticModel semanticModel, Dictionary<string, string> bufferNames)
        {
            _semanticModel = semanticModel;
            _bufferNames = bufferNames;
            _output = new StringBuilder();
            _indent = 0;
        }

        public string Transpile(MethodDeclarationSyntax method)
        {
            _output.Clear();
            _indent = 0;
            Visit(method.Body);
            return _output.ToString();
        }

        string GetHlslType(ITypeSymbol type)
        {
            string fullName = type.ToDisplayString();
            if (TypeMap.TryGetValue(fullName, out string hlsl))
                return hlsl;

            if (type is INamedTypeSymbol named && named.IsGenericType)
            {
                string defName = named.OriginalDefinition.ToDisplayString();
                if (defName.StartsWith("ECSGPU.GpuEcs.GpuComponentBuffer`1"))
                {
                    var componentType = named.TypeArguments[0];
                    string compHlsl = GetHlslType(componentType);
                    return $"StructuredBuffer<{compHlsl}>";
                }
            }

            return fullName;
        }

        string ResolveTypeName(TypeSyntax typeSyntax)
        {
            var symbol = _semanticModel.GetSymbolInfo(typeSyntax);
            if (symbol.Symbol is INamedTypeSymbol namedType)
            {
                string fullName = namedType.ToDisplayString();
                if (TypeMap.TryGetValue(fullName, out string hlsl))
                    return hlsl;
            }
            return typeSyntax.ToString();
        }

        string GetLiteralSuffix(string typeName)
        {
            if (typeName == "float" || typeName == "float2" || typeName == "float3" || typeName == "float4")
                return "f";
            return "";
        }

        void AppendLine(string text)
        {
            _output.Append(new string(' ', _indent * 4));
            _output.AppendLine(text);
        }

        void AppendRaw(string text)
        {
            _output.Append(text);
        }

        public override SyntaxNode VisitBlock(BlockSyntax node)
        {
            AppendLine("{");
            _indent++;
            foreach (var statement in node.Statements)
            {
                Visit(statement);
            }
            _indent--;
            AppendLine("}");
            return node;
        }

        public override SyntaxNode VisitLocalDeclarationStatement(LocalDeclarationStatementSyntax node)
        {
            var decl = node.Declaration;
            string hlslType = ResolveTypeName(decl.Type);

            foreach (var variable in decl.Variables)
            {
                string name = variable.Identifier.Text;
                if (variable.Initializer != null)
                {
                    string initExpr = VisitExpression(variable.Initializer.Value);
                    AppendLine($"{hlslType} {name} = {initExpr};");
                }
                else
                {
                    AppendLine($"{hlslType} {name};");
                }
            }

            return node;
        }

        public override SyntaxNode VisitExpressionStatement(ExpressionStatementSyntax node)
        {
            string expr = VisitExpression(node.Expression);
            AppendLine($"{expr};");
            return node;
        }

        public override SyntaxNode VisitIfStatement(IfStatementSyntax node)
        {
            string condition = VisitExpression(node.Condition);
            AppendLine($"if ({condition})");
            Visit(node.Statement);
            if (node.Else != null)
            {
                AppendLine("else");
                Visit(node.Else.Statement);
            }
            return node;
        }

        public override SyntaxNode VisitForStatement(ForStatementSyntax node)
        {
            var parts = new StringBuilder();
            parts.Append("for (");

            if (node.Declaration != null)
            {
                string type = ResolveTypeName(node.Declaration.Type);
                string names = string.Join(", ", node.Declaration.Variables.Select(v =>
                    v.Initializer != null
                        ? $"{v.Identifier.Text} = {VisitExpression(v.Initializer.Value)}"
                        : v.Identifier.Text));
                parts.Append($"{type} {names}");
            }

            parts.Append("; ");

            if (node.Condition != null)
                parts.Append(VisitExpression(node.Condition));

            parts.Append("; ");

            var incrParts = new List<string>();
            foreach (var incr in node.Incrementors)
            {
                incrParts.Add(VisitExpression(incr));
            }
            parts.Append(string.Join(", ", incrParts));

            parts.Append(")");
            AppendLine(parts.ToString());
            Visit(node.Statement);
            return node;
        }

        public override SyntaxNode VisitWhileStatement(WhileStatementSyntax node)
        {
            string condition = VisitExpression(node.Condition);
            AppendLine($"while ({condition})");
            Visit(node.Statement);
            return node;
        }

        public override SyntaxNode VisitReturnStatement(ReturnStatementSyntax node)
        {
            if (node.Expression != null)
            {
                string expr = VisitExpression(node.Expression);
                AppendLine($"return {expr};");
            }
            else
            {
                AppendLine("return;");
            }
            return node;
        }

        public override SyntaxNode VisitBreakStatement(BreakStatementSyntax node)
        {
            AppendLine("break;");
            return node;
        }

        public override SyntaxNode VisitContinueStatement(ContinueStatementSyntax node)
        {
            AppendLine("continue;");
            return node;
        }

        public override SyntaxNode VisitPrefixUnaryExpression(PrefixUnaryExpressionSyntax node)
        {
            string operand = VisitExpression(node.Operand);
            string op = node.OperatorToken.Text;
            return SyntaxFactory.LiteralExpression(
                SyntaxKind.NumericLiteralExpression,
                SyntaxFactory.Literal($"{op}{operand}"));
        }

        public override SyntaxNode VisitPostfixUnaryExpression(PostfixUnaryExpressionSyntax node)
        {
            string operand = VisitExpression(node.Operand);
            string op = node.OperatorToken.Text;
            return SyntaxFactory.LiteralExpression(
                SyntaxKind.NumericLiteralExpression,
                SyntaxFactory.Literal($"{operand}{op}"));
        }

        public override SyntaxNode VisitAssignmentExpression(AssignmentExpressionSyntax node)
        {
            string left = VisitExpression(node.Left);
            string right = VisitExpression(node.Right);
            string op = node.OperatorToken.Text;
            return SyntaxFactory.LiteralExpression(
                SyntaxKind.NumericLiteralExpression,
                SyntaxFactory.Literal($"{left} {op} {right}"));
        }

        string VisitExpression(ExpressionSyntax expr)
        {
            if (expr == null) return "";

            if (expr is LiteralExpressionSyntax literal)
            {
                return VisitLiteral(literal);
            }

            if (expr is IdentifierNameSyntax identifier)
            {
                return VisitIdentifier(identifier);
            }

            if (expr is MemberAccessExpressionSyntax memberAccess)
            {
                return VisitMemberAccess(memberAccess);
            }

            if (expr is InvocationExpressionSyntax invocation)
            {
                return VisitInvocation(invocation);
            }

            if (expr is ElementAccessExpressionSyntax elementAccess)
            {
                return VisitElementAccess(elementAccess);
            }

            if (expr is BinaryExpressionSyntax binary)
            {
                return VisitBinary(binary);
            }

            if (expr is ConditionalExpressionSyntax conditional)
            {
                return VisitConditional(conditional);
            }

            if (expr is ParenthesizedExpressionSyntax paren)
            {
                return $"({VisitExpression(paren.Expression)})";
            }

            if (expr is ObjectCreationExpressionSyntax objCreate)
            {
                return VisitObjectCreation(objCreate);
            }

            if (expr is ImplicitObjectCreationExpressionSyntax implicitCreate)
            {
                return VisitImplicitObjectCreation(implicitCreate);
            }

            if (expr is CastExpressionSyntax cast)
            {
                string target = ResolveTypeName(cast.Type);
                string inner = VisitExpression(cast.Expression);
                return $"({target}){inner}";
            }

            if (expr is ConditionalAccessExpressionSyntax condAccess)
            {
                return VisitExpression(condAccess.WhenNotNull);
            }

            if (expr is AssignmentExpressionSyntax assignment)
            {
                string left = VisitExpression(assignment.Left);
                string right = VisitExpression(assignment.Right);
                string op = assignment.OperatorToken.Text;
                return $"{left} {op} {right}";
            }

            if (expr is PrefixUnaryExpressionSyntax prefix)
            {
                string operand = VisitExpression(prefix.Operand);
                string op = prefix.OperatorToken.Text;
                return $"{op}{operand}";
            }

            if (expr is PostfixUnaryExpressionSyntax postfix)
            {
                string operand = VisitExpression(postfix.Operand);
                string op = postfix.OperatorToken.Text;
                return $"{operand}{op}";
            }

            return expr.ToString();
        }

        string VisitLiteral(LiteralExpressionSyntax literal)
        {
            if (literal.Token.IsKind(SyntaxKind.NumericLiteralToken))
            {
                string value = literal.Token.Text;
                if (!value.Contains(".") && !value.Contains("f") && !value.Contains("e"))
                    return value;
                if (value.EndsWith("f") || value.EndsWith("F"))
                    return value;
                return value + "f";
            }

            if (literal.Token.IsKind(SyntaxKind.TrueKeyword))
                return "1u";
            if (literal.Token.IsKind(SyntaxKind.FalseKeyword))
                return "0u";

            return literal.ToString();
        }

        string VisitIdentifier(IdentifierNameSyntax identifier)
        {
            string name = identifier.Identifier.Text;

            if (name == "GpuDispatch")
                return null;

            return name;
        }

        string VisitMemberAccess(MemberAccessExpressionSyntax memberAccess)
        {
            string expression = VisitExpression(memberAccess.Expression);
            string memberName = memberAccess.Name.Identifier.Text;

            if (expression == null && memberName == "threadId")
                return "id";

            if (expression == "id" && memberName == "x")
                return "id.x";
            if (expression == "id" && memberName == "y")
                return "id.y";
            if (expression == "id" && memberName == "z")
                return "id.z";

            if (expression == "id")
                return $"id.{memberName}";

            if (expression == null)
                return memberName;

            if (expression == "math" || expression == "Mathf" || expression == "Math" || expression == "System.Math" || expression == "GpuMath")
            {
                if (memberName == "zero")
                    return VectorZero(expression);
                if (MathMethodMap.TryGetValue(memberName, out string hlsl))
                    return hlsl;
                if (MathPropertyMap.TryGetValue(memberName, out string prop))
                    return prop;
                return memberName;
            }

            if (memberName == "zero" && IsVectorType(expression))
                return VectorZero(expression);

            if (memberName == "Length" || memberName == "Count")
                return memberName.ToLower();

            if (expression != null && _bufferNames.ContainsKey(expression))
            {
                return $"{expression}.{memberName}";
            }

            return $"{expression}.{memberName}";
        }

        static bool IsVectorType(string expression)
        {
            return expression.StartsWith("float") || expression.StartsWith("int") || expression.StartsWith("uint")
                || expression == "half";
        }

        static string VectorZero(string typeName)
        {
            if (typeName == "float2" || typeName == "int2" || typeName == "uint2" || typeName == "half2")
                return $"{typeName}(0, 0)";
            if (typeName == "float3" || typeName == "int3" || typeName == "uint3" || typeName == "half3")
                return $"{typeName}(0, 0, 0)";
            if (typeName == "float4" || typeName == "int4" || typeName == "uint4" || typeName == "half4")
                return $"{typeName}(0, 0, 0, 0)";
            return $"{typeName}(0, 0, 0)";
        }

        string VisitInvocation(InvocationExpressionSyntax invocation)
        {
            string methodText = "";

            if (invocation.Expression is MemberAccessExpressionSyntax memberAccess)
            {
                string expression = VisitExpression(memberAccess.Expression);
                string methodName = memberAccess.Name.Identifier.Text;

                if (expression == null && methodName == "GpuDispatch")
                    return "id";

                if (expression == null && (methodName == "math" || methodName == "Mathf" || methodName == "Math" || methodName == "GpuMath"))
                    return null;

                if (expression == "math" || expression == "Math" || expression == "Mathf" || expression == "GpuMath")
                {
                    if (MathMethodMap.TryGetValue(methodName, out string hlslFunc))
                    {
                        var args = invocation.ArgumentList.Arguments
                            .Select(a => VisitExpression(a.Expression));
                        return $"{hlslFunc}({string.Join(", ", args)})";
                    }
                    if (methodName == "select")
                    {
                        var args = invocation.ArgumentList.Arguments.Select(a => VisitExpression(a.Expression)).ToList();
                        if (args.Count == 3)
                            return $"{args[2]} ? {args[1]} : {args[0]}";
                    }
                    if (methodName == "lengthsq")
                    {
                        var args = invocation.ArgumentList.Arguments.Select(a => VisitExpression(a.Expression)).ToList();
                        if (args.Count == 1)
                            return $"dot({args[0]}, {args[0]})";
                    }
                    if (methodName == "normalizesafe")
                    {
                        var args = invocation.ArgumentList.Arguments.Select(a => VisitExpression(a.Expression)).ToList();
                        if (args.Count == 1)
                        {
                            string arg = args[0];
                            return $"normalize({arg})";
                        }
                    }
                    var fallbackArgs = invocation.ArgumentList.Arguments.Select(a => VisitExpression(a.Expression));
                    return $"{methodName}({string.Join(", ", fallbackArgs)})";
                }

                if (expression != null && _bufferNames.ContainsKey(expression))
                {
                    var args = invocation.ArgumentList.Arguments.Select(a => VisitExpression(a.Expression));
                    return $"{expression}[{string.Join(", ", args)}]";
                }

                {
                    var args = invocation.ArgumentList.Arguments.Select(a => VisitExpression(a.Expression));
                    methodText = $"{expression}.{methodName}({string.Join(", ", args)})";
                }
            }
            else if (invocation.Expression is IdentifierNameSyntax ident)
            {
                string methodName = ident.Identifier.Text;
                if (MathMethodMap.TryGetValue(methodName, out string hlslFunc))
                {
                    var args = invocation.ArgumentList.Arguments
                        .Select(a => VisitExpression(a.Expression));
                    return $"{hlslFunc}({string.Join(", ", args)})";
                }
                var idArgs = invocation.ArgumentList.Arguments.Select(a => VisitExpression(a.Expression));
                methodText = $"{methodName}({string.Join(", ", idArgs)})";
            }

            return methodText;
        }

        string VisitElementAccess(ElementAccessExpressionSyntax elementAccess)
        {
            string expression = VisitExpression(elementAccess.Expression);
            var args = elementAccess.ArgumentList.Arguments
                .Select(a => VisitExpression(a.Expression));
            string index = string.Join(", ", args);

            if (expression != null && _bufferNames.ContainsKey(expression))
                return $"{expression}[{index}]";

            return $"{expression}[{index}]";
        }

        string VisitBinary(BinaryExpressionSyntax binary)
        {
            string left = VisitExpression(binary.Left);
            string right = VisitExpression(binary.Right);
            string op = binary.OperatorToken.Text;
            return $"{left} {op} {right}";
        }

        string VisitConditional(ConditionalExpressionSyntax conditional)
        {
            string condition = VisitExpression(conditional.Condition);
            string whenTrue = VisitExpression(conditional.WhenTrue);
            string whenFalse = VisitExpression(conditional.WhenFalse);
            return $"{condition} ? {whenTrue} : {whenFalse}";
        }

        string VisitObjectCreation(ObjectCreationExpressionSyntax objCreate)
        {
            string typeName = ResolveTypeName(objCreate.Type);
            var args = objCreate.ArgumentList?.Arguments
                .Select(a => VisitExpression(a.Expression)) ?? Enumerable.Empty<string>();
            return $"{typeName}({string.Join(", ", args)})";
        }

        string VisitImplicitObjectCreation(ImplicitObjectCreationExpressionSyntax implicitCreate)
        {
            var args = implicitCreate.ArgumentList?.Arguments
                .Select(a => VisitExpression(a.Expression)) ?? Enumerable.Empty<string>();
            return $"({string.Join(", ", args)})";
        }

        public override SyntaxNode VisitEqualsValueClause(EqualsValueClauseSyntax node)
        {
            return node;
        }
    }
}
