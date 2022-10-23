﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace System.Windows.Forms.PrivateSourceGenerators
{
    [Generator]
    public class EnumValidationGenerator : ISourceGenerator
    {
        private const string EnumValidatorStub = @"
// <auto-generated />
namespace SourceGenerated
{
    internal static partial class EnumValidator
    {
        /// <summary>Validates that the enum value passed in is valid for the enum type. Calling this overload will result in a type-specific version being generated.</summary>
        public static void Validate(System.Enum enumToValidate, string parameterName = ""value"")
        {
            // This will be filled in by the generator once you call EnumValidator.Validate()
        }
    }
}
";
        private const string ReportErrorMethod = @"
        private static void ReportEnumValidationError(string parameterName, int value, System.Type enumType)
        {
            throw new System.ComponentModel.InvalidEnumArgumentException(parameterName, value, enumType);
        }";

        public void Initialize(GeneratorInitializationContext context)
        {
            context.RegisterForSyntaxNotifications(() => new SyntaxReceiver());
        }

        public void Execute(GeneratorExecutionContext context)
        {
            if (context.SyntaxReceiver is not SyntaxReceiver syntaxReceiver)
            {
                throw new InvalidOperationException("We were given the wrong syntax receiver.");
            }

            List<EnumValidationInfo> enumsToValidate = GetEnumValidationInfo(context.Compilation, syntaxReceiver.ArgumentsToValidate, context.CancellationToken).ToList();

            if (context.CancellationToken.IsCancellationRequested)
            {
                return;
            }

            if (enumsToValidate.Count > 0)
            {
                var sb = new StringBuilder();
                GenerateValidator(context, sb, enumsToValidate);

                if (context.CancellationToken.IsCancellationRequested)
                {
                    return;
                }

                context.AddSource("Validation.cs", sb.ToString());
            }

            // Always generate an Enum overload so people o
            context.AddSource("BaseValidator.cs", EnumValidatorStub);
        }

        private static void GenerateValidator(GeneratorExecutionContext context, StringBuilder sb, IEnumerable<EnumValidationInfo> infos)
        {
            const string indent = "        ";

            sb.AppendLine(
@"// <auto-generated />
namespace SourceGenerated
{
    internal static partial class EnumValidator
    {");

            foreach (EnumValidationInfo info in infos)
            {
                if (context.CancellationToken.IsCancellationRequested)
                {
                    return;
                }

                sb.AppendLine($"{indent}/// <summary>Validates that the enum value passed in is valid for the enum type.</summary>");
                sb.AppendLine($"{indent}public static void Validate({info.EnumType} enumToValidate, string parameterName = \"value\")");
                sb.AppendLine($"{indent}{{");

                GenerateValidateMethodBody(context, sb, info, indent + "    ");

                sb.AppendLine($"{indent}}}");
                sb.AppendLine();
            }

            sb.AppendLine(ReportErrorMethod);
            sb.AppendLine(@"    }
}");
        }

        private static void GenerateValidateMethodBody(GeneratorExecutionContext context, StringBuilder sb, EnumValidationInfo info, string indent)
        {
            sb.AppendLine($"{indent}int intValue = (int)enumToValidate;");
            if (info.IsFlags)
            {
                GenerateFlagsValidationMethodBody(context, sb, info, indent);
            }
            else
            {
                GenerateSequenceValidationMethodBody(context, sb, info, indent);
            }

            sb.AppendLine($"{indent}ReportEnumValidationError(parameterName, intValue, typeof({info.EnumType}));");
        }

        private static void GenerateFlagsValidationMethodBody(GeneratorExecutionContext context, StringBuilder sb, EnumValidationInfo info, string indent)
        {
            int total = 0;
            foreach (int value in info.Values)
            {
                total |= value;
            }

            sb.AppendLine($"{indent}if ((intValue & {total}) == intValue) return;");
        }

        private static void GenerateSequenceValidationMethodBody(GeneratorExecutionContext context, StringBuilder sb, EnumValidationInfo info, string indent)
        {
            foreach ((int min, int max) in GetElementSets(context, info.Values))
            {
                if (min == max)
                {
                    sb.AppendLine($"{indent}if (intValue == {min}) return;");
                }
                else
                {
                    sb.AppendLine($"{indent}if (intValue >= {min} && intValue <= {max}) return;");
                }
            }
        }

        private static IEnumerable<(int min, int max)> GetElementSets(GeneratorExecutionContext context, List<int> values)
        {
            int min = 0;
            int? max = null;
            foreach (int value in values)
            {
                if (max is null || value != max + 1)
                {
                    if (max != null)
                    {
                        yield return (min, max.Value);
                    }

                    min = value;
                    max = value;
                }
                else
                {
                    max = value;
                }
            }

            if (max is null)
            {
                context.ReportDiagnostic(Diagnostic.Create("EV1", nameof(EnumValidationGenerator), $"Can't validate an enum that has no elements", DiagnosticSeverity.Error, DiagnosticSeverity.Error, true, 4));
                yield break;
            }

            yield return (min, max.Value);
        }

        private static IEnumerable<EnumValidationInfo> GetEnumValidationInfo(Compilation compilation, List<SyntaxNode> argumentsToValidate, CancellationToken cancellationToken)
        {
            // The compiler doesn't necessarily cache semantic models for a single syntax tree
            // so we will do that here, ensuring we only do the expensive work once per tree.
            // We can't cache this at a higher level because generator lifetime is not to be relied on.
            var semanticModelCache = new Dictionary<SyntaxTree, SemanticModel>();

            INamedTypeSymbol? flagsAttributeType = compilation.GetTypeByMetadataName("System.FlagsAttribute");

#pragma warning disable RS1024 // Compare symbols correctly, see https://github.com/dotnet/roslyn-analyzers/issues/3427 and https://github.com/dotnet/roslyn-analyzers/issues/4539
            var foundTypes = new HashSet<ITypeSymbol>(SymbolEqualityComparer.Default);
#pragma warning restore RS1024 // Compare symbols correctly

            foreach (SyntaxNode argument in argumentsToValidate)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    yield break;
                }

                SemanticModel semanticModel = GetSemanticModel(compilation, argument.SyntaxTree);

                ITypeSymbol? enumType = semanticModel.GetTypeInfo(argument, cancellationToken).Type;
                if (enumType is null || foundTypes.Contains(enumType))
                {
                    continue;
                }

                foundTypes.Add(enumType);

                var isFlags = enumType.GetAttributes().Any(a => SymbolEqualityComparer.Default.Equals(a.AttributeClass, flagsAttributeType));

                var info = new EnumValidationInfo(enumType, isFlags);

                yield return info;
            }

            SemanticModel GetSemanticModel(Compilation compilation, SyntaxTree syntaxTree)
            {
                if (!semanticModelCache.TryGetValue(syntaxTree, out SemanticModel model))
                {
                    model = compilation.GetSemanticModel(syntaxTree);
                    semanticModelCache.Add(syntaxTree, model);
                }

                return model;
            }
        }
    }
}