using System.Collections.Generic;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;
using MethodDefinition = Mono.Cecil.MethodDefinition;
using TypeReference = Mono.Cecil.TypeReference;

namespace Unbound.Host;

static partial class WideFloats
{
    enum Kind { Other, Load, Operation, Merge }

    /// <summary>A value on the evaluation stack: a float32 pushed by an instruction (a load, a call's
    /// result, a constant, conv.r4), the result of float32 arithmetic, the values of one stack slot
    /// where control flow joins, or anything else.</summary>
    sealed class Value
    {
        internal readonly Kind Kind;
        internal readonly Instruction? Producer;
        /// <summary>An operation's operands; the values a join receives from its predecessors.</summary>
        internal readonly List<Value> Inputs = new();
        /// <summary>The operations and joins that take this value.</summary>
        internal readonly List<Value> Users = new();
        internal bool Wide;

        internal Value(Kind kind, Instruction? producer = null)
        {
            Kind = kind;
            Producer = producer;
        }

        internal static readonly Value Other = new(Kind.Other);

        /// <summary>A float32 value. A join is one when what flows into it is; one that has nothing
        /// flowing in yet is not decided (null).</summary>
        internal bool? Float => Kind switch
        {
            Kind.Load or Kind.Operation => true,
            Kind.Merge => Inputs.Count == 0 ? null : Inputs[0].Float,
            _ => false,
        };
    }

    /// <summary>
    /// Which float32 loads of a method must become doubles: those under an arithmetic operation whose
    /// result Mono's double evaluation can change. One operation on float32 values whose result is
    /// rounded to float32 straight away (stored, passed, returned, converted with conv.r4) gives the
    /// same float32 in double (double has more than twice float32's precision, so rounding twice is
    /// rounding once), so such an operation stays as it is. An operation taking another operation's
    /// result, or whose result is compared, converted to another type or used in any other way, is
    /// evaluated in double, with every operation and load of its expression. Null when the method's
    /// stack cannot be followed (it is then widened whole).
    /// </summary>
    static (HashSet<Instruction> Loads, List<Instruction> Unboxed)? WideExpressions(MethodDefinition method)
    {
        var body = method.Body;
        var code = body.Instructions;
        if (code.Count == 0) return (new HashSet<Instruction>(), new List<Instruction>());
        var leaders = new HashSet<Instruction> { code[0] };
        foreach (var instruction in code)
        {
            if (instruction.Operand is Instruction target) leaders.Add(target);
            else if (instruction.Operand is Instruction[] cases) leaders.UnionWith(cases);
            if (instruction.Next != null && instruction.OpCode.FlowControl is FlowControl.Branch or FlowControl.Cond_Branch
                    or FlowControl.Return or FlowControl.Throw)
                leaders.Add(instruction.Next);
        }
        var entries = new Dictionary<Instruction, Value[]>();
        foreach (var handler in body.ExceptionHandlers)
        {
            leaders.Add(handler.TryStart);
            if (handler.TryEnd != null) leaders.Add(handler.TryEnd);
            if (handler.HandlerEnd != null) leaders.Add(handler.HandlerEnd);
            leaders.Add(handler.HandlerStart);
            entries[handler.HandlerStart] = handler.HandlerType is ExceptionHandlerType.Catch or ExceptionHandlerType.Filter
                ? new[] { Value.Other } : System.Array.Empty<Value>();
            if (handler.FilterStart != null)
            {
                leaders.Add(handler.FilterStart);
                entries[handler.FilterStart] = new[] { Value.Other };
            }
        }

        var sensitive = new List<Value>();
        var operations = new List<Value>();
        var loads = new List<Value>();
        var pairs = new List<(Instruction Box, Instruction Unbox, Value Value)>();
        var targets = new HashSet<Instruction>(leaders);

        bool Flow(Instruction target, List<Value> stack)
        {
            if (!entries.TryGetValue(target, out var entry))
            {
                entry = new Value[stack.Count];
                for (int i = 0; i < entry.Length; i++) entry[i] = new Value(Kind.Merge);
                entries[target] = entry;
            }
            if (entry.Length != stack.Count) return false;
            for (int i = 0; i < entry.Length; i++)
            {
                if (entry[i].Kind != Kind.Merge) continue; // a handler's exception object
                entry[i].Inputs.Add(stack[i]);
                if (stack[i].Kind != Kind.Other) stack[i].Users.Add(entry[i]);
            }
            return true;
        }

        var stack = new List<Value>();
        for (int at = 0; at < code.Count; at++)
        {
            var instruction = code[at];
            if (leaders.Contains(instruction))
                stack = entries.TryGetValue(instruction, out var entry) ? new List<Value>(entry) : new List<Value>();
            var opcode = instruction.OpCode;
            int pops = opcode.StackBehaviourPop == StackBehaviour.PopAll ? stack.Count : Pops(method, instruction);
            if (pops < 0 || pops > stack.Count) return null;
            var popped = stack.GetRange(stack.Count - pops, pops);
            stack.RemoveRange(stack.Count - pops, pops);
            var code0 = opcode.Code;
            if (code0 == Code.Dup)
            {
                stack.Add(popped[0]);
                stack.Add(popped[0]);
            }
            else if (code0 is Code.Add or Code.Sub or Code.Mul or Code.Div or Code.Rem or Code.Neg)
            {
                if (popped.Any(v => v.Float == null)) return null;
                if (popped.All(v => v.Float == true))
                {
                    var result = new Value(Kind.Operation, instruction);
                    operations.Add(result);
                    foreach (var operand in popped)
                    {
                        result.Inputs.Add(operand);
                        operand.Users.Add(result);
                    }
                    stack.Add(result);
                }
                else
                {
                    // float32 mixed with another type: the float32 operand's own value counts.
                    sensitive.AddRange(popped.Where(v => v.Float == true));
                    stack.Add(Value.Other);
                }
            }
            else if (code0 == Code.Box && IsSingle(instruction.Operand as TypeReference) && at + 1 < code.Count
                     && code[at + 1].OpCode.Code == Code.Unbox_Any && IsSingle(code[at + 1].Operand as TypeReference)
                     && !targets.Contains(code[at + 1]))
            {
                // Boxing straight back keeps the value as it is.
                pairs.Add((instruction, code[at + 1], popped[0]));
                stack.Add(popped[0]);
                at++;
                continue;
            }
            else
            {
                for (int i = 0; i < popped.Count; i++)
                    if (popped[i].Float != false && !Rounds(method, instruction, i, popped.Count))
                        sensitive.Add(popped[i]);
                if (Pushes(method, instruction))
                {
                    var load = new Value(Kind.Load, instruction);
                    loads.Add(load);
                    stack.Add(load);
                }
                else
                    for (int i = Pushed(method, instruction); i > 0; i--) stack.Add(Value.Other);
            }
            switch (opcode.FlowControl)
            {
                case FlowControl.Branch:
                    if (!Flow((Instruction)instruction.Operand, code0 is Code.Leave or Code.Leave_S ? new List<Value>() : stack)) return null;
                    break;
                case FlowControl.Cond_Branch:
                    if (instruction.Operand is Instruction[] cases)
                    {
                        foreach (var target in cases)
                            if (!Flow(target, stack)) return null;
                    }
                    else if (!Flow((Instruction)instruction.Operand, stack)) return null;
                    goto default;
                case FlowControl.Return:
                case FlowControl.Throw:
                    break;
                default:
                    if (instruction.Next != null && leaders.Contains(instruction.Next) && !Flow(instruction.Next, stack)) return null;
                    break;
            }
        }

        // Every join takes values of one kind (float32 or not), as when it was used.
        var merges = entries.Values.SelectMany(e => e).Where(v => v.Kind == Kind.Merge).ToList();
        foreach (var merge in merges)
            if (merge.Inputs.Any(v => v.Float != merge.Float)) return null;

        // Seeds: operations taking another operation's result (through joins too), and float32
        // results used other than by rounding to float32. A wide value widens what it is made of
        // and what it flows into, so a whole expression is evaluated in double.
        var work = new Stack<Value>();
        void Widen(Value value)
        {
            if (value.Wide || value.Float != true) return;
            value.Wide = true;
            work.Push(value);
        }
        bool HoldsOperation(Value value, HashSet<Value> seen) => value.Kind == Kind.Operation
            || (value.Kind == Kind.Merge && seen.Add(value) && value.Inputs.Any(v => HoldsOperation(v, seen)));
        foreach (var value in sensitive)
            if (value.Kind is Kind.Operation or Kind.Merge && HoldsOperation(value, new HashSet<Value>())) Widen(value);
        foreach (var operation in operations)
            if (operation.Inputs.Any(v => HoldsOperation(v, new HashSet<Value>()))) Widen(operation);
        while (work.Count > 0)
        {
            var value = work.Pop();
            foreach (var input in value.Inputs) Widen(input);
            foreach (var user in value.Users) Widen(user);
        }
        var wide = new HashSet<Instruction>();
        foreach (var load in loads)
        {
            if (!load.Wide) continue;
            if (load.Producer!.Previous is { OpCode.Code: Code.Tail }) return null;
            wide.Add(load.Producer);
        }
        var unboxed = new List<Instruction>();
        foreach (var (box, unbox, value) in pairs)
            if (value.Wide)
            {
                unboxed.Add(box);
                unboxed.Add(unbox);
            }
        return (wide, unboxed);
    }

    /// <summary>How many values the instruction pops (-1: not known).</summary>
    static int Pops(MethodDefinition method, Instruction instruction)
    {
        switch (instruction.OpCode.StackBehaviourPop)
        {
            case StackBehaviour.Pop0:
                return 0;
            case StackBehaviour.Pop1:
            case StackBehaviour.Popi:
            case StackBehaviour.Popref:
                return 1;
            case StackBehaviour.Pop1_pop1:
            case StackBehaviour.Popi_pop1:
            case StackBehaviour.Popi_popi:
            case StackBehaviour.Popi_popi8:
            case StackBehaviour.Popi_popr4:
            case StackBehaviour.Popi_popr8:
            case StackBehaviour.Popref_pop1:
            case StackBehaviour.Popref_popi:
                return 2;
            case StackBehaviour.Popi_popi_popi:
            case StackBehaviour.Popref_popi_popi:
            case StackBehaviour.Popref_popi_popi8:
            case StackBehaviour.Popref_popi_popr4:
            case StackBehaviour.Popref_popi_popr8:
            case StackBehaviour.Popref_popi_popref:
                return 3;
            case StackBehaviour.Varpop:
                switch (instruction.OpCode.Code)
                {
                    case Code.Ret:
                        return method.ReturnType.MetadataType == MetadataType.Void ? 0 : 1;
                    case Code.Newobj:
                        return ((MethodReference)instruction.Operand).Parameters.Count;
                    case Code.Call:
                    case Code.Callvirt:
                    {
                        var callee = (MethodReference)instruction.Operand;
                        return callee.Parameters.Count + (callee.HasThis && !callee.ExplicitThis ? 1 : 0);
                    }
                    case Code.Calli:
                    {
                        var site = (CallSite)instruction.Operand;
                        return site.Parameters.Count + (site.HasThis && !site.ExplicitThis ? 1 : 0) + 1;
                    }
                    default:
                        return -1;
                }
            default:
                return -1;
        }
    }

    /// <summary>How many values the instruction pushes.</summary>
    static int Pushed(MethodDefinition method, Instruction instruction) => instruction.OpCode.StackBehaviourPush switch
    {
        StackBehaviour.Push0 => 0,
        StackBehaviour.Push1_push1 => 2,
        StackBehaviour.Varpush => instruction.Operand switch
        {
            MethodReference callee when instruction.OpCode.Code == Code.Newobj => 1,
            MethodReference callee => callee.ReturnType.MetadataType == MetadataType.Void ? 0 : 1,
            CallSite site => site.ReturnType.MetadataType == MetadataType.Void ? 0 : 1,
            _ => 0,
        },
        _ => 1,
    };

    /// <summary>Whether the instruction rounds its operand <paramref name="index"/> (of
    /// <paramref name="count"/>, 0 the deepest) to float32: a store into a float32 location, a
    /// float32 argument or return value, conv.r4; pop discards it.</summary>
    static bool Rounds(MethodDefinition method, Instruction instruction, int index, int count)
    {
        bool last = index == count - 1;
        switch (instruction.OpCode.Code)
        {
            case Code.Pop:
            case Code.Conv_R4:
                return true;
            case Code.Stloc_0: return IsSingle(Local(method, 0));
            case Code.Stloc_1: return IsSingle(Local(method, 1));
            case Code.Stloc_2: return IsSingle(Local(method, 2));
            case Code.Stloc_3: return IsSingle(Local(method, 3));
            case Code.Stloc:
            case Code.Stloc_S:
                return IsSingle((instruction.Operand as VariableDefinition)?.VariableType);
            case Code.Starg:
            case Code.Starg_S:
                return IsSingle((instruction.Operand as ParameterDefinition)?.ParameterType);
            case Code.Stfld:
            case Code.Stsfld:
                return last && instruction.Operand is FieldReference field && IsSingle(Resolve(field.FieldType, field));
            case Code.Stelem_R4:
            case Code.Stind_R4:
                return last;
            case Code.Stelem_Any:
            case Code.Stobj:
            case Code.Box:
                return last && IsSingle(instruction.Operand as TypeReference);
            case Code.Ret:
                return IsSingle(method.ReturnType);
            case Code.Call:
            case Code.Callvirt:
            case Code.Newobj:
            {
                var callee = (MethodReference)instruction.Operand;
                int parameter = index - (instruction.OpCode.Code != Code.Newobj && callee.HasThis && !callee.ExplicitThis ? 1 : 0);
                return parameter >= 0 && parameter < callee.Parameters.Count
                    && IsSingle(Resolve(callee.Parameters[parameter].ParameterType, callee));
            }
            case Code.Calli:
            {
                var site = (CallSite)instruction.Operand;
                int parameter = index - (site.HasThis && !site.ExplicitThis ? 1 : 0);
                return parameter >= 0 && parameter < site.Parameters.Count && IsSingle(site.Parameters[parameter].ParameterType);
            }
            default:
                return false;
        }
    }
}
