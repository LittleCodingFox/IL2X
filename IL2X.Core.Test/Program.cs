﻿using System;
using System.IO;

namespace IL2X.Core.Test
{
	class Program
	{
		static void Main(string[] args)
		{
			MonoCecilUtils.SetCustomCoreLibName("IL2X.Portable.CoreLib");
			
			const string testName = "TestApp.Portable";
			//const string testName = "IL2X.Portable.CoreLib";

			string binaryPathFolder = Path.Combine(Environment.CurrentDirectory, $@"..\..\..\..\{testName}\bin\Debug\netcoreapp2.2");
			string binaryPath = Path.Combine(binaryPathFolder, $"{testName}.dll");
			if (!File.Exists(binaryPath)) throw new Exception($"{testName} doesn't exist");

			string outputPath = $@"..\..\..\..\{testName}\bin\Debug\netcoreapp2.2\TestOutput";
			if (!Directory.Exists(outputPath)) Directory.CreateDirectory(outputPath);
			using (var translator = new ILAssemblyTranslator_C(binaryPath, true, Path.Combine(Environment.CurrentDirectory, $@"..\..\..\..\{testName}\bin\Debug\netcoreapp2.2")))
			{
				translator.Translate(outputPath);
			}
		}
	}
}
