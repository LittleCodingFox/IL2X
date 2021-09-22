﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using Mono.Cecil;
using IL2X.Core.Jit;

namespace IL2X.Core
{
	public sealed class Solution : IDisposable
	{
		public enum Type
		{
			Executable,
			Library
		}

		public readonly Type type;
		public readonly string dllPath, dllFolderPath;

		public Assembly mainAssembly, coreAssembly;
		public List<Assembly> assemblies;

		public AssemblyJit mainAssemblyJit, coreAssemblyJit;
		public List<AssemblyJit> assemblyJits;

		public Solution(Type type, string dllPath)
		{
			this.type = type;
			this.dllPath = dllPath;
			dllFolderPath = Path.GetDirectoryName(dllPath);
			string ext = Path.GetExtension(dllPath);
			if (ext != ".dll") throw new NotSupportedException("File must be '.dll'");
		}

		public void Dispose()
		{
			if (assemblies != null)
			{
				foreach (var assembly in assemblies) assembly.Dispose();
				assemblies = null;
			}
		}

		internal Assembly AddAssembly(string binaryPath)
		{
			var assembly = new Assembly(this, binaryPath);
			assemblies.Add(assembly);
			return assembly;
		}

		public void ReLoad()
		{
			assemblies = new List<Assembly>();
			using (var assemblyResolver = new DefaultAssemblyResolver())
			{
				assemblyResolver.AddSearchDirectory(dllFolderPath);
				mainAssembly = AddAssembly(dllPath);
				mainAssembly.Load(assemblyResolver);
			}
		}

		public void Jit()
		{
			assemblyJits = new List<AssemblyJit>();
			mainAssemblyJit = new AssemblyJit(this, mainAssembly);
		}

		public void Optimize()
		{
			if (!mainAssemblyJit.optimized) mainAssemblyJit.Optimize();
		}

		public TypeJit FindJitTypeRecursive(TypeDefinition type)
		{
			return mainAssemblyJit.FindJitTypeRecursive(type);
		}

		public FieldJit FindJitFieldRecursive(FieldDefinition field)
		{
			return mainAssemblyJit.FindJitFieldRecursive(field);
		}
	}
}
