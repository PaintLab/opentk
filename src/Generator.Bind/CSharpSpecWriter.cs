//
// The Open Toolkit Library License
//
// Copyright (c) 2006 - 2010 the Open Toolkit library.
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights to
// use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of
// the Software, and to permit persons to whom the Software is furnished to do
// so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in all
// copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
// EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES
// OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
// NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT
// HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
// WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
// FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR
// OTHER DEALINGS IN THE SOFTWARE.
//

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using Bind.Structures;

namespace Bind
{
    using Delegate = Bind.Structures.Delegate;
    using Enum = Bind.Structures.Enum;
    using Type = Bind.Structures.Type;

    static class CodeGenSetting
    {
        public const string GLDelegateClass = "Delegates";
    }

    internal sealed class CSharpSpecWriter
    {
        private IBind Generator { get; set; }
        private Settings Settings { get { return Generator.Settings; } }

        public void WriteBindings(IBind generator)
        {
            Generator = generator;
            WriteBindings(generator.Delegates, generator.Wrappers, generator.Enums);
        }

        private static void ConsoleRewrite(string text)
        {
            int left = Console.CursorLeft;
            int top = Console.CursorTop;
            Console.Write(text);
            for (int i = text.Length; i < 80; i++)
            {
                Console.Write(" ");
            }
            Console.WriteLine();
            Console.SetCursorPosition(left, top);
        }

        private void WriteBindings(DelegateCollection delegates, FunctionCollection wrappers, EnumCollection enums)
        {
            Console.WriteLine("Writing bindings to {0}", Settings.OutputPath);
            if (!Directory.Exists(Settings.OutputPath))
            {
                Directory.CreateDirectory(Settings.OutputPath);
            }

            string temp_enums_file = Path.GetTempFileName();
            string temp_wrappers_file = Path.GetTempFileName();

            // Enums
            using (BindStreamWriter sw = new BindStreamWriter(temp_enums_file))
            {
                sw.WriteLine("//autogen " + DateTime.Now.ToString("u"));

                WriteLicense(sw);

                sw.WriteLine("using System;");
                sw.WriteLine();

                if ((Settings.Compatibility & Settings.Legacy.NestedEnums) != Settings.Legacy.None)
                {
                    sw.WriteLine("namespace {0}", Settings.OutputNamespace);
                    sw.WriteLine("{");
                    sw.Indent();
                    sw.WriteLine("static partial class {0}", Settings.OutputClass);
                }
                else
                {
                    sw.WriteLine("namespace {0}", Settings.EnumsOutput);
                }

                sw.WriteLine("{");

                sw.Indent();
                WriteEnums(sw, enums, wrappers);
                sw.Unindent();

                if ((Settings.Compatibility & Settings.Legacy.NestedEnums) != Settings.Legacy.None)
                {
                    sw.WriteLine("}");
                    sw.Unindent();
                }

                sw.WriteLine("}");
            }

            // Wrappers
            using (BindStreamWriter sw = new BindStreamWriter(temp_wrappers_file))
            {
                WriteLicense(sw);
                sw.WriteLine("namespace {0}", Settings.OutputNamespace);
                sw.WriteLine("{");
                sw.Indent();

                sw.WriteLine("using System;");
                sw.WriteLine("using System.Text;");
                sw.WriteLine("using System.Runtime.InteropServices;");

                WriteWrappers(sw, wrappers, delegates, enums, Generator.CSTypes);

                sw.Unindent();
                sw.WriteLine("}");
            }

            string output_enums = Path.Combine(Settings.OutputPath, Settings.EnumsFile);
            string output_delegates = Path.Combine(Settings.OutputPath, Settings.DelegatesFile);
            string output_core = Path.Combine(Settings.OutputPath, Settings.ImportsFile);
            string output_wrappers = Path.Combine(Settings.OutputPath, Settings.WrappersFile);

            if (File.Exists(output_enums))
            {
                File.Delete(output_enums);
            }
            if (File.Exists(output_delegates))
            {
                File.Delete(output_delegates);
            }
            if (File.Exists(output_core))
            {
                File.Delete(output_core);
            }
            if (File.Exists(output_wrappers))
            {
                File.Delete(output_wrappers);
            }

            File.Move(temp_enums_file, output_enums);
            File.Move(temp_wrappers_file, output_wrappers);
            //COPY
            string es30EnumFile = @"src\MiniOpenTK\Graphics\ES30\ES30Enum.cs";
            File.Copy(output_enums, es30EnumFile, true);
        }

        private void WriteWrappers(BindStreamWriter sw, FunctionCollection wrappers,
            DelegateCollection delegates, EnumCollection enums,
            IDictionary<string, string> CSTypes)
        {
            Trace.WriteLine(String.Format("Writing wrappers to:\t{0}.{1}", Settings.OutputNamespace, Settings.OutputClass));

            sw.WriteLine("#pragma warning disable 3019"); // CLSCompliant attribute
            sw.WriteLine("#pragma warning disable 1591"); // Missing doc comments
            sw.WriteLine("#pragma warning disable 1572"); // Wrong param comments
            sw.WriteLine("#pragma warning disable 1573"); // Missing param comments
            sw.WriteLine("#pragma warning disable 626"); // extern method without DllImport

            sw.WriteLine();
            sw.WriteLine("partial class {0}", Settings.OutputClass);
            sw.WriteLine("{");
            sw.Indent();

            // Write constructor
            sw.WriteLine("static {0}()", Settings.OutputClass);
            sw.WriteLine("{");
            sw.Indent();
            // Write entry point names.
            // Instead of strings, which are costly to construct,
            // we use a 1d array of ASCII bytes. Names are laid out
            // sequentially, with a nul-terminator between them.
            sw.WriteLine("EntryPointNames = new byte[]", delegates.Count);
            sw.WriteLine("{");
            sw.Indent();
            foreach (var d in delegates.Values.Select(d => d.First()))
            {
                if (d.RequiresSlot(Settings))
                {
                    var name = Settings.FunctionPrefix + d.Name;
                    sw.WriteLine("{0}, 0,", String.Join(", ",
                        System.Text.Encoding.ASCII.GetBytes(name).Select(b => b.ToString()).ToArray()));
                }
            }
            sw.Unindent();
            sw.WriteLine("};");
            // Write entry point name offsets.
            // This is an array of offsets into the EntryPointNames[] array above.
            sw.WriteLine("EntryPointNameOffsets = new int[]", delegates.Count);
            sw.WriteLine("{");
            sw.Indent();
            int offset = 0;
            foreach (var d in delegates.Values.Select(d => d.First()))
            {
                if (d.RequiresSlot(Settings))
                {
                    sw.WriteLine("{0},", offset);
                    var name = Settings.FunctionPrefix + d.Name;
                    offset += name.Length + 1;
                }
            }
            sw.Unindent();
            sw.WriteLine("};");
            sw.WriteLine("EntryPoints = new IntPtr[EntryPointNameOffsets.Length];");
            sw.Unindent();
            sw.WriteLine("}");
            sw.WriteLine();


            string glClassFile = @"src\MiniOpenTK\Graphics\ES30\ES30.cs";
            BindStreamWriter sw2 = new BindStreamWriter(glClassFile);
            {
                sw2.WriteLine("//autogen " + DateTime.Now.ToString("u"));
                sw2.WriteLine("namespace OpenTK.Graphics.ES30{");
                sw2.WriteLine(" using System;");
                sw2.WriteLine(" using System.Text;");
                sw2.WriteLine(" using System.Runtime.InteropServices; ");
                //
                sw2.WriteLine("  public partial class GL{");
                sw2.WriteLine("  public void LoadAll(){");
                sw2.WriteLine("     GLDelInit.LoadAll();");
                sw2.WriteLine("}");
            }


            int current_wrapper = 0;
            foreach (string key in wrappers.Keys)
            {
                if (((Settings.Compatibility & Settings.Legacy.NoSeparateFunctionNamespaces) == Settings.Legacy.None) && key != "Core")
                {
                    if (!Char.IsDigit(key[0]))
                    {
                        sw.WriteLine("public static partial class {0}", key);
                        sw2.WriteLine("public static partial class {0}", key);
                    }
                    else
                    {
                        // Identifiers cannot start with a number:
                        sw.WriteLine("public static partial class {0}{1}", Settings.ConstantPrefix, key);
                        sw2.WriteLine("public static partial class {0}{1}", Settings.ConstantPrefix, key);
                    }
                    sw.WriteLine("{");
                    sw.Indent();
                    //
                    sw2.WriteLine("{");
                    sw2.Indent();
                }

                wrappers[key].Sort();

                foreach (Function f in wrappers[key])
                {
                    WriteWrapper(sw, f, enums);
                    WriteWrapper2(sw2, f, enums);
                    current_wrapper++;
                }



                if (((Settings.Compatibility & Settings.Legacy.NoSeparateFunctionNamespaces) == Settings.Legacy.None) && key != "Core")
                {
                    sw.Unindent();
                    sw.WriteLine("}");
                    sw.WriteLine();
                    //
                    sw2.Unindent();
                    sw2.WriteLine("}");
                    sw2.WriteLine();
                }
            }

            {
                sw2.WriteLine("}"); //close GLES class
                sw2.WriteLine("}"); //close namespace
                sw2.Flush();
                sw2.Close();
            }

            // Emit native signatures.
            // These are required by the patcher.

            int current_signature = 0;




            List<Delegate> outputFuncs = new List<Delegate>();
            foreach (Delegate d in wrappers.Values.SelectMany(e => e).Select(w => w.WrappedDelegate).Distinct())
            {
                sw.WriteLine("[Slot({0})]", d.Slot);
                sw.WriteLine("[DllImport(Library, ExactSpelling = true, CallingConvention = CallingConvention.Winapi)]");
                sw.WriteLine("private static extern {0};", GetDeclarationString(d, false));
                outputFuncs.Add(d);
                current_signature++;
            }

            //---------
            WriteDelegatesAndDelegateSlots(outputFuncs);
            //---------


            sw.Unindent();
            sw.WriteLine("}");

            Console.WriteLine("Wrote {0} wrappers for {1} signatures", current_wrapper, current_signature);
        }

        //my extension
        void WriteDelegatesAndDelegateSlots(List<Delegate> outputFuncs)
        {

            using (MemoryStream ms = new MemoryStream())
            using (StreamWriter w = new StreamWriter(ms))
            {
                w.WriteLine("//autogen " + DateTime.Now.ToString("u"));
                w.WriteLine("namespace OpenTK.Graphics.ES30{");
                w.WriteLine(" using System;");
                w.WriteLine(" using System.Text;");
                w.WriteLine(" using System.Runtime.InteropServices; ");
                //my experiment

                //create delegate slot
                w.WriteLine();
                w.WriteLine("[System.Security.SuppressUnmanagedCodeSecurity()] //apply to all members");
                w.WriteLine($"static class {CodeGenSetting.GLDelegateClass} {{");

                int marker_count = 0;
                for (int i = 0; i < outputFuncs.Count; ++i)
                {
                    Delegate d = outputFuncs[i];
                    w.WriteLine();

                    ++marker_count;
                    w.WriteLine("//m* " + marker_count);
#if DEBUG
                    //if (marker_count == 8)
                    //{

                    //}
#endif
                    w.WriteLine();
                    w.WriteLine("[UnmanagedFunctionPointer(CallingConvention.Winapi, CharSet = CharSet.Ansi)]");
                    w.WriteLine($"public {GetDeclarationString2(d, true)};");

                    w.WriteLine($"public static {d.Name}  {Settings.FunctionPrefix + d.Name};");
                    w.WriteLine();
                }

                w.WriteLine("}"); //close class GLDelegateClass
                //------------

                //GLDelInit
                w.WriteLine("static class GLDelInit{");
                w.WriteLine("static void AssignDelegate<T>(out T del, string funcName){");
                /**
                 *      IntPtr funcPtr = PlatformAddressPortal.GetAddressDelegate(funcName);
                        del = (funcPtr == IntPtr.Zero) ?
                        default(T) :
                        (T)(object)(Marshal.GetDelegateForFunctionPointer(funcPtr, typeof(T)));
                  */

                w.WriteLine("IntPtr funcPtr = PlatformAddressPortal.GetAddressDelegate(funcName);");
                w.WriteLine(" del = (funcPtr == IntPtr.Zero) ? default(T) :  (T)(object)(Marshal.GetDelegateForFunctionPointer(funcPtr, typeof(T)));");
                w.WriteLine("}");

                w.WriteLine("public static void LoadAll(){");
                for (int i = 0; i < outputFuncs.Count; ++i)
                {
                    Delegate d = outputFuncs[i];
                    w.WriteLine("AssignDelegate(out " + CodeGenSetting.GLDelegateClass + "." + Settings.FunctionPrefix + d.Name + ",\"" + Settings.FunctionPrefix + d.Name + "\");");
                }
                w.WriteLine("}"); //close LoadAll()
                // 




                w.WriteLine("}"); //close class
                //------------
                w.WriteLine("}"); //namespace
                w.Flush();
                //
                string api1 = Encoding.UTF8.GetString(ms.ToArray());

                string curDir = System.IO.Directory.GetCurrentDirectory();
                File.WriteAllText(@"src\MiniOpenTK\Graphics\ES30\ES30Delegate.cs", api1);
                // 
            }
        }


        private void WriteWrapper(BindStreamWriter sw, Function f, EnumCollection enums)
        {
            if ((Settings.Compatibility & Settings.Legacy.NoDocumentation) == 0)
            {
                WriteDocumentation(sw, f);
            }
            WriteMethod(sw, f, enums);
            sw.WriteLine();
        }
        private void WriteWrapper2(BindStreamWriter sw, Function f, EnumCollection enums)
        {
            if ((Settings.Compatibility & Settings.Legacy.NoDocumentation) == 0)
            {
                WriteDocumentation(sw, f);
            }
            WriteMethod2(sw, f, enums);
            sw.WriteLine();
        }
        private void WriteMethod2(BindStreamWriter sw, Function f, EnumCollection enums)
        {

            if (!String.IsNullOrEmpty(f.Obsolete))
            {
                return; // TODO: add note that we 
                //sw.WriteLine("[Obsolete(\"{0}\")]", f.Obsolete);
            }
            else if (f.Deprecated && Settings.IsEnabled(Settings.Legacy.AddDeprecationWarnings))
            {
                return; //
                //sw.WriteLine("[Obsolete(\"Deprecated in OpenGL {0}\")]", f.DeprecatedVersion);
            }


            //--------------------------------------------------------------------------------------------

#if DEBUG
            _dbugWrite_Id++;
            if (_dbugWrite_Id == 332)
            {

            }

            sw.WriteLine("//x* " + _dbugWrite_Id);
#endif

            //we may not need this...
            sw.WriteLine("//[AutoGenerated(Category = \"{0}\", Version = \"{1}\", EntryPoint = \"{2}\")]",
                f.Category, f.Version, Settings.FunctionPrefix + f.WrappedDelegate.EntryPoint);

            //we may not need to gen [CLSCompliant(false)] if our asm dose not require it
            //if (!f.CLSCompliant)
            //{
            //    sw.WriteLine("[CLSCompliant(false)]");
            //}

            sw.WriteLine($"public static { GetDeclarationString(f, Settings.Compatibility)}");
            //body
            //-----
            //sw.WriteLine("//hello!");
            CreateBody(f, f.CLSCompliant, enums, Settings.FunctionPrefix, f.WrappedDelegate);
            sw.WriteLine(f.Body.ToString());
        }
#if DEBUG
        int _dbugWrite_Id;
#endif

        //-------------------------------------------------------------------
        readonly List<string> handle_statements = new List<string>();
        readonly List<string> handle_release_statements = new List<string>();
        readonly List<string> fixed_statements = new List<string>();
        readonly List<string> assign_statements = new List<string>();
        readonly List<string> stringOutAlloc_statements = new List<string>();
        readonly List<string> stringOut_set_statements = new List<string>();


        void CreateBody(Function func, bool wantCLSCompliance, EnumCollection enums, string funcPrefix, Delegate wrappedDel)
        {

            string FindBuffSizeVarName(Parameter[] inputPars, string varName, string expectVarName, bool retry = true)
            {
                for (int i = 0; i < inputPars.Length; ++i)
                {
                    if (inputPars[i].Name.ToLower() == expectVarName)
                    {
                        return inputPars[i].Name;
                    }
                }
                //temp fix
                if (!retry)
                {
                    return null;
                }
                return FindBuffSizeVarName(inputPars, varName, (varName + "Length").ToLower(), false);
            }


            Function f = new Function(func);
            f.Body.Clear();
            handle_statements.Clear();
            handle_release_statements.Clear();
            fixed_statements.Clear();
            assign_statements.Clear();

            //
            stringOutAlloc_statements.Clear();
            stringOut_set_statements.Clear();
            //
            // Obtain pointers by pinning the parameters

            // check parameter type is match or not



#if DEBUG
            if (func.Parameters.Count != wrappedDel.Parameters.Count)
            {

            }
#endif


            foreach (Parameter p in f.Parameters)
            {

                if (p.NeedsPin)
                {
                    if (p.WrapperType == WrapperTypes.GenericParameter)
                    {
                        // Use GCHandle to obtain pointer to generic parameters and 'fixed' for arrays.
                        // This is because fixed can only take the address of fields, not managed objects.
                        handle_statements.Add(String.Format(
                            "{0} {1}_ptr = {0}.Alloc({1}, GCHandleType.Pinned);",
                            "GCHandle", p.Name));

                        handle_release_statements.Add(String.Format("{0}_ptr.Free();", p.Name));

                        // Due to the GCHandle-style pinning (which boxes value types), we need to assign the modified
                        // value back to the reference parameter (but only if it has an out or in/out flow direction).
                        if ((p.Flow == FlowDirection.Out || p.Flow == FlowDirection.Undefined) && p.Reference)
                        {
                            assign_statements.Add(String.Format(
                                "{0} = ({1}){0}_ptr.Target;",
                                p.Name, p.QualifiedType));
                        }

                        // Note! The following line modifies f.Parameters, *not* this.Parameters
                        p.Name = "(IntPtr)" + p.Name + "_ptr.AddrOfPinnedObject()";
                    }
                    else if ((
                        p.WrapperType == WrapperTypes.PointerParameter ||
                        p.WrapperType == WrapperTypes.ArrayParameter ||
                        p.WrapperType == WrapperTypes.ReferenceParameter) ||
                        //
                        ((p.WrapperType & WrapperTypes.PointerParameter) == WrapperTypes.PointerParameter) ||
                        ((p.WrapperType & WrapperTypes.ArrayParameter) == WrapperTypes.ArrayParameter) ||
                        ((p.WrapperType & WrapperTypes.ReferenceParameter) == WrapperTypes.ReferenceParameter)
                        )
                    {
                        // A fixed statement is issued for all non-generic pointers, arrays and references.



                        fixed_statements.Add(String.Format(
                            "fixed ({0}{3} {1} = {2})",
                            (wantCLSCompliance && !p.CLSCompliant) ? p.GetCLSCompliantType() : p.QualifiedType,
                            p.Name + "_ptr",
                            p.Array > 0 ? p.Name : "&" + p.Name,
                            pointer_levels[p.IndirectionLevel]));

                        if (p.Name == "pixels_ptr")
                            System.Diagnostics.Debugger.Break();

                        // Arrays are not value types, so we don't need to do anything for them.
                        // Pointers are passed directly by value, so we don't need to assign them back either (they don't change).
                        if ((p.Flow == FlowDirection.Out || p.Flow == FlowDirection.Undefined) && p.Reference)
                        {
                            assign_statements.Add(String.Format("{0} = *{0}_ptr;", p.Name));
                        }

                        p.Name = p.Name + "_ptr";

                        if (p.CurrentType == "IntPtr")
                        {
                            p.ExplicitCastType = "IntPtr";
                        }
                        else if (p.IsEnum)
                        {
                            p.ExplicitCastType = "int*";
                        }
                    }
                    else
                    {
                        throw new ApplicationException("Unknown parameter type");
                    }
                }
                else if (p.Flow == FlowDirection.Out && p.QualifiedType.ToLower() == "string")
                {

                    if (p.Name.StartsWith("@"))
                    {
                        p.TempOutStringName = "c_" + p.Name.Substring(1);
                    }
                    else
                    {
                        p.TempOutStringName = "c_" + p.Name;
                    }

                    //find bufSize 
                    string bufSizeVarName = FindBuffSizeVarName(func.Parameters.ToArray(), p.Name, "bufsize");
                    if (bufSizeVarName == null)
                    {
                        stringOutAlloc_statements.Add("char* " + p.TempOutStringName + " = stackalloc char[256];"); //TODO: review size 256?
                    }
                    else
                    {
                        stringOutAlloc_statements.Add("char* " + p.TempOutStringName + " = stackalloc char[(int)" + bufSizeVarName + "];");
                    }

                    //this char name has change
                    assign_statements.Add(p.Name + "= new string(" + p.TempOutStringName + ");");
                }
                else if (p.Flow == FlowDirection.In && p.QualifiedType.ToLower() == "string")
                {
                    if ((p.WrapperType & WrapperTypes.StringArrayParameter) == WrapperTypes.StringArrayParameter)
                    {

                    }
                    else
                    {
                        //if (p.Name.StartsWith("@"))
                        //{
                        //    p.TempOutStringName = "c_" + p.Name.Substring(1);
                        //}
                        //else
                        //{
                        //    p.TempOutStringName = "c_" + p.Name;
                        //}
                        //fixed_statements.Add("fixed(char* " + p.TempOutStringName + "=" + p.Name + ")");
                    }

                }
                else if (p.IsEnum)
                {
                    if (p.Flow == FlowDirection.Out ||
                        p.Pointer != 0)
                    {
                        p.ExplicitCastType = "int*";
                    }
                }
                else if ((
                        p.WrapperType == WrapperTypes.PointerParameter ||
                        p.WrapperType == WrapperTypes.ArrayParameter ||
                        p.WrapperType == WrapperTypes.ReferenceParameter) ||
                        //
                        ((p.WrapperType & WrapperTypes.PointerParameter) == WrapperTypes.PointerParameter) ||
                        ((p.WrapperType & WrapperTypes.ArrayParameter) == WrapperTypes.ArrayParameter) ||
                        ((p.WrapperType & WrapperTypes.ReferenceParameter) == WrapperTypes.ReferenceParameter)
                        )
                {
                    if (p.CurrentType.Contains("[]"))
                    {

                    }
                }

            }

            // Automatic OpenGL error checking.
            // See OpenTK.Graphics.ErrorHelper for more information.
            // Make sure that no error checking is added to the GetError function,
            // as that would cause infinite recursion!
            if ((Settings.Compatibility & Settings.Legacy.NoDebugHelpers) == 0)
            {
                if (f.TrimmedName != "GetError")
                {
                    f.Body.Add("#if DEBUG");
                    f.Body.Add("using (new ErrorHelper(GraphicsContext.CurrentContext))");
                    f.Body.Add("{");
                    if (f.TrimmedName == "Begin")
                        f.Body.Add("GraphicsContext.CurrentContext.ErrorChecking = false;");
                    f.Body.Add("#endif");
                }
            }

            if (!f.Unsafe && fixed_statements.Count > 0 || stringOutAlloc_statements.Count > 0)
            {
                f.Body.Add("unsafe");
                f.Body.Add("{");
                f.Body.Indent();
            }


            if (stringOutAlloc_statements.Count > 0)
            {
                f.Body.AddRange(stringOutAlloc_statements);
            }
            if (fixed_statements.Count > 0)
            {
                f.Body.AddRange(fixed_statements);
                f.Body.Add("{");
                f.Body.Indent();
            }

            if (handle_statements.Count > 0)
            {
                f.Body.AddRange(handle_statements);
                f.Body.Add("try");
                f.Body.Add("{");
                f.Body.Indent();
            }

            // Hack: When creating untyped enum wrappers, it is possible that the wrapper uses an "All"
            // enum, while the delegate uses a specific enum (e.g. "TextureUnit"). For this reason, we need
            // to modify the parameters before generating the call string.
            // Note: We cannot generate a callstring using WrappedDelegate directly, as its parameters will
            // typically be different than the parameters of the wrapper. We need to modify the parameters
            // of the wrapper directly.
            if ((Settings.Compatibility & Settings.Legacy.KeepUntypedEnums) != 0)
            {
                int parameter_index = -1; // Used for comparing wrapper parameters with delegate parameters
                foreach (Parameter p in f.Parameters)
                {
                    parameter_index++;
                    //if (IsEnum(p.Name, enums) && p.QualifiedType != f.WrappedDelegate.Parameters[parameter_index].QualifiedType)
                    //{
                    //    p.QualifiedType = f.WrappedDelegate.Parameters[parameter_index].QualifiedType;
                    //}
                    if (IsEnum(p.CurrentType, enums))//&& p.QualifiedType != f.WrappedDelegate.Parameters[parameter_index].QualifiedType)
                    {
                        p.QualifiedType = "int"; // f.WrappedDelegate.Parameters[parameter_index].QualifiedType;
                    }
                }
            }
            string wrappedDelegateEntryPointName = funcPrefix + f.WrappedDelegate.EntryPoint;

            if (assign_statements.Count > 0)
            {
                // Call function
                string method_call = f.CallString(wrappedDelegateEntryPointName, f.WrappedDelegate);
                if (f.ReturnType.CurrentType.ToLower().Contains("void"))
                {
                    f.Body.Add(String.Format("{0};", method_call));
                }
                else if (func.ReturnType.CurrentType.ToLower().Contains("string"))
                {
                    f.Body.Add(String.Format("{0} {1} = null; unsafe {{ {1} = new string((sbyte*){2}); }}",
                        func.ReturnType.QualifiedType, "retval", method_call));
                }
                else if (func.ReturnType.CurrentType.ToLower() == "bool")
                {
                    f.Body.Add(String.Format("{0} {1} = {2} !=0;", f.ReturnType.QualifiedType, "retval", method_call));
                }
                else
                {
                    f.Body.Add(String.Format("{0} {1} = {2};", f.ReturnType.QualifiedType, "retval", method_call));
                }
                // Assign out parameters
                f.Body.AddRange(assign_statements);

                // Return
                if (!f.ReturnType.CurrentType.ToLower().Contains("void"))
                {
                    f.Body.Add("return retval;");
                }
            }
            else
            {
                // Call function and return
                if (f.ReturnType.CurrentType.ToLower().Contains("void"))
                {
                    f.Body.Add(String.Format("{0};", f.CallString(wrappedDelegateEntryPointName, f.WrappedDelegate)));
                }
                else if (func.ReturnType.CurrentType.ToLower().Contains("string"))
                {
                    f.Body.Add(String.Format("unsafe {{ return new string((sbyte*){0}); }}",
                        f.CallString(wrappedDelegateEntryPointName, f.WrappedDelegate)));
                }
                else if (func.ReturnType.CurrentType.ToLower() == "bool")
                {
                    f.Body.Add(String.Format($"return  ({ f.CallString(wrappedDelegateEntryPointName, f.WrappedDelegate)}) != 0;"));
                }
                else if (func.ReturnType.IsEnum)
                {
                    //cast enum
                    f.Body.Add(String.Format($"return ({func.ReturnType}) ({ f.CallString(wrappedDelegateEntryPointName, f.WrappedDelegate)});"));
                }
                else
                {
                    f.Body.Add(String.Format("return {0};", f.CallString(wrappedDelegateEntryPointName, f.WrappedDelegate)));
                }
            }


            // Free all allocated GCHandles
            if (handle_statements.Count > 0)
            {
                f.Body.Unindent();
                f.Body.Add("}");
                f.Body.Add("finally");
                f.Body.Add("{");
                f.Body.Indent();

                f.Body.AddRange(handle_release_statements);

                f.Body.Unindent();
                f.Body.Add("}");
            }

            if (!f.Unsafe && fixed_statements.Count > 0 || stringOutAlloc_statements.Count > 0)
            {
                f.Body.Unindent();
                f.Body.Add("}");
            }

            if (fixed_statements.Count > 0)
            {
                f.Body.Unindent();
                f.Body.Add("}");
            }

            if ((Settings.Compatibility & Settings.Legacy.NoDebugHelpers) == 0)
            {
                if (f.TrimmedName != "GetError")
                {
                    f.Body.Add("#if DEBUG");
                    if (f.TrimmedName == "End")
                        f.Body.Add("GraphicsContext.CurrentContext.ErrorChecking = true;");
                    f.Body.Add("}");
                    f.Body.Add("#endif");
                }
            }

            func.Body = f.Body;
        }
        private void WriteMethod(BindStreamWriter sw, Function f, EnumCollection enums)
        {
            if (!String.IsNullOrEmpty(f.Obsolete))
            {
                sw.WriteLine("[Obsolete(\"{0}\")]", f.Obsolete);
            }
            else if (f.Deprecated && Settings.IsEnabled(Settings.Legacy.AddDeprecationWarnings))
            {
                sw.WriteLine("[Obsolete(\"Deprecated in OpenGL {0}\")]", f.DeprecatedVersion);
            }

            sw.WriteLine("[AutoGenerated(Category = \"{0}\", Version = \"{1}\", EntryPoint = \"{2}\")]",
                f.Category, f.Version, Settings.FunctionPrefix + f.WrappedDelegate.EntryPoint);

            if (!f.CLSCompliant)
            {
                sw.WriteLine("[CLSCompliant(false)]");
            }

            sw.WriteLine("public static {0} {{ throw new BindingsNotRewrittenException(); }}", GetDeclarationString(f, Settings.Compatibility));
        }

        private void WriteDocumentation(BindStreamWriter sw, Function f)
        {
            var docs = f.Documentation;

            try
            {
                string warning = String.Empty;
                string category = String.Empty;
                if (f.Deprecated)
                {
                    warning = String.Format("[deprecated: v{0}]", f.DeprecatedVersion);
                }

                if (f.Extension != "Core" && !String.IsNullOrEmpty(f.Category))
                {
                    category = String.Format("[requires: {0}]", f.Category);
                }
                else if (!String.IsNullOrEmpty(f.Version))
                {
                    if (f.Category.StartsWith("VERSION"))
                    {
                        category = String.Format("[requires: {0}]", "v" + f.Version);
                    }
                    else
                    {
                        category = String.Format("[requires: {0}]", "v" + f.Version + " or " + f.Category);
                    }
                }

                // Write function summary
                sw.Write("/// <summary>");
                if (!String.IsNullOrEmpty(category) || !String.IsNullOrEmpty(warning))
                {
                    sw.Write(WriteOptions.NoIndent, "{0}{1}", category, warning);
                }
                if (!String.IsNullOrEmpty(docs.Summary))
                {
                    sw.WriteLine();
                    sw.WriteLine("/// {0}", docs.Summary);
                    sw.WriteLine("/// </summary>");
                }
                else
                {
                    sw.WriteLine(WriteOptions.NoIndent, "</summary>");
                }

                // Write function parameters
                for (int i = 0; i < f.Parameters.Count; i++)
                {
                    var param = f.Parameters[i];

                    string length = String.Empty;
                    if (!String.IsNullOrEmpty(param.ComputeSize))
                    {
                        length = String.Format("[length: {0}]", param.ComputeSize);
                    }

                    // Try to match the correct parameter from documentation:
                    // - first by name
                    // - then by index
                    var docparam =
                        (docs.Parameters
                            .Where(p => p.Name == param.RawName)
                            .FirstOrDefault()) ??
                        (docs.Parameters.Count > i ?
                            docs.Parameters[i] : null);

                    if (docparam != null)
                    {
                        if (docparam.Name != param.RawName &&
                            docparam.Name != param.RawName.Substring(1)) // '@ref' -> 'ref' etc
                        {
                            Console.Error.WriteLine(
                                "[Warning] Parameter '{0}' in function '{1}' has incorrect doc name '{2}'",
                                param.RawName, f.Name, docparam.Name);
                        }

                        // Note: we use param.Name, because the documentation sometimes
                        // uses different names than the specification.
                        sw.Write("/// <param name=\"{0}\">", param.Name);
                        if (!String.IsNullOrEmpty(length))
                        {
                            sw.Write(WriteOptions.NoIndent, "{0}", length);
                        }
                        if (!String.IsNullOrEmpty(docparam.Documentation))
                        {
                            sw.WriteLine(WriteOptions.NoIndent, "");
                            sw.WriteLine("/// {0}", docparam.Documentation);
                            sw.WriteLine("/// </param>");
                        }
                        else
                        {
                            sw.WriteLine(WriteOptions.NoIndent, "</param>");
                        }
                    }
                    else
                    {
                        Console.Error.WriteLine(
                            "[Warning] Parameter '{0}' in function '{1}' not found in documentation '{{{2}}}'",
                            param.Name, f.Name,
                            String.Join(",", docs.Parameters.Select(p => p.Name).ToArray()));
                        sw.WriteLine("/// <param name=\"{0}\">{1}</param>",
                            param.Name, length);
                    }
                }
            }
            catch (Exception e)
            {
                Console.WriteLine("[Warning] Error documenting function {0}: {1}", f.WrappedDelegate.Name, e.ToString());
            }
        }

        public void WriteTypes(BindStreamWriter sw, Dictionary<string, string> CSTypes)
        {
            sw.WriteLine();
            foreach (string s in CSTypes.Keys)
            {
                sw.WriteLine("using {0} = System.{1};", s, CSTypes[s]);
            }
        }

        private void WriteConstants(BindStreamWriter sw, IEnumerable<Constant> constants)
        {
            // Make sure everything is sorted. This will avoid random changes between
            // consecutive runs of the program.
            constants = constants.OrderBy(c => c);

            foreach (var c in constants)
            {
                if (!Settings.IsEnabled(Settings.Legacy.NoDocumentation))
                {
                    sw.WriteLine("/// <summary>");
                    sw.WriteLine("/// Original was " + Settings.ConstantPrefix + c.OriginalName + " = " + c.Value);
                    sw.WriteLine("/// </summary>");
                }

                var str = String.Format("{0} = {1}((int){2}{3})", c.Name, c.Unchecked ? "unchecked" : "",
                    !String.IsNullOrEmpty(c.Reference) ? c.Reference + Settings.NamespaceSeparator : "", c.Value);

                sw.Write(str);
                if (!String.IsNullOrEmpty(str))
                {
                    sw.WriteLine(",");
                }
            }
        }

        private void WriteEnums(BindStreamWriter sw, EnumCollection enums, FunctionCollection wrappers)
        {
            //sw.WriteLine("#pragma warning disable 3019");   // CLSCompliant attribute
            //sw.WriteLine("#pragma warning disable 1591");   // Missing doc comments
            //sw.WriteLine();

            if ((Settings.Compatibility & Settings.Legacy.NestedEnums) != Settings.Legacy.None)
            {
                Trace.WriteLine(String.Format("Writing enums to:\t{0}.{1}.{2}", Settings.OutputNamespace, Settings.OutputClass, Settings.NestedEnumsClass));
            }
            else
            {
                Trace.WriteLine(String.Format("Writing enums to:\t{0}", Settings.EnumsOutput));
            }

            if ((Settings.Compatibility & Settings.Legacy.ConstIntEnums) == Settings.Legacy.None)
            {
                if ((Settings.Compatibility & Settings.Legacy.NestedEnums) != Settings.Legacy.None &&
                    !String.IsNullOrEmpty(Settings.NestedEnumsClass))
                {
                    sw.WriteLine("public class Enums");
                    sw.WriteLine("{");
                    sw.Indent();
                }

                // Build a dictionary of which functions use which enums
                var enum_counts = new Dictionary<Enum, List<Function>>();
                foreach (var e in enums.Values)
                {
                    // Initialize the dictionary
                    enum_counts.Add(e, new List<Function>());
                }
                foreach (var wrapper in wrappers.Values.SelectMany(w => w))
                {
                    // Add every function to every enum parameter it references
                    foreach (var parameter in wrapper.Parameters.Where(p => p.IsEnum))
                    {
                        var e = enums[parameter.CurrentType];
                        var list = enum_counts[e];
                        list.Add(wrapper);
                    }
                }

                foreach (Enum @enum in enums.Values)
                {
                    if (!Settings.IsEnabled(Settings.Legacy.NoDocumentation))
                    {
                        // Document which functions use this enum.
                        var functions = enum_counts[@enum]
                            .Select(w => Settings.GLClass + (w.Extension != "Core" ? ("." + w.Extension) : "") + "." + w.TrimmedName)
                            .Distinct();

                        sw.WriteLine("/// <summary>");
                        sw.WriteLine(String.Format("/// {0}",
                            functions.Count() >= 3 ?
                                String.Format("Used in {0} and {1} other function{2}",
                                    String.Join(", ", functions.Take(2).ToArray()),
                                    functions.Count() - 2,
                                    functions.Count() - 2 > 1 ? "s" : "") :
                            functions.Count() >= 1 ?
                                String.Format("Used in {0}",
                                    String.Join(", ", functions.ToArray())) :
                                "Not used directly."));
                        sw.WriteLine("/// </summary>");
                    }

                    if (@enum.IsObsolete)
                    {
                        sw.WriteLine("[Obsolete(\"{0}\")]", @enum.Obsolete);
                    }
                    if (!@enum.CLSCompliant)
                    {
                        sw.WriteLine("[CLSCompliant(false)]");
                    }
                    if (@enum.IsFlagCollection)
                    {
                        sw.WriteLine("[Flags]");
                    }
                    sw.WriteLine("public enum " + @enum.Name + " : " + @enum.Type);
                    sw.WriteLine("{");
                    sw.Indent();
                    WriteConstants(sw, @enum.ConstantCollection.Values);
                    sw.Unindent();
                    sw.WriteLine("}");
                    sw.WriteLine();
                }

                if ((Settings.Compatibility & Settings.Legacy.NestedEnums) != Settings.Legacy.None &&
                    !String.IsNullOrEmpty(Settings.NestedEnumsClass))
                {
                    sw.Unindent();
                    sw.WriteLine("}");
                }
            }
            else
            {
                // Tao legacy mode: dump all enums as constants in GLClass.
                foreach (Constant c in enums[Settings.CompleteEnumName].ConstantCollection.Values)
                {
                    // Print constants avoiding circular definitions
                    if (c.Name != c.Value)
                    {
                        sw.WriteLine(String.Format(
                            "public const int {0} = {2}((int){1});",
                            c.Name.StartsWith(Settings.ConstantPrefix) ? c.Name : Settings.ConstantPrefix + c.Name,
                            Char.IsDigit(c.Value[0]) ? c.Value : c.Value.StartsWith(Settings.ConstantPrefix) ? c.Value : Settings.ConstantPrefix + c.Value,
                            c.Unchecked ? "unchecked" : ""));
                    }
                    else
                    {
                    }
                }
            }
        }

        public void WriteLicense(BindStreamWriter sw)
        {
            sw.WriteLine(File.ReadAllText(Path.Combine(Settings.InputPath, Settings.LicenseFile)));
            sw.WriteLine();
        }

        // For example, if parameter foo has indirection level = 1, then it
        // is consumed as 'foo*' in the fixed_statements and the call string.
        private readonly static string[] pointer_levels = new string[] { "", "*", "**", "***", "****" };

        private readonly static string[] array_levels = new string[] { "", "[]", "[,]", "[,,]", "[,,,]" };

        private static bool IsEnum(string s, EnumCollection enums)
        {
            return enums.ContainsKey(s);
        }

        private string GetDeclarationString(Constant c)
        {
            if (String.IsNullOrEmpty(c.Name))
            {
                throw new InvalidOperationException("Invalid Constant: Name is empty");
            }

            return String.Format("{0} = {1}((int){2}{3})",
                c.Name,
                c.Unchecked ? "unchecked" : String.Empty,
                !String.IsNullOrEmpty(c.Reference) ?
                    c.Reference + Settings.NamespaceSeparator :
                    String.Empty,
                c.Value);
        }

        private string GetDeclarationString(Delegate d, bool is_delegate)
        {
            StringBuilder sb = new StringBuilder();

            sb.Append(d.Unsafe ? "unsafe " : "");
            if (is_delegate)
            {
                sb.Append("delegate ");
            }
            sb.Append(GetDeclarationString(d.ReturnType, Settings.Legacy.ConstIntEnums));
            sb.Append(" ");
            sb.Append(Settings.FunctionPrefix);
            sb.Append(d.Name);
            sb.Append(GetDeclarationString(d.Parameters, Settings.Legacy.ConstIntEnums));

            return sb.ToString();
        }
        private string GetDeclarationString2(Delegate d, bool is_delegate)
        {
            StringBuilder sb = new StringBuilder();

            sb.Append(d.Unsafe ? "unsafe " : "");
            if (is_delegate)
            {
                sb.Append("delegate ");
            }
            sb.Append(GetDeclarationString(d.ReturnType, Settings.Legacy.ConstIntEnums));
            sb.Append(" ");
            sb.Append(d.Name);
            sb.Append(GetDeclarationString(d.Parameters, Settings.Legacy.ConstIntEnums));

            return sb.ToString();
        }
        private string GetDeclarationString(Enum e)
        {
            StringBuilder sb = new StringBuilder();
            List<Constant> constants = new List<Constant>(e.ConstantCollection.Values);
            constants.Sort(delegate (Constant c1, Constant c2)
            {
                int ret = String.Compare(c1.Value, c2.Value);
                if (ret == 0)
                {
                    return String.Compare(c1.Name, c2.Name);
                }
                return ret;
            });

            if (e.IsFlagCollection)
            {
                sb.AppendLine("[Flags]");
            }
            sb.Append("public enum ");
            sb.Append(e.Name);
            sb.Append(" : ");
            sb.AppendLine(e.Type);
            sb.AppendLine("{");

            foreach (Constant c in constants)
            {
                var declaration = GetDeclarationString(c);
                sb.Append("    ");
                sb.Append(declaration);
                if (!String.IsNullOrEmpty(declaration))
                {
                    sb.AppendLine(",");
                }
            }
            sb.Append("}");

            return sb.ToString();
        }

        private string GetDeclarationString(Function f, Settings.Legacy settings)
        {
            StringBuilder sb = new StringBuilder();

            sb.Append(f.Unsafe ? "unsafe " : "");
            sb.Append(GetDeclarationString(f.ReturnType, settings));
            sb.Append(" ");
            if ((Settings.Compatibility & Settings.Legacy.NoTrimFunctionEnding) != Settings.Legacy.None)
            {
                sb.Append(Settings.FunctionPrefix);
            }
            sb.Append(!String.IsNullOrEmpty(f.TrimmedName) ? f.TrimmedName : f.Name);




            if (f.Parameters.HasGenericParameters)
            {
                sb.Append("<");
                foreach (Parameter p in f.Parameters.Where(p => p.Generic))
                {
                    sb.Append(p.CurrentType);
                    sb.Append(", ");
                }

                sb.Remove(sb.Length - 2, 2);
                sb.Append(">");
            }

            sb.Append(GetDeclarationString(f.Parameters, settings));

            if (f.Parameters.HasGenericParameters)
            {
                sb.AppendLine();
                foreach (Parameter p in f.Parameters.Where(p => p.Generic))
                {
                    sb.AppendLine(String.Format("    where {0} : struct", p.CurrentType));
                }
            }

            return sb.ToString();
        }

        private string GetDeclarationString(Parameter p, bool override_unsafe_setting, Settings.Legacy settings)
        {
            StringBuilder sb = new StringBuilder();

            List<string> attributes = new List<string>();
            if (p.Flow == FlowDirection.Out)
            {
                attributes.Add("OutAttribute");
            }
            else if (p.Flow == FlowDirection.Undefined)
            {
                attributes.Add("InAttribute");
                attributes.Add("OutAttribute");
            }

            if (!String.IsNullOrEmpty(p.ComputeSize))
            {
                int count;
                if (Int32.TryParse(p.ComputeSize, out count))
                {
                    attributes.Add(String.Format("CountAttribute(Count = {0})", count));
                }
                else
                {
                    if (p.ComputeSize.StartsWith("COMPSIZE"))
                    {
                        //remove the compsize hint, just keep comma delimited param names
                        var len = "COMPSIZE(".Length;
                        var computed = p.ComputeSize.Substring(len, (p.ComputeSize.Length - len) - 1);
                        attributes.Add(String.Format("CountAttribute(Computed = \"{0}\")", computed));
                    }
                    else
                    {
                        attributes.Add(String.Format("CountAttribute(Parameter = \"{0}\")", p.ComputeSize));
                    }
                }
            }

            if (attributes.Count != 0)
            {
                sb.Append("[");
                sb.Append(string.Join(", ", attributes));
                sb.Append("] ");
            }

            if (p.Reference)
            {
                if (p.Flow == FlowDirection.Out)
                {
                    sb.Append("out ");
                }
                else
                {
                    sb.Append("ref ");
                }
            }

            if (!override_unsafe_setting && ((Settings.Compatibility & Settings.Legacy.NoPublicUnsafeFunctions) != Settings.Legacy.None))
            {
                if (p.Pointer != 0)
                {
                    sb.Append("IntPtr");
                }
                else
                {
                    sb.Append(GetDeclarationString(p as Type, settings));
                }
            }
            else
            {
                if ((p.WrapperType & WrapperTypes.StringArrayParameter) == WrapperTypes.StringArrayParameter)
                {
                    sb.Append("string[]");
                }
                else if (p.Flow == FlowDirection.In && (p.WrapperType & WrapperTypes.StringParameter) == WrapperTypes.StringParameter)
                {
                    sb.Append("string");
                }
                else
                {
                    sb.Append(GetDeclarationString(p as Type, settings));
                }

            }
            if (!String.IsNullOrEmpty(p.Name))
            {
                sb.Append(" ");
                sb.Append(p.Name);
            }

            return sb.ToString();
        }

        private string GetDeclarationString(ParameterCollection parameters, Settings.Legacy settings)
        {
            StringBuilder sb = new StringBuilder();

            sb.Append("(");
            if (parameters.Count > 0)
            {
                foreach (Parameter p in parameters)
                {
                    sb.Append(GetDeclarationString(p, false, settings));
                    sb.Append(", ");
                }
                sb.Replace(", ", ")", sb.Length - 2, 2);
            }
            else
            {
                sb.Append(")");
            }

            return sb.ToString();
        }

        private string GetDeclarationString(Type type, Settings.Legacy settings)
        {
            var t = type.QualifiedType;

            if ((settings & Settings.Legacy.ConstIntEnums) != 0)
            {
                if (type.IsEnum)
                {
                    t = "System.Int32";
                }
            }

            return String.Format("{0}{1}{2}",
                t,
                pointer_levels[type.Pointer],
                array_levels[type.Array]);
        }
    }
}
