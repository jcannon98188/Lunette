using Xunit;

namespace Lunette.Analyzers.Tests;

public class Ld001HotPathAllocationTests
{
    private static Task Run(string body, string extra = "") => Verify.Analyzer<HotPathAllocationAnalyzer>($$"""
        class Box { }
        {{extra}}
        class C
        {
            [LuaHotPath]
            static int Hot(object o, int[] arr)
            {
                {{body}}
                return 0;
            }
        }
        """);

    [Fact] public Task New_reference_type_is_reported() => Run("var b = {|LD001:new Box()|};");
    [Fact] public Task Boxing_is_reported() => Run("object b = {|LD001:arr.Length|};");
    [Fact] public Task Lambda_is_reported() => Run("Func<int> f = {|LD001:() => 1|};");
    [Fact] public Task String_concat_is_reported() => Run("var s = {|LD001:o.ToString() + \"x\"|};");
    [Fact] public Task Interpolation_is_reported() => Run("var s = {|LD001:$\"{arr.Length}\"|};");
    [Fact] public Task Linq_is_reported() => Run("var n = {|LD001:System.Linq.Enumerable.Count(arr)|};");
    [Fact] public Task Foreach_over_list_is_reported() => Verify.Analyzer<HotPathAllocationAnalyzer>("""
        class C { [LuaHotPath] static void Hot(System.Collections.Generic.List<int> l) { {|LD001:foreach (var x in l) { }|} } }
        """);

    [Fact] public Task Array_creation_is_reported() => Run("var a = {|LD001:new int[4]|};");

    [Fact] public Task Params_array_is_reported() => Verify.Analyzer<HotPathAllocationAnalyzer>("""
        class C
        {
            static void P(params int[] xs) { }
            [LuaHotPath] static void Hot() { {|LD001:P(1, 2)|}; }
        }
        """);

    [Fact] public Task Struct_creation_and_array_foreach_and_arithmetic_are_clean() => Verify.Analyzer<HotPathAllocationAnalyzer>("""
        struct V { public long X; }
        class C
        {
            [LuaHotPath]
            static long Hot(int[] arr, ReadOnlySpan<int> span)
            {
                var v = new V { X = 1 };
                long sum = v.X;
                foreach (var x in arr) sum += x;
                foreach (var x in span) sum += x;
                return sum + "a".Length;
            }
        }
        """);

    [Fact] public Task Unmarked_code_is_not_checked() => Verify.Analyzer<HotPathAllocationAnalyzer>("""
        class Box { }
        class C { static object Cold() => new Box(); }
        """);

    [Fact] public Task Marker_on_type_applies_to_all_members() => Verify.Analyzer<HotPathAllocationAnalyzer>("""
        class Box { }
        [LuaHotPath]
        class C { static object Hot() => {|LD001:new Box()|}; }
        """);
}

public class Ld002HotPathThrowTests
{
    [Fact] public Task Throw_in_hot_path_is_reported() => Verify.Analyzer<HotPathThrowAnalyzer>("""
        class C { [LuaHotPath] static void Hot() { {|LD002:throw new InvalidOperationException();|} } }
        """);

    [Fact] public Task Throw_expression_in_hot_path_is_reported() => Verify.Analyzer<HotPathThrowAnalyzer>("""
        class C { [LuaHotPath] static object Hot(object o) => o ?? {|LD002:throw new InvalidOperationException()|}; }
        """);

    [Fact] public Task Throw_in_cold_throw_method_is_allowed() => Verify.Analyzer<HotPathThrowAnalyzer>("""
        [LuaHotPath]
        class C { [LuaColdThrow] static void Fail() { throw new InvalidOperationException(); } }
        """);

    [Fact] public Task Throw_in_unmarked_code_is_allowed() => Verify.Analyzer<HotPathThrowAnalyzer>("""
        class C { static void Cold() { throw new InvalidOperationException(); } }
        """);
}

public class Ld006NoTryInDispatchLoopTests
{
    [Fact] public Task Try_catch_in_loop_method_is_reported() => Verify.Analyzer<NoTryInDispatchLoopAnalyzer>("""
        class C { [LuaInterpreterLoop] static void Loop() { {|LD006:try { } catch (Exception) { }|} } }
        """);

    [Fact] public Task Try_finally_in_loop_method_is_reported() => Verify.Analyzer<NoTryInDispatchLoopAnalyzer>("""
        class C { [LuaInterpreterLoop] static void Loop() { {|LD006:try { } finally { }|} } }
        """);

    [Fact] public Task Using_statement_in_loop_method_is_reported() => Verify.Analyzer<NoTryInDispatchLoopAnalyzer>("""
        class C { [LuaInterpreterLoop] static void Loop(IDisposable d) { {|LD006:using (d) { }|} } }
        """);

    [Fact] public Task Lock_statement_in_loop_method_is_reported() => Verify.Analyzer<NoTryInDispatchLoopAnalyzer>("""
        class C { [LuaInterpreterLoop] static void Loop(object o) { {|LD006:lock (o) { }|} } }
        """);

    [Fact] public Task Generic_loop_method_is_checked() => Verify.Analyzer<NoTryInDispatchLoopAnalyzer>("""
        class C { [LuaInterpreterLoop] static void Loop<T>() { {|LD006:try { } catch (Exception) { }|} } }
        """);

    [Fact] public Task Try_in_the_caller_of_the_loop_method_is_allowed() => Verify.Analyzer<NoTryInDispatchLoopAnalyzer>("""
        class C
        {
            [LuaInterpreterLoop] static void Loop() { }
            static void Run() { try { Loop(); } catch (Exception) { } }
        }
        """);

    [Fact] public Task Loop_method_without_try_is_clean() => Verify.Analyzer<NoTryInDispatchLoopAnalyzer>("""
        class C { [LuaInterpreterLoop] static int Loop(int n) { for (int i = 0; i < n; i++) n--; return n; } }
        """);
}

public class Ld003LuaStringBackingArrayTests
{
    private const string LuaString = """
        namespace Lunette
        {
            public sealed class LuaString
            {
                internal byte[] Bytes = new byte[4];
                public byte At(int i) => Bytes[i];
            }
        }
        """;

    [Fact] public Task Indexing_backing_array_from_outside_is_reported() => Verify.Analyzer<LuaStringBackingArrayAnalyzer>(LuaString + """
        class C { static byte Read(LuaString s) => {|LD003:s.Bytes[0]|}; }
        """);

    [Fact] public Task Indexing_through_the_accessor_is_clean() => Verify.Analyzer<LuaStringBackingArrayAnalyzer>(LuaString + """
        class C { static byte Read(LuaString s) => s.At(0); }
        """);

    [Fact] public Task Indexing_other_byte_arrays_is_clean() => Verify.Analyzer<LuaStringBackingArrayAnalyzer>("""
        class C { static byte Read(byte[] b) => b[0]; }
        """);
}

public class Ld004StringPathTests
{
    [Fact] public Task Encoding_is_reported() => Verify.Analyzer<StringPathAnalyzer>("""
        class C { [LuaStringPath] static byte[] Hot(byte[] b) { var s = {|LD004:System.Text.Encoding.UTF8|}; return b; } }
        """);

    [Fact] public Task StringBuilder_is_reported() => Verify.Analyzer<StringPathAnalyzer>("""
        class C { [LuaStringPath] static void Hot() { var sb = {|LD004:new System.Text.StringBuilder()|}; } }
        """);

    [Fact] public Task ToArray_is_reported() => Verify.Analyzer<StringPathAnalyzer>("""
        class C { [LuaStringPath] static byte[] Hot(ReadOnlySpan<byte> b) => {|LD004:b.ToArray()|}; }
        """);

    [Fact] public Task Byte_span_work_is_clean() => Verify.Analyzer<StringPathAnalyzer>("""
        class C
        {
            [LuaStringPath]
            static int Hot(ReadOnlySpan<byte> b)
            {
                int n = 0;
                for (int i = 0; i < b.Length; i++) n += b[i];
                return n;
            }
        }
        """);

    [Fact] public Task Unmarked_code_may_use_Encoding() => Verify.Analyzer<StringPathAnalyzer>("""
        class C { static byte[] Cold(string s) => System.Text.Encoding.UTF8.GetBytes(s); }
        """);
}

public class Ld005UnsafeConfinementTests
{
    [Fact] public Task Unsafe_block_outside_helpers_is_reported() => Verify.Analyzer<UnsafeConfinementAnalyzer>("""
        class C { {|LD005:static unsafe int Read(int* p) => *p;|} }
        """, allowUnsafe: true);
    [Fact] public Task Unsafe_class_is_allowed_inside_Lunette_Unsafe() => Verify.Analyzer<UnsafeConfinementAnalyzer>("""
        namespace Lunette.Unsafe { static unsafe class Ptr { public static int Read(int* p) => *p; } }
        """, allowUnsafe: true);

    [Fact] public Task Unsafe_helpers_are_reported_outside_Lunette_Unsafe() => Verify.Analyzer<UnsafeConfinementAnalyzer>("""
        class C { static int Cast(ref uint u) => {|LD005:System.Runtime.CompilerServices.Unsafe.As<uint, int>(ref u)|}; }
        """);

    [Fact] public Task MemoryMarshal_is_reported_outside_Lunette_Unsafe() => Verify.Analyzer<UnsafeConfinementAnalyzer>("""
        class C { static ref int First(int[] a) => ref {|LD005:System.Runtime.InteropServices.MemoryMarshal.GetArrayDataReference(a)|}; }
        """);

    [Fact] public Task Unsafe_helpers_are_allowed_inside_Lunette_Unsafe() => Verify.Analyzer<UnsafeConfinementAnalyzer>("""
        namespace Lunette.Unsafe { static class H { public static int Cast(ref uint u) => System.Runtime.CompilerServices.Unsafe.As<uint, int>(ref u); } }
        """);
}
