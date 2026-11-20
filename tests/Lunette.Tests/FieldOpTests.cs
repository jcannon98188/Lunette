using System.Text;
using Lunette.Compiler;
using Xunit;

namespace Lunette.Tests;

/// <summary>
/// Constant-key table opcodes (GetField, SetField, SelfK): what the compiler emits for <c>t.name</c>, <c>t["name"]</c>, <c>t.name = v</c>,
/// constructor fields and method calls, and that errors, metamethods and yields behave exactly as with a key held in a register.
/// </summary>
public class FieldOpTests
{
    private static Proto Compile(string source) => LuaCompiler.Compile(source, "=test");

    private static OpCode[] Ops(string source) => Compile(source).Code.Select(i => i.Op).ToArray();

    private static string Run(string source) => ControlFlowTests.Run(source);

    // ---- what the compiler emits ----

    [Fact]
    public void A_field_read_is_one_get_field_with_no_key_load()
    {
        var ops = Ops("local t = {} return t.x");
        Assert.Contains(OpCode.GetField, ops);
        Assert.DoesNotContain(OpCode.GetTable, ops);
        Assert.DoesNotContain(OpCode.LoadK, ops);
    }

    [Fact]
    public void A_string_literal_index_is_a_field_too()
    {
        var ops = Ops("local t = {} return t['x']");
        Assert.Contains(OpCode.GetField, ops);
        Assert.DoesNotContain(OpCode.GetTable, ops);
    }

    [Fact]
    public void A_variable_or_number_index_keeps_get_table()
    {
        Assert.Contains(OpCode.GetTable, Ops("local t, k = {}, 'x' return t[k]"));
        Assert.Contains(OpCode.GetTable, Ops("local t = {} return t[1]"));
        Assert.DoesNotContain(OpCode.GetField, Ops("local t = {} return t[1]"));
    }

    [Fact]
    public void A_field_store_is_one_set_field()
    {
        var ops = Ops("local t, v = {}, 1 t.x = v");
        Assert.Contains(OpCode.SetField, ops);
        Assert.DoesNotContain(OpCode.SetTable, ops);
        Assert.Equal(1, ops.Count(o => o == OpCode.LoadK)); // only the value 1; the key is not loaded
    }

    [Fact]
    public void Constructor_fields_are_set_field()
    {
        var ops = Ops("local v = 1 local t = {x = v, y = 2}");
        Assert.Equal(2, ops.Count(o => o == OpCode.SetField));
        Assert.DoesNotContain(OpCode.SetTable, ops);
    }

    [Fact]
    public void A_method_call_with_a_constant_name_is_self_k()
    {
        var ops = Ops("local o = {} o:m()");
        Assert.Contains(OpCode.SelfK, ops);
        Assert.DoesNotContain(OpCode.Self, ops);
    }

    [Fact]
    public void Function_statements_on_fields_use_field_ops()
    {
        var ops = Ops("local a = {b = {}} function a.b.f() end function a.b:g() end");
        Assert.Contains(OpCode.GetField, ops);
        Assert.Equal(3, ops.Count(o => o == OpCode.SetField)); // b = {} in the constructor, then f and g
        Assert.DoesNotContain(OpCode.SetTable, ops);
    }

    [Fact]
    public void A_repeated_field_name_shares_one_constant()
    {
        var p = Compile("local t = {} t.name = 1 t.name = t.name + 1 return t.name");
        Assert.Single(p.Constants, c => c.Ref is byte[] s && new LuaString(s).ToString() == "name");
    }

    [Fact]
    public void Field_names_beyond_the_first_256_constants_fall_back_to_register_keys()
    {
        var sb = new StringBuilder("local t = {}\n");
        for (int i = 0; i < 300; i++) sb.Append($"t.f{i} = {i}\n");
        sb.Append("return t.f0, t.f255, t.f256, t.f299");
        var ops = Ops(sb.ToString());
        Assert.Contains(OpCode.SetField, ops);
        Assert.Contains(OpCode.SetTable, ops);
        Assert.Contains(OpCode.GetTable, ops);
        var sum = new StringBuilder("local t = {}\n");
        for (int i = 0; i < 300; i++) sum.Append($"t.f{i} = {i}\n");
        sum.Append("print(t.f0, t.f255, t.f256, t.f299)");
        Assert.Equal("0\t255\t256\t299\n", Run(sum.ToString()));
    }

    [Fact]
    public void Many_method_names_beyond_the_first_256_constants_still_call()
    {
        var sb = new StringBuilder("local o = {}\n");
        for (int i = 0; i < 300; i++) sb.Append($"function o:m{i}() return {i} end\n");
        sb.Append("print(o:m0(), o:m255(), o:m256(), o:m299())");
        Assert.Equal("0\t255\t256\t299\n", Run(sb.ToString()));
    }

    // ---- behaviour is the register-key behaviour ----

    [Fact]
    public void Fields_read_write_and_nest()
    {
        Assert.Equal("1\t2\t3\n", Run("local t = {a = {b = 1}} t.a.c = 2 t.a.d = {e = 3} print(t.a.b, t.a.c, t.a.d.e)"));
    }

    [Fact]
    public void Assigning_nil_to_a_field_removes_it_and_a_missing_field_reads_nil()
    {
        Assert.Equal("nil\tnil\t5\n", Run("local t = {x = 1} t.x = nil t.y = nil t.z = 5 print(t.x, t.y, t.z)"));
    }

    [Fact]
    public void A_multiple_assignment_with_field_targets_swaps_correctly()
    {
        Assert.Equal("2\t1\n", Run("local t = {a = 1, b = 2} t.a, t.b = t.b, t.a print(t.a, t.b)"));
    }

    [Fact]
    public void A_field_target_whose_table_is_reassigned_in_the_same_statement_uses_the_old_table()
    {
        Assert.Equal("1\tnil\n", Run("local t, u = {}, {} local o = t t, t.x = u, 1 print(o.x, t.x)"));
    }

    [Fact]
    public void Index_chains_through_tables_and_functions()
    {
        Assert.Equal("base\tfn:own\tnil\n", Run("""
            local base = {name = "base"}
            local mid = setmetatable({}, {__index = base})
            local obj = setmetatable({}, {__index = mid})
            local fobj = setmetatable({}, {__index = function(_, k) return "fn:" .. k end})
            print(obj.name, fobj.own, obj.absent)
            """));
    }

    [Fact]
    public void Newindex_function_table_and_plain_stores()
    {
        Assert.Equal("fn:x=1\tnil\t2\t3\n", Run("""
            local log
            local f = setmetatable({}, {__newindex = function(t, k, v) log = k .. "=" .. v end})
            f.x = 1
            local sink = {}
            local p = setmetatable({}, {__newindex = sink})
            p.y = 2
            local q = setmetatable({}, {})
            q.z = 3
            print("fn:" .. log, rawget(p, "y"), sink.y, q.z)
            """));
    }

    [Fact]
    public void Newindex_is_skipped_for_a_key_that_already_exists()
    {
        Assert.Equal("10\n", Run("""
            local t = setmetatable({a = 1}, {__newindex = function() error("no") end})
            t.a = 10
            print(t.a)
            """));
    }

    [Fact]
    public void A_metatable_gaining_newindex_after_a_store_is_noticed()
    {
        Assert.Equal("1\tintercepted\n", Run("""
            local mt = {}
            local t = setmetatable({}, mt)
            t.a = 1
            local seen = "none"
            mt.__newindex = function() seen = "intercepted" end
            t.b = 2
            print(t.a, seen)
            """));
    }

    [Fact]
    public void A_metatable_gaining_index_after_a_miss_is_noticed()
    {
        Assert.Equal("nil\tfound\n", Run("""
            local mt = {}
            local t = setmetatable({}, mt)
            local before = t.k
            mt.__index = {k = "found"}
            print(before, t.k)
            """));
    }

    [Fact]
    public void String_methods_resolve_through_the_string_metatable()
    {
        Assert.Equal("ABC\t3\tb\t5\n", Run("local s = 'abc' print(s:upper(), s:len(), s:sub(2, 2), ('x'):rep(5):len())"));
    }

    [Fact]
    public void Replacing_the_string_metatable_index_is_seen_by_method_calls()
    {
        Assert.Equal("hello\n42\n", Run("""
            local s = "x"
            string.hello = function() return "hello" end
            local mt = getmetatable("")
            local old = mt.__index
            print(s:hello())
            mt.__index = function(_, k) return function() return 42 end end
            print(s:anything())
            mt.__index = old
            """));
    }

    [Fact]
    public void Indexing_a_non_table_without_a_metatable_names_the_variable()
    {
        Assert.Equal(
            "false\t(load):1: attempt to index a nil value (local 't')\n" +
            "false\t(load):2: attempt to index a nil value (field 'a')\n" +
            "false\t(load):4: attempt to index a number value (upvalue 'n')\n" +
            "false\t(load):5: attempt to index a nil value (global 'g')\n",
            Run("""
                print(pcall(function() local t return t.x end))
                print(pcall(function() local t = {} t.a.b = 1 end))
                local n = 5
                print(pcall(function() return n.x end))
                print(pcall(function() return g.x end))
                """));
    }

    [Fact]
    public void A_nil_field_used_in_arithmetic_or_a_call_is_named()
    {
        Assert.Equal(
            "false\t(load):1: attempt to perform arithmetic on a nil value (field 'x')\n" +
            "false\t(load):2: attempt to call a nil value (field 'f')\n" +
            "false\t(load):3: attempt to call a nil value (method 'm')\n",
            Run("""
                print(pcall(function() local t = {} return t.x + 1 end))
                print(pcall(function() local t = {} t.f() end))
                print(pcall(function() local t = {} t:m() end))
                """));
    }

    [Fact]
    public void A_local_environment_field_is_still_a_global_in_messages()
    {
        Assert.Equal("false\t(load):2: attempt to index a nil value (global 'x')\n", Run("""
            local _ENV = {pcall = pcall, print = print}
            print(pcall(function() return x.y end))
            """));
    }

    [Fact]
    public void A_function_stored_in_a_field_is_named_by_it_in_argument_errors()
    {
        Assert.Equal("false\t(load):2: bad argument #1 to 'f' (string expected, got no value)\n", Run("""
            local t = {f = string.rep}
            print(pcall(function() return t.f() end))
            """));
    }

    [Fact]
    public void An_index_metamethod_may_yield_through_a_field_read()
    {
        Assert.Equal("1\tnil\n2\tv:x\n3\tdone\n", Run("""
            local t = setmetatable({}, {__index = function(_, k) return "v:" .. coroutine.yield(k) end})
            local co = coroutine.wrap(function() return t.name end)
            print(1, co() == "name" and nil)
            print(2, co("x"))
            print(3, "done")
            """));
    }

    [Fact]
    public void A_newindex_metamethod_may_yield_through_a_field_store()
    {
        Assert.Equal("yielded\tk\nstored\tk=1\n", Run("""
            local log
            local t = setmetatable({}, {__newindex = function(_, k, v) coroutine.yield(k) log = k .. "=" .. v end})
            local co = coroutine.create(function() t.k = 1 return "end" end)
            local ok, y = coroutine.resume(co)
            print("yielded", y)
            coroutine.resume(co)
            print("stored", log)
            """));
    }

    [Fact]
    public void A_method_resolved_through_a_class_table_receives_the_object()
    {
        Assert.Equal("7\t9\n", Run("""
            local C = {}
            C.__index = C
            function C.new(v) return setmetatable({v = v}, C) end
            function C:get() return self.v end
            function C:add(n) self.v = self.v + n return self end
            local o = C.new(5)
            print(o:add(2):get(), o:add(2):get())
            """));
    }

    [Fact]
    public void Line_hooks_and_debug_info_see_the_field_instruction_line()
    {
        Assert.Equal("false\t(load):3: attempt to index a nil value (field 'a')\n", Run("""
            local t = {}
            print(pcall(function()
              return t.a.b
            end))
            """));
    }
}
