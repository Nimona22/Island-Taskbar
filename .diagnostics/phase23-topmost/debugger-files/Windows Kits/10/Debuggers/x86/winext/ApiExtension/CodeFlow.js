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
// SIG // MIImEwYJKoZIhvcNAQcCoIImBDCCJgACAQExDzANBglg
// SIG // hkgBZQMEAgEFADB3BgorBgEEAYI3AgEEoGkwZzAyBgor
// SIG // BgEEAYI3AgEeMCQCAQEEEBDgyQbOONQRoqMAEEvTUJAC
// SIG // AQACAQACAQACAQACAQAwMTANBglghkgBZQMEAgEFAAQg
// SIG // 8Vd5mJ3w6q+ClnHrnQIoaqCXDHQ0K4ZNgLaaX8x0Gjag
// SIG // ggt2MIIE/jCCA+agAwIBAgITMwAABJFkYvO3PuIMzQAA
// SIG // AAAEkTANBgkqhkiG9w0BAQsFADB+MQswCQYDVQQGEwJV
// SIG // UzETMBEGA1UECBMKV2FzaGluZ3RvbjEQMA4GA1UEBxMH
// SIG // UmVkbW9uZDEeMBwGA1UEChMVTWljcm9zb2Z0IENvcnBv
// SIG // cmF0aW9uMSgwJgYDVQQDEx9NaWNyb3NvZnQgQ29kZSBT
// SIG // aWduaW5nIFBDQSAyMDEwMB4XDTIyMDUxMjIwNDcwNloX
// SIG // DTIzMDUxMTIwNDcwNlowdDELMAkGA1UEBhMCVVMxEzAR
// SIG // BgNVBAgTCldhc2hpbmd0b24xEDAOBgNVBAcTB1JlZG1v
// SIG // bmQxHjAcBgNVBAoTFU1pY3Jvc29mdCBDb3Jwb3JhdGlv
// SIG // bjEeMBwGA1UEAxMVTWljcm9zb2Z0IENvcnBvcmF0aW9u
// SIG // MIIBIjANBgkqhkiG9w0BAQEFAAOCAQ8AMIIBCgKCAQEA
// SIG // nhY/7ygo8W4MElNZXT0OpOrlxnQff1zsTzysiYv//AUr
// SIG // fumkbUy6UltTyoPS2nmxcpKZq5ndvC9ph9JE9BH7Z1FO
// SIG // YhQLmffNb2khVqXR2iNccz+CQup1mOaIBH/v4n6TZKBx
// SIG // Nngq4HLZlAcufVovvc1nR6poleFgVK+PscmHu66fZRkp
// SIG // BrWEhSU0oCaw8Z4vjCTthnOgkBpIQWv/9A3dv4ibaRJL
// SIG // hVIYyY3Pj7YwJ9uS7cgMDn5WbI9UftI5Kr+q6nqSi7ZL
// SIG // fA0r+wHMEv8IDhdggKAGPlbkK0MVMOAhabEvK0l9atR7
// SIG // uRCEc5ibanwBdD9A6P0/CDNox996YGhziwIDAQABo4IB
// SIG // fTCCAXkwHwYDVR0lBBgwFgYKKwYBBAGCNz0GAQYIKwYB
// SIG // BQUHAwMwHQYDVR0OBBYEFMha1AwF7LjeoDKigJPMy/aN
// SIG // OlovMFQGA1UdEQRNMEukSTBHMS0wKwYDVQQLEyRNaWNy
// SIG // b3NvZnQgSXJlbGFuZCBPcGVyYXRpb25zIExpbWl0ZWQx
// SIG // FjAUBgNVBAUTDTIzMDg2NSs0NzA1NjMwHwYDVR0jBBgw
// SIG // FoAU5vxfe7siAFjkck619CF0IzLm76wwVgYDVR0fBE8w
// SIG // TTBLoEmgR4ZFaHR0cDovL2NybC5taWNyb3NvZnQuY29t
// SIG // L3BraS9jcmwvcHJvZHVjdHMvTWljQ29kU2lnUENBXzIw
// SIG // MTAtMDctMDYuY3JsMFoGCCsGAQUFBwEBBE4wTDBKBggr
// SIG // BgEFBQcwAoY+aHR0cDovL3d3dy5taWNyb3NvZnQuY29t
// SIG // L3BraS9jZXJ0cy9NaWNDb2RTaWdQQ0FfMjAxMC0wNy0w
// SIG // Ni5jcnQwDAYDVR0TAQH/BAIwADANBgkqhkiG9w0BAQsF
// SIG // AAOCAQEAEsvDfuS0pi0W8ZJXkxSiVYQD44KJKhJkA2Q6
// SIG // vlJX1AS6V4GcIpriVUsgGmPAuuJAO8NMiIuYyArzwSnl
// SIG // sRtrzSNu7sSz3c4SO8T0hxkSBwJ1w6o9V8BhBH0eIDlF
// SIG // 2e3vCH3uL49TUXdo0aNhoudG0W4/xbV1RUEtKf5RyNWC
// SIG // JYPJOddok0tr+O/QJxeDVLYikPyWLdYi3J4/cqpBivTG
// SIG // GtJSDoBm3MODycAvFWY/qZgJwpil38cwjbhBXxXBEvgL
// SIG // HhlEsAnSc4//+KDQy23KUjElf0VeHmjqa7N75PpEeTCx
// SIG // GKw98zDnQ6w1g0gpa6c5VYYadlHCXfyYGJ8S3VI8BDCC
// SIG // BnAwggRYoAMCAQICCmEMUkwAAAAAAAMwDQYJKoZIhvcN
// SIG // AQELBQAwgYgxCzAJBgNVBAYTAlVTMRMwEQYDVQQIEwpX
// SIG // YXNoaW5ndG9uMRAwDgYDVQQHEwdSZWRtb25kMR4wHAYD
// SIG // VQQKExVNaWNyb3NvZnQgQ29ycG9yYXRpb24xMjAwBgNV
// SIG // BAMTKU1pY3Jvc29mdCBSb290IENlcnRpZmljYXRlIEF1
// SIG // dGhvcml0eSAyMDEwMB4XDTEwMDcwNjIwNDAxN1oXDTI1
// SIG // MDcwNjIwNTAxN1owfjELMAkGA1UEBhMCVVMxEzARBgNV
// SIG // BAgTCldhc2hpbmd0b24xEDAOBgNVBAcTB1JlZG1vbmQx
// SIG // HjAcBgNVBAoTFU1pY3Jvc29mdCBDb3Jwb3JhdGlvbjEo
// SIG // MCYGA1UEAxMfTWljcm9zb2Z0IENvZGUgU2lnbmluZyBQ
// SIG // Q0EgMjAxMDCCASIwDQYJKoZIhvcNAQEBBQADggEPADCC
// SIG // AQoCggEBAOkOZFB5Z7XE4/0JAEyelKz3VmjqRNjPxVhP
// SIG // qaV2fG1FutM5krSkHvn5ZYLkF9KP/UScCOhlk84sVYS/
// SIG // fQjjLiuoQSsYt6JLbklMaxUH3tHSwokecZTNtX9LtK8I
// SIG // 2MyI1msXlDqTziY/7Ob+NJhX1R1dSfayKi7VhbtZP/iQ
// SIG // tCuDdMorsztG4/BGScEXZlTJHL0dxFViV3L4Z7klIDTe
// SIG // XaallV6rKIDN1bKe5QO1Y9OyFMjByIomCll/B+z/Du2A
// SIG // EjVMEqa+Ulv1ptrgiwtId9aFR9UQucboqu6Lai0FXGDG
// SIG // tCpbnCMcX0XjGhQebzfLGTOAaolNo2pmY3iT1TDPlR8C
// SIG // AwEAAaOCAeMwggHfMBAGCSsGAQQBgjcVAQQDAgEAMB0G
// SIG // A1UdDgQWBBTm/F97uyIAWORyTrX0IXQjMubvrDAZBgkr
// SIG // BgEEAYI3FAIEDB4KAFMAdQBiAEMAQTALBgNVHQ8EBAMC
// SIG // AYYwDwYDVR0TAQH/BAUwAwEB/zAfBgNVHSMEGDAWgBTV
// SIG // 9lbLj+iiXGJo0T2UkFvXzpoYxDBWBgNVHR8ETzBNMEug
// SIG // SaBHhkVodHRwOi8vY3JsLm1pY3Jvc29mdC5jb20vcGtp
// SIG // L2NybC9wcm9kdWN0cy9NaWNSb29DZXJBdXRfMjAxMC0w
// SIG // Ni0yMy5jcmwwWgYIKwYBBQUHAQEETjBMMEoGCCsGAQUF
// SIG // BzAChj5odHRwOi8vd3d3Lm1pY3Jvc29mdC5jb20vcGtp
// SIG // L2NlcnRzL01pY1Jvb0NlckF1dF8yMDEwLTA2LTIzLmNy
// SIG // dDCBnQYDVR0gBIGVMIGSMIGPBgkrBgEEAYI3LgMwgYEw
// SIG // PQYIKwYBBQUHAgEWMWh0dHA6Ly93d3cubWljcm9zb2Z0
// SIG // LmNvbS9QS0kvZG9jcy9DUFMvZGVmYXVsdC5odG0wQAYI
// SIG // KwYBBQUHAgIwNB4yIB0ATABlAGcAYQBsAF8AUABvAGwA
// SIG // aQBjAHkAXwBTAHQAYQB0AGUAbQBlAG4AdAAuIB0wDQYJ
// SIG // KoZIhvcNAQELBQADggIBABp071dPKXvEFoV4uFDTIvwJ
// SIG // nayCl/g0/yosl5US5eS/z7+TyOM0qduBuNweAL7SNW+v
// SIG // 5X95lXflAtTx69jNTh4bYaLCWiMa8IyoYlFFZwjjPzwe
// SIG // k/gwhRfIOUCm1w6zISnlpaFpjCKTzHSY56FHQ/JTrMAP
// SIG // MGl//tIlIG1vYdPfB9XZcgAsaYZ2PVHbpjlIyTdhbQfd
// SIG // UxnLp9Zhwr/ig6sP4GubldZ9KFGwiUpRpJpsyLcfShoO
// SIG // aanX3MF+0Ulwqratu3JHYxf6ptaipobsqBBEm2O2smmJ
// SIG // BsdGhnoYP+jFHSHVe/kCIy3FQcu/HUzIFu+xnH/8IktJ
// SIG // im4V46Z/dlvRU3mRhZ3V0ts9czXzPK5UslJHasCqE5XS
// SIG // jhHamWdeMoz7N4XR3HWFnIfGWleFwr/dDY+Mmy3rtO7P
// SIG // J9O1Xmn6pBYEAackZ3PPTU+23gVWl3r36VJN9HcFT4XG
// SIG // 2Avxju1CCdENduMjVngiJja+yrGMbqod5IXaRzNij6TJ
// SIG // kTNfcR5Ar5hlySLoQiElihwtYNk3iUGJKhYP12E8lGhg
// SIG // Uu/WR5mggEDuFYF3PpzgUxgaUB04lZseZjMTJzkXeIc2
// SIG // zk7DX7L1PUdTtuDl2wthPSrXkizON1o+QEIxpB8QCMJW
// SIG // nL8kXVECnWp50hfT2sGUjgd7JXFEqwZq5tTG3yOalnXF
// SIG // MYIZ9TCCGfECAQEwgZUwfjELMAkGA1UEBhMCVVMxEzAR
// SIG // BgNVBAgTCldhc2hpbmd0b24xEDAOBgNVBAcTB1JlZG1v
// SIG // bmQxHjAcBgNVBAoTFU1pY3Jvc29mdCBDb3Jwb3JhdGlv
// SIG // bjEoMCYGA1UEAxMfTWljcm9zb2Z0IENvZGUgU2lnbmlu
// SIG // ZyBQQ0EgMjAxMAITMwAABJFkYvO3PuIMzQAAAAAEkTAN
// SIG // BglghkgBZQMEAgEFAKCCAQQwGQYJKoZIhvcNAQkDMQwG
// SIG // CisGAQQBgjcCAQQwHAYKKwYBBAGCNwIBCzEOMAwGCisG
// SIG // AQQBgjcCARUwLwYJKoZIhvcNAQkEMSIEIKIX4Udx4U4A
// SIG // dMn9MnuHG+bn5tt2e42s/w5Ec/DcmBeMMDwGCisGAQQB
// SIG // gjcKAxwxLgwsc1BZN3hQQjdoVDVnNUhIcll0OHJETFNN
// SIG // OVZ1WlJ1V1phZWYyZTIyUnM1ND0wWgYKKwYBBAGCNwIB
// SIG // DDFMMEqgJIAiAE0AaQBjAHIAbwBzAG8AZgB0ACAAVwBp
// SIG // AG4AZABvAHcAc6EigCBodHRwOi8vd3d3Lm1pY3Jvc29m
// SIG // dC5jb20vd2luZG93czANBgkqhkiG9w0BAQEFAASCAQCJ
// SIG // 3OPDsscc4xdN7dcpYc5laAP2boRuqBwbKkZEYe44bQ5s
// SIG // aX58ihGsOO2NJfjLwTFELuLtIB9SlnKwbMf2GsT2SfKU
// SIG // R0+jmcl1WXEnTyKBdBQjxMjTSqruDndvL5s1/uNHSux0
// SIG // NyIx/Jid0N6+UTMJfzLgHnUoq5tkNLt8r+zb8oQHgjvU
// SIG // mdq+5OpHsTFyaNsx5/naKqeOwQ+cqrkuJfnJCaejNeeY
// SIG // xkzdcINBt1E9L41MwZJ0Fx9Cy4j+iMOgA5s6Ah7hdkfq
// SIG // ixAA95vc21GOm1MclM/pcBHatvu3GMkQIp2SigvHiFcp
// SIG // BCO3wG2I06qKpxJa1/7E3bnfiajijmb+oYIXKDCCFyQG
// SIG // CisGAQQBgjcDAwExghcUMIIXEAYJKoZIhvcNAQcCoIIX
// SIG // ATCCFv0CAQMxDzANBglghkgBZQMEAgEFADCCAVkGCyqG
// SIG // SIb3DQEJEAEEoIIBSASCAUQwggFAAgEBBgorBgEEAYRZ
// SIG // CgMBMDEwDQYJYIZIAWUDBAIBBQAEIMbEA7Hr5MQpRdku
// SIG // stceqQOYZcXGq+7YO+FFausfPKqhAgZjT9eq+5gYEzIw
// SIG // MjIxMDE5MjEzMTEyLjY5OVowBIACAfSggdikgdUwgdIx
// SIG // CzAJBgNVBAYTAlVTMRMwEQYDVQQIEwpXYXNoaW5ndG9u
// SIG // MRAwDgYDVQQHEwdSZWRtb25kMR4wHAYDVQQKExVNaWNy
// SIG // b3NvZnQgQ29ycG9yYXRpb24xLTArBgNVBAsTJE1pY3Jv
// SIG // c29mdCBJcmVsYW5kIE9wZXJhdGlvbnMgTGltaXRlZDEm
// SIG // MCQGA1UECxMdVGhhbGVzIFRTUyBFU046MkFENC00Qjky
// SIG // LUZBMDExJTAjBgNVBAMTHE1pY3Jvc29mdCBUaW1lLVN0
// SIG // YW1wIFNlcnZpY2WgghF3MIIHJzCCBQ+gAwIBAgITMwAA
// SIG // AbHKkEPuC/ADqwABAAABsTANBgkqhkiG9w0BAQsFADB8
// SIG // MQswCQYDVQQGEwJVUzETMBEGA1UECBMKV2FzaGluZ3Rv
// SIG // bjEQMA4GA1UEBxMHUmVkbW9uZDEeMBwGA1UEChMVTWlj
// SIG // cm9zb2Z0IENvcnBvcmF0aW9uMSYwJAYDVQQDEx1NaWNy
// SIG // b3NvZnQgVGltZS1TdGFtcCBQQ0EgMjAxMDAeFw0yMjA5
// SIG // MjAyMDIxNTlaFw0yMzEyMTQyMDIxNTlaMIHSMQswCQYD
// SIG // VQQGEwJVUzETMBEGA1UECBMKV2FzaGluZ3RvbjEQMA4G
// SIG // A1UEBxMHUmVkbW9uZDEeMBwGA1UEChMVTWljcm9zb2Z0
// SIG // IENvcnBvcmF0aW9uMS0wKwYDVQQLEyRNaWNyb3NvZnQg
// SIG // SXJlbGFuZCBPcGVyYXRpb25zIExpbWl0ZWQxJjAkBgNV
// SIG // BAsTHVRoYWxlcyBUU1MgRVNOOjJBRDQtNEI5Mi1GQTAx
// SIG // MSUwIwYDVQQDExxNaWNyb3NvZnQgVGltZS1TdGFtcCBT
// SIG // ZXJ2aWNlMIICIjANBgkqhkiG9w0BAQEFAAOCAg8AMIIC
// SIG // CgKCAgEAhqKrPtXsG8fsg4w8R4MzZTAKkzwvEBQ94ntS
// SIG // +72rRGIMF0GCyEL9IOt7f9gkGoamfbtrtdY4y+KIFR8w
// SIG // 19/nU3EoWhJfrYamrfpgtFmTaE3XCKCsI7rnrPmlVOMm
// SIG // ndDyN1gAlfeu4l5rdxx9ODECBPdS/+w/jDT7JkBhrYll
// SIG // qVXcwGAgWLdXAoUDgKVByv5XhKkbOrPx9qppuZjKm4nf
// SIG // lmfwb/bTWkA3aMMQ67tBoMLSsbIN3BJNWZdwczjoQVXo
// SIG // 3YXr2fB+PYNmHviCcDUMHs0Vxmf7i/WSpBafsDMEn6WY
// SIG // 7G8qtRGVX+7X0zDVg/7NVDLMqfn/iv++5hJGP+2Fmv4W
// SIG // ZkBS1MBpwvOi4EQ25pIG45jWTffR4ynyed1I1SxSOP+e
// SIG // fuBx0WrN1A250lv5fGZHCL0vCMDT/w+U6wpNnxfDoQRY
// SIG // 9Ut82iNK5alkxNozPP/DNI+nknTaSliaR2XnSXDIZEs7
// SIG // lfuJYg0qahfJJ1CZF2IYxOS9FK1crEigSb8QnEJoj6Th
// SIG // Lf4FYpYLTsRXlPdQbvBsVvgt++BttooznwfK0DKMOc71
// SIG // 8SLS+unwkVO0aF23CEQSStoy0ZW34K+cbRmUfia+k9E+
// SIG // 4luoTnT17oKqYfDNO5Rk8UwVa8mfh8+/R3fZaz2O/Zhi
// SIG // YT/RZHV9Quz5PHGlaCfXPQ8A6zFJlE8CAwEAAaOCAUkw
// SIG // ggFFMB0GA1UdDgQWBBT0m2eR7w2thIr18WehUTSmvQ45
// SIG // kzAfBgNVHSMEGDAWgBSfpxVdAF5iXYP05dJlpxtTNRnp
// SIG // cjBfBgNVHR8EWDBWMFSgUqBQhk5odHRwOi8vd3d3Lm1p
// SIG // Y3Jvc29mdC5jb20vcGtpb3BzL2NybC9NaWNyb3NvZnQl
// SIG // MjBUaW1lLVN0YW1wJTIwUENBJTIwMjAxMCgxKS5jcmww
// SIG // bAYIKwYBBQUHAQEEYDBeMFwGCCsGAQUFBzAChlBodHRw
// SIG // Oi8vd3d3Lm1pY3Jvc29mdC5jb20vcGtpb3BzL2NlcnRz
// SIG // L01pY3Jvc29mdCUyMFRpbWUtU3RhbXAlMjBQQ0ElMjAy
// SIG // MDEwKDEpLmNydDAMBgNVHRMBAf8EAjAAMBYGA1UdJQEB
// SIG // /wQMMAoGCCsGAQUFBwMIMA4GA1UdDwEB/wQEAwIHgDAN
// SIG // BgkqhkiG9w0BAQsFAAOCAgEA2Oc3kmql5VKEitAhoBCc
// SIG // 1U6/VwMSYKQPqhC59f00Y5fbwnD+B2Qa0wnJqADSVVu6
// SIG // bBCVrks+EGbkuMhRb/lpiHNKVnuXF4PKTDnvCnYCqgwA
// SIG // mbttdxe0m38fJpGU3fmECEFX4OYacEhFwTkLZtIUVjdq
// SIG // wPnQpRII+YqX/Q0Vp096g2puPllSdrxUB8xIOx3F7LGO
// SIG // zyv/1WmrLyWAhUGpGte0W3qfX4YWkn7YCM+yl887tj5j
// SIG // +jO/l1MRi6bl4MsN0PW2FCYeRbyzQEENsg5Pd351Z08R
// SIG // OR/nR8z+cAuQwR29ijaDKIms5IbRr1nZL/qZskFSuCuS
// SIG // A+nYeMuTJxHg2HCXrt6ECFbEkYoPaBGTzxPYopcuJEcC
// SIG // hhNlWkduCRguykEsmz0LvtmS7Fe68g4Zoh3sQkIE5VEw
// SIG // nKC3HwVemhK7eNYR1q7RYExfGFUDMQdO7tQpbcPD4oaB
// SIG // btFGWGu3nz1IryWs9K88zo8+eoQV/o9SxNU7Rs6TMqcL
// SIG // dM6C6LgmGVaWKKC0S2DVKU8zFx0y5z25h1ZJ7X/Zhaav
// SIG // 1mtXVG6+lJIq8ktJgOU5/pomumdftgosxGjIp3NORy9f
// SIG // DUll+KQl4YmN9GzZxPYkhuI0QYriLmytBtUK+AK91hUR
// SIG // VldVbUjP8sksr1dsiQwyOYQIkSxrTuhp0pw7h5329jph
// SIG // gEYwggdxMIIFWaADAgECAhMzAAAAFcXna54Cm0mZAAAA
// SIG // AAAVMA0GCSqGSIb3DQEBCwUAMIGIMQswCQYDVQQGEwJV
// SIG // UzETMBEGA1UECBMKV2FzaGluZ3RvbjEQMA4GA1UEBxMH
// SIG // UmVkbW9uZDEeMBwGA1UEChMVTWljcm9zb2Z0IENvcnBv
// SIG // cmF0aW9uMTIwMAYDVQQDEylNaWNyb3NvZnQgUm9vdCBD
// SIG // ZXJ0aWZpY2F0ZSBBdXRob3JpdHkgMjAxMDAeFw0yMTA5
// SIG // MzAxODIyMjVaFw0zMDA5MzAxODMyMjVaMHwxCzAJBgNV
// SIG // BAYTAlVTMRMwEQYDVQQIEwpXYXNoaW5ndG9uMRAwDgYD
// SIG // VQQHEwdSZWRtb25kMR4wHAYDVQQKExVNaWNyb3NvZnQg
// SIG // Q29ycG9yYXRpb24xJjAkBgNVBAMTHU1pY3Jvc29mdCBU
// SIG // aW1lLVN0YW1wIFBDQSAyMDEwMIICIjANBgkqhkiG9w0B
// SIG // AQEFAAOCAg8AMIICCgKCAgEA5OGmTOe0ciELeaLL1yR5
// SIG // vQ7VgtP97pwHB9KpbE51yMo1V/YBf2xK4OK9uT4XYDP/
// SIG // XE/HZveVU3Fa4n5KWv64NmeFRiMMtY0Tz3cywBAY6GB9
// SIG // alKDRLemjkZrBxTzxXb1hlDcwUTIcVxRMTegCjhuje3X
// SIG // D9gmU3w5YQJ6xKr9cmmvHaus9ja+NSZk2pg7uhp7M62A
// SIG // W36MEBydUv626GIl3GoPz130/o5Tz9bshVZN7928jaTj
// SIG // kY+yOSxRnOlwaQ3KNi1wjjHINSi947SHJMPgyY9+tVSP
// SIG // 3PoFVZhtaDuaRr3tpK56KTesy+uDRedGbsoy1cCGMFxP
// SIG // LOJiss254o2I5JasAUq7vnGpF1tnYN74kpEeHT39IM9z
// SIG // fUGaRnXNxF803RKJ1v2lIH1+/NmeRd+2ci/bfV+Autuq
// SIG // fjbsNkz2K26oElHovwUDo9Fzpk03dJQcNIIP8BDyt0cY
// SIG // 7afomXw/TNuvXsLz1dhzPUNOwTM5TI4CvEJoLhDqhFFG
// SIG // 4tG9ahhaYQFzymeiXtcodgLiMxhy16cg8ML6EgrXY28M
// SIG // yTZki1ugpoMhXV8wdJGUlNi5UPkLiWHzNgY1GIRH29wb
// SIG // 0f2y1BzFa/ZcUlFdEtsluq9QBXpsxREdcu+N+VLEhReT
// SIG // wDwV2xo3xwgVGD94q0W29R6HXtqPnhZyacaue7e3Pmri
// SIG // Lq0CAwEAAaOCAd0wggHZMBIGCSsGAQQBgjcVAQQFAgMB
// SIG // AAEwIwYJKwYBBAGCNxUCBBYEFCqnUv5kxJq+gpE8RjUp
// SIG // zxD/LwTuMB0GA1UdDgQWBBSfpxVdAF5iXYP05dJlpxtT
// SIG // NRnpcjBcBgNVHSAEVTBTMFEGDCsGAQQBgjdMg30BATBB
// SIG // MD8GCCsGAQUFBwIBFjNodHRwOi8vd3d3Lm1pY3Jvc29m
// SIG // dC5jb20vcGtpb3BzL0RvY3MvUmVwb3NpdG9yeS5odG0w
// SIG // EwYDVR0lBAwwCgYIKwYBBQUHAwgwGQYJKwYBBAGCNxQC
// SIG // BAweCgBTAHUAYgBDAEEwCwYDVR0PBAQDAgGGMA8GA1Ud
// SIG // EwEB/wQFMAMBAf8wHwYDVR0jBBgwFoAU1fZWy4/oolxi
// SIG // aNE9lJBb186aGMQwVgYDVR0fBE8wTTBLoEmgR4ZFaHR0
// SIG // cDovL2NybC5taWNyb3NvZnQuY29tL3BraS9jcmwvcHJv
// SIG // ZHVjdHMvTWljUm9vQ2VyQXV0XzIwMTAtMDYtMjMuY3Js
// SIG // MFoGCCsGAQUFBwEBBE4wTDBKBggrBgEFBQcwAoY+aHR0
// SIG // cDovL3d3dy5taWNyb3NvZnQuY29tL3BraS9jZXJ0cy9N
// SIG // aWNSb29DZXJBdXRfMjAxMC0wNi0yMy5jcnQwDQYJKoZI
// SIG // hvcNAQELBQADggIBAJ1VffwqreEsH2cBMSRb4Z5yS/yp
// SIG // b+pcFLY+TkdkeLEGk5c9MTO1OdfCcTY/2mRsfNB1OW27
// SIG // DzHkwo/7bNGhlBgi7ulmZzpTTd2YurYeeNg2LpypglYA
// SIG // A7AFvonoaeC6Ce5732pvvinLbtg/SHUB2RjebYIM9W0j
// SIG // VOR4U3UkV7ndn/OOPcbzaN9l9qRWqveVtihVJ9AkvUCg
// SIG // vxm2EhIRXT0n4ECWOKz3+SmJw7wXsFSFQrP8DJ6LGYnn
// SIG // 8AtqgcKBGUIZUnWKNsIdw2FzLixre24/LAl4FOmRsqlb
// SIG // 30mjdAy87JGA0j3mSj5mO0+7hvoyGtmW9I/2kQH2zsZ0
// SIG // /fZMcm8Qq3UwxTSwethQ/gpY3UA8x1RtnWN0SCyxTkct
// SIG // wRQEcb9k+SS+c23Kjgm9swFXSVRk2XPXfx5bRAGOWhmR
// SIG // aw2fpCjcZxkoJLo4S5pu+yFUa2pFEUep8beuyOiJXk+d
// SIG // 0tBMdrVXVAmxaQFEfnyhYWxz/gq77EFmPWn9y8FBSX5+
// SIG // k77L+DvktxW/tM4+pTFRhLy/AsGConsXHRWJjXD+57XQ
// SIG // KBqJC4822rpM+Zv/Cuk0+CQ1ZyvgDbjmjJnW4SLq8CdC
// SIG // PSWU5nR0W2rRnj7tfqAxM328y+l7vzhwRNGQ8cirOoo6
// SIG // CGJ/2XBjU02N7oJtpQUQwXEGahC0HVUzWLOhcGbyoYIC
// SIG // 0zCCAjwCAQEwggEAoYHYpIHVMIHSMQswCQYDVQQGEwJV
// SIG // UzETMBEGA1UECBMKV2FzaGluZ3RvbjEQMA4GA1UEBxMH
// SIG // UmVkbW9uZDEeMBwGA1UEChMVTWljcm9zb2Z0IENvcnBv
// SIG // cmF0aW9uMS0wKwYDVQQLEyRNaWNyb3NvZnQgSXJlbGFu
// SIG // ZCBPcGVyYXRpb25zIExpbWl0ZWQxJjAkBgNVBAsTHVRo
// SIG // YWxlcyBUU1MgRVNOOjJBRDQtNEI5Mi1GQTAxMSUwIwYD
// SIG // VQQDExxNaWNyb3NvZnQgVGltZS1TdGFtcCBTZXJ2aWNl
// SIG // oiMKAQEwBwYFKw4DAhoDFQDtZLG+pANsDu/LLr1OfTA/
// SIG // kEbHK6CBgzCBgKR+MHwxCzAJBgNVBAYTAlVTMRMwEQYD
// SIG // VQQIEwpXYXNoaW5ndG9uMRAwDgYDVQQHEwdSZWRtb25k
// SIG // MR4wHAYDVQQKExVNaWNyb3NvZnQgQ29ycG9yYXRpb24x
// SIG // JjAkBgNVBAMTHU1pY3Jvc29mdCBUaW1lLVN0YW1wIFBD
// SIG // QSAyMDEwMA0GCSqGSIb3DQEBBQUAAgUA5vpWJTAiGA8y
// SIG // MDIyMTAxOTE4NTUzM1oYDzIwMjIxMDIwMTg1NTMzWjBz
// SIG // MDkGCisGAQQBhFkKBAExKzApMAoCBQDm+lYlAgEAMAYC
// SIG // AQACAQowBwIBAAICEUAwCgIFAOb7p6UCAQAwNgYKKwYB
// SIG // BAGEWQoEAjEoMCYwDAYKKwYBBAGEWQoDAqAKMAgCAQAC
// SIG // AwehIKEKMAgCAQACAwGGoDANBgkqhkiG9w0BAQUFAAOB
// SIG // gQCiOtkgoS89i9vJ7KMWkWn6PKFZwtH1smzvwk52pXkz
// SIG // UimtDU9UadEpF0K9+prSqovewNahANcmmKklWKoxpVmd
// SIG // qVaIVu+ji5ABoSi6LAlnmOs/h8bsKTE21SZ2GhUQ+m4D
// SIG // BNk43QqXeozEa4iCUihfKI/jbA7gD7VpfAfFWmt6EzGC
// SIG // BA0wggQJAgEBMIGTMHwxCzAJBgNVBAYTAlVTMRMwEQYD
// SIG // VQQIEwpXYXNoaW5ndG9uMRAwDgYDVQQHEwdSZWRtb25k
// SIG // MR4wHAYDVQQKExVNaWNyb3NvZnQgQ29ycG9yYXRpb24x
// SIG // JjAkBgNVBAMTHU1pY3Jvc29mdCBUaW1lLVN0YW1wIFBD
// SIG // QSAyMDEwAhMzAAABscqQQ+4L8AOrAAEAAAGxMA0GCWCG
// SIG // SAFlAwQCAQUAoIIBSjAaBgkqhkiG9w0BCQMxDQYLKoZI
// SIG // hvcNAQkQAQQwLwYJKoZIhvcNAQkEMSIEIFXUwQf1FvYk
// SIG // R3xhUOGZgNaxsPsO9gWqN0H3Tn1LF5QwMIH6BgsqhkiG
// SIG // 9w0BCRACLzGB6jCB5zCB5DCBvQQgg+0NixRb36ng6MvY
// SIG // Kbt+benWHq6wztHDE5TiKJs0z2wwgZgwgYCkfjB8MQsw
// SIG // CQYDVQQGEwJVUzETMBEGA1UECBMKV2FzaGluZ3RvbjEQ
// SIG // MA4GA1UEBxMHUmVkbW9uZDEeMBwGA1UEChMVTWljcm9z
// SIG // b2Z0IENvcnBvcmF0aW9uMSYwJAYDVQQDEx1NaWNyb3Nv
// SIG // ZnQgVGltZS1TdGFtcCBQQ0EgMjAxMAITMwAAAbHKkEPu
// SIG // C/ADqwABAAABsTAiBCCLxBMAt3+oN9y33AYmEsEel7Hv
// SIG // JQIxXMY57tL4EL6Z+zANBgkqhkiG9w0BAQsFAASCAgCD
// SIG // G70acvwJTJKfKx+ybAxieah5+bfj6wmL4uS0l+cX0peN
// SIG // CBzAVPUauDx+0kAonx/ChkpBWLwlhf21cAPFewPWeI5K
// SIG // N+sTzg9U5gu7TPWFxAaOuLBKkAh+kXj9r+41ljHuSt/c
// SIG // ms77993zRISVbv2S+4ocWfcfs4o3bpx4YKFUc2Ua64rX
// SIG // 1bopM32acabTixLHg7N/26xmkxZ9XaPoC8AFiqyX6Sn6
// SIG // TExjWZOMxAoZGQKgxu29PMbxyIK97o9nLmCBk1qSQTlb
// SIG // gDvCEq00+0iHKqo9c1i3QgOLwqrM4vjvkRDTppo1rNRB
// SIG // XnlSydijCs9m488jaQc+N6gze27WhePsAIvYQePqNNpO
// SIG // yPY2tTCMVttTM+TYGvkHW9S/4VUn4V+z7/FFkuzRgpAw
// SIG // 4bU/QSoistcwBgBAWcO20wTlmYpoFnPcGUpuZol6DWeL
// SIG // fOC24ASZbjLSMbeWz9kdOvFVnDVkNzfGsHwbyAbhVQrf
// SIG // SOFO0ha/yhfFPt8Cmchi9QNbfFEQaZQUu6m0PfuqB+Me
// SIG // m54y8vE91+v7RaI2j27+5x37kxOPVV+shSMT8dAU4pi1
// SIG // Dp3yxV3GECXN8+tNURT8sL1w8i9NGbZ45JsiWN4lSLSW
// SIG // 4wUznq4bbFk0KdjgatmpTQhZIReUMiXmPBl4LdqK+Ex6
// SIG // egGTJMMHqDDEfTm3cR/NcA==
// SIG // End signature block
