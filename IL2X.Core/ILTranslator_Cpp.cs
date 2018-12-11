﻿using Mono.Cecil;
using System.IO;
using System;
using System.Collections.Generic;
using System.Linq;

namespace IL2X.Core
{
	public class ILTranslator_Cpp : ILTranslator
	{
		private StreamWriter writer;
		private Stack<string> referencesParsed;
		private readonly string precompiledHeader;

		private TypeReference activeType;

		public ILTranslator_Cpp(string binaryPath, string precompiledHeader = null)
		: base(binaryPath)
		{
			this.precompiledHeader = precompiledHeader;
		}

		public override void Translate(string outputPath, bool translateReferences)
		{
			referencesParsed = new Stack<string>();
			TranslateModule(assemblyDefinition, outputPath, translateReferences);
		}

		private void TranslateModule(AssemblyDefinition assemblyDefinition, string outputPath, bool translateReferences)
		{
			var module = assemblyDefinition.MainModule;

			// translate references
			if (translateReferences)
			{
				foreach (var reference in module.AssemblyReferences)
				{
					using (var refAssemblyDefinition = assemblyResolver.Resolve(reference))
					{
						if (referencesParsed.Contains(refAssemblyDefinition.FullName)) continue;
						TranslateModule(refAssemblyDefinition, outputPath, translateReferences);
						referencesParsed.Push(refAssemblyDefinition.FullName);
					}
				}
			}

			// translate assembly
			outputPath = Path.Combine(outputPath, assemblyDefinition.Name.Name.Replace('.', '_'));
			if (!Directory.Exists(outputPath)) Directory.CreateDirectory(outputPath);
			WriteAllTypesHeader(module, outputPath);
			foreach (var type in module.GetTypes()) WriteType(type, outputPath);
		}

		private string GetTypeDeclarationKeyword(TypeDefinition type)
		{
			if (type.IsEnum) return "enum";
			else if (type.IsInterface) return "class";
			else if (type.IsClass) return type.IsValueType ? "struct" : "class";
			else throw new Exception("Unsuported type kind: " + type.Name);
		}

		private void WriteAllTypesHeader(ModuleDefinition module, string outputPath)
		{
			using (var stream = new FileStream(Path.Combine(outputPath, "__ALL_TYPES.h"), FileMode.Create, FileAccess.Write))
			using (writer = new StreamWriter(stream))
			{
				writer.WriteLine("#pragma once");
				writer.WriteLine();
				foreach (var type in module.GetTypes())
				{
					if (type.Name == "<Module>") continue;
					
					int namespaceCount = WriteNamespaceStart(type, false);
					if (namespaceCount != 0) StreamWriterEx.AddTab();
					string typeKindKeyword = GetTypeDeclarationKeyword(type);
					writer.Write($"{typeKindKeyword} {GetNestedNameFlat(type)};");
					if (namespaceCount != 0) StreamWriterEx.RemoveTab();
					WriteNamespaceEnd(namespaceCount, true);
				}
			}
		}

		private void WriteType(TypeDefinition type, string outputPath)
		{
			if (type.Name == "<Module>") return;

			activeType = type;
			string filename = GetFullNameFlat(type, "_", "_");
			string filePath = Path.Combine(outputPath, filename);

			// write header
			using (var stream = new FileStream(filePath + ".h", FileMode.Create, FileAccess.Write))
			using (writer = new StreamWriter(stream))
			{
				writer.WriteLine("#pragma once");
				writer.WriteLine("#include \"__ALL_TYPES.h\";");
				writer.WriteLine();
				WriteTypeHeader(type);
			}

			// write source
			using (var stream = new FileStream(filePath + ".cpp", FileMode.Create, FileAccess.Write))
			using (writer = new StreamWriter(stream))
			{
				writer.WriteLine($"#include \"{precompiledHeader}\";");
				writer.WriteLine($"#include \"{filename}.h\";");
				writer.WriteLine();
				WriteTypeSource(type);
			}
		}

		private int WriteNamespaceStart(TypeDefinition type, bool returnLine)
		{
			if (!string.IsNullOrEmpty(type.Namespace))
			{
				var namespaces = type.Namespace.Split('.');
				for (int i = 0; i != namespaces.Length; ++i)
				{
					writer.Write($"namespace {namespaces[i]}");
					if (i != namespaces.Length - 1 || !returnLine) writer.Write('{');
					else writer.WriteLine(Environment.NewLine + '{');
				}
				return namespaces.Length;
			}
			else if (type.DeclaringType != null)
			{
				return WriteNamespaceStart(type.DeclaringType, returnLine);
			}

			return 0;
		}

		private void WriteNamespaceEnd(int namespaceCount, bool returnLine)
		{
			for (int i = 0; i != namespaceCount; ++i) writer.Write('}');
			if (returnLine) writer.WriteLine();
		}

		private void WriteTypeHeader(TypeDefinition type)
		{
			// write namespace
			int namespaceCount = WriteNamespaceStart(type, true);
			StreamWriterEx.AddTab();

			// write type body
			string typeKindKeyword = GetTypeDeclarationKeyword(type);
			writer.WriteLinePrefix($"{typeKindKeyword} {GetNestedNameFlat(type)}");
			writer.WriteLinePrefix('{');
			StreamWriterEx.AddTab();

			bool membersWritten = false;
			if (type.HasFields)
			{
				if (membersWritten) writer.WriteLine();
				membersWritten = true;
				writer.WriteLinePrefix("// FIELDS");
				foreach (var field in type.Fields) WriteField(field);
			}

			if (type.HasMethods)
			{
				if (membersWritten) writer.WriteLine();
				membersWritten = true;
				writer.WriteLinePrefix("// METHODS");
				foreach (var method in type.Methods) WriteMethod(method);
			}

			StreamWriterEx.RemoveTab();
			writer.WriteLinePrefix("};");

			// close namespace
			StreamWriterEx.RemoveTab();
			WriteNamespaceEnd(namespaceCount, true);
		}

		private void WriteTypeSource(TypeDefinition type)
		{
			// write namespace
			int namespaceCount = WriteNamespaceStart(type, true);

			// TODO

			// close namespace
			WriteNamespaceEnd(namespaceCount, true);
		}

		private void WriteField(FieldDefinition field)
		{
			if (field.Attributes.HasFlag(FieldAttributes.RTSpecialName)) return;
			string accessModifier = (field.IsPublic || field.IsAssembly) ? "public:" : "private:";
			writer.WriteLinePrefix($"{accessModifier} {GetFullNameFlat(field.FieldType)} {field.Name};");
		}

		private void WriteMethod(MethodDefinition method)
		{
			string accessModifier = (method.IsPublic || method.IsAssembly) ? "public:" : "private:";
			string name = method.IsConstructor ? method.DeclaringType.Name : method.Name;
			writer.WritePrefix($"{accessModifier} {GetFullNameFlat(method.ReturnType)} {name}(");
			var lastParameter = method.Parameters.LastOrDefault();
			foreach (var parameter in method.Parameters)
			{
				writer.Write($"{GetFullNameFlat(parameter.ParameterType)} {parameter.Name}");
				if (parameter != lastParameter) writer.Write(", ");
			}
			writer.WriteLine(");");
		}

		private string GetFullNameFlat(TypeReference type)
		{
			if (activeType.Namespace == type.Namespace || activeType == type.DeclaringType) return GetNestedNameFlat(type, "_");// remove verbosity if possible
			return GetFullNameFlat(type, "::", "_");
		}

		private string GetNestedNameFlat(TypeReference type)
		{
			return GetNestedNameFlat(type, "_");
		}
	}
}
