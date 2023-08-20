using Mono.Cecil;
using Mono.Cecil.Cil;
using System.Collections.Generic;
using System.IO;
using System;
using System.Text;
using IL2X.Core.Jit;
using System.Linq;

namespace IL2X.Core.Emitters
{
    public sealed class Emiter_Cpp : Emitter
    {
        private StreamWriter activeWriter;
        private string writerTab = string.Empty;
        private void AddTab() => writerTab = "\t" + writerTab;
        private void RemoveTab() => writerTab = writerTab.Substring(1, writerTab.Length - 1);
        private void Write(string value) => activeWriter.Write(value);
        private void WriteTab(string value) => activeWriter.Write(writerTab + value);
        private void WriteLine(string value) => activeWriter.WriteLine(value);
        private void WriteLineTab(string value) => activeWriter.WriteLine(writerTab + value);
        private void WriteLine() => activeWriter.WriteLine();

        private ModuleJit activeModule;
        private MethodDebugInformation activemethodDebugInfo;

        public Emiter_Cpp(Solution solution)
        : base(solution)
        {

        }

        protected override void TranslateModule(ModuleJit module, string outputDirectory)
        {
            activeModule = module;

            // write header forward-declare all types
            {
                using (var stream = new FileStream(Path.Combine(outputDirectory, "__ForwardDeclares.hpp"), FileMode.Create, FileAccess.Write, FileShare.Read))
                using (var writer = new StreamWriter(stream))
                {
                    activeWriter = writer;

                    // write standard header
                    WriteLine("#pragma once");

                    // write normal type declare
                    WriteLine();
                    WriteLine("/* === Normal Types === */");
                    foreach (var type in module.allTypes)
                    {
                        if (type.typeDefinition.IsEnum) continue;
                        if (GetNativeTypeAttributeInfo(NativeTarget.C, type.typeDefinition, out _, out _)) continue;

                        string typename = GetTypeFullName(type.typeReference);
                        WriteLine($"class {typename};");
                    }

                    // write native type declare
                    bool nativeTypesExist = false;
                    var nativeDefTypesSet = new HashSet<string>();
                    var nativeHeadersSet = new HashSet<string>
                    {
                        "string",
                    };

                    foreach (var type in module.allTypes)
                    {
                        if (GetNativeTypeAttributeInfo(NativeTarget.C, type.typeDefinition, out string nativeType, out var nativeHeaders) &&
                            type.typeReference.IsPinned == false)
                        {
                            nativeTypesExist = true;
                            string typename = GetTypeFullName(type.typeReference);
                            nativeDefTypesSet.Add($"#define {typename} {nativeType}");
                            foreach (string header in nativeHeaders) nativeHeadersSet.Add(header);
                        }
                    }

                    if (nativeTypesExist)
                    {
                        WriteLine();
                        WriteLine("/* === Native Types === */");

                        foreach (string header in nativeHeadersSet)
                        {
                            WriteLine($"#include <{header}>");
                        }

                        foreach (string typeDef in nativeDefTypesSet)
                        {
                            WriteLine(typeDef);
                        }
                    }

                    // write enum type declare
                    if (module.enumTypes.Count != 0)
                    {
                        WriteLine();
                        WriteLine("/* === Enums === */");
                        foreach (var type in module.enumTypes)
                        {
                            string typename = GetTypeFullName(type.typeReference);
                            var field = type.typeDefinition.Fields[0];

                            WriteLine($"enum class {typename} : {GetTypeFullName(field.FieldType)}");
                            WriteLine("{");

                            AddTab();

                            for(var i = 1; i < type.typeDefinition.Fields.Count; i++)
                            {
                                var t = type.typeDefinition.Fields[i];

                                if(t.HasConstant)
                                {
                                    WriteTab($"{t.Name} = {t.Constant}");
                                }
                                else
                                {
                                    WriteTab($"{t.Name}");
                                }

                                if(i + 1 < type.typeDefinition.Fields.Count)
                                {
                                    WriteLine(",");
                                }
                                else
                                {
                                    WriteLine();
                                }
                            }

                            RemoveTab();

                            WriteLine("};");

                            if(module.enumTypes.IndexOf(type) < module.enumTypes.Count - 1)
                            {
                                WriteLine();
                            }
                        }
                    }

                    // write runtime-type-base if core-lib
                    if (module.assembly.assembly.isCoreLib)
                    {
                        WriteLine();
                        WriteLine("/* === RuntimeTypeBase === */");
                        WriteLine("class IL2X_RuntimeTypeBase");
                        WriteLine("{");
                        AddTab();
                        WriteLineTab($"virtual ~IL2X_RuntimeTypeBase() {{}};");
                        WriteLineTab($"{GetTypeFullName(typeJit.typeReference)}* Type;");
                        WriteLineTab("virtual std::string Name() { return \"RuntimeTypeBase\"; }");
                        WriteLineTab("virtual std::string FullName() { return \"IL2X.RuntimeTypeBase\"; }");
                        RemoveTab();
                        WriteLine("};");
                    }
                }
            }

            // write header type-def-field file
            foreach (var type in module.allTypes)
            {
                string filename = FormatTypeFilename(type.typeReference.FullName);
                using (var stream = new FileStream(Path.Combine(outputDirectory, filename + ".hpp"), FileMode.Create, FileAccess.Write, FileShare.Read))
                using (var writer = new StreamWriter(stream))
                {
                    activeWriter = writer;

                    // write standard header
                    WriteLine("#pragma once");
                    WriteLine("#include \"__ForwardDeclares.hpp\"");

                    // write type definition
                    WriteTypeDefinition(type);
                }
            }

            // write code file
            foreach (var type in module.allTypes)
            {
                string filename = FormatTypeFilename(type.typeReference.FullName);
                using (var stream = new FileStream(Path.Combine(outputDirectory, filename + ".cpp"), FileMode.Create, FileAccess.Write, FileShare.Read))
                using (var writer = new StreamWriter(stream))
                {
                    activeWriter = writer;

                    // write type field metadata
                    WriteLine($"#include \"{filename}.hpp\"");
                    IncludeSTD(type);

                    // write type method metadata
                    WriteTypeMethodImplementation(type);
                }
            }
        }

        private void IncludeSTD(TypeJit type)
        {
            foreach (var method in type.methods)
            {
                if (method.asmOperations == null) continue;

                foreach (var op in method.asmOperations)
                {
                    if (op.code == ASMCode.InitObject)
                    {
                        WriteLine("#include <cstdio>");
                        WriteLine("#include <string>");
                        break;
                    }
                }
            }
        }

        private void WriteRuntimeType(TypeJit type)
        {
            string typename = GetRuntimeTypeFullName(type.typeReference);
            Write($"class {typename}");

            var derived = new List<string>();

            if(type.typeDefinition.HasInterfaces)
            {
                foreach(var i in type.typeDefinition.Interfaces)
                {
                    derived.Add(GetTypeFullName(i.InterfaceType));
                }
            }

            if(type.typeDefinition.BaseType != null)
            {
                derived.Add(GetTypeFullName(type.typeDefinition.BaseType));
            }

            if(derived.Count > 0)
            {
                WriteLine($" : {string.Join(", ", derived)}");
            }
            else
            {
                WriteLine();
            }

            WriteLine("{");
            WriteLine("public:");
            AddTab();
            WriteLineTab("IL2X_RuntimeTypeBase RuntimeTypeBase;");// TODO: write special value-type that contains BaseClass, Name & Fullname
            RemoveTab();
            WriteLine("};");
        }

        private void WriteTypeDefinition(TypeJit type)
        {
            // include value-type dependencies
            foreach (var d in type.dependencies)
            {
                if (d.IsValueType || type.typeDefinition.IsEnum || type.typeDefinition.IsClass)
                {
                    char s = Path.DirectorySeparatorChar;
                    if (d.Scope != type.typeReference.Scope) WriteLine($"#include \"..{s}{GetScopeName(d.Scope)}{s}{FormatTypeFilename(d.FullName)}.hpp\"");
                    else WriteLine($"#include \"{FormatTypeFilename(d.FullName)}.hpp\"");
                }
            }

            // write type
            WriteLine();

            string typename = GetTypeFullName(type.typeReference);

            if (GetNativeTypeAttributeInfo(NativeTarget.C, type.typeDefinition, out _, out _))
            {
                WriteLine("/* Defined in '__ForwardDeclared.hpp' */");
            }
            else if (type.typeDefinition.IsEnum)
            {
                WriteLine("/* Defined in '__ForwardDeclared.hpp' */");
            }
            else
            {
                if (type.fields.Count != 0 || !type.isValueType)
                {
                    if(type.isValueType)
                    {
                        WriteLine($"struct {typename}");
                        WriteLine("{");
                    }
                    else
                    {
                        WriteLine($"class {typename}");

                        var derived = new List<string>();

                        if (type.typeDefinition.HasInterfaces)
                        {
                            foreach (var i in type.typeDefinition.Interfaces)
                            {
                                derived.Add(GetTypeFullName(i.InterfaceType));
                            }
                        }

                        if (type.typeDefinition.BaseType != null)
                        {
                            derived.Add(GetTypeFullName(type.typeDefinition.BaseType));
                        }

                        if (derived.Count > 0)
                        {
                            WriteLine($" : {string.Join(", ", derived)}");
                        }
                        else
                        {
                            WriteLine();
                        }

                        WriteLine("{");
                        WriteLine("public:");
                    }
                    AddTab();
                    if (!type.isValueType) WriteLineTab("void* RuntimeType;");
                    WriteTypeNonStaticFieldDefinition(type);

                    // write method signatures
                    WriteLine();

                    var publicMethods = type.methods.Where(x => x.methodDefinition.IsPublic).ToList();
                    var privateMethods = type.methods.Where(x => x.methodDefinition.IsPrivate).ToList();
                    var protectedMethods = type.methods.Where(x => x.methodDefinition.Attributes.HasFlag(MethodAttributes.Family) &&
                        x.methodDefinition.IsPrivate == false &&
                        x.methodDefinition.IsPublic == false).ToList();

                    void Handle(List<MethodJit> methods)
                    {
                        foreach (var method in methods)
                        {
                            WriteMethodDeclarationSignature(method);
                            WriteLine(";");
                        }
                    }

                    if(publicMethods.Count != 0)
                    {
                        WriteLine("public:");

                        Handle(publicMethods);
                    }

                    if (privateMethods.Count != 0)
                    {
                        WriteLine("private:");

                        Handle(privateMethods);
                    }

                    if (protectedMethods.Count != 0)
                    {
                        WriteLine("protected:");

                        Handle(protectedMethods);
                    }

                    RemoveTab();
                    Write("};");
                }
                else
                {
                    WriteLine($"#define {typename} void");
                }

                WriteLine();
                WriteTypeStaticFieldDefinition(type);
            }
        }

        private void WriteTypeNonStaticFieldDefinition(TypeJit type)
        {
            var publicFields = type.fields.Where(x => x.field.IsPublic).ToList();
            var privateFields = type.fields.Where(x => x.field.IsPrivate).ToList();
            var protectedFields = type.fields.Where(x => x.field.Attributes.HasFlag(FieldAttributes.Family) &&
                x.field.IsPrivate == false &&
                x.field.IsPublic == false).ToList();

            void Handle(List<FieldJit> fields)
            {
                foreach (var field in fields)
                {
                    if (field.field.IsStatic)
                    {
                        continue;
                    }

                    WriteTab(GetTypeReferenceName(field.resolvedFieldType));

                    WriteLine($" {GetFieldName(field.field)};");
                }
            }

            Handle(publicFields);

            if(privateFields.Count != 0)
            {
                WriteLine("private:");

                Handle(privateFields);
            }

            if (protectedFields.Count != 0)
            {
                WriteLine("protected:");

                Handle(protectedFields);
            }
        }

        private void WriteTypeStaticFieldDefinition(TypeJit type)
        {
            foreach (var field in type.fields)
            {
                if (!field.field.IsStatic) continue;
                WriteLineTab($"{GetTypeReferenceName(field.resolvedFieldType)} {GetFieldFullName(field.field)};");
            }
        }

        private void WriteTypeMethodDefinition(TypeJit type)
        {
            // include all dependencies
            foreach (var d in type.dependencies)
            {
                char s = Path.DirectorySeparatorChar;
                if (d.Scope != type.typeReference.Scope) WriteLine($"#include \"..{s}{GetScopeName(d.Scope)}{s}{FormatTypeFilename(d.FullName)}.hpp\"");
                else WriteLine($"#include \"{FormatTypeFilename(d.FullName)}.hpp\"");
            }
        }

        private void WriteMethodDeclarationSignature(MethodJit method)
        {
            if(method.methodDefinition.IsConstructor == false)
            {
                WriteTab(GetTypeReferenceName(method.methodReference.ReturnType));
                Write(" ");
                Write(GetMethodDeclarationName(method.methodReference));
            }
            else
            {
                WriteTab(GetTypeFullName(method.methodReference.DeclaringType));
            }

            Write("(");
            WriteMethodParameters(method);
            Write(")");
        }

        private void WriteMethodSignature(MethodJit method)
        {
            if(method.methodDefinition.IsConstructor == false)
            {
                Write(GetTypeReferenceName(method.methodReference.ReturnType));
                Write(" ");
                Write(GetMethodName(method.methodReference));
            }
            else
            {
                var name = GetTypeFullName(method.methodReference.DeclaringType);

                Write($"{name}::{name}");
            }

            Write("(");
            WriteMethodParameters(method);
            Write(")");
        }

        private void WriteMethodParameters(MethodJit method)
        {
            for (int i = 0; i != method.asmParameters.Count; ++i)
            {
                var p = method.asmParameters[i];
                Write(GetTypeReferenceName(p.parameter.ParameterType));
                Write(" ");
                Write(GetParameterName(p.parameter));
                if (i != method.asmParameters.Count - 1) Write(", ");
            }
        }

        private void WriteTypeMethodImplementation(TypeJit type)
        {
            foreach (var method in type.methods)
            {
                // load debug info if avaliable
                if (activeModule.module.symbolReader != null) activemethodDebugInfo = activeModule.module.symbolReader.Read(method.methodDefinition);

                // write method
                WriteLine();
                WriteMethodSignature(method);

                var constructors = new List<ASMCallMethod>();

                while(method.asmOperations != null && method.asmOperations.Count > 0)
                {
                    var first = method.asmOperations.FirstOrDefault();

                    if (first.code == ASMCode.CallMethod && first is ASMCallMethod callMethod && callMethod.method.Name.Contains(".ctor"))
                    {
                        constructors.Add(callMethod);

                        method.asmOperations.RemoveFirst();
                    }
                    else
                    {
                        break;
                    }
                }

                if(constructors.Count  > 0)
                {
                    WriteLine(" :");

                    for(var i = 0; i < constructors.Count; i++)
                    {
                        var constructor = constructors[i];

                        var result = new StringBuilder();

                        result.Append($"{GetTypeFullName(constructor.method.DeclaringType)}(");

                        int count = 0;

                        foreach (var p in constructor.parameters)
                        {
                            if(p is ASMThisPtr)
                            {
                                continue;
                            }

                            result.Append(GetOperationValue(p));
                            if (count != constructor.parameters.Count - 1) result.Append(", ");
                            ++count;
                        }

                        result.Append(")");

                        if(i + 1 < constructors.Count)
                        {
                            result.Append(",");
                        }

                        WriteTab(result.ToString());

                        WriteLine();
                    }
                }

                WriteLine();
                WriteLine("{");
                AddTab();

                if (method.asmOperations != null && method.asmOperations.Count != 0)
                {
                    WriteMethodLocals(method);
                    WriteMethodInstructions(method);
                }
                else
                {
                    // write implementation detail
                    if (method.methodDefinition.IsInternalCall) WriteMethodImplementationDetail(method);
                }

                RemoveTab();
                WriteLine("}");
            }
        }

        private void WriteMethodLocals(MethodJit method)
        {
            foreach (var local in method.asmLocals)
            {
                WriteTab($"{GetTypeReferenceName(local.type)} {GetLocalVariableName(local.variable)}");
                if (method.methodDefinition.Body.InitLocals) Write(" = {0}");
                WriteLine(";");
            }
        }

        private void WriteMethodInstructions(MethodJit method)
        {
            // write jit-generated locals
            foreach (var local in method.asmEvalLocals)
            {
                WriteLineTab($"{GetTypeReferenceName(local.type)} {GetLocalEvalVariableName(local.index)};");
            }

            // write instructions
            foreach (var op in method.asmOperations)
            {
                var resultLocal = op.GetResultLocal();
                var resultField = op.GetResultField();
                if (resultLocal != null && !IsVoidType(resultLocal.type))
                {
                    WriteTab($"{GetOperationValue(resultLocal)} = ");
                    Write(GetOperationValue(op));
                }
                else if (resultField != null)
                {
                    WriteTab($"{GetOperationValue(resultField)} = ");
                    Write(GetOperationValue(op));
                }
                else
                {
                    WriteTab(GetOperationValue(op));
                }
                WriteLine(";");
            }
        }

        private void WriteMethodImplementationDetail(MethodJit method)
        {
            if (method.methodDefinition.DeclaringType.FullName == "System.Object")
            {
                if (method.methodDefinition.FullName == "System.Type System.Object::GetType()")
                {
                    WriteLineTab("return ((IL2X_RuntimeTypeBase*)self->RuntimeType)->Type;");
                }
            }
        }

        private string GetOperationValue(ASMObject op)
        {
            switch (op.code)
            {
                // ===================================
                // variables
                // ===================================
                case ASMCode.Field:
                    {
                        var fieldOp = (ASMField)op;
                        string result = GetFieldName(fieldOp.field);
                        ASMObject accessorOp = op;
                        while (true)
                        {
                            var field = (ASMField)accessorOp;
                            if (field.self is ASMThisPtr)
                            {
                                return "this->" + result;
                            }
                            else if (field.self is ASMField f)
                            {
                                result = GetFieldName(f.field) + GetTypeReferenceMemberAccessor(f.field.FieldType) + result;
                                accessorOp = f;
                                continue;
                            }
                            else if (field.self is ParameterReference p)
                            {
                                result = GetParameterName(p) + GetTypeReferenceMemberAccessor(p.ParameterType) + result;
                            }
                            else if(field.self is VariableDefinition v)
                            {
                                result = GetLocalVariableName(v) + GetTypeReferenceMemberAccessor(v.VariableType) + result;
                            }
                            else
                            {
                                throw new Exception("Unsupported field accesor: " + field.self.ToString());
                            }

                            return result;
                        }
                    }

                case ASMCode.Local:
                    {
                        var local = (ASMLocal)op;
                        return GetLocalVariableName(local.variable);
                    }

                case ASMCode.EvalStackLocal:
                    {
                        var local = (ASMEvalStackLocal)op;
                        return GetLocalEvalVariableName(local.index);
                    }

                case ASMCode.ThisPtr:
                    {
                        return "this";
                    }

                case ASMCode.Parameter:
                    {
                        var parameter = (ASMParameter)op;
                        return GetParameterName(parameter.parameter);
                    }

                case ASMCode.PrimitiveLiteral:
                    {
                        var primitive = (ASMPrimitiveLiteral)op;

                        if (primitive.value == null)
                        {
                            return "NULL";
                        }

                        if (primitive.value is string stringValue)
                        {
                            return $"std::string(\"{stringValue}\")";
                        }

                        return primitive.value.ToString();
                    }

                case ASMCode.StringLiteral:
                    {
                        var primitive = (ASMStringLiteral)op;
                        return $"std::string(\"{primitive.value}\")";
                    }

                case ASMCode.Cast:
                    {
                        var cast = (ASMCast)op;

                        if(cast.castType.IsValueType)
                        {
                            return $"({GetTypeFullName(cast.castType)}){GetOperationValue(cast.value)}";
                        }
                        else
                        {
                            return $"dynamic_cast<{GetTypeFullName(cast.castType)}>({GetOperationValue(cast.value)})";
                        }
                    }

                case ASMCode.SizeOf:
                    {
                        var size = (ASMSizeOf)op;
                        return $"sizeof({GetTypeReferenceName(size.type)})";
                    }

                // ===================================
                // arithmatic
                // ===================================
                case ASMCode.Add:
                    {
                        var arithmaticOp = (ASMArithmatic)op;
                        return $"{GetOperationValue(arithmaticOp.value1)} + {GetOperationValue(arithmaticOp.value2)}";
                    }

                case ASMCode.Sub:
                    {
                        var arithmaticOp = (ASMArithmatic)op;
                        return $"{GetOperationValue(arithmaticOp.value1)} - {GetOperationValue(arithmaticOp.value2)}";
                    }

                case ASMCode.Mul:
                    {
                        var arithmaticOp = (ASMArithmatic)op;
                        return $"{GetOperationValue(arithmaticOp.value1)} * {GetOperationValue(arithmaticOp.value2)}";
                    }

                case ASMCode.Div:
                    {
                        var arithmaticOp = (ASMArithmatic)op;
                        return $"{GetOperationValue(arithmaticOp.value1)} / {GetOperationValue(arithmaticOp.value2)}";
                    }

                case ASMCode.BitwiseAnd:
                    {
                        var arithmaticOp = (ASMArithmatic)op;
                        return $"{GetOperationValue(arithmaticOp.value1)} & {GetOperationValue(arithmaticOp.value2)}";
                    }

                case ASMCode.BitwiseOr:
                    {
                        var arithmaticOp = (ASMArithmatic)op;
                        return $"{GetOperationValue(arithmaticOp.value1)} | {GetOperationValue(arithmaticOp.value2)}";
                    }

                case ASMCode.BitwiseXor:
                    {
                        var arithmaticOp = (ASMArithmatic)op;
                        return $"{GetOperationValue(arithmaticOp.value1)} ^ {GetOperationValue(arithmaticOp.value2)}";
                    }

                case ASMCode.BitwiseNot:
                    {
                        var arithmaticOp = (ASMArithmatic)op;
                        return $"~{GetOperationValue(arithmaticOp.value1)}";
                    }

                case ASMCode.ShiftLeft:
                    {
                        var arithmaticOp = (ASMArithmatic)op;
                        return $"{GetOperationValue(arithmaticOp.value1)} << {GetOperationValue(arithmaticOp.value2)}";
                    }

                //NOTE: this is an arithmetic shift right
                case ASMCode.ShiftRight:
                    {
                        var arithmaticOp = (ASMArithmatic)op;
                        return $"{GetOperationValue(arithmaticOp.value1)} >> {GetOperationValue(arithmaticOp.value2)}";
                    }

                //NOTE: this is a logical shift right
                case ASMCode.ShiftRightUnsigned:
                    {
                        var arithmaticOp = (ASMArithmatic)op;
                        return $"{GetOperationValue(arithmaticOp.value1)} >> {GetOperationValue(arithmaticOp.value2)}";
                    }

                // ===================================
                // stores
                // ===================================
                case ASMCode.WriteLocal:
                    {
                        var writeOp = (ASMWriteLocal)op;
                        return GetOperationValue(writeOp.value);
                    }

                case ASMCode.WriteField:
                    {
                        var writeOp = (ASMWriteField)op;
                        return GetOperationValue(writeOp.value);
                    }

                case ASMCode.InitObject:
                    {
                        var writeOp = (ASMInitObject)op;

                        if(writeOp.obj != null)
                        {
                            var o = GetOperationValue(writeOp.obj);

                            return $"IL2X_TypeSystem_Init({o})";
                        }
                        else if(writeOp.field != null)
                        {
                            return $"{GetTypeFullName(writeOp.field.DeclaringType)}::{GetFieldFullName(writeOp.field)} = 0";
                        }
                        else
                        {
                            throw new NotImplementedException($"InitObject requires either field or object");
                        }
                    }

                case ASMCode.LoadStaticField:
                    {
                        var loadOp = (ASMLoadStaticField)op;

                        return $"{GetTypeFullName(loadOp.field.DeclaringType)}::{GetFieldFullName(loadOp.field)}";
                    }

                case ASMCode.StoreStaticField:
                    {
                        var storeOp = (ASMStoreStaticField)op;

                        return $"{GetTypeFullName(storeOp.field.DeclaringType)}::{GetFieldFullName(storeOp.field)} = {GetOperationValue(storeOp.value)}";
                    }

                case ASMCode.Localloc:
                    {
                        var locallocOp = (ASMLocalloc)op;

                        return $"new uint8_t[{GetOperationValue(locallocOp.count)}]";
                    }

                case ASMCode.Store:
                    {
                        var storeOp = (ASMStore)op;

                        if(storeOp.parameter != null)
                        {
                            return $"{GetParameterName(storeOp.parameter)} = {GetOperationValue(storeOp.value)}";
                        }
                        else if(storeOp.evalLocal != null)
                        {
                            return $"{GetLocalEvalVariableName(storeOp.evalLocal.index)} = {GetOperationValue(storeOp.evalLocal)}";
                        }

                        throw new NotImplementedException("Unexpected value for Store");
                    }

                // ===================================
                // branching
                // ===================================
                case ASMCode.ReturnVoid:
                    {
                        return "return";
                    }

                case ASMCode.ReturnValue:
                    {
                        var returnOp = (ASMReturnValue)op;
                        return $"return {GetOperationValue(returnOp.value)}";
                    }

                case ASMCode.BranchMarker:
                    {
                        var branchOp = (ASMBranchMarker)op;
                        return $"{GetJumpIndexName(branchOp.asmIndex)}:";
                    }

                case ASMCode.Branch:
                    {
                        var branchOp = (ASMBranch)op;
                        return $"goto {GetJumpIndexName(branchOp.asmJumpToIndex)}";
                    }

                case ASMCode.BranchIfTrue:
                    {
                        var branchOp = (ASMBranchCondition)op;
                        return $"if ({GetOperationValue(branchOp.values[0])}) goto {GetJumpIndexName(branchOp.asmJumpToIndex)}";
                    }

                case ASMCode.BranchIfFalse:
                    {
                        var branchOp = (ASMBranchCondition)op;
                        return $"if (!{GetOperationValue(branchOp.values[0])}) goto {GetJumpIndexName(branchOp.asmJumpToIndex)}";
                    }

                case ASMCode.BranchIfEqual:
                    {
                        var branchOp = (ASMBranchCondition)op;
                        return $"if ({GetOperationValue(branchOp.values[0])} == {GetOperationValue(branchOp.values[1])}) goto {GetJumpIndexName(branchOp.asmJumpToIndex)}";
                    }

                case ASMCode.BranchIfNotEqual:
                    {
                        var branchOp = (ASMBranchCondition)op;
                        return $"if ({GetOperationValue(branchOp.values[0])} != {GetOperationValue(branchOp.values[1])}) goto {GetJumpIndexName(branchOp.asmJumpToIndex)}";
                    }

                case ASMCode.BranchIfGreater:
                    {
                        var branchOp = (ASMBranchCondition)op;
                        return $"if ({GetOperationValue(branchOp.values[0])} > {GetOperationValue(branchOp.values[1])}) goto {GetJumpIndexName(branchOp.asmJumpToIndex)}";
                    }

                case ASMCode.BranchIfLess:
                    {
                        var branchOp = (ASMBranchCondition)op;
                        return $"if ({GetOperationValue(branchOp.values[0])} < {GetOperationValue(branchOp.values[1])}) goto {GetJumpIndexName(branchOp.asmJumpToIndex)}";
                    }

                case ASMCode.BranchIfGreaterOrEqual:
                    {
                        var branchOp = (ASMBranchCondition)op;
                        return $"if ({GetOperationValue(branchOp.values[0])} >= {GetOperationValue(branchOp.values[1])}) goto {GetJumpIndexName(branchOp.asmJumpToIndex)}";
                    }

                case ASMCode.BranchIfLessOrEqual:
                    {
                        var branchOp = (ASMBranchCondition)op;
                        return $"if ({GetOperationValue(branchOp.values[0])} <= {GetOperationValue(branchOp.values[1])}) goto {GetJumpIndexName(branchOp.asmJumpToIndex)}";
                    }

                case ASMCode.CmpEqual_1_0:
                    {
                        var branchOp = (ASMCmp)op;
                        return $"({GetOperationValue(branchOp.value1)} == {GetOperationValue(branchOp.value2)}) ? 1 : 0";
                    }

                case ASMCode.CmpNotEqual_1_0:
                    {
                        var branchOp = (ASMCmp)op;
                        return $"({GetOperationValue(branchOp.value1)} != {GetOperationValue(branchOp.value2)}) ? 1 : 0";
                    }

                case ASMCode.CmpGreater_1_0:
                    {
                        var branchOp = (ASMCmp)op;
                        return $"({GetOperationValue(branchOp.value1)} > {GetOperationValue(branchOp.value2)}) ? 1 : 0";
                    }

                case ASMCode.CmpLess_1_0:
                    {
                        var branchOp = (ASMCmp)op;
                        return $"({GetOperationValue(branchOp.value1)} < {GetOperationValue(branchOp.value2)}) ? 1 : 0";
                    }

                // ===================================
                // instancing
                // ===================================
                case ASMCode.IsInst:
                    {
                        var instOp = (ASMIsInst)op;

                        //TODO: proper logic

                        var value = GetOperationValue(instOp.value);

                        if(instOp.typeReference != null)
                        {
                            return $"(dynamic_cast<{GetTypeFullName(instOp.typeReference)}>({value}))";
                        }
                        else
                        {
                            return $"(dynamic_cast<{GetTypeFullName(instOp.typeDefinition)}>({value}))";
                        }
                    }

                case ASMCode.NewObj:
                    {
                        var newObj = (ASMNewObj)op;

                        var result = new StringBuilder();

                        result.Append($"{GetTypeFullName(newObj.type)}(");

                        for(var i = 0; i < newObj.parameters.Count; i++)
                        {
                            result.Append(GetOperationValue(newObj.parameters[i]));

                            if (i + 1 < newObj.parameters.Count) result.Append(", ");
                        }

                        result.Append(")");

                        return result.ToString();
                    }

                // ===================================
                // invoke
                // ===================================
                case ASMCode.CallMethod:
                    {
                        var invokeOp = (ASMCallMethod)op;
                        var result = new StringBuilder();
                        result.Append($"{GetMethodName(invokeOp.method)}(");
                        int count = 0;
                        foreach (var p in invokeOp.parameters)
                        {
                            result.Append(GetOperationValue(p));
                            if (count != invokeOp.parameters.Count - 1) result.Append(", ");
                            ++count;
                        }
                        result.Append(")");
                        return result.ToString();
                    }

                case ASMCode.Throw:
                    {
                        var throwOp = (ASMThrow)op;

                        return $"throw {GetOperationValue(throwOp.exception)}";
                    }

                default: throw new NotImplementedException("Operation not implimented: " + op.code.ToString());
            }
        }

        private bool IsVoidType(TypeReference type)
        {
            return type.FullName == "System.Void";// TODO: don't just validate by name
        }

        public static string GetTypeName(TypeReference type)
        {
            string result = type.FullName.Replace('.', '_').Replace('`', '_').Replace('<', '_').Replace('>', '_').Replace('/', '_');
            if (type.IsArray) result = result.Replace("[]", "");
            if (type.IsGenericInstance) result = "g_" + result;
            return result;
        }

        public static string GetTypeFullName(TypeReference type)
        {
            return $"{GetScopeName(type.Scope)}_{GetTypeName(type)}";
        }

        private string GetTypeReferenceName(TypeReference type)
        {
            string result = GetTypeFullName(type);
            if (!IsVoidType(type))
            {
                while (!type.IsValueType)
                {
                    result += "*";
                    var lastType = type;
                    if (type.IsGenericInstance) break;
                    type = type.GetElementType();
                    if (type == lastType) break;
                }
            }
            return result;
        }

        public static string GetRuntimeTypeFullName(TypeReference type)
        {
            return GetTypeFullName(type);
        }

        private static string GetTypeReferenceMemberAccessor(TypeReference type)
        {
            if (type.IsPointer || type.IsByReference || !type.IsValueType) return "->";
            return ".";
        }

        private string GetLocalVariableName(VariableDefinition variable)
        {
            if (activemethodDebugInfo.TryGetName(variable, out string name)) return "l_" + name;
            //return "l_" + variable.GetHashCode().ToString();
            return "l_" + variable.Index.ToString();
        }

        private string GetLocalEvalVariableName(int index)
        {
            return "le_" + index.ToString();
        }

        private static string GetFieldName(FieldReference field)
        {
            return "f_" + field.Name.Replace('<', '_').Replace('>', '_');
        }

        private static string GetFieldFullName(FieldDefinition field)
        {
            return $"f_{GetScopeName(field.DeclaringType.Scope)}_{field.FullName}".Replace('.', '_').Replace(' ', '_').Replace("::", "_");
        }

        private static string GetParameterName(ParameterReference parameter)
        {
            return "p_" + parameter.Name;
        }

        private static string GetMethodDeclarationName(MethodReference method)
        {
            string name = method.Name.Replace('.', '_');
            if (method.IsGenericInstance)
            {
                var genericMethod = (GenericInstanceMethod)method;
                name += "_";
                for (int i = 0; i != genericMethod.GenericArguments.Count; ++i)
                {
                    name += GetTypeName(genericMethod.GenericArguments[i]);
                    if (i != genericMethod.GenericArguments.Count - 1) name += "_";
                }
                name += "_";
            }

            return $"{name}";
        }

        private static string GetMethodName(MethodReference method)
        {
            string name = method.Name.Replace('.', '_');
            if (method.IsGenericInstance)
            {
                var genericMethod = (GenericInstanceMethod)method;
                name += "_";
                for (int i = 0; i != genericMethod.GenericArguments.Count; ++i)
                {
                    name += GetTypeName(genericMethod.GenericArguments[i]);
                    if (i != genericMethod.GenericArguments.Count - 1) name += "_";
                }
                name += "_";
            }

            return $"{GetTypeFullName(method.DeclaringType)}::{name}";
        }

        private string GetJumpIndexName(int jumpIndex)
        {
            return "JMP_" + jumpIndex.ToString("X4");
        }
    }
}
