"use strict";

//**************************************************************************
// CodeFlow.js:
//
// Uses the ApiExtension Code library to perform analysis about data flow
// within a function.
//
// Usage: !dflow <address>
//
//     Disassembles the function containing <address>, finds any instruction
//     in the control flow which influences the source operands of the instruction
//     at <address> and adds such instruction to the output collection.
//
// @TODO:
// 
//     - !dflow should be able to work without any symbolic information given a range
//       of assembly instructions to consider for the analysis
//

var __diagLevel = 0; // 1 is most important, increasingly less

function __diag(level)
{
    return (level <= __diagLevel);
}

class __TraceDataFlow
{
    constructor(disassembler, functionDisassembly, address)
    {
        this.__disassembler = disassembler;
        this.__functionDisassembly = functionDisassembly;
        this.__address = address;
    }

    toString()
    {
        var instr = this.__disassembler.DisassembleInstructions(this.__address).First();

        var str = "Traced data flow of " + this.__address.toString(16) + ": " + instr +") for source operands { ";
        var first = true;
        for (var operand of instr.Operands)
        {
            if (operand.Attributes.IsInput)
            {
                if (!first)
                {
                    str += ", ";
                }
                first = false;
                str += operand;
            }
        }
        str += " }";

        return str;
    }

    // __findBasicBlock:
    //
    // Finds a basic block containing the instruction at the given address.
    //
    __findBasicBlock(address)
    {
        var predicate = function(b) { return (address.compareTo(b.StartAddress) >= 0 && address.compareTo(b.EndAddress) < 0); } 
        return this.__functionDisassembly.BasicBlocks.First(predicate);
    }

    // __dumpRegisterSet:
    //
    // Diagnostic method to dump a register set.
    //
    __dumpRegisterSet(registerSet)
    {
        host.diagnostics.debugLog("    Register Set== ");
        for (var setReg of registerSet)
        {
            host.diagnostics.debugLog("'", this.__disassembler.GetRegister(setReg), "'(", setReg, "), ");
        }
        host.diagnostics.debugLog("\n");
    }

    // __addRegisterReferences:
    //
    // Adds a register (and all sub-registers) to a register set.
    //
    __addRegisterReferences(registerSet, reg)
    {
        registerSet.add(reg.Id);
        for(var subReg of reg.GetSubRegisters())
        {
            registerSet.add(subReg.Id);
        }
    }

    // __removeRegisterReferences:
    //
    // Removes a register (and all sub-registers) from a register set.
    //
    __removeRegisterReferences(registerSet, reg)
    {
        registerSet.delete(reg.Id);
        for(var subReg of reg.GetSubRegisters())
        {
            registerSet.delete(subReg.Id);
        }
    }

    // __hasRegisterReference
    //
    // Is the register 'reg' (or any sub-register) in the register set.
    //
    __hasRegisterReference(registerSet, reg)
    {
        if (__diag(3))
        {
            this.__dumpRegisterSet(registerSet);
            host.diagnostics.debugLog("    Comparison Set== '", reg, "'(", reg.Id, "), ");
            for( var subReg of reg.GetSubRegisters())
            {
                host.diagnostics.debugLog("'", subReg, "'(", subReg.Id, "), ");
            }
            host.diagnostics.debugLog("\n");
        }

        if (registerSet.has(reg.Id))
        {
            return true;
        }

        for (var subReg of reg.GetSubRegisters())
        {
            if (registerSet.has(subReg.Id))
            {
                return true;
            }
        }

        return false;
    }

    // __hasWriteOfMemory:
    //
    // Determines whether an operand in the set writes to memory in the memory reference set.
    //
    __hasWriteOfMemory(operandSet, memoryReferences)
    {
        for (var operand of operandSet)
        {
            var attrs = operand.Attributes;
            if (attrs.IsOutput && attrs.IsMemoryReference)
            {
                for (var ref of memoryReferences)
                {
                    if (__diag(5))
                    {
                        host.diagnostics.debugLog("    Checking '" + operand + "' against '" + ref + "'\n");
                    }

                    if (operand.ReferencesSameMemory(ref))
                    {
                        if (__diag(5))
                        {
                            host.diagnostics.debugLog("         Match on memory write!\n");
                        }
                        return true;
                    }
                }
            }
        }

        return false;
    }

    // __writesToRegister
    //
    // Determines whether an operand is a write to a register in a register reference set.
    //
    __writesToRegister(instr, operandSet, registerReferences)
    {
        for (var operand of operandSet)
        {
            var attrs = operand.Attributes;
            if (attrs.IsOutput && attrs.IsRegister)
            {
                for (var reg of registerReferences)
                {
                    if (operand.UsesRegister(reg))
                    {
                        return true;
                    }
                }
            }
        }

        if (instr.Attributes.IsCall)
        {
            var retReg = instr.Attributes.ReturnRegister;

            if (__diag(2))
            {
                host.diagnostics.debugLog("Check for return register '", retReg, "' in instruction '", instr, "'\n");
            }
            
            if (retReg !== undefined)
            {
                if (__diag(2))
                {
                    host.diagnostics.debugLog("    Check id == ", retReg.Id, "\n");
                }

                if (this.__hasRegisterReference(registerReferences, retReg))
                {
                    return true;
                }
            }
        }

        return false;
    }

    // __kill:
    //
    // Removes a set of registers from the register set.
    //
    __kill(registerSet, registerReferences)
    {
        for(var reg of registerSet)
        {
            this.__removeRegisterReferences(registerReferences, reg);
        }
    }

    // __live:
    //
    // Makes a set of registers live in the register set.
    //
    __live(registerSet, registerReferences)
    {
        for (var reg of registerSet)
        {
            this.__addRegisterReferences(registerReferences, reg);
        }
    }

    // __killMemoryReference:
    //
    // Removes a memory reference from the set of live memory references.
    //
    __killMemoryReference(memRef, memoryReferences)
    {
        var i = 0;
        var len = memoryReferences.length;
        while (i < len)
        {
            if (memRef.ReferencesSameMemory(memoryReferences[i]))
            {
                memoryReferences.splice(i, 1);
                break;
            }
            ++i;
        }
    }

    // __liveMemoryReference:
    //
    // Adds a memory reference to the set of live memory references.
    //
    __liveMemoryReference(memRef, memoryReferences)
    {
        var i = 0;
        var len = memoryReferences.length;
        while (i < len)
        {
            if (memRef.ReferencesSameMemory(memoryReferences[i]))
            {
                return;
            }
            ++i;
        }
        memoryReferences.push(memRef);
    }

    // __addCallInputRegisters:
    //
    // Make an attempt to determine what were register inputs to the call and add them to the
    // lifetime set.  This is done by looking at the call target, disassembling it, looking
    // at the first instruction and whether any variables are live in registers as of the 
    // first instruction of the call target.
    //
    __addCallInputRegisters(instr, registerReferences)
    {
        if (__diag(4))
        {
            host.diagnostics.debugLog("Looking at call for inputs: '", instr, "'\n");
        }
   
        var callTarget;
        try
        {
            //
            // We may not be able to read this.  If we cannot, don't bother.
            //
            var opCount = instr.Operands.Count();
            if (opCount == 1)
            {
                var destOperand = instr.Operands.First();
                var attrs = destOperand.Attributes;

                if (attrs.IsImmediate)
                {
                    callTarget = destOperand.ImmediateValue;
                    if (__diag(2))
                    {
                        host.diagnostics.debugLog("Call has direct target: '", callTarget, "'\n");
                    }
                }
                else if (attrs.HasImmediate && attrs.IsMemoryReference && destOperand.Registers.Count() == 0)
                {
                    //
                    // @TODO: This should be sizeof(*) and *NOT* hard code to 64-bit.
                    //
                    var indirectCallTarget = destOperand.ImmediateValue;
                    if (__diag(2))
                    {
                        host.diagnostics.debugLog("Call has indirect target: '", indirectCallTarget, "'\n");
                    }

                    var tableRead = host.memory.readMemoryValues(indirectCallTarget, 1, 8, false);
                    callTarget = tableRead[0];

                    if (__diag(2))
                    {
                        host.diagnostics.debugLog("    Call destination read: '", callTarget, "'\n");
                    }
                }
            }
        }
        catch(exc1)
        {
        }

        try
        {
            //
            // We may not be able to read and disassemble the call target.  If we cannot, don't bother.
            //
            if (callTarget !== undefined)
            {
                //
                // We found the call target.  Disassemble it, get the first instruction, and go through all
                // live variables which are enregistered at this point.
                //
                var targetDis = this.__disassembler.DisassembleInstructions(callTarget);
                var firstInstr = targetDis.First();
                if (__diag(1))
                {
                    host.diagnostics.debugLog("Looking at call destination instruction '", firstInstr, "' for live variables.\n");
                }
                for (var liveVar of firstInstr.LiveVariables)
                {
                    if (liveVar.LocationKind == "Register" && liveVar.Offset == 0)
                    {
                        if (__diag(1))
                        {
                            host.diagnostics.debugLog("    Found call input register '", liveVar.Register, "'\n");
                        }
                        this.__addRegisterReferences(registerReferences, liveVar.Register);
                    }
                }
            }
        }
        catch(exc2)
        {
        }
    }

    // __reformLifetimes
    //
    // Performs any kills of written registers or memory references and 
    // adds all source registers and memory references to the set
    //
    // @TODO: If we pass the operandSet instead of instr, the second for...of will crash.  Fix!
    //
    __reformLifetimes(instr, registerReferences, memoryReferences)
    {
        if (instr.Attributes.IsCall)
        {
            var setCopy = new Set(registerReferences);
            for (var regId of setCopy)
            {
                var preserves = instr.PreservesRegisterValue(regId);
                if (__diag(3))
                {
                    host.diagnostics.debugLog("    Check preservation of (", regId, ") == ", preserves, "\n");
                }
                if (!preserves)
                {
                    this.__removeRegisterReferences(registerReferences, this.__disassembler.GetRegister(regId));
                }
            }
        }
        else
        {
            for (var operand of instr.Operands /*operandSet*/)
            {
                var attrs = operand.Attributes;
                if (attrs.IsOutput)
                {
                    if (attrs.IsRegister)
                    {
                        //
                        // Kill the registers.
                        //
                        this.__kill(operand.Registers, registerReferences);
                    }
                    else if (attrs.IsMemoryReference)
                    {
                        //
                        // Is there a memory reference in the array.
                        //
                        this.__killMemoryReference(operand, memoryReferences);
                    }
                }
            }
        }

        for (var operand of instr.Operands /*operandSet*/)
        {
            var attrs = operand.Attributes;
            if (attrs.IsInput)
            {
                this.__live(operand.Registers, registerReferences);
                if (attrs.IsMemoryReference)
                {
                    this.__liveMemoryReference(operand, memoryReferences);
                }
            }
        }

        //
        // If we have a call and can determine register passed values, do so.
        //
        if (instr.Attributes.IsCall)
        {
            this.__addCallInputRegisters(instr, registerReferences);
        }
    }

    // __dbgOutputSets:
    //
    // Diagnostic helper to output the live register and memory sets.
    //
    __dbgOutputSets(msg, registerReferences, memoryReferences)
    {
        if (__diag(2))
        {
            host.diagnostics.debugLog(msg, "\n");
            for (var regRef of registerReferences)
            {
                host.diagnostics.debugLog("    ", regRef, "\n");
            }
            for (var memRef of memoryReferences)
            {
                host.diagnostics.debugLog("    ", memRef, "\n");
            }
        }
    }

    // __scanBlockBackwards:
    //
    // For the given basic block, an instruction within that block
    // scan the block backwards looking for instructions that write to the source operands. 
    //
    // If one of the sources is written to, kill it from the scan.
    //
    *__scanBlockBackwards(basicBlock, instruction, registerReferences, memoryReferences, skipInstruction)
    {
        if (this.__exploredBlocks.has(basicBlock.StartAddress))
        {
            return;
        }
        this.__exploredBlocks.add(basicBlock.StartAddress);

        this.__dbgOutputSets("Scan: ", registerReferences, memoryReferences);

        //
        // Get the set of instructions in the basic block and walk them backwards.
        //
        var blockBackwards = basicBlock.Instructions.Reverse();
        var hitInstr = false;
        var address = instruction.Address;
        for (var instr of blockBackwards)
        {
            //
            // We have to get to the instruction in reverse first.
            //
            if (!hitInstr)
            {
                if (instr.Address.compareTo(address) == 0)
                {
                    hitInstr = true;
                }

                if (!hitInstr || skipInstruction)
                {
                    continue;
                }
            }

            //
            // This is in the basic block *BEFORE* the starting instruction.
            //
            if (__diag(2))
            {
                host.diagnostics.debugLog("Looking at instruction '", instr, "'\n");
            }

            //
            // If we have an instruction that writes to the same memory, it matches.
            //
            // If we have an instruction that writes to a referenced register, it matches -- add the source registers,
            //     and kill the destination registers.
            //
            var hasSameMemRef = this.__hasWriteOfMemory(instr.Operands, memoryReferences);
            var hasRegRef = this.__writesToRegister(instr, instr.Operands, registerReferences);

            if (__diag(5))
            {
                host.diagnostics.debugLog("    Has write: '", hasSameMemRef, "'\n");
                host.diagnostics.debugLog("    Has reg  : '", hasRegRef, "'\n");
            }

            if (hasSameMemRef || hasRegRef)
            {
                yield new host.indexedValue(instr, [instr.Address]);

                //
                // Once we have yielded that instruction, change the live register set.  Kill anything written
                // in instr and add anything read.
                //
                this.__reformLifetimes(instr, registerReferences, memoryReferences);
                this.__dbgOutputSets("Reform: ", registerReferences, memoryReferences);
            }
        }

        if (__diag(1))
        {
            host.diagnostics.debugLog("Traverse to blocks:\n");
            for (var inboundFlow of basicBlock.InboundControlFlows)
            {
                host.diagnostics.debugLog("    ", inboundFlow.LinkedBlock, "\n");
            }
        }

        //
        // The basic block has entries from other blocks, scan them.
        //
        for (var inboundFlow of basicBlock.InboundControlFlows)
        {
            var childSet = new Set(registerReferences);
            var childMem = memoryReferences.slice();
            yield* this.__scanBlockBackwards(inboundFlow.LinkedBlock, inboundFlow.SourceInstruction, childSet, childMem, false);
        }
    }

    // [Symbol.iterator]:
    //
    // Find all instructions in the data flow.
    //
    *[Symbol.iterator]()
    {
        this.__exploredBlocks = new Set();

        //
        // Find the starting instruction.  It is obviously part of the data flow.
        //
        var startingBlock = this.__findBasicBlock(this.__address);
        var startingInstruction = startingBlock.Instructions.getValueAt(this.__address);
        yield new host.indexedValue(startingInstruction, [startingInstruction.Address]);

        var memoryReferences = [];
        var registerReferences = new Set();

        if (__diag(2))
        {
            host.diagnostics.debugLog("Starting Instruction: ", startingInstruction, "\n");
        }
        for (var operand of startingInstruction.Operands)
        {
            if (__diag(5))
            {
                host.diagnostics.debugLog("Is '", operand, "' a source?\n");
            }
            var attrs = operand.Attributes;
            if (attrs.IsInput)
            {
                if (__diag(5))
                {
                    host.diagnostics.debugLog("    Yes\n");
                }
                if (attrs.IsMemoryReference)
                {
                    if (__diag(5))
                    {
                        host.diagnostics.debugLog("MemRef: ", operand, "\n");
                    }
                    memoryReferences.push(operand);
                }

                for (var reg of operand.Registers)
                {
                    if (__diag(5))
                    {
                        host.diagnostics.debugLog("RegRef: ", reg, "\n");
                    }
                    this.__addRegisterReferences(registerReferences, reg);
                }
            }
        }

        yield* this.__scanBlockBackwards(startingBlock, startingInstruction, registerReferences, memoryReferences, true);
    }

    // getDimensionality:
    //
    // Return the dimensionality of our indexer (1 -- by instruction address)
    //
    getDimensionality()
    {
        return 1;
    }

    // getValueAt:
    //
    // Return the instruction at the given address.  @TODO: It would be nice if this only allowed indexing
    // instructions in the data flow. 
    //
    getValueAt(addr)
    {
        var basicBlock = this.__findBasicBlock(this.__address);
        return basicBlock.Instructions.getValueAt(addr);
    }
}

// __getDisassemblyInfo:
//
// Gets information about where to disassemble for the data flow.  From the given address, this attempts
// to go back and find the start of the function to walk its dataflow.
//
function __getDisassemblyInfo(instrAddr)
{
    // 
    // If there is no specified address, grab IP.
    // @TODO: This should *NOT* directly reference RIP.  The stack frame should have an abstract IP/SP/FP
    //
    if (instrAddr === undefined)
    {
        if (__diag(5))
        {
            host.diagnostics.debugLog("Override to IP, instrAddr\n");
        }
        instrAddr = host.currentThread.Registers.User.rip;
    }

    //
    // If we can get the disassembly info from the new host.getModuleContainingSymbol, do so
    //
    var func;
    try
    {
        func = host.getModuleContainingSymbol(instrAddr);
    }
    catch(exc)
    {
    }

    if (func === undefined)
    {
        //
        // There should be a better way of doing this.  We should also use address instead!
        //
        var frame = host.currentThread.Stack.Frames[0];
        var frameStr = frame.toString();

        //
        // MODULE!NAME + OFFSET
        //
        var idx = frameStr.indexOf('+');
        if (idx != -1)
        {
            frameStr = frameStr.substr(0, idx).trim();
        }

        //
        // MODULE!NAME
        //
        var bangIdx = frameStr.indexOf('!');
        if (idx == -1)
        {
            throw new Error("Unable to find function name to disassemble");
        }

        var moduleName = frameStr.substr(0, bangIdx);
        var funcName = frameStr.substr(bangIdx + 1);

        if (__diag(2))
        {
            host.diagnostics.debugLog("ModuleName = '", moduleName, "'; funcName = '", funcName, "'\n");
        }

        func = host.getModuleSymbol(moduleName, funcName);
    }

    return { function: func, address: instrAddr };
}

// __CodeExtension:
//
// Provides an extension on Debugger.Utility.Code
//
class __CodeExtension
{
    TraceDataFlow(address)
    {
        var disassemblyInfo = __getDisassemblyInfo(address);
        var disassembler = host.namespace.Debugger.Utility.Code.CreateDisassembler();
        var funcDisassembly = disassembler.DisassembleFunction(disassemblyInfo.function, true);
        return new __TraceDataFlow(disassembler, funcDisassembly, disassemblyInfo.address);
    }
}

// __traceDataFlow:
//
// Function alias for !dflow
//
function __traceDataFlow(address)
{
    return host.namespace.Debugger.Utility.Code.TraceDataFlow(address);
}

// __disassembleCode:
//
// Function alias for !dis
//
function __disassembleCode(addressObj)
{
    var dbg = host.namespace.Debugger;

    if (addressObj === undefined)
    {
        //
        // @TODO:
        // This is *NOT* generic.  This is *DBGENG* specific.  We should get an IP from the stack.
        //
        addressObj = dbg.State.PseudoRegisters.RegisterAliases.ip.address;
    }

    return dbg.Utility.Code.CreateDisassembler().DisassembleInstructions(addressObj);
}

// __InstructionExtension:
//
// Provides an extension on an instruction
//
class __InstructionExtension
{
    get SourceDataFlow()
    {
        return __traceDataFlow(this.Address);
    }
}

// initializeScript:
//
// Initializes our script.  Registers our extensions and !dflow alias.
//
function initializeScript()
{
    return [new host.apiVersionSupport(1, 2),
            new host.namespacePropertyParent(__CodeExtension, "Debugger.Models.Utility", "Debugger.Models.Utility.Code", "Code"),
            new host.namedModelParent(__InstructionExtension, "Debugger.Models.Utility.Code.Instruction"),
            new host.functionAlias(__traceDataFlow, "dflow"),
            new host.functionAlias(__disassembleCode, "dis")];
}

// SIG // Begin signature block
// SIG // MIIl9gYJKoZIhvcNAQcCoIIl5zCCJeMCAQExDzANBglg
// SIG // hkgBZQMEAgEFADB3BgorBgEEAYI3AgEEoGkwZzAyBgor
// SIG // BgEEAYI3AgEeMCQCAQEEEBDgyQbOONQRoqMAEEvTUJAC
// SIG // AQACAQACAQACAQACAQAwMTANBglghkgBZQMEAgEFAAQg
// SIG // 8Vd5mJ3w6q+ClnHrnQIoaqCXDHQ0K4ZNgLaaX8x0Gjag
// SIG // gguBMIIFCTCCA/GgAwIBAgITMwAABHCfcxf1mw5kZgAA
// SIG // AAAEcDANBgkqhkiG9w0BAQsFADB+MQswCQYDVQQGEwJV
// SIG // UzETMBEGA1UECBMKV2FzaGluZ3RvbjEQMA4GA1UEBxMH
// SIG // UmVkbW9uZDEeMBwGA1UEChMVTWljcm9zb2Z0IENvcnBv
// SIG // cmF0aW9uMSgwJgYDVQQDEx9NaWNyb3NvZnQgQ29kZSBT
// SIG // aWduaW5nIFBDQSAyMDEwMB4XDTIyMDEyNzE5MzIyMVoX
// SIG // DTIzMDEyNjE5MzIyMVowfzELMAkGA1UEBhMCVVMxEzAR
// SIG // BgNVBAgTCldhc2hpbmd0b24xEDAOBgNVBAcTB1JlZG1v
// SIG // bmQxHjAcBgNVBAoTFU1pY3Jvc29mdCBDb3Jwb3JhdGlv
// SIG // bjEpMCcGA1UEAxMgTWljcm9zb2Z0IFdpbmRvd3MgS2l0
// SIG // cyBQdWJsaXNoZXIwggEiMA0GCSqGSIb3DQEBAQUAA4IB
// SIG // DwAwggEKAoIBAQCVDWJkrimipfHoYzaKQr7se+gP72Rz
// SIG // FjWGY4S7MjjHKalJOqO4c5cJ18CyWs+VVT+bvCsyQ4Zn
// SIG // EK98JePG4DJ2Osxsyz9oY3Pc/E9XFa21fCrGJCiWLrCg
// SIG // hP3soGkAXyZDL3/nZr9CsFu1zThDnwEpjKIlMj/YA09/
// SIG // cNE25pmMnwpoWjZwaxXC1TM6SZHhOV271XlsgRn6OjFG
// SIG // Wdw7fmouu/tc6uu8MKKb+PZ0KAyDNa0gOjPUB9j6dZKD
// SIG // fr7p/D1CSvs2gSFN6f+v0XXUNp/sn1XH8x8nSSrt7LFJ
// SIG // Dq169hM9LaSGDjQ4iF71UH26lBr4pMJzF7jbLRNaMQr0
// SIG // XGqZAgMBAAGjggF9MIIBeTAfBgNVHSUEGDAWBgorBgEE
// SIG // AYI3CgMUBggrBgEFBQcDAzAdBgNVHQ4EFgQUFGZ2RTD4
// SIG // k22AqdFnqXZHPvpSWzQwVAYDVR0RBE0wS6RJMEcxLTAr
// SIG // BgNVBAsTJE1pY3Jvc29mdCBJcmVsYW5kIE9wZXJhdGlv
// SIG // bnMgTGltaXRlZDEWMBQGA1UEBRMNMjI5OTAzKzQ2OTA2
// SIG // MjAfBgNVHSMEGDAWgBTm/F97uyIAWORyTrX0IXQjMubv
// SIG // rDBWBgNVHR8ETzBNMEugSaBHhkVodHRwOi8vY3JsLm1p
// SIG // Y3Jvc29mdC5jb20vcGtpL2NybC9wcm9kdWN0cy9NaWND
// SIG // b2RTaWdQQ0FfMjAxMC0wNy0wNi5jcmwwWgYIKwYBBQUH
// SIG // AQEETjBMMEoGCCsGAQUFBzAChj5odHRwOi8vd3d3Lm1p
// SIG // Y3Jvc29mdC5jb20vcGtpL2NlcnRzL01pY0NvZFNpZ1BD
// SIG // QV8yMDEwLTA3LTA2LmNydDAMBgNVHRMBAf8EAjAAMA0G
// SIG // CSqGSIb3DQEBCwUAA4IBAQB6oT/eW1Dk6SENj/OuFcIf
// SIG // vLpUoLivcfVbQwNtpk+lNWF4cG3endkCyhiALEJMIPuS
// SIG // JG07OjcK0ZhKvvLKdrZ8EaYNwgsdBOQGhtEb9yFuG+X+
// SIG // O0VFSVq2o3yKJLImJh9WS6/BX13mdEuwASb8Zmtf613w
// SIG // C2sB7wdApagduw/5yoXEZsP5M0bFNFqTmt8xAVNyaNZK
// SIG // zjDyW8vfEQnASCraD+OSyKN6nPGUJSCDk6uGnWYLbsQ2
// SIG // uZARXw74kGACVNgnrtlL20vnbvbM6amV6nJs/MsvU2MV
// SIG // Xng/xM+J0GswE+XbxjO+bDVxwKIPLgGZ5t0ly0jsRjPj
// SIG // L4pVZl9AeScNMIIGcDCCBFigAwIBAgIKYQxSTAAAAAAA
// SIG // AzANBgkqhkiG9w0BAQsFADCBiDELMAkGA1UEBhMCVVMx
// SIG // EzARBgNVBAgTCldhc2hpbmd0b24xEDAOBgNVBAcTB1Jl
// SIG // ZG1vbmQxHjAcBgNVBAoTFU1pY3Jvc29mdCBDb3Jwb3Jh
// SIG // dGlvbjEyMDAGA1UEAxMpTWljcm9zb2Z0IFJvb3QgQ2Vy
// SIG // dGlmaWNhdGUgQXV0aG9yaXR5IDIwMTAwHhcNMTAwNzA2
// SIG // MjA0MDE3WhcNMjUwNzA2MjA1MDE3WjB+MQswCQYDVQQG
// SIG // EwJVUzETMBEGA1UECBMKV2FzaGluZ3RvbjEQMA4GA1UE
// SIG // BxMHUmVkbW9uZDEeMBwGA1UEChMVTWljcm9zb2Z0IENv
// SIG // cnBvcmF0aW9uMSgwJgYDVQQDEx9NaWNyb3NvZnQgQ29k
// SIG // ZSBTaWduaW5nIFBDQSAyMDEwMIIBIjANBgkqhkiG9w0B
// SIG // AQEFAAOCAQ8AMIIBCgKCAQEA6Q5kUHlntcTj/QkATJ6U
// SIG // rPdWaOpE2M/FWE+ppXZ8bUW60zmStKQe+fllguQX0o/9
// SIG // RJwI6GWTzixVhL99COMuK6hBKxi3oktuSUxrFQfe0dLC
// SIG // iR5xlM21f0u0rwjYzIjWaxeUOpPOJj/s5v40mFfVHV1J
// SIG // 9rIqLtWFu1k/+JC0K4N0yiuzO0bj8EZJwRdmVMkcvR3E
// SIG // VWJXcvhnuSUgNN5dpqWVXqsogM3Vsp7lA7Vj07IUyMHI
// SIG // iiYKWX8H7P8O7YASNUwSpr5SW/Wm2uCLC0h31oVH1RC5
// SIG // xuiq7otqLQVcYMa0KlucIxxfReMaFB5vN8sZM4BqiU2j
// SIG // amZjeJPVMM+VHwIDAQABo4IB4zCCAd8wEAYJKwYBBAGC
// SIG // NxUBBAMCAQAwHQYDVR0OBBYEFOb8X3u7IgBY5HJOtfQh
// SIG // dCMy5u+sMBkGCSsGAQQBgjcUAgQMHgoAUwB1AGIAQwBB
// SIG // MAsGA1UdDwQEAwIBhjAPBgNVHRMBAf8EBTADAQH/MB8G
// SIG // A1UdIwQYMBaAFNX2VsuP6KJcYmjRPZSQW9fOmhjEMFYG
// SIG // A1UdHwRPME0wS6BJoEeGRWh0dHA6Ly9jcmwubWljcm9z
// SIG // b2Z0LmNvbS9wa2kvY3JsL3Byb2R1Y3RzL01pY1Jvb0Nl
// SIG // ckF1dF8yMDEwLTA2LTIzLmNybDBaBggrBgEFBQcBAQRO
// SIG // MEwwSgYIKwYBBQUHMAKGPmh0dHA6Ly93d3cubWljcm9z
// SIG // b2Z0LmNvbS9wa2kvY2VydHMvTWljUm9vQ2VyQXV0XzIw
// SIG // MTAtMDYtMjMuY3J0MIGdBgNVHSAEgZUwgZIwgY8GCSsG
// SIG // AQQBgjcuAzCBgTA9BggrBgEFBQcCARYxaHR0cDovL3d3
// SIG // dy5taWNyb3NvZnQuY29tL1BLSS9kb2NzL0NQUy9kZWZh
// SIG // dWx0Lmh0bTBABggrBgEFBQcCAjA0HjIgHQBMAGUAZwBh
// SIG // AGwAXwBQAG8AbABpAGMAeQBfAFMAdABhAHQAZQBtAGUA
// SIG // bgB0AC4gHTANBgkqhkiG9w0BAQsFAAOCAgEAGnTvV08p
// SIG // e8QWhXi4UNMi/AmdrIKX+DT/KiyXlRLl5L/Pv5PI4zSp
// SIG // 24G43B4AvtI1b6/lf3mVd+UC1PHr2M1OHhthosJaIxrw
// SIG // jKhiUUVnCOM/PB6T+DCFF8g5QKbXDrMhKeWloWmMIpPM
// SIG // dJjnoUdD8lOswA8waX/+0iUgbW9h098H1dlyACxphnY9
// SIG // UdumOUjJN2FtB91TGcun1mHCv+KDqw/ga5uV1n0oUbCJ
// SIG // SlGkmmzItx9KGg5pqdfcwX7RSXCqtq27ckdjF/qm1qKm
// SIG // huyoEESbY7ayaYkGx0aGehg/6MUdIdV7+QIjLcVBy78d
// SIG // TMgW77Gcf/wiS0mKbhXjpn92W9FTeZGFndXS2z1zNfM8
// SIG // rlSyUkdqwKoTldKOEdqZZ14yjPs3hdHcdYWch8ZaV4XC
// SIG // v90Nj4ybLeu07s8n07VeafqkFgQBpyRnc89NT7beBVaX
// SIG // evfpUk30dwVPhcbYC/GO7UIJ0Q124yNWeCImNr7KsYxu
// SIG // qh3khdpHM2KPpMmRM19xHkCvmGXJIuhCISWKHC1g2TeJ
// SIG // QYkqFg/XYTyUaGBS79ZHmaCAQO4VgXc+nOBTGBpQHTiV
// SIG // mx5mMxMnORd4hzbOTsNfsvU9R1O24OXbC2E9KteSLM43
// SIG // Wj5AQjGkHxAIwlacvyRdUQKdannSF9PawZSOB3slcUSr
// SIG // Bmrm1MbfI5qWdcUxghnNMIIZyQIBATCBlTB+MQswCQYD
// SIG // VQQGEwJVUzETMBEGA1UECBMKV2FzaGluZ3RvbjEQMA4G
// SIG // A1UEBxMHUmVkbW9uZDEeMBwGA1UEChMVTWljcm9zb2Z0
// SIG // IENvcnBvcmF0aW9uMSgwJgYDVQQDEx9NaWNyb3NvZnQg
// SIG // Q29kZSBTaWduaW5nIFBDQSAyMDEwAhMzAAAEcJ9zF/Wb
// SIG // DmRmAAAAAARwMA0GCWCGSAFlAwQCAQUAoIIBBDAZBgkq
// SIG // hkiG9w0BCQMxDAYKKwYBBAGCNwIBBDAcBgorBgEEAYI3
// SIG // AgELMQ4wDAYKKwYBBAGCNwIBFTAvBgkqhkiG9w0BCQQx
// SIG // IgQgohfhR3HhTgB0yf0ye4cb5ufm23Z7jaz/DkRz8NyY
// SIG // F4wwPAYKKwYBBAGCNwoDHDEuDCxzUFk3eFBCN2hUNWc1
// SIG // SEhyWXQ4ckRMU005VnVaUnVXWmFlZjJlMjJSczU0PTBa
// SIG // BgorBgEEAYI3AgEMMUwwSqAkgCIATQBpAGMAcgBvAHMA
// SIG // bwBmAHQAIABXAGkAbgBkAG8AdwBzoSKAIGh0dHA6Ly93
// SIG // d3cubWljcm9zb2Z0LmNvbS93aW5kb3dzMA0GCSqGSIb3
// SIG // DQEBAQUABIIBAAW8LYzxntsqv6tuwRGUtP+GBOgNFPLu
// SIG // taXLFpwngFYTyBtyFgw9FnZZyx9OeAJtcu/KL/KA0dyz
// SIG // qMlGcQIsWQLB3AJpqnD8yfzzvGLhn+bMBzeitjdZa/XE
// SIG // N23AGkIwDnG3lidDBQ8GElMwY7tcFLdyLBT5BurXLamH
// SIG // 01btOzHIQIsfPYeHp3Fb4IhRk+jwAoWYOSDk8xRwXYmu
// SIG // sEvJNbb73TJMtn28vZigsUqWSMHx1nlrVFMzAr7Ome9l
// SIG // z2HTxzKDyow7rGkU3WEDKr7C1I444BAJdBNBfR1IVmHI
// SIG // 8kUPu0xG3zo+GU73jsMZeg+2iPrQ3J7ePnFojR3WtoI5
// SIG // K8WhghcAMIIW/AYKKwYBBAGCNwMDATGCFuwwghboBgkq
// SIG // hkiG9w0BBwKgghbZMIIW1QIBAzEPMA0GCWCGSAFlAwQC
// SIG // AQUAMIIBUQYLKoZIhvcNAQkQAQSgggFABIIBPDCCATgC
// SIG // AQEGCisGAQQBhFkKAwEwMTANBglghkgBZQMEAgEFAAQg
// SIG // CMCH1vhys0/b27gJlNZvrMMhkTM+C6Meb6FgRSEs4hwC
// SIG // BmNIF1wngRgTMjAyMjEwMTkyMTMxMjguMzA4WjAEgAIB
// SIG // 9KCB0KSBzTCByjELMAkGA1UEBhMCVVMxEzARBgNVBAgT
// SIG // Cldhc2hpbmd0b24xEDAOBgNVBAcTB1JlZG1vbmQxHjAc
// SIG // BgNVBAoTFU1pY3Jvc29mdCBDb3Jwb3JhdGlvbjElMCMG
// SIG // A1UECxMcTWljcm9zb2Z0IEFtZXJpY2EgT3BlcmF0aW9u
// SIG // czEmMCQGA1UECxMdVGhhbGVzIFRTUyBFU046M0U3QS1F
// SIG // MzU5LUEyNUQxJTAjBgNVBAMTHE1pY3Jvc29mdCBUaW1l
// SIG // LVN0YW1wIFNlcnZpY2WgghFXMIIHDDCCBPSgAwIBAgIT
// SIG // MwAAAaDpu4y7DqLRegABAAABoDANBgkqhkiG9w0BAQsF
// SIG // ADB8MQswCQYDVQQGEwJVUzETMBEGA1UECBMKV2FzaGlu
// SIG // Z3RvbjEQMA4GA1UEBxMHUmVkbW9uZDEeMBwGA1UEChMV
// SIG // TWljcm9zb2Z0IENvcnBvcmF0aW9uMSYwJAYDVQQDEx1N
// SIG // aWNyb3NvZnQgVGltZS1TdGFtcCBQQ0EgMjAxMDAeFw0y
// SIG // MTEyMDIxOTA1MjNaFw0yMzAyMjgxOTA1MjNaMIHKMQsw
// SIG // CQYDVQQGEwJVUzETMBEGA1UECBMKV2FzaGluZ3RvbjEQ
// SIG // MA4GA1UEBxMHUmVkbW9uZDEeMBwGA1UEChMVTWljcm9z
// SIG // b2Z0IENvcnBvcmF0aW9uMSUwIwYDVQQLExxNaWNyb3Nv
// SIG // ZnQgQW1lcmljYSBPcGVyYXRpb25zMSYwJAYDVQQLEx1U
// SIG // aGFsZXMgVFNTIEVTTjozRTdBLUUzNTktQTI1RDElMCMG
// SIG // A1UEAxMcTWljcm9zb2Z0IFRpbWUtU3RhbXAgU2Vydmlj
// SIG // ZTCCAiIwDQYJKoZIhvcNAQEBBQADggIPADCCAgoCggIB
// SIG // AL/a4g5ocZ0A6PZi+GEjoLyIUCrvCuX/lak+OPR8QcKL
// SIG // oi/H8jTYmyz8dWGR8ZMwa21HmNGGGslfJ1YOc6bNFTcH
// SIG // gbnblPunCQU0UFe6Yz/3yXmTRTZeJpn5u6Vcd7njHq6P
// SIG // /q/lUYW/7QeHV3KMZRM9pETBLvH3xN7m0UcSKY9r4Cwb
// SIG // aYz6f03B9BGN3UuIQJtwJkWSDBuWJD9qSIyvW1maOQNv
// SIG // G27ZpBxeMR8Odrwkf7BmZ927RfuTp0nlYZEXDNmIs4lb
// SIG // hCEASl6HZzWaZu0B8GeFI834TnJQt+l9NPbBfxFsfpd8
// SIG // CDqxnHzz3rGrT6BQP2YfLxm2l0R4TVkDQeAHINbaskJt
// SIG // MAkCG3vUHtHPak9CaDlHf69IKuLwF5xIH5nybBgve45L
// SIG // dHpt5QEnuITis9L1YLXDV9lHsrjhlKZ7Z0j473+eGBvc
// SIG // wtiCbrPHceG0ugaEZU8v5agOQye33cgag7qQ0EIzZf4K
// SIG // zbaqXa0+OQhHLHDEXpGxeH9AeeEomAHN8757zgjdNhsj
// SIG // SLb3MBMvrIc6/mwSzXNo0yKHbsjB9aDhDv7jQvt7ry4L
// SIG // rIPwRQSloPazZPn02FJq5PMIScCnlitKxxz0xBUiLuD7
// SIG // kYB0LykK/7SJFrtAi9qVT72VaCTIAFT+eIUdY+PIagHj
// SIG // HcrOje1cHpoLfJn91NFekmdpAgMBAAGjggE2MIIBMjAd
// SIG // BgNVHQ4EFgQUcrkuUNgpTpG4kWwUL0TPC2GM0OgwHwYD
// SIG // VR0jBBgwFoAUn6cVXQBeYl2D9OXSZacbUzUZ6XIwXwYD
// SIG // VR0fBFgwVjBUoFKgUIZOaHR0cDovL3d3dy5taWNyb3Nv
// SIG // ZnQuY29tL3BraW9wcy9jcmwvTWljcm9zb2Z0JTIwVGlt
// SIG // ZS1TdGFtcCUyMFBDQSUyMDIwMTAoMSkuY3JsMGwGCCsG
// SIG // AQUFBwEBBGAwXjBcBggrBgEFBQcwAoZQaHR0cDovL3d3
// SIG // dy5taWNyb3NvZnQuY29tL3BraW9wcy9jZXJ0cy9NaWNy
// SIG // b3NvZnQlMjBUaW1lLVN0YW1wJTIwUENBJTIwMjAxMCgx
// SIG // KS5jcnQwDAYDVR0TAQH/BAIwADATBgNVHSUEDDAKBggr
// SIG // BgEFBQcDCDANBgkqhkiG9w0BAQsFAAOCAgEAMX3GAqv4
// SIG // 36yUCaVtwSpAGWkvk98EypwuRLrJJ4YmMb4spfdai/mn
// SIG // KCuEp+1dn3Q1hZR+0hmnN1izh6FHlGVoTd5i5taVw2XC
// SIG // wqRysZIMkprBtsMZ+87iH/UvXeyDrqE+JvruYb3oICpF
// SIG // UXRzkwDXCIqOOX/mB+tt00seC1ERDzMKlsrj7rqXUV3S
// SIG // 6n2bFw4QSmGEmvxTfCHAXCgr5An+TFaiAa18OJqrpHKg
// SIG // VaE/y4pItUNX4xMRMdlvenY7CcFYTVbrTvVcMp3FGQ3+
// SIG // kARnXkTUsk2/2JijWXU/9F0X4jOkwsYMB/8+VW9NTDdW
// SIG // sf25qptiv6tHG2L5szdpvXC/2/fFEtz7A+ieJLFbanr0
// SIG // M3haj3ogMVkiQtH4dxYKKodXAVeeCQJR90x7fGa84hdh
// SIG // GRMrQ/rA8t8L46zuh5VF4yPZMDVAwySBn6uE87TuJbs3
// SIG // nvMAG8yIclB0dEx4rcUGBHh9oS9ONQB0pFCQoVXza4Tv
// SIG // DRQyex4l9fsQsNj1PhpbHarASpqb2XsYdfgDZmbSefWh
// SIG // pr9jFzMnuAhURFIZI5YvAQzFwWoEY+b1BJDuiAwGflYf
// SIG // CR1NOSZqcFv9nZwOnEHF3W86PIibgE4WUuMHimK8KU7Z
// SIG // mSxYofuoFiWhhrTtTU7cLyO1vMZR0fIsJpUYFXwN8PmH
// SIG // x46fKOtoEbs801kwggdxMIIFWaADAgECAhMzAAAAFcXn
// SIG // a54Cm0mZAAAAAAAVMA0GCSqGSIb3DQEBCwUAMIGIMQsw
// SIG // CQYDVQQGEwJVUzETMBEGA1UECBMKV2FzaGluZ3RvbjEQ
// SIG // MA4GA1UEBxMHUmVkbW9uZDEeMBwGA1UEChMVTWljcm9z
// SIG // b2Z0IENvcnBvcmF0aW9uMTIwMAYDVQQDEylNaWNyb3Nv
// SIG // ZnQgUm9vdCBDZXJ0aWZpY2F0ZSBBdXRob3JpdHkgMjAx
// SIG // MDAeFw0yMTA5MzAxODIyMjVaFw0zMDA5MzAxODMyMjVa
// SIG // MHwxCzAJBgNVBAYTAlVTMRMwEQYDVQQIEwpXYXNoaW5n
// SIG // dG9uMRAwDgYDVQQHEwdSZWRtb25kMR4wHAYDVQQKExVN
// SIG // aWNyb3NvZnQgQ29ycG9yYXRpb24xJjAkBgNVBAMTHU1p
// SIG // Y3Jvc29mdCBUaW1lLVN0YW1wIFBDQSAyMDEwMIICIjAN
// SIG // BgkqhkiG9w0BAQEFAAOCAg8AMIICCgKCAgEA5OGmTOe0
// SIG // ciELeaLL1yR5vQ7VgtP97pwHB9KpbE51yMo1V/YBf2xK
// SIG // 4OK9uT4XYDP/XE/HZveVU3Fa4n5KWv64NmeFRiMMtY0T
// SIG // z3cywBAY6GB9alKDRLemjkZrBxTzxXb1hlDcwUTIcVxR
// SIG // MTegCjhuje3XD9gmU3w5YQJ6xKr9cmmvHaus9ja+NSZk
// SIG // 2pg7uhp7M62AW36MEBydUv626GIl3GoPz130/o5Tz9bs
// SIG // hVZN7928jaTjkY+yOSxRnOlwaQ3KNi1wjjHINSi947SH
// SIG // JMPgyY9+tVSP3PoFVZhtaDuaRr3tpK56KTesy+uDRedG
// SIG // bsoy1cCGMFxPLOJiss254o2I5JasAUq7vnGpF1tnYN74
// SIG // kpEeHT39IM9zfUGaRnXNxF803RKJ1v2lIH1+/NmeRd+2
// SIG // ci/bfV+AutuqfjbsNkz2K26oElHovwUDo9Fzpk03dJQc
// SIG // NIIP8BDyt0cY7afomXw/TNuvXsLz1dhzPUNOwTM5TI4C
// SIG // vEJoLhDqhFFG4tG9ahhaYQFzymeiXtcodgLiMxhy16cg
// SIG // 8ML6EgrXY28MyTZki1ugpoMhXV8wdJGUlNi5UPkLiWHz
// SIG // NgY1GIRH29wb0f2y1BzFa/ZcUlFdEtsluq9QBXpsxREd
// SIG // cu+N+VLEhReTwDwV2xo3xwgVGD94q0W29R6HXtqPnhZy
// SIG // acaue7e3PmriLq0CAwEAAaOCAd0wggHZMBIGCSsGAQQB
// SIG // gjcVAQQFAgMBAAEwIwYJKwYBBAGCNxUCBBYEFCqnUv5k
// SIG // xJq+gpE8RjUpzxD/LwTuMB0GA1UdDgQWBBSfpxVdAF5i
// SIG // XYP05dJlpxtTNRnpcjBcBgNVHSAEVTBTMFEGDCsGAQQB
// SIG // gjdMg30BATBBMD8GCCsGAQUFBwIBFjNodHRwOi8vd3d3
// SIG // Lm1pY3Jvc29mdC5jb20vcGtpb3BzL0RvY3MvUmVwb3Np
// SIG // dG9yeS5odG0wEwYDVR0lBAwwCgYIKwYBBQUHAwgwGQYJ
// SIG // KwYBBAGCNxQCBAweCgBTAHUAYgBDAEEwCwYDVR0PBAQD
// SIG // AgGGMA8GA1UdEwEB/wQFMAMBAf8wHwYDVR0jBBgwFoAU
// SIG // 1fZWy4/oolxiaNE9lJBb186aGMQwVgYDVR0fBE8wTTBL
// SIG // oEmgR4ZFaHR0cDovL2NybC5taWNyb3NvZnQuY29tL3Br
// SIG // aS9jcmwvcHJvZHVjdHMvTWljUm9vQ2VyQXV0XzIwMTAt
// SIG // MDYtMjMuY3JsMFoGCCsGAQUFBwEBBE4wTDBKBggrBgEF
// SIG // BQcwAoY+aHR0cDovL3d3dy5taWNyb3NvZnQuY29tL3Br
// SIG // aS9jZXJ0cy9NaWNSb29DZXJBdXRfMjAxMC0wNi0yMy5j
// SIG // cnQwDQYJKoZIhvcNAQELBQADggIBAJ1VffwqreEsH2cB
// SIG // MSRb4Z5yS/ypb+pcFLY+TkdkeLEGk5c9MTO1OdfCcTY/
// SIG // 2mRsfNB1OW27DzHkwo/7bNGhlBgi7ulmZzpTTd2YurYe
// SIG // eNg2LpypglYAA7AFvonoaeC6Ce5732pvvinLbtg/SHUB
// SIG // 2RjebYIM9W0jVOR4U3UkV7ndn/OOPcbzaN9l9qRWqveV
// SIG // tihVJ9AkvUCgvxm2EhIRXT0n4ECWOKz3+SmJw7wXsFSF
// SIG // QrP8DJ6LGYnn8AtqgcKBGUIZUnWKNsIdw2FzLixre24/
// SIG // LAl4FOmRsqlb30mjdAy87JGA0j3mSj5mO0+7hvoyGtmW
// SIG // 9I/2kQH2zsZ0/fZMcm8Qq3UwxTSwethQ/gpY3UA8x1Rt
// SIG // nWN0SCyxTkctwRQEcb9k+SS+c23Kjgm9swFXSVRk2XPX
// SIG // fx5bRAGOWhmRaw2fpCjcZxkoJLo4S5pu+yFUa2pFEUep
// SIG // 8beuyOiJXk+d0tBMdrVXVAmxaQFEfnyhYWxz/gq77EFm
// SIG // PWn9y8FBSX5+k77L+DvktxW/tM4+pTFRhLy/AsGConsX
// SIG // HRWJjXD+57XQKBqJC4822rpM+Zv/Cuk0+CQ1ZyvgDbjm
// SIG // jJnW4SLq8CdCPSWU5nR0W2rRnj7tfqAxM328y+l7vzhw
// SIG // RNGQ8cirOoo6CGJ/2XBjU02N7oJtpQUQwXEGahC0HVUz
// SIG // WLOhcGbyoYICzjCCAjcCAQEwgfihgdCkgc0wgcoxCzAJ
// SIG // BgNVBAYTAlVTMRMwEQYDVQQIEwpXYXNoaW5ndG9uMRAw
// SIG // DgYDVQQHEwdSZWRtb25kMR4wHAYDVQQKExVNaWNyb3Nv
// SIG // ZnQgQ29ycG9yYXRpb24xJTAjBgNVBAsTHE1pY3Jvc29m
// SIG // dCBBbWVyaWNhIE9wZXJhdGlvbnMxJjAkBgNVBAsTHVRo
// SIG // YWxlcyBUU1MgRVNOOjNFN0EtRTM1OS1BMjVEMSUwIwYD
// SIG // VQQDExxNaWNyb3NvZnQgVGltZS1TdGFtcCBTZXJ2aWNl
// SIG // oiMKAQEwBwYFKw4DAhoDFQATBriNaNpxs5hT773nJ0nu
// SIG // FIKLmKCBgzCBgKR+MHwxCzAJBgNVBAYTAlVTMRMwEQYD
// SIG // VQQIEwpXYXNoaW5ndG9uMRAwDgYDVQQHEwdSZWRtb25k
// SIG // MR4wHAYDVQQKExVNaWNyb3NvZnQgQ29ycG9yYXRpb24x
// SIG // JjAkBgNVBAMTHU1pY3Jvc29mdCBUaW1lLVN0YW1wIFBD
// SIG // QSAyMDEwMA0GCSqGSIb3DQEBBQUAAgUA5vp+jjAiGA8y
// SIG // MDIyMTAxOTIxNDc1OFoYDzIwMjIxMDIwMjE0NzU4WjB3
// SIG // MD0GCisGAQQBhFkKBAExLzAtMAoCBQDm+n6OAgEAMAoC
// SIG // AQACAgwrAgH/MAcCAQACAhHFMAoCBQDm+9AOAgEAMDYG
// SIG // CisGAQQBhFkKBAIxKDAmMAwGCisGAQQBhFkKAwKgCjAI
// SIG // AgEAAgMHoSChCjAIAgEAAgMBhqAwDQYJKoZIhvcNAQEF
// SIG // BQADgYEAPPyUuFNR0/6d1hAq3u9eSGXKklCIRdcrqURy
// SIG // zZ9FooNtTjqc8xBDLIaNPNzGWfcWiNn5hL2nVz7OHQdx
// SIG // YCMy+PbR6/MWhZqEuPVXDQYhHM7DJycstLwUgs5gmkFB
// SIG // FXGWPgNXq1l2IqT3hJekeWSfazioz8a6KeviTyy0mbur
// SIG // 3rkxggQNMIIECQIBATCBkzB8MQswCQYDVQQGEwJVUzET
// SIG // MBEGA1UECBMKV2FzaGluZ3RvbjEQMA4GA1UEBxMHUmVk
// SIG // bW9uZDEeMBwGA1UEChMVTWljcm9zb2Z0IENvcnBvcmF0
// SIG // aW9uMSYwJAYDVQQDEx1NaWNyb3NvZnQgVGltZS1TdGFt
// SIG // cCBQQ0EgMjAxMAITMwAAAaDpu4y7DqLRegABAAABoDAN
// SIG // BglghkgBZQMEAgEFAKCCAUowGgYJKoZIhvcNAQkDMQ0G
// SIG // CyqGSIb3DQEJEAEEMC8GCSqGSIb3DQEJBDEiBCDEORVz
// SIG // KrNYlGi6sOwb4ixFKwjEIL70L3cOOGbzEGQQGzCB+gYL
// SIG // KoZIhvcNAQkQAi8xgeowgecwgeQwgb0EIC9HijxoZQQi
// SIG // Ei3dES+zHT6y+czpaJqBrlI9oriDr7TTMIGYMIGApH4w
// SIG // fDELMAkGA1UEBhMCVVMxEzARBgNVBAgTCldhc2hpbmd0
// SIG // b24xEDAOBgNVBAcTB1JlZG1vbmQxHjAcBgNVBAoTFU1p
// SIG // Y3Jvc29mdCBDb3Jwb3JhdGlvbjEmMCQGA1UEAxMdTWlj
// SIG // cm9zb2Z0IFRpbWUtU3RhbXAgUENBIDIwMTACEzMAAAGg
// SIG // 6buMuw6i0XoAAQAAAaAwIgQgb0Zba20s2Ps2dHhZ34XC
// SIG // ZxpmtJKHWsp4ktBZAv31GAYwDQYJKoZIhvcNAQELBQAE
// SIG // ggIATz7fIdu3SVU2RStB6gTkr3wm7pqb7AYYIIQuXODW
// SIG // Fo9qaP7j2bhUlh7LNpsXNs5pGESMElMpWUTnTiUtVYZg
// SIG // f0ou+L6Xpt9NX+sbESP0Dh6m9UIWHhprQoRhKu8SRc7s
// SIG // jBH4vV2fs4wfqDmsniTW3+7K0IXCoUjT3wLJQkUHtr47
// SIG // X1zoBzXRZ1u/R0abvGVoE431so9WQEP58roxIv9BdtQr
// SIG // n345trLDhlpa3wFP6ceZlTI9HnLieL/deuUDSqtEUUUx
// SIG // gZTC1etaOCC5RD/y3zYaxlO0pEu3HZatGl67LIeH+XQ4
// SIG // ++yqta493V8ZgPhOpRJFxKaC/9Ofi0L123L+GgsrOFvV
// SIG // ILbKvtRS4BVqet/cvHEa4D8euGjpjY9WseF8lztdSay4
// SIG // tY6q+yM858ipwbogMqo9fKLS6zMfPRZS0XQZWg0oPoM0
// SIG // f4pN7KG906K8uykD8jtYx8rqzl4L4mc+FAU3mX6kgK62
// SIG // p68C+q+Zds7jrswPrKUzuwCkDPkZdEeBNt6raF9uX4TH
// SIG // blvNCAgE66GTKfJYltkXdrljNVj+Rcv8NrFK/NB7yW00
// SIG // ckc9GXumf9P2pCaUTcOptOgjlASWbi5kectZesAWjckH
// SIG // wrA7nHY2NnkyAZsOLRPnDPT1vhVvHR6T5FvSniVaWCkq
// SIG // FuE+xAnFARIZmQvIChhPUMctBmc=
// SIG // End signature block
