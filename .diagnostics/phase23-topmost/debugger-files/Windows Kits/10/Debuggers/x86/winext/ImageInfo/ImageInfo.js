"use strict";

/*************************************************

ImageInfo.js

Debugger extension which parses PE images mapped into the address
space of the target and presents the resources within those images
on the debugger's module object.

*************************************************/

//
// Permanently remove the generic object "[Object object]" string conversion.
//
delete Object.prototype.toString;

//*************************************************
// Type System Utilities:
//

//
// Unfortunately, the PE structures are not in the public symbols for ntdll despite being
// publicly documented on MSDN.  Include a 'type creator' utility.  At some point in the future
// when JsProvider supports modules, this should be extracted into such.
//
// This also helps in circumstances where the debugger is opened "-z" against a PE image.  Even
// with private symbols, the ntdll symbols (which contain all the PE definitions) would not be
// loaded and this would fail.
//

var __keyModule = "nt";

var __syntheticTypes = {};
var __globalScope = this;

var __forceUsePublicSymbols = false;
var __usePrivateSymbols = !__forceUsePublicSymbols;
var __checkedPrivateSymbols = false;

// __getSyntheticTypeSize:
//
// Gets the size of a synthetic type.
//
function __getSyntheticTypeSize(typeName, contextInheritorModule)
{
    var typeInfo = __syntheticTypes[typeName];
    var size = typeInfo.size;
    if (size === undefined)
    {
        size = 0;
        var curBit = 0;
        var addBit = true;
        for (var field of typeInfo.descriptor)
        {
            var fieldSize = __getSyntheticFieldSize(field, contextInheritorModule);
            var isBitField = !(field.bitLength === undefined);
            if (isBitField)
            {
                var originalFieldSize = fieldSize;
                if (!addBit)
                {
                    //
                    // Do not count subsequent portions of the bit field in the overall type size.
                    //
                    fieldSize = 0;
                }
                curBit += field.bitLength;
                if (curBit >= originalFieldSize * 8)
                {
                    curBit = 0;
                    addBit = true;
                }
                else
                {
                    addBit = false;
                }
            }
            else
            {
                curBit = 0;
                addBit = true;
            }

            if (typeInfo.isUnion)
            {
                if (fieldSize > size)
                {
                    size = fieldSize;
                }
            }
            else
            {
                size += fieldSize;
            }
        }
        typeInfo.size = size;
    }
    return size;
}

// __getSyntheticFieldSize:
//
// Returns the size of a field (whether synthetic or native) of a synthetic type.
//
function __getSyntheticFieldSize(field, contextInheritorModule)
{
    if (field.fieldType === undefined)
    {
        return __getSyntheticTypeSize(field.fieldSyntheticType, contextInheritorModule);
    }
    else
    {
        return host.evaluateExpression("sizeof(" + field.fieldType + ")", contextInheritorModule);
    }
}

// __getFieldValue:
//
// Returns the value of a field (whether synthetic or native) of a synthetic type.
//
function __getSyntheticFieldValue(field, addr, contextInheritorModule)
{
    if (field.fieldType === undefined)
    {
        var typeClass = __syntheticTypes[field.fieldSyntheticType].classObject;

        var arraySize = field.syntheticArraySize;
        if (arraySize === undefined)
        {
            return new typeClass(addr, contextInheritorModule);
        }
        else
        {
            var typeSize = __getSyntheticTypeSize(field.fieldSyntheticType, contextInheritorModule);
            var result = [];
            for (var i = 0; i < arraySize; ++i)
            {
                var entry = new typeClass(addr, contextInheritorModule);
                result.push(entry);
                addr = addr.add(typeSize);
            }
            return result;
        }
    }
    else
    {
        //
        // fieldType references basic types that should be present in **ANY** symbolic information.
        // Just grab the first module as the "reference module" for this purpose.  We cannot grab
        // "ntdll" generically as we want to avoid a situation in which the debugger opens a module (-z ...)
        // from failing.
        //
        var moduleName = contextInheritorModule.__ComparisonName;
        var typeObject = host.getModuleType(moduleName, field.fieldType, contextInheritorModule);
        var result = host.createTypedObject(addr, typeObject);

        //
        // If this is a synthetic bit field, do the appropriate mask and shift.
        //
        if (field.bitLength)
        {
            var size = host.evaluateExpression("sizeof(" + field.fieldType + ")");
            var one = 1;
            var allBits = 0xFFFFFFFF;
            if (size > 4)
            {
                one = host.Int64(1);
                allBits = host.Int64(0xFFFFFFFF, 0xFFFFFFFF);
            }

            var topBit = field.startingBit + field.bitLength;
            var topMask = one.bitwiseShiftLeft(topBit).subtract(1);

            result = result.bitwiseAnd(topMask);
            if (field.startingBit > 0)
            {
                var bottomMask = one.bitwiseShiftLeft(field.startingBit).subtract(1).bitwiseXor(allBits);
                result = result.bitwiseAnd(bottomMask);
                result = result.bitwiseShiftRight(field.startingBit);
            }
        }

        return result;
    }
}

// __preprocessTypeDescriptor:
//
// Preprocess the type descriptor and make certain things easier.
//
function __preprocessTypeDescriptor(typeDescriptor)
{
    for (var field of typeDescriptor)
    {
        var synType = field.fieldSyntheticType;
        if (!(synType === undefined))
        {
            //
            // If the synthetic type is an "array", we need to get the base type and actually tag it as an array
            //
            synType = synType.trim();
            if (synType.endsWith("]"))
            {
                //
                // Extract the array portion.
                //
                var extractor = /(.*)\[(\d+)\]$/;
                var results = extractor.exec(synType);
                field.fieldSyntheticType = results[1].trim();
                field.syntheticArraySize = parseInt(results[2]);
            }
        }
    }
}

// __embedType:
//
// Takes a field (for what might be an unnamed struct/union) and embeds it in the outer structure.
//
function __embedType(outerObject, fieldValue, contextInheritorModule)
{
    var names = Object.getOwnPropertyNames(fieldValue);
    for (var name of names)
    {
        //
        // Do *NOT* overwrite the projected things we place on the objects.
        //
        if (name != "targetLocation" && name != "targetSize")
        {
            outerObject[name] = fieldValue[name];
        }
    }
}

// __defineSyntheticType:
//
// Defines a "synthetic" type based on a descriptor (list of fields).  For a structure, FOO, this would
// allow syntax like this:
//
//     var myFoo = new __FOO(location);
//
// and then use of any synthetic field much as if it were an actual native object created with createTypedObject.
//
function __defineSyntheticType(typeName, typeDescriptor)
{
    __preprocessTypeDescriptor(typeDescriptor);

    class typeClass
    {
        constructor(addr, contextInheritorModule)
        {
            var curBit = 0;
            var fieldSize = 0;
            var addr64 = host.Int64(addr);
            this.targetLocation = addr64;
            this.targetSize = __getSyntheticTypeSize(typeName, contextInheritorModule);
            for (var field of typeDescriptor)
            {
                var fldSize = __getSyntheticFieldSize(field, contextInheritorModule);
                var isBitField = !(field.bitLength === undefined);
                if (isBitField)
                {
                    if (fieldSize == 0)
                    {
                        curBit = 0;
                        fieldSize = fldSize;
                    }
                    field.startingBit = curBit;
                }

                var fldValue = __getSyntheticFieldValue(field, addr64, contextInheritorModule);

                var fldName = field.fieldName;
                if (fldName === undefined)
                {
                    __embedType(this, fldValue, contextInheritorModule);
                }
                else
                {
                    this[field.fieldName] = fldValue;
                }

                if (isBitField)
                {
                    curBit += field.bitLength;
                    if (curBit >= fieldSize * 8)
                    {
                        curBit = 0;
                        fieldSize = 0;
                        addr64 = addr64.add(fldSize);
                    }
                }
                else
                {
                    addr64 = addr64.add(fldSize);
                }
            }
        }
    };

    __syntheticTypes[typeName] =
    {
        classObject: typeClass,
        descriptor: typeDescriptor,
        isUnion: false
    }

    __globalScope["__" + typeName] = typeClass;

    return typeClass;
}

// __defineSyntheticUnion:
//
// Defines a "synthetic" union based on a descriptor (list of all fields in the union).  For a union, FOO, this would
// allow syntax like this:
//
//     var myUnionFoo = new __FOO(location);
//
// and then use of any synthetic field much as if it were an actual native object created with createTypedObject.
//
function __defineSyntheticUnion(typeName, typeDescriptor)
{
    __preprocessTypeDescriptor(typeDescriptor);

    class typeClass
    {
        constructor(addr, contextInheritorModule)
        {
            var largestSize = 0;
            var curBit = 0;
            var fieldSize = 0;
            var addr64 = host.Int64(addr);
            this.targetLocation = addr64;
            this.targetSize = __getSyntheticTypeSize(typeName, contextInheritorModule);
            for (var field of typeDescriptor)
            {
                var fldSize = __getSyntheticFieldSize(field, contextInheritorModule);
                var isBitField = !(field.bitLength === undefined);
                if (isBitField)
                {
                    if (fieldSize == 0)
                    {
                        curBit = 0;
                        fieldSize = fldSize;
                    }
                    field.startingBit = curBit;
                }

                var fldValue = __getSyntheticFieldValue(field, addr64, contextInheritorModule);

                var fldName = field.fieldName;
                if (fldName === undefined)
                {
                    __embedType(this, fldValue, contextInheritorModule);
                }
                else
                {
                    this[field.fieldName] = fldValue;
                }

                if (isBitField)
                {
                    curBit += field.bitLength;
                    if (curBit >= fieldSize * 8)
                    {
                        curBit = 0;
                        fieldSize = 0;
                    }
                }
            }
        }
    };

    __syntheticTypes[typeName] =
    {
        classObject: typeClass,
        descriptor: typeDescriptor,
        isUnion: true
    }

    __globalScope["__" + typeName] = typeClass;
}

//
// Define the core PE data structures that we must have "type information" for in order for the rest of the
// script to work properly.
//
// Documentation on these can be found on MSDN: https://msdn.microsoft.com/en-us/library/ms809762.aspx
// This is "Peering Inside the PE: A Tour of the Win32 Portable Executable File Format"
//
__defineSyntheticType("_IMAGE_DOS_HEADER",            [{fieldName: "e_magic", fieldType: "unsigned short"},
                                                       {fieldName: "e_cblp", fieldType: "unsigned short"},
                                                       {fieldName: "e_cp", fieldType: "unsigned short"},
                                                       {fieldName: "e_crlc", fieldType: "unsigned short"},
                                                       {fieldName: "e_cparhdr", fieldType: "unsigned short"},
                                                       {fieldName: "e_minalloc", fieldType: "unsigned short"},
                                                       {fieldName: "e_maxalloc", fieldType: "unsigned short"},
                                                       {fieldName: "e_ss", fieldType: "unsigned short"},
                                                       {fieldName: "e_sp", fieldType: "unsigned short"},
                                                       {fieldName: "e_csum", fieldType: "unsigned short"},
                                                       {fieldName: "e_ip", fieldType: "unsigned short"},
                                                       {fieldName: "e_cs", fieldType: "unsigned short"},
                                                       {fieldName: "e_lfarlc", fieldType: "unsigned short"},
                                                       {fieldName: "e_ovno", fieldType: "unsigned short"},
                                                       {fieldName: "e_res", fieldType: "unsigned short[4]"},
                                                       {fieldName: "e_oemid", fieldType: "unsigned short"},
                                                       {fieldName: "e_oeminfo", fieldType: "unsigned short"},
                                                       {fieldName: "e_res2", fieldType: "unsigned short[10]"},
                                                       {fieldName: "e_lfanew", fieldType: "unsigned long"}]);

__defineSyntheticType("_IMAGE_FILE_HEADER",           [{fieldName: "Machine", fieldType: "unsigned short"},
                                                       {fieldName: "NumberOfSections", fieldType: "unsigned short"},
                                                       {fieldName: "TimeDateStamp", fieldType: "unsigned long"},
                                                       {fieldName: "PointerToSymbolTable", fieldType: "unsigned long"},
                                                       {fieldName: "NumberOfSymbols", fieldType: "unsigned long"},
                                                       {fieldName: "SizeOfOptionalHeader", fieldType: "unsigned short"},
                                                       {fieldName: "Characteristics", fieldType: "unsigned short"}]);

__defineSyntheticType("_IMAGE_OPTIONAL_HEADER32",     [{fieldName: "Magic", fieldType: "unsigned short"},
                                                       {fieldName: "MajorLinkerVersion", fieldType: "unsigned char"},
                                                       {fieldName: "MinorLinkerVersion", fieldType: "unsigned char"},
                                                       {fieldName: "SizeOfCode", fieldType: "unsigned long"},
                                                       {fieldName: "SizeOfInitializedData", fieldType: "unsigned long"},
                                                       {fieldName: "SizeOfUninitializedData", fieldType: "unsigned long"},
                                                       {fieldName: "AddressOfEntryPoint", fieldType: "unsigned long"},
                                                       {fieldName: "BaseOfCode", fieldType: "unsigned long"},
                                                       {fieldName: "BaseOfData", fieldType: "unsigned long"},
                                                       {fieldName: "ImageBase", fieldType: "unsigned long"},
                                                       {fieldName: "SectionAlignment", fieldType: "unsigned long"},
                                                       {fieldName: "FileAlignment", fieldType: "unsigned long"},
                                                       {fieldName: "MajorOperatingSystemVersion", fieldType: "unsigned short"},
                                                       {fieldName: "MinorOperatingSystemVersion", fieldType: "unsigned short"},
                                                       {fieldName: "MajorImageVersion", fieldType: "unsigned short"},
                                                       {fieldName: "MinorImageVersion", fieldType: "unsigned short"},
                                                       {fieldName: "MajorSubsystemVersion", fieldType: "unsigned short"},
                                                       {fieldName: "MinorSubsystemVersion", fieldType: "unsigned short"},
                                                       {fieldName: "Win32VersionValue", fieldType: "unsigned long"},
                                                       {fieldName: "SizeOfImage", fieldType: "unsigned long"},
                                                       {fieldName: "SizeOfHeaders", fieldType: "unsigned long"},
                                                       {fieldName: "CheckSum", fieldType: "unsigned long"},
                                                       {fieldName: "Subsystem", fieldType: "unsigned short"},
                                                       {fieldName: "DllCharacteristics", fieldType: "unsigned short"},
                                                       {fieldName: "SizeOfStackReserve", fieldType: "unsigned long"},
                                                       {fieldName: "SizeOfStackCommit", fieldType: "unsigned long"},
                                                       {fieldName: "SizeOfHeapReserve", fieldType: "unsigned long"},
                                                       {fieldName: "SizeOfHeapCommit", fieldType: "unsigned long"},
                                                       {fieldName: "LoaderFlags", fieldType: "unsigned long"},
                                                       {fieldName: "NumberOfRvaAndSizes", fieldType: "unsigned long"},
                                                       {fieldName: "DataDirectory", fieldSyntheticType: "_IMAGE_DATA_DIRECTORY[16]"}]);

__defineSyntheticType("_IMAGE_OPTIONAL_HEADER64",     [{fieldName: "Magic", fieldType: "unsigned short"},
                                                       {fieldName: "MajorLinkerVersion", fieldType: "unsigned char"},
                                                       {fieldName: "MinorLinkerVersion", fieldType: "unsigned char"},
                                                       {fieldName: "SizeOfCode", fieldType: "unsigned long"},
                                                       {fieldName: "SizeOfInitializedData", fieldType: "unsigned long"},
                                                       {fieldName: "SizeOfUninitializedData", fieldType: "unsigned long"},
                                                       {fieldName: "AddressOfEntryPoint", fieldType: "unsigned long"},
                                                       {fieldName: "BaseOfCode", fieldType: "unsigned long"},
                                                       {fieldName: "ImageBase", fieldType: "unsigned __int64"},
                                                       {fieldName: "SectionAlignment", fieldType: "unsigned long"},
                                                       {fieldName: "FileAlignment", fieldType: "unsigned long"},
                                                       {fieldName: "MajorOperatingSystemVersion", fieldType: "unsigned short"},
                                                       {fieldName: "MinorOperatingSystemVersion", fieldType: "unsigned short"},
                                                       {fieldName: "MajorImageVersion", fieldType: "unsigned short"},
                                                       {fieldName: "MinorImageVersion", fieldType: "unsigned short"},
                                                       {fieldName: "MajorSubsystemVersion", fieldType: "unsigned short"},
                                                       {fieldName: "MinorSubsystemVersion", fieldType: "unsigned short"},
                                                       {fieldName: "Win32VersionValue", fieldType: "unsigned long"},
                                                       {fieldName: "SizeOfImage", fieldType: "unsigned long"},
                                                       {fieldName: "SizeOfHeaders", fieldType: "unsigned long"},
                                                       {fieldName: "CheckSum", fieldType: "unsigned long"},
                                                       {fieldName: "Subsystem", fieldType: "unsigned short"},
                                                       {fieldName: "DllCharacteristics", fieldType: "unsigned short"},
                                                       {fieldName: "SizeOfStackReserve", fieldType: "unsigned __int64"},
                                                       {fieldName: "SizeOfStackCommit", fieldType: "unsigned __int64"},
                                                       {fieldName: "SizeOfHeapReserve", fieldType: "unsigned __int64"},
                                                       {fieldName: "SizeOfHeapCommit", fieldType: "unsigned __int64"},
                                                       {fieldName: "LoaderFlags", fieldType: "unsigned long"},
                                                       {fieldName: "NumberOfRvaAndSizes", fieldType: "unsigned long"},
                                                       {fieldName: "DataDirectory", fieldSyntheticType: "_IMAGE_DATA_DIRECTORY[16]"}]);

__defineSyntheticType("_IMAGE_DATA_DIRECTORY",        [{fieldName: "VirtualAddress", fieldType: "unsigned long"},
                                                       {fieldName: "Size", fieldType: "unsigned long"}]);

__defineSyntheticType("_IMAGE_RESOURCE_DIRECTORY",    [{fieldName: "Characteristics", fieldType: "unsigned long"},
                                                       {fieldName: "TimeDateStamp", fieldType: "unsigned long"},
                                                       {fieldName: "MajorVersion", fieldType: "unsigned short"},
                                                       {fieldName: "MinorVersion", fieldType: "unsigned short"},
                                                       {fieldName: "NumberOfNamedEntries", fieldType: "unsigned short"},
                                                       {fieldName: "NumberOfIdEntries", fieldType: "unsigned short"}]);

__defineSyntheticType("_IMAGE_EXPORT_DIRECTORY",      [{fieldName: "Characteristics", fieldType: "unsigned long"},
                                                       {fieldName: "TimeDateStamp", fieldType: "unsigned long"},
                                                       {fieldName: "MajorVersion", fieldType: "unsigned short"},
                                                       {fieldName: "MinorVersion", fieldType: "unsigned short"},
                                                       {fieldName: "Name", fieldType: "unsigned long"},
                                                       {fieldName: "Base", fieldType: "unsigned long"},
                                                       {fieldName: "NumberOfFunctions", fieldType: "unsigned long"},
                                                       {fieldName: "NumberOfNames", fieldType: "unsigned long"},
                                                       {fieldName: "AddressOfFunctions", fieldType: "unsigned long"},
                                                       {fieldName: "AddressOfNames", fieldType: "unsigned long"},
                                                       {fieldName: "AddressOfNameOrdinals", fieldType: "unsigned long"}]);

__defineSyntheticUnion("__UNNAMED_IIDUNION",          [{fieldName: "OriginalFirstThunk", fieldType: "unsigned long"},
                                                       {fieldName: "Characteristics", fieldType: "unsigned long"}]);

__defineSyntheticType("_IMAGE_IMPORT_DESCRIPTOR",     [{fieldSyntheticType: "__UNNAMED_IIDUNION"},
                                                       {fieldName: "TimeDateStamp", fieldType: "unsigned long"},
                                                       {fieldName: "ForwarderChain", fieldType: "unsigned long"},
                                                       {fieldName: "Name", fieldType: "unsigned long"},
                                                       {fieldName: "FirstThunk", fieldType: "unsigned long"}]);

__defineSyntheticType("_IMAGE_RESOURCE_DIR_STRING_U", [{fieldName: "Length", fieldType: "unsigned short"},
                                                       {fieldName: "NameString", fieldType: "wchar_t[1]"}]);

__defineSyntheticType("_IMAGE_RESOURCE_DATA_ENTRY",   [{fieldName: "OffsetToData", fieldType: "unsigned long"},
                                                       {fieldName: "Size", fieldType: "unsigned long"},
                                                       {fieldName: "CodePage", fieldType: "unsigned long"},
                                                       {fieldName: "Reserved", fieldType: "unsigned long"}]);

__defineSyntheticType("__UNNAMED_IDD_ATTSTRUCT",      [{fieldName: "RvaBased", fieldType: "unsigned long", bitLength: 1},
                                                       {fieldName: "ReservedAttributes", fieldType: "unsigned long", bitLength: 31}]);

__defineSyntheticUnion("__IDD_ATTUNION",              [{fieldName: "AllAttributes", fieldType: "unsigned long"},
                                                       {fieldSyntheticType: "__UNNAMED_IDD_ATTSTRUCT"}]);

__defineSyntheticType("_IMAGE_DELAYLOAD_DESCRIPTOR",  [{fieldName: "Attributes", fieldSyntheticType: "__IDD_ATTUNION"},
                                                       {fieldName: "DllNameRVA", fieldType: "unsigned long"},
                                                       {fieldName: "ModuleHandleRVA", fieldType: "unsigned long"},
                                                       {fieldName: "ImportAddressTableRVA", fieldType: "unsigned long"},
                                                       {fieldName: "ImportNameTableRVA", fieldType: "unsigned long"},
                                                       {fieldName: "BoundImportAddressTableRVA", fieldType: "unsigned long"},
                                                       {fieldName: "UnloadInformationTableRVA", fieldType: "unsigned long"},
                                                       {fieldName: "TimeDateStamp", fieldType: "unsigned long"}]);

__defineSyntheticType("__UNNAMED_IRDE_UNION1_STRUCT", [{fieldName: "NameOffset", fieldType: "unsigned long", bitLength: 31},
                                                       {fieldName: "NameIsString", fieldType: "unsigned long", bitLength: 1}]);


__defineSyntheticUnion("__UNNAMED_IRDE_UNION1",       [{fieldSyntheticType: "__UNNAMED_IRDE_UNION1_STRUCT"},
                                                       {fieldName: "Name", fieldType: "unsigned long"},
                                                       {fieldName: "Id", fieldType: "unsigned short"}]);

__defineSyntheticType("__UNNAMED_IRDE_UNION2_STRUCT", [{fieldName: "OffsetToDirectory", fieldType: "unsigned long", bitLength: 31},
                                                       {fieldName: "DataIsDirectory", fieldType: "unsigned long", bitLength: 1}]);

__defineSyntheticUnion("__UNNAMED_IRDE_UNION2",       [{fieldName: "OffsetToData", fieldType: "unsigned long"},
                                                       {fieldSyntheticType: "__UNNAMED_IRDE_UNION2_STRUCT"}]);


__defineSyntheticType("_IMAGE_RESOURCE_DIRECTORY_ENTRY", [{fieldSyntheticType: "__UNNAMED_IRDE_UNION1"},
                                                          {fieldSyntheticType: "__UNNAMED_IRDE_UNION2"}]);

__defineSyntheticType("VS_FIXEDFILEINFO", [{fieldName: "dwSignature", fieldType: "unsigned long"},
                                           {fieldName: "dwStrucVersion", fieldType: "unsigned long"},
                                           {fieldName: "dwFileVersionMS", fieldType: "unsigned long"},
                                           {fieldName: "dwFileVersionLS", fieldType: "unsigned long"},
                                           {fieldName: "dwProductVersionMS", fieldType: "unsigned long"},
                                           {fieldName: "dwProductVersionLS", fieldType: "unsigned long"},
                                           {fieldName: "dwFileFlagsMask", fieldType: "unsigned long"},
                                           {fieldName: "dwFileFlags", fieldType: "unsigned long"},
                                           {fieldName: "dwFileOS", fieldType: "unsigned long"},
                                           {fieldName: "dwFileType", fieldType: "unsigned long"},
                                           {fieldName: "dwFileSubtype", fieldType: "unsigned long"},
                                           {fieldName: "dwFileDateMS", fieldType: "unsigned long"},
                                           {fieldName: "dwFileDateLS", fieldType: "unsigned long"}]);

__defineSyntheticType("_IMAGE_DEBUG_DIRECTORY", [{fieldName: "Characteristics", fieldType: "unsigned long"},
                                                 {fieldName: "TimeDateStamp", fieldType: "unsigned long"},
                                                 {fieldName: "MajorVersion", fieldType: "unsigned short"},
                                                 {fieldName: "MinorVersion", fieldType: "unsigned short"},
                                                 {fieldName: "Type", fieldType: "unsigned long"},
                                                 {fieldName: "SizeOfData", fieldType: "unsigned long"},
                                                 {fieldName: "AddressOfRawData", fieldType: "unsigned long"},
                                                 {fieldName: "PointerToRawData", fieldType: "unsigned long"}]);

__defineSyntheticType("___ELF_HEADER32", [{fieldName: "Identifier", fieldType: "unsigned char[4]"}, // e_ident 0-3
                                          {fieldName: "Class", fieldType: "unsigned char"},         // .. EI_CLASS
                                          {fieldName: "Endianness", fieldType: "unsigned char"},    // .. EI_DATA
                                          {fieldName: "Version", fieldType: "unsigned char"},       // .. EI_VERSION
                                          {fieldName: "OSABI", fieldType: "unsigned char"},         // .. EI_OSABI
                                          {fieldName: "ABIVersion", fieldType: "unsigned char"},    // .. EI_ABIVERSION
                                          {fieldName: "Pad", fieldType: "unsigned char[7]"},        // .. EI_PAD
                                          {fieldName: "Type", fieldType: "unsigned short"},         // e_type
                                          {fieldName: "Machine", fieldType: "unsigned short"},      // e_machine
                                          {fieldName: "ELFVersion", fieldType: "unsigned long"},    // e_version
                                          {fieldName: "Entry", fieldType: "unsigned long"},         // e_entry
                                          {fieldName: "OffsetPHdr", fieldType: "unsigned long"},    // e_phoff
                                          {fieldName: "OffsetSHdr", fieldType: "unsigned long"},    // e_shoff
                                          {fieldName: "Flags", fieldType: "unsigned long"},         // e_flags
                                          {fieldName: "EHSize", fieldType: "unsigned short"},       // e_ehsize
                                          {fieldName: "PHEntSize", fieldType: "unsigned short"},    // e_phentsize
                                          {fieldName: "PHNum", fieldType: "unsigned short"},        // e_phnum
                                          {fieldName: "SHEntSize", fieldType: "unsigned short"},    // e_shentsize
                                          {fieldName: "SHNum", fieldType: "unsigned short"},        // e_shnum
                                          {fieldName: "SHStrNdx", fieldType: "unsigned short"}]);   // e_shstrndx

__defineSyntheticType("___ELF_HEADER64", [{fieldName: "Identifier", fieldType: "unsigned char[4]"}, // e_ident 0-3
                                          {fieldName: "Class", fieldType: "unsigned char"},         // .. EI_CLASS
                                          {fieldName: "Endianness", fieldType: "unsigned char"},    // .. EI_DATA
                                          {fieldName: "Version", fieldType: "unsigned char"},       // .. EI_VERSION
                                          {fieldName: "OSABI", fieldType: "unsigned char"},         // .. EI_OSABI
                                          {fieldName: "ABIVersion", fieldType: "unsigned char"},    // .. EI_ABIVERSION
                                          {fieldName: "Pad", fieldType: "unsigned char[7]"},        // .. EI_PAD
                                          {fieldName: "Type", fieldType: "unsigned short"},         // e_type
                                          {fieldName: "Machine", fieldType: "unsigned short"},      // e_machine
                                          {fieldName: "ELFVersion", fieldType: "unsigned long"},    // e_version
                                          {fieldName: "Entry", fieldType: "unsigned __int64"},      // e_entry
                                          {fieldName: "OffsetPHdr", fieldType: "unsigned __int64"}, // e_phoff
                                          {fieldName: "OffsetSHdr", fieldType: "unsigned __int64"}, // e_shoff
                                          {fieldName: "Flags", fieldType: "unsigned long"},         // e_flags
                                          {fieldName: "EHSize", fieldType: "unsigned short"},       // e_ehsize
                                          {fieldName: "PHEntSize", fieldType: "unsigned short"},    // e_phentsize
                                          {fieldName: "PHNum", fieldType: "unsigned short"},        // e_phnum
                                          {fieldName: "SHEntSize", fieldType: "unsigned short"},    // e_shentsize
                                          {fieldName: "SHNum", fieldType: "unsigned short"},        // e_shnum
                                          {fieldName: "SHStrNdx", fieldType: "unsigned short"}]);   // e_shstrndx

__defineSyntheticType("___ELF_SECTIONHEADER32", [{fieldName: "Name", fieldType: "unsigned long"},    // sh_name
                                                 {fieldName: "Type", fieldType: "unsigned long"},    // sh_type
                                                 {fieldName: "Flags", fieldType: "unsigned long"},   // sh_flags
                                                 {fieldName: "Address", fieldType: "unsigned long"}, // sh_addr
                                                 {fieldName: "Offset", fieldType: "unsigned long"},  // sh_offset
                                                 {fieldName: "Size", fieldType: "unsigned long"},    // sh_size
                                                 {fieldName: "Link", fieldType: "unsigned long"},    // sh_link
                                                 {fieldName: "Info", fieldType: "unsigned long"},    // sh_info
                                                 {fieldName: "Align", fieldType: "unsigned long"},   // sh_addralign
                                                 {fieldName: "EntSize", fieldType: "unsigned long"}]); // sh_entsize
 
__defineSyntheticType("___ELF_SECTIONHEADER64", [{fieldName: "Name", fieldType: "unsigned long"},    // sh_name
                                                 {fieldName: "Type", fieldType: "unsigned long"},    // sh_type
                                                 {fieldName: "Flags", fieldType: "unsigned __int64"}, // sh_flags
                                                 {fieldName: "Address", fieldType: "unsigned __int64"}, // sh_addr
                                                 {fieldName: "Offset", fieldType: "unsigned __int64"}, // sh_offset
                                                 {fieldName: "Size", fieldType: "unsigned __int64"}, // sh_size
                                                 {fieldName: "Link", fieldType: "unsigned long"},    // sh_link
                                                 {fieldName: "Info", fieldType: "unsigned long"},    // sh_info
                                                 {fieldName: "Align", fieldType: "unsigned __int64"}, // sh_addralign
                                                 {fieldName: "EntSize", fieldType: "unsigned __int64"}]); // sh_entsize

__defineSyntheticType("___ELF_PROGRAMHEADER32", [{fieldName: "Type", fieldType: "unsigned long"},     // p_type
                                                 {fieldName: "Offset", fieldType: "unsigned long"},   // p_offset
                                                 {fieldName: "VAddr", fieldType: "unsigned long"},    // p_vaddr
                                                 {fieldName: "PAddr", fieldType: "unsigned long"},    // p_paddr
                                                 {fieldName: "FileSize", fieldType: "unsigned long"}, // p_filesz
                                                 {fieldName: "MemSize", fieldType: "unsigned long"},  // p_memsz
                                                 {fieldName: "Flags", fieldType: "unsigned long"},    // p_flags
                                                 {fieldName: "Align", fieldType: "unsigned long"}]);  // p_align

__defineSyntheticType("___ELF_PROGRAMHEADER64", [{fieldName: "Type", fieldType: "unsigned long"},     // p_type
                                                 {fieldName: "Flags", fieldType: "unsigned long"},    // p_flags
                                                 {fieldName: "Offset", fieldType: "unsigned __int64"},// p_offset
                                                 {fieldName: "VAddr", fieldType: "unsigned __int64"}, // p_vaddr
                                                 {fieldName: "PAddr", fieldType: "unsigned __int64"}, // p_paddr
                                                 {fieldName: "FileSize", fieldType: "unsigned __int64"},// p_filesz
                                                 {fieldName: "MemSize", fieldType: "unsigned __int64"},// p_memsz
                                                 {fieldName: "Align", fieldType: "unsigned __int64"}]);// p_align

__defineSyntheticType("___ELF_NOTE", [{fieldName: "NameSize", fieldType: "unsigned long"},            // n_namesz
                                      {fieldName: "DescSize", fieldType: "unsigned long"},            // n_descsz
                                      {fieldName: "Type", fieldType: "unsigned long"}]);              // n_type

__defineSyntheticType("___ELF_DYNAMICENTRY32", [{fieldName: "Tag", fieldType: "unsigned long"},
                                                {fieldName: "Val", fieldType: "unsigned long"}]);

__defineSyntheticType("___ELF_DYNAMICENTRY64", [{fieldName: "Tag", fieldType: "unsigned __int64"},
                                                {fieldName: "Val", fieldType: "unsigned __int64"}]);

__defineSyntheticType("___ELF_RDEBUG64", [{fieldName: "Version", fieldType: "int"},
                                          {fieldName: "Padding", fieldType: "int"},
                                          {fieldName: "AddrRMap", fieldType: "unsigned __int64"},
                                          {fieldName: "RBrk", fieldType: "unsigned __int64"}]);

__defineSyntheticType("___ELF_RDEBUG32", [{fieldName: "Version", fieldType: "int"},
                                          {fieldName: "Padding", fieldType: "int"},
                                          {fieldName: "AddrRMap", fieldType: "unsigned long"},
                                          {fieldName: "RBrk", fieldType: "unsigned long"}]);

__defineSyntheticType("___ELF_LINKMAPENTRY64", [{fieldName: "LAddr", fieldType: "unsigned __int64"},
                                                {fieldName: "LName", fieldType: "unsigned __int64"},
                                                {fieldName: "LDyn", fieldType: "unsigned __int64"},
                                                {fieldName: "LNext", fieldType: "unsigned __int64"}]);

__defineSyntheticType("___ELF_LINKMAPENTRY32", [{fieldName: "LAddr", fieldType: "unsigned long"},
                                                {fieldName: "LName", fieldType: "unsigned long"},
                                                {fieldName: "LDyn", fieldType: "unsigned long"},
                                                {fieldName: "LNext", fieldType: "unsigned long"}]);
 
var __guidType = __defineSyntheticType("GUID", [{fieldName: "Data1", fieldType: "unsigned long"},
                                                {fieldName: "Data2", fieldType: "unsigned short"},
                                                {fieldName: "Data3", fieldType: "unsigned short"},
                                                {fieldName: "Data4", fieldType: "unsigned char[8]"}]);

// __convertHex:
//
// Does a hex string conversion forcing the size to be the number of bits specified.
//
function __convertHex(val, bits)
{
    var strVal = "";
    bits -= 4;
    while(true)
    {
        var nyb = (val >> bits) & 0xF;
        if (nyb >= 0 && nyb <= 9)
        {
            strVal += String.fromCharCode(48 + nyb);
        }
        else
        {
            strVal += String.fromCharCode(55 + nyb);
        }
        if (bits < 4)
        {
            break;
        }
        bits -= 4;
    }
    return strVal;
}

//
// If we do not have the key module, we need a string conversion function for GUIDs.
//
__guidType.prototype.toString = function()
{
    var guidStr = "{";
    guidStr =  "{" +
               __convertHex(this.Data1, 32) + "-" + 
               __convertHex(this.Data2, 16) + "-" +
               __convertHex(this.Data3, 16) + "-" +
               __convertHex(this.Data4[0], 8) + __convertHex(this.Data4[1], 8) + "-" +
               __convertHex(this.Data4[2], 8) +
               __convertHex(this.Data4[3], 8) +
               __convertHex(this.Data4[4], 8) +
               __convertHex(this.Data4[5], 8) +
               __convertHex(this.Data4[6], 8) +
               __convertHex(this.Data4[7], 8) +
               "}";

    return guidStr;
};

// __createSyntheticTypedObject:
//
// Wrapper that forces creation of a type based on our synthetic type tables.
//
function __createSyntheticTypedObject(addrLoc, moduleName, typeName, contextInheritorModule, syntheticAltName)
{
    var creatorName = typeName;
    if (!(syntheticAltName === undefined))
    {
        creatorName = syntheticAltName;
    }
    var typeClass = __globalScope["__" + creatorName];
    var result = new typeClass(addrLoc, contextInheritorModule);
    return result;
}

// __createTypedObject:
//
// Wrapper for host.createTypedObject which will either do so or will use our synthetic symbols if a public
// PDB is loaded.
//
function __createTypedObject(addrLoc, moduleName, typeName, contextInheritorModule, syntheticAltName)
{
    if (!__checkedPrivateSymbols && !__forceUsePublicSymbols)
    {
        //
        // If we haven't yet checked, do a type check in NTDLL against something we know isn't in the publics.
        //
        __usePrivateSymbols = false;
        try
        {
            var privateType = host.getModuleType(moduleName, "_IMAGE_RESOURCE_DATA_ENTRY", contextInheritorModule);
            __usePrivateSymbols = !(privateType === undefined || privateType == null);
        }
        catch(e)
        {
        }
        __checkedPrivateSymbols = true;
    }

    if (__usePrivateSymbols)
    {
        return host.createTypedObject(addrLoc, moduleName, typeName, contextInheritorModule);
    }
    else
    {
        var creatorName = typeName;
        if (!(syntheticAltName === undefined))
        {
            creatorName = syntheticAltName;
        }
        var typeClass = __globalScope["__" + creatorName];
        var result = new typeClass(addrLoc, contextInheritorModule);
        return result;
    }
}

function __getEnumerant(enumNames, val)
{
    var enumerants = Object.getOwnPropertyNames(enumNames);
    for (var enumerantName of enumerants)
    {
        if (enumNames[enumerantName] == val)
        {
            return enumerantName;
        }
    }

    return null;
}

//*************************************************
// Image Information and ELF Parsing

class __ELFImageInformation
{
    constructor(module)
    {
        this.__module = module;
        this.__ranges = [];

        //
        // Go through the program headers and figure out all file offset <-> RVA mappings
        // 
        for (var ph of this.ProgramHeaders)
        {
            var header = ph.__programHeader;
            if (header.FileSize != 0 && header.MemSize != 0 && header.FileSize == header.MemSize)
            {
                this.__ranges.push( { memoryStart: header.VAddr, fileStart: header.Offset, size: header.FileSize });
            }
        }
    }

    get ClassTypes()
    {
        return { 
            Bitness32 : 1,
            Bitness64 : 2
        };
    }
    
    get OSTypes()
    {
        return {
            SystemV: 0,
            HPUX: 1,
            NetBSD: 2,
            Linux: 3,
            GNUHurd: 4,
            Solaris: 6,
            AIX: 7,
            IRIX: 8,
            FreeBSD: 9,
            Tru64: 10,
            NovellModesto: 11,
            OpenBSD: 12,
            OpenVMS: 13,
            NonStopKernel: 13,
            AROS: 15,
            FenixOS: 16,
            CloudABI: 17,
            Sortix: 83
        };
    }

    get ImageTypes()
    {
        return {
            Relocatable: 1,
            Executable: 2,
            SharedObject: 3,
            CoreFile: 4
        };
    }

    get MachineTypes()
    {
        return {
            Undefined: 0,
            SPARC: 2,
            x86: 3,
            MIPS: 8,
            PowerPC: 20,
            S390: 22,
            ARM: 40,
            SuperH: 42,
            IA64: 50,
            x64: 62,
            AArch64: 0xB7,
            RISCV: 0xF3
        };
    }

    get ProgramHeaderTypes()
    {
        return {
            PT_NULL: 0,
            PT_LOAD: 1,
            PT_DYNAMIC: 2,
            PT_INTERP: 3,
            PT_NOTE: 4,
            PT_SHLIB: 5,
            PT_PHDR: 6,
            PT_LOOS: 0x60000000,
            GNU_EH_FRAME: 0x6474E550,
            GNU_STACK: 0x6474E551,
            GNU_RELRO: 0x6474E552,
            PT_HIOS: 0x6FFFFFFF,
            PT_LOPROC: 0x70000000,
            PT_HIPROC: 0x7FFFFFFF
        };
    }

    get DynamicTypes()
    {
        return {
            DT_NULL: 0,
            DT_NEEDED: 1,
            DT_PLTRELSZ: 2,
            DT_PLTGOT: 3,
            DT_HASH: 4,
            DT_STRTAB: 5,
            DT_SYMTAB: 6,
            DT_RELA: 7,
            DT_RELASZ: 8,
            DT_RELAENT: 9,
            DT_STRSZ: 10,
            DT_SYMENT: 11,
            DT_INIT: 12,
            DT_FINI: 13,
            DT_SONAME: 14,
            DT_RPATH: 15,
            DT_SYMBOLIC: 16,
            DT_REL: 17,
            DT_RELSZ: 18,
            DT_RELENT: 19,
            DT_PLTREL: 20,
            DT_DEBUG: 21,
            DT_TEXTREL: 22,
            DT_JMPREL: 23,
            DT_BIND_NOW: 24,
            DT_INIT_ARRAY: 25,
            DT_FINI_ARRAY: 26,
            DT_INIT_ARRAYSZ: 27,
            DT_FINI_ARRAYSZ: 28,
            DT_RUNPATH: 29,
            DT_FLAGS: 30,
            DT_ENCODING: 31,
            DT_PREINIT_ARRAY: 32,
            DT_PREINIT_ARRAYSZ: 33,
            DT_MAXPOSTAGS: 34,

            DT_SUNW_AUXILIARY: 0x6000000D,
            DT_SUNW_RTLDINF: 0x6000000E,
            DT_SUNW_FILTER: 0x6000000F,
            DT_SUNW_CAP: 0x60000010,
            DT_VALRNGLO: 0x6FFFFD00,
            DT_GNU_PRELINKED: 0x6FFFFDF5,
            DT_GNU_CONFLICTSZ: 0x6FFFFDF6,
            DT_GNU_LIBLISTSZ: 0x6FFFFDF7,
            DT_CHECKSUM: 0x6FFFFDF8,
            DT_PLTPADSZ: 0x6FFFFDF9,
            DT_MOVEENT: 0x6FFFFDFA,
            DT_MOVESZ: 0x6FFFFDFB,
            DT_FEATURE_1: 0x6FFFFDFC,
            DT_POSFLAG_1: 0x6FFFFDFD,
            DT_SYMINSZ: 0x6FFFFDFE,
            DT_SYMINENT: 0x6FFFFDFF,

            DT_GNU_HASH: 0x6FFFFEF5,
            DT_GNU_CONFLICT: 0x6FFFFEF8,
            DT_GNU_LIBLIST: 0x6FFFFEF9,
            DT_CONFIG: 0x6FFFFEFA,
            DT_DEPAUDIT: 0x6FFFFEFB,
            DT_AUDIT: 0x6FFFFEFC,
            DT_PLTPAD: 0x6FFFFEFD,
            DT_MOVETAB: 0x6FFFFEFE,
            DT_SYMINFO: 0x6FFFFEFF,
            DT_VERSYM: 0x6FFFFFF0,
            DT_RELACOUNT: 0x6FFFFFF9,
            DT_RELCOUNT: 0x6FFFFFFA,
            DT_FLAGS_1: 0x6FFFFFFB,
            DT_VERDEF: 0x6FFFFFFC,
            DT_VERDEFNUM: 0x6FFFFFFD,
            DT_VERNEED: 0x6FFFFFFE,
            DT_NVERNEEDNUM: 0x6FFFFFFF
        };
    }

    get SectionHeaderTypes()
    {
        return {
            SHT_NULL: 0,
            SHT_PROGBITS: 1,
            SHT_SYMTAB: 2,
            SHT_STRTAB: 3,
            SHT_RELA: 4,
            SHT_HASH: 5,
            SHT_DYNAMIC: 6,
            SHT_NOTE: 7,
            SHT_NOBITS: 8,
            SHT_REL: 9,
            SHT_SHLIB: 10,
            SHT_DYNSYM: 11,
            SHT_INIT_ARRAY: 14,
            SHT_FINI_ARRAY: 15,
            SHT_PREINIT_ARRAY: 16,
            SHT_GROUP: 17,
            SHT_SYMTAB_SHNDX: 18,
            SHT_NUM: 19,
            SHT_LOOS: 0x60000000
        };
    }

    get NoteTypes()
    {
        return {
            NT_ABI_TAG: 1,
            NT_PRSTATUS: 1,
            NT_GNU_HWCAP: 2,
            NT_FPREGSET: 2,
            NT_GNU_BUILD_ID: 3,
            NT_PRPSINFO: 3,
            NT_GNU_GOLD_VERSION: 4,
            NT_AUXV: 6,
            NT_PSTATUS: 10,
            NT_FPREGS: 12,
            NT_PSINFO: 13,
            NT_LWPSTATUS: 16,
            NT_LPWSINFO: 17,
            NT_FDO_BUILDINFO: 0xcafe1a7e
        };
    }

    get ELFHeader()
    {
        var header = __createSyntheticTypedObject(this.__module.BaseAddress, __keyModule, "___ELF_HEADER32", this.__module);

        if (header.Identifier[0] != 0x7F || header.Identifier[1] != 0x45 ||
            header.Identifier[2] != 0x4c || header.Identifier[3] != 0x46)
        {
            throw new Error("Unrecognized ELF header for module: " + this.__module.Name);
        }

        if (header.Class == this.ClassTypes.Bitness32)
        {
            return header;
        }
        else if (header.Class == this.ClassTypes.Bitness64)
        {
            return __createSyntheticTypedObject(this.__module.BaseAddress, __keyModule, "___ELF_HEADER64", this.__module);
        }
    }

    // GetRVAForOffset
    //
    // Gets the RVA for an offset within an ELF image.
    //
    GetRVAForOffset(offset)
    {
        for (var range of this.__ranges)
        {
            if (offset.compareTo(range.fileStart) >= 0 && offset.compareTo(range.fileStart.add(range.size)))
            {
                var delta = offset.subtract(range.fileStart);
                var addr = range.memoryStart.add(delta);
                var size = range.size.subtract(delta);
                return { vaddr: addr, size: size };
            }
        }
        return null;
    }

    // GetSectionHeaderBase():
    //
    // Gets the base address of a section header given its index.
    //
    GetSectionHeaderBase(ndx)
    {
        var elfHeader = this.ELFHeader;
        var shdrsBase = elfHeader.OffsetSHdr;

        if (ndx < 0 || ndx >= elfHeader.SHNum)
        {
            throw new RangeError("Invalid section index: " + ndx);
        }

        var shdrsVAddr = GetRVAForOffset(shdrsBase);
        if (!shdrsVAddr)
        {
            return null;
        }
        shrdsVAddr.vaddr.add(this.__module.BaseAddress);

        var entSize = elfHeader.SHEntSize;
        return shdrsVAddr.vaddr.add(entSize.multiply(ndx));
    }

    // GetSectionHeader():
    //
    // Gets the section header for a given section.  If it is not mapped (the table is often not mapped
    // into process memory), null is returned.
    //
    GetSectionHeader(ndx)
    {
        var addr = GetSectionHeaderBase(ndx);
        if (!addr)
        {
            return null;
        }

        return _createSyntheticTypedObject(addr, 
                                           __keyModule, 
                                           this.Is64Bit ? "___ELF_SECTIONHEADER64" : "___ELF_SECTIONHEADER32",
                                           this.__module);
    }

    // GetSection():
    //
    // Gets a particular section in the ELF.  If it is not mapped (the table is often not mapped
    // into process memory), null is returned.
    //
    GetSection(ndx)
    {
        var header = GetSectionHeader(ndx);
        if (!header)
        {
            return null;
        }

        // @TODO:
        return null;
    }

    get SectionNamesBase()
    {
        return this.__module.BaseAddress.add(this.ELFHeader.SHStrNdx);
    }

    get ProgramHeaders()
    {
        return new __ELFProgramHeaders(this.__module, this);
    }

    get Is64Bit()
    {
        return (this.ELFHeader.Class == this.ClassTypes.Bitness64);
    }
}

class __GNUBuildIdData
{
    constructor(note)
    {
        this.__note = note;
    }

    get BuildID()
    {
        var str = "";
        var dataBytes = this.__note.Data;
        var dataSize = this.__note.__noteHeader.DescSize;
        for (var i = 0; i < dataSize; ++i)
        {
            str += __convertHex(dataBytes[i], 8);
        }
        return str;
    }
}

class __FdoBuildInfo
{
    constructor(note)
    {
        this.__note = note;
        
        var dataBytes = this.__note.Data;
        var dataSize = this.__note.__noteHeader.DescSize;

        var jsonStr = "";
        for (var i = 0; i < dataSize; ++i)
        {
            jsonStr += String.fromCharCode(dataBytes[i]);
        }

        this.__buildInfo = JSON.parse(jsonStr);
    }

    get BuildInfo()
    {
        return this.__buildInfo;
    }
}

class __ELFNote
{
    constructor(content, noteHeader)
    {
        this.__content = content;
        this.__noteHeader = noteHeader;
    }

    toString()
    {
        var str = this.Name + " (" + this.Type + ")";
        return str;
    }

    get Name()
    {
        var nameAddr = this.__noteHeader.targetLocation.add(12);
        var name = host.memory.readString(nameAddr, this.__noteHeader.NameSize - 1);

        //
        // In the event someone put the alignment padding in the namesz and there are embedded nulls
        // in the string, strip them.  Chakra seems to return Length==<to first null terminator> with embedded nulls
        // which I don't believe is correct.  In any case, walk backwards from .NameSize to find the first non
        // null and strip.
        //
        var idx = this.__noteHeader.NameSize - 2;
        while(idx > 0 && name.charCodeAt(idx) == 0) { --idx; }
        if (idx < this.__noteHeader.NameSize - 2)
        {
            name = name.substring(0, idx + 1);
        }

        return name;
    }

    get Type()
    {
        var typeName = __getEnumerant(this.__content.__programHeader.__imageInfo.NoteTypes, this.__noteHeader.Type);
        if (!typeName)
        {
            return "Unknown (" + this.__programHeader.Type.toString(16) + ")";
        }
        return typeName;
    }

    get DataAddress()
    {
        return this.__noteHeader.targetLocation.add(12).add(this.__noteHeader.NameSize);
    }

    get Data()
    {
        var dataBytes = host.memory.readMemoryValues(this.DataAddress, this.__noteHeader.DescSize, 1, false, 
                                                     this.__content.__programHeader.__module);
        return dataBytes;
    }

    get Content()
    {
        var ntTypes = this.__content.__programHeader.__imageInfo.NoteTypes;
        if (this.Name == "GNU" && this.__noteHeader.Type == ntTypes.NT_GNU_BUILD_ID)
        {
            return new __GNUBuildIdData(this);
        }
        else if (this.Name == "FDO" && this.__noteHeader.Type == ntTypes.NT_FDO_BUILDINFO)
        {
            return new __FdoBuildInfo(this);
        }
        return undefined;
    }
}

class __ELFNotes
{
    constructor(content)
    {
        this.__content = content;
    }

    *[Symbol.iterator]()
    {
        var noteHeaderSize = 12;

        var address = this.__content.__programHeader.Address;
        var bytesRemaining = this.__content.__programHeader.Header.MemSize;
        while (bytesRemaining > noteHeaderSize)
        {
            var noteHeader = __createSyntheticTypedObject(address, __keyModule, "___ELF_NOTE", 
                                                          this.__content.__programHeader.__module);

            var totalSize = noteHeaderSize + noteHeader.NameSize + noteHeader.DescSize;
            if (bytesRemaining < totalSize)
            {
                break;
            }

            yield new __ELFNote(this.__content, noteHeader);
            address = address.add(totalSize);
            bytesRemaining = bytesRemaining.subtract(totalSize);
        }
    }
}

class __ELFNoteContent
{
    constructor(programHeader)
    {
        this.__programHeader = programHeader;
    }

    get Notes()
    {
        return new __ELFNotes(this);
    }
};

class __ELFDynamicDebugDataEntry
{
    constructor(content, linkMapEntry)
    {
        this.__content = content;
        this.__linkMapEntry = linkMapEntry;
    }

    toString()
    {
        return this.Name + " == 0x" + this.Address.toString(16);
    }

    get Address()
    {
        return this.__linkMapEntry.LAddr;
    }

    get Name()
    {
        return host.memory.readString(this.__linkMapEntry.LName, this.__content.__programHeader.__module);
    }
}

class __ELFDynamicDebugDataEntries
{
    constructor(content, dynamicEntry)
    {
        this.__content = content;
        this.__dynamicEntry = dynamicEntry;
    }

    *[Symbol.iterator]()
    {
        var is64 = this.__content.__imageInfo.Is64Bit;
        var rdebug = __createSyntheticTypedObject(this.__dynamicEntry.Val, __keyModule,
                                                  (is64 ? "___ELF_RDEBUG64" : "___ELF_RDEBUG32"),
                                                  this.__content.__programHeader.__module);

        var linkMapEntryAddr = rdebug.AddrRMap;
        while (linkMapEntryAddr != 0)
        {
            var linkMapEntry = __createSyntheticTypedObject(linkMapEntryAddr, __keyModule,
                                                            (is64 ? "___ELF_LINKMAPENTRY64" : "___ELF_LINKMAPENTRY32"),
                                                            this.__content.__programHeader.__module);

            yield new __ELFDynamicDebugDataEntry(this.__content, linkMapEntry);

            linkMapEntryAddr = linkMapEntry.LNext;
        }
    }
};

class __ELFDynamicDebugData
{
    constructor(content, dynamicEntry)
    {
        this.__content = content;
        this.__dynamicEntry = dynamicEntry;
    }

    get Entries()
    {
        return new __ELFDynamicDebugDataEntries(this.__content, this.__dynamicEntry);
    }
};

class __ELFDynamicEntry
{
    constructor(content, dynamicEntry, address)
    {
        this.__content = content;
        this.__dynamicEntry = dynamicEntry;
        this.__address = address;
    }

    toString()
    {
        var str = this.Type + " == 0x" + this.Value.toString(16);
        return str;
    }

    get Type()
    {
        var typeName = __getEnumerant(this.__content.__imageInfo.DynamicTypes, this.__dynamicEntry.Tag);
        if (!typeName)
        {
            return "Unknown (" + this.__dynamicEntry.Tag.toString(16) + ")";
        }
        return typeName;
    }

    get Value()
    {
        return this.__dynamicEntry.Val;
    }

    get Content()
    {
        if (this.__dynamicEntry.Tag == this.__content.__imageInfo.DynamicTypes.DT_DEBUG)
        {
            return new __ELFDynamicDebugData(this.__content, this.__dynamicEntry);
        }
    }
};

class __ELFDynamicEntries
{
    constructor(content)
    {
        this.__content = content;
    }

    *[Symbol.iterator]()
    {
        var is64 = this.__content.__imageInfo.Is64Bit;
        var dynamicEntrySize = (is64 ? 16 : 8);

        var address = this.__content.__programHeader.Address;
        var bytesRemaining = this.__content.__programHeader.Header.MemSize;
        while (bytesRemaining > dynamicEntrySize)
        {
            var dynamicEntry = __createSyntheticTypedObject(address, __keyModule,
                                                            (is64 ? "___ELF_DYNAMICENTRY64" : "___ELF_DYNAMICENTRY32"),
                                                            this.__content.__programHeader.__module);

            yield new __ELFDynamicEntry(this.__content, dynamicEntry, address);
            address = address.add(dynamicEntrySize);
            bytesRemaining = bytesRemaining.subtract(dynamicEntrySize);
        }
    }
}

class __ELFDynamicContent
{
    constructor(programHeader, imageInfo)
    {
        this.__programHeader = programHeader;
        this.__imageInfo = imageInfo;
    }

    get Entries()
    {
        return new __ELFDynamicEntries(this);
    }
};

class __ELFProgramHeader
{
    constructor(module, imageInfo, programHeader)
    {
        this.__module = module;
        this.__imageInfo = imageInfo;
        this.__programHeader = programHeader;
    }

    toString()
    {
        var str = this.Type;

        var fileSz = this.__programHeader.FileSize;
        var memSz = this.__programHeader.MemSize;
        if (fileSz != 0 && memSz != 0)
        {
            var fileBegin = this.__programHeader.Offset;
            var fileEnd = fileBegin.add(fileSz);
            var memBegin = this.__programHeader.VAddr;
            var memEnd = memBegin.add(memSz);

            str += " { File : [" + fileBegin + ", " + fileEnd + ") Memory : [" + memBegin + ", " + memEnd + ") }";
        }

        return str;
    }

    get Type()
    {
        var typeName = __getEnumerant(this.__imageInfo.ProgramHeaderTypes, this.__programHeader.Type);
        if (!typeName)
        {
            return "Unknown (" + this.__programHeader.Type.toString(16) + ")";
        }
        return typeName;
    }

    get Address()
    {
        if (this.__programHeader.MemSize == 0)
        {
            return undefined;
        }

        return this.__module.BaseAddress.add(this.__programHeader.VAddr);
    }

    get Header()
    {
        return this.__programHeader;
    }

    get Content()
    {
        var phTypes = this.__imageInfo.ProgramHeaderTypes;
        if (this.__programHeader.Type == phTypes.PT_NOTE)
        {
            return new __ELFNoteContent(this, this.__imageInfo);
        }
        else if (this.__programHeader.Type == phTypes.PT_DYNAMIC)
        {
            return new __ELFDynamicContent(this, this.__imageInfo);
        }
    }
}

// __ELFProgramHeaders
//
// Enumerator for program headers in a mapped ELF
//
class __ELFProgramHeaders
{
    constructor(module, imageInfo)
    {
        this.__module = module;
        this.__imageInfo = imageInfo;
    }

    *[Symbol.iterator]()
    {
        var header = this.__imageInfo.ELFHeader;
        var is64 = this.__imageInfo.Is64Bit;

        var entryCount = header.PHNum;
        var entrySize = header.PHEntSize;
        var offset = header.OffsetPHdr;

        while (entryCount > 0)
        {
            var programHeader = __createSyntheticTypedObject(this.__module.BaseAddress.add(offset), __keyModule,
                                                             is64 ? "___ELF_PROGRAMHEADER64" : "___ELF_PROGRAMHEADER32",
                                                             this.__module);

            yield new __ELFProgramHeader(this.__module, this.__imageInfo, programHeader);

            entryCount--;
            offset = offset.add(entrySize);
        }
    }
}

//*************************************************
// Image Information and PE Parsing

function __identifyImageType(module)
{
    var magic = host.memory.readMemoryValues(module.BaseAddress, 4, 1, false, module);
    if (magic[0] == 0x4D && magic[1] == 0x5a)
    {
        return "PE";
    }
    else if (magic[0] == 0x7F && magic[1] == 0x45 && magic[2] == 0x4C && magic[3] == 0x46)
    {
        return "ELF";
    }

    return null;
}

function __padToDword(offset32)
{
    return (offset32 + 3) & ~3;
}

// __MultiDescriptor:
//
// A class which abstracts away a set of descriptors.
//
class __MultiDescriptor
{
    constructor(module, entry, typeName)
    {
        this.__module = module;
        this.__entry = entry;
        this.__typeName = typeName;
    }

    *[Symbol.iterator]()
    {
        if (this.__entry.VirtualAddress != 0)
        {
            var address = this.__module.BaseAddress.add(this.__entry.VirtualAddress);
            var descriptor = __createTypedObject(address, __keyModule, this.__typeName, this.__module);
            var descriptorSize = descriptor.targetSize;
            var count = this.__entry.Size / descriptorSize;
            for (var i = 0; i < count; ++i)
            {
                yield descriptor;
                address += descriptorSize;
                descriptor = __createTypedObject(address, __keyModule, this.__typeName, this.__module);
            }
        }
    }
}

// __PEImageInformation:
//
// A class which abstracts image information for a PE image.
//
class __PEImageInformation
{
    constructor(module)
    {
        this.__module = module;
    }
        
    get DirectoryNumbers()
    {
        return { 
            exportDirectory : 0,
            importDirectory : 1,
            resourceDirectory : 2,
            exceptionDirectory : 3,
            securityDirectory : 4,
            baseRelocationDirectory : 5,
            debugDirectory : 6,
            architectureDirectory : 7,
            globalPointerDirectory : 8,
            TLSDirectory : 9,
            loadConfigurationDirectory : 10,
            boundImportDirectory : 11,
            iatDirectory : 12,
            delayImportDirectory : 13,
            COMRuntimeDescriptorDirectory : 14
        };
    }

    get ImageHeader()
    {
        var header = __createTypedObject(this.__module.BaseAddress, __keyModule, "_IMAGE_DOS_HEADER", this.__module);
        if (header.e_magic != 0x5A4D)
        {
            throw new Error("Unrecognized image header for module: " + this.__module.Name);
        }
        return header;
    }
    
    get FileHeader()
    {
        var imageHeader = this.ImageHeader;
        var offset = imageHeader.e_lfanew;
        var fileOffset = this.__module.BaseAddress.add(offset);
        var moduleName = this.__module.__ComparisonName;
        var signaturePointer = host.createPointerObject(fileOffset, moduleName, "unsigned long *", this.__module);
        var signature = signaturePointer.dereference();
        if (signature != 0x00004550)
        {
            throw new Error("Unrecognized file header for module: " + this.__module.Name);
        }
        fileOffset = fileOffset.add(4); // +sizeof(ULONG) -- PE signature
        var header = __createTypedObject(fileOffset, __keyModule, "_IMAGE_FILE_HEADER", this.__module);
        return header;
    }
    
    get OptionalHeader()
    {
        var fileHeader = this.FileHeader;
        var fileHeaderLocation = fileHeader.targetLocation;
        var fileHeaderSize = fileHeader.targetSize;
        var optionalHeaderLocation = fileHeaderLocation.add(fileHeaderSize);
        var optionalHeader = __createTypedObject(optionalHeaderLocation, __keyModule, "_IMAGE_OPTIONAL_HEADER", this.__module, "_IMAGE_OPTIONAL_HEADER32");
        if (optionalHeader.Magic == 0x010B)
        {
            // PE32 (optionalHeader is correct)

        }
        else if (optionalHeader.Magic == 0x020B)
        {
            // PE32+ (optionalHeader is incorrect!)
            optionalHeader = __createTypedObject(optionalHeaderLocation, __keyModule, "_IMAGE_OPTIONAL_HEADER64", this.__module);
        }
        else
        {
            throw new Error("Unrecognized optional header for module: " + this.__module.Name);
        }
        return optionalHeader;
    }

    get IsPE32()
    {
        return this.OptionalHeader.Magic == 0x010B;
    }
    
    get Directories()
    {
        var optionalHeader = this.OptionalHeader;
        return optionalHeader.DataDirectory;
    }
    
    get ResourceDirectory()
    {
        var entry = this.Directories[this.DirectoryNumbers.resourceDirectory];
        if (entry.VirtualAddress == 0)
        {
            //
            // It does not exist!
            //
            return undefined;
        }
        var address = this.__module.BaseAddress.add(entry.VirtualAddress);
        return __createTypedObject(address, __keyModule, "_IMAGE_RESOURCE_DIRECTORY", this.__module);
    }

    get ImportDescriptor()
    {
        var entry = this.Directories[this.DirectoryNumbers.importDirectory];
        if (entry.VirtualAddress == 0)
        {
            //
            // It does not exist
            //
            return undefined;
        }
        var address = this.__module.BaseAddress.add(entry.VirtualAddress);
        return __createTypedObject(address, __keyModule, "_IMAGE_IMPORT_DESCRIPTOR", this.__module);
    }

    get DelayImportDescriptor()
    {
        var entry = this.Directories[this.DirectoryNumbers.delayImportDirectory];
        if (entry.VirtualAddress == 0)
        {
            //
            // It does not exist
            //
            return undefined;
        }
        var address = this.__module.BaseAddress.add(entry.VirtualAddress);
        return __createTypedObject(address, __keyModule, "_IMAGE_DELAYLOAD_DESCRIPTOR", this.__module);
    }

    get ExportDescriptor()
    {
        var entry = this.Directories[this.DirectoryNumbers.exportDirectory];
        if (entry.VirtualAddress == 0)
        {
            //
            // It does not exist
            //
            return undefined;
        }
        var address = this.__module.BaseAddress.add(entry.VirtualAddress);
        return __createTypedObject(address, __keyModule, "_IMAGE_EXPORT_DIRECTORY", this.__module);
    }

    get DebugDescriptors()
    {
        var entry = this.Directories[this.DirectoryNumbers.debugDirectory];
        if (entry.VirtualAddress == 0)
        {
            //
            // It does not exist
            //
            return undefined;
        }
        return new __MultiDescriptor(this.__module, entry, "_IMAGE_DEBUG_DIRECTORY");
    }

    get IATAddress()
    {
        var entry = this.Directories[this.DirectoryNumbers.iatDirectory];
        if (entry.VirtualAddress == 0)
        {
            //
            // It does not exist
            //
            return undefined;
        }
        var address = this.__module.BaseAddress.add(entry.VirtualAddress);
        return address;
    }
}

class __ImageDirectories
{
    constructor(descModule, imageInfo)
    {
        this.__descModule = descModule;
        this.__imageInfo = imageInfo;
    }

    get ExportDirectory()
    {
        return this.__imageInfo.ExportDescriptor;
    }

    get ImportDirectory()
    {
        return this.__imageInfo.ImportDescriptor;
    }

    get ResourceDirectory()
    {
        return this.__imageInfo.ResourceDirectory;
    }

    get DelayImportDirectory()
    {
        return this.__imageInfo.DelayImportDescriptor;
    }

    get DebugDirectories()
    {
        return this.__imageInfo.DebugDescriptors;
    }
}

// __ImageInformation:
//
// A class which abstracts image information for an image (PE or otherwise)
//
class __ImageInformation
{
    constructor(module)
    {
        this.__module = module;

        //
        // Take a look at the header.  Do we recognize the format.
        //
        var imageType = __identifyImageType(module);
        if (imageType == null)
        {
            throw new Error("Unrecognized image format");
        }

        this.ImageType = imageType;

        if (imageType == "PE")
        {
            this.Information = new __PEImageInformation(module);
        }
        else if (imageType == "ELF")
        {
            this.Information = new __ELFImageInformation(module);
        }
        else
        {
            throw new Error("Unsupported image format");
        }
    }
}

// __ImageHeaders:
//
// Class which abstracts the collection of PE image headers.
//
class __ImageHeaders
{
    constructor(descModule, imageInfo)
    {
        this.__descModule = descModule;
        this.__imageInfo = imageInfo;
    }

    get ImageHeader()
    {
        return this.__imageInfo.ImageHeader;
    }

    get FileHeader()
    {
        return this.__imageInfo.FileHeader;
    }

    get OptionalHeader()
    {
        return this.__imageInfo.OptionalHeader;
    }
}

// __findRelatedModule:
//
// From the context of a given source module, find a module in the same context.  This module can be found
// either via its name (less optimal) or via a bound address within the module.
//
function __findRelatedModule(srcModule, relatedName, boundAddress)
{
    //
    // Two ways this can work.  If we have a bound address within the related module, that is very easy
    // to find the module.  If we do not (or it is zero), we will resolve by name (which is a far poorer choice)
    //
    var modCtx = srcModule.hostContext;
    var modSession = host.namespace.Debugger.Sessions.getValueAt(modCtx);
    var modProcess = modSession.Processes.getValueAt(modCtx);
    var modMatches = undefined;

    if (boundAddress === undefined || boundAddress.compareTo(0) == 0)
    {
        var matchName = relatedName;
        modMatches = modProcess.Modules.Where(function (mod) {
            //
            // Make sure we don't inadvertantly get any path elements.  This really should just be the name.
            //
            var modName = mod.__ComparisonName;
            return modName.toLowerCase() == matchName.toLowerCase();
        });
    }
    else
    {
        modMatches = modProcess.Modules.Where(function (mod) {
            var low = boundAddress.compareTo(mod.BaseAddress);
            var high = boundAddress.compareTo(mod.BaseAddress.add(mod.Size));
            return (low >= 0 && high < 0);
        });
    }

    if (modMatches.Count() == 1)
    {
        return modMatches.First();
    }
    else
    {
        return undefined;
    }    
}

//*******************************************************************************
// Resource Table Implementation:
//

// __ResourceEntry:
//
// Represents a single entry in an image's resource table.
//
class __ResourceEntry
{
    constructor(resourceModule, resourceDirectory, directoryEntry, resourceEntry, priorPath, rootIdx)
    {
        this.__resourceModule = resourceModule;
        this.__resourceDirectory = resourceDirectory;
        this.__directoryEntry = directoryEntry;
        this.__resourceEntry = resourceEntry;
        this.__priorPath = priorPath;
        this.__rootIdx = rootIdx;

        if (this.__rootIdx === undefined)
        {
            this.__rootIdx = this.__Index;
        }
    }
    
    toString()
    {
        var baseDesc = "Resource '" + this.Path + "'";
        if (this.Type !== undefined)
        {
            baseDesc += " (" + this.Type + ")";
        }
        return baseDesc;
    }
    
    get Path()
    {
        var pathElement;
        
        if (this.__IsNamed)
        {
            pathElement = this.Name;
        }
        else
        {
            pathElement = this.Id.toString(10);
        }

        if (this.__priorPath == undefined)
        {
            return pathElement;
        }        
        else
        {
            return this.__priorPath + "/" + pathElement;
        }
    }

    get Type()
    {
        if (this.__rootIdx === undefined || typeof this.__rootIdx === 'string')
        {
            return undefined;
        }

        var typeName = __resourceTypes.getNameForType(this.__rootIdx);
        if (typeName == null)
        {
            return undefined;
        }

        return typeName;
    }
    
    get __IsNamed()
    {
        return this.__resourceEntry.NameIsString != 0;
    }
    
    get __HasChildren()
    {
        return this.__resourceEntry.DataIsDirectory != 0;
    }

    get __Index()
    {
        if (this.__IsNamed)
        {
            return this.Name;
        }
        else
        {
            return this.Id;
        }
    }
    
    get Name()
    {
        if (this.__IsNamed)
        {
            var nameLocation = this.__resourceDirectory.targetLocation.add(this.__resourceEntry.NameOffset);
            var nameData = __createTypedObject(nameLocation, __keyModule, "_IMAGE_RESOURCE_DIR_STRING_U", this.__resourceModule);
            var nameLocation = nameData.NameString.targetLocation;
            //
            // @TODO: We have no easy way to read a non-null terminated string from the
            //        target address space!
            //
            var nameString = host.memory.readWideString(nameLocation, this.__resourceModule);
            return nameString.slice(0, nameData.Length);
        }
        return undefined;
    }
    
    get Id()
    {
        if (this.__IsNamed)
        {
            return undefined;
        }
        else
        {
            return this.__resourceEntry.NameOffset;
        }
    }
    
    get Children()
    {
        if (this.__HasChildren)
        {
            var dataLocation = this.__resourceDirectory.targetLocation.add(this.__resourceEntry.OffsetToDirectory);
            var childDirectory = __createTypedObject(dataLocation, __keyModule, "_IMAGE_RESOURCE_DIRECTORY", this.__resourceModule);
            return new __ResourceTable(this.__resourceModule, this.__resourceDirectory, childDirectory, this.Path, this.__rootIdx);
        }
        else
        {
            return undefined;
        }
    }

    get DataAddress()
    {
        if (this.__HasChildren)
        {
            return undefined;
        }
        else
        {
            var dataEntryLocation = this.__resourceDirectory.targetLocation.add(this.__resourceEntry.OffsetToData);
            var dataEntry = __createTypedObject(dataEntryLocation, __keyModule, "_IMAGE_RESOURCE_DATA_ENTRY", this.__resourceModule);
            var dataAddress = this.__resourceModule.BaseAddress.add(dataEntry.OffsetToData);
            return dataAddress;
        }
    }
   
    get Data()
    {
        if (this.__HasChildren)
        {
            return undefined;
        }
        else
        {
            var dataEntryLocation = this.__resourceDirectory.targetLocation.add(this.__resourceEntry.OffsetToData);
            var dataEntry = __createTypedObject(dataEntryLocation, __keyModule, "_IMAGE_RESOURCE_DATA_ENTRY", this.__resourceModule);
            var dataAddress = this.__resourceModule.BaseAddress.add(dataEntry.OffsetToData);

            var dataBlob = host.memory.readMemoryValues(dataAddress, dataEntry.Size);
            return dataBlob;
        }
    }

    get DataValue()
    {
        if (this.__HasChildren || this.__rootIdx === undefined)
        {
            return undefined;
        }

        var idx = this.__rootIdx;
        if (typeof idx != 'string')
        {
            switch(idx)
            {
                case 16:        // RT_VERSION:
                {
                    return new __VersionResourceEntry(this.__resourceModule, 
                                                      this.DataAddress, 
                                                      this.Data, 
                                                      this.Data.length, 
                                                      0, 
                                                      "");
                }

                case 24:        // RT_MANIFEST:
                {
                    return new __StringResource(this.Data,
                                                this.Data.length,
                                                false);
                }
            }
        }
        else
        {
            if (idx == "XML" || idx == "XSD" || idx == "REGISTRY" || idx == "REGINST")
            {
                return new __StringResource(this.Data,
                                            this.Data.length,
                                            false);
            }
        }

        return undefined;
    }
}

// __ResourceTypeInfo:
//
// Describes the types of resources.
//
class __ResourceTypeInfo
{
    constructor()
    {
        this.NameIndex = [null,                 // 0
                          "RT_CURSOR",          // 1
                          "RT_BITMAP",          // 2
                          "RT_ICON",            // 3
                          "RT_MENU",            // 4
                          "RT_DIALOG",          // 5
                          "RT_STRING",          // 6
                          "RT_FONTDIR",         // 7
                          "RT_FONT",            // 8
                          "RT_ACCELERATOR",     // 9
                          "RT_RCDATA",          // 10
                          "RT_MESSAGETABLE",    // 11
                          "RT_GROUP_CURSOR",    // 12
                          null,                 // 13
                          "RT_GROUP_ICON",      // 14
                          null,                 // 15
                          "RT_VERSION",         // 16
                          "RT_DLGINCLUDE",      // 17
                          null,                 // 18
                          "RT_PLUGPLAY",        // 19
                          "RT_VXD",             // 20
                          "RT_ANICURSOR",       // 21
                          "RT_ANIICON",         // 22
                          "RT_HTML",            // 23
                          "RT_MANIFEST"];       // 24

        //
        // Forward index:
        //
        this.RT_CURSOR = 1;
        this.BT_BITMAP = 2;
        this.RT_ICON = 3;
        this.RT_MENU = 4;
        this.RT_DIALOG = 5;
        this.RT_STRING = 6;
        this.RT_FONTDIR = 7;
        this.RT_FONT = 8;
        this.RT_ACCELERATOR = 9;
        this.RT_RCDATA = 10;
        this.RT_MESSAGETABLE = 11;
        this.RT_GROUP_CURSOR = 12;
        this.RT_GROUP_ICON = 14;
        this.RT_VERSION = 16;
        this.RT_DLGINCLUDE = 17;
        this.RT_PLUGPLAY = 19;
        this.RT_VXD = 20;
        this.RT_ANICURSOR = 21;
        this.RT_ANIICON = 22;
        this.RT_HTML = 23;
        this.RT_MANIFEST = 24;
    }

    getNameForType(idx)
    {
        if (idx >= this.NameIndex.length)
        {
            return null;
        }
        return this.NameIndex[idx];
    }
};

var __resourceTypes = new __ResourceTypeInfo();

// __ResourceTable:
//
// An abstraction over a resource directory (or sub-directory) within an image.
//
class __ResourceTable
{
    constructor(resourceModule, resourceDirectory, directoryEntry, priorPath, rootIdx)
    {
        this.__resourceModule = resourceModule;
        this.__resourceDirectory = resourceDirectory;
        this.__directoryEntry = directoryEntry;
        this.__priorPath = priorPath;
        this.__rootIdx = rootIdx;
    }
    
    getDimensionality()
    {
        return 1;
    }
    
    getValueAt(idx)
    {
        var isString = (typeof idx === 'string');
        for (var idxEntry of this)
        {
            var entry = idxEntry.value;
            if (isString)
            {
                if (entry.Name == idx)
                {
                    return entry;
                }
            }
            else
            {
                if (entry.Id == idx)
                {
                    return entry;
                }
            }
        }
        
        throw new RangeError("Unable to find specified resource: " + idx);
    }
    
    *[Symbol.iterator]()
    {
        var entryStart = this.__directoryEntry.targetLocation.add(this.__directoryEntry.targetSize);
        var count = this.__directoryEntry.NumberOfNamedEntries + this.__directoryEntry.NumberOfIdEntries;
        var entryLoc = entryStart;

        for (var cur = 0; cur < count; ++cur)
        {
            var resourceInfo = __createTypedObject(entryLoc, __keyModule, "_IMAGE_RESOURCE_DIRECTORY_ENTRY", this.__resourceModule);
            var resourceEntry = new __ResourceEntry(this.__resourceModule, this.__resourceDirectory, this.__directoryEntry, resourceInfo, this.__priorPath, this.__rootIdx);
            
            if (resourceEntry.__IsNamed)
            {
                yield new host.indexedValue(resourceEntry, [resourceEntry.Name]);
            }
            else
            {
                yield new host.indexedValue(resourceEntry, [resourceEntry.Id]);
            }

            entryLoc = entryLoc.add(resourceInfo.targetSize);
        }
    }
}

class __StringResource
{
    constructor(blobData, blobSize, isUnicode)
    {
        this.__blobData = blobData;
        this.__blobSize = blobSize;
        this.__isUnicode = isUnicode;
        this.__size = (this.__isUnicode ? 2 : 1);
    }

    *[Symbol.iterator]()
    {
        var curIdx = 0;
        var remaining = this.__blobSize;
        var str = "";
        while(remaining > 0)
        {
            var cc = this.__next(curIdx);
            curIdx += this.__size;
            remaining -= this.__size;
            if (cc == 10) // '\n'
            {
                yield str;
                str = "";
            }
            else if (cc != 13) // '\r'
            {
                str += String.fromCharCode(cc);
            }
        }

        if (str.length != 0)
        {
            yield str;
        }
    }

    __next(idx)
    {
        if (this.__isUnicode)
        {
            return (this.__blobData[idx] | (this.__blobData[idx + 1] << 8));
        }
        else
        {
            return this.__blobData[idx];
        }
    }
}

// __VersionResourceChildren:
//
// Parses the children of a version resource.
//
class __VersionResourceChildren
{
    constructor(entry)
    {
        this.__entry = entry;
        this.__firstChildBufferOffset = __padToDword(this.__entry.__dataOffset + this.__entry.__dataSize);
        this.__firstChildEntryOffset = this.__firstChildBufferOffset - this.__entry.__entryOffset;
        this.__entrySize = this.__entry.__entrySize;
    }

    getDimensionality()
    {
        return 1;
    }
    
    getValueAt(idx)
    {
        var isString = (typeof idx === 'string');
        if (!isString)
        {
            throw new RangeError("Invalid index: " + idx.toString());
        }

        for (var idxEntry of this)
        {
            var entry = idxEntry.value;
            if (entry.Name == idx)
            {
                return entry;
            }
        }
        
        throw new RangeError("Unable to find specified entry: " + idx);
    }

    *[Symbol.iterator]()
    {
        var bufferOffset = this.__firstChildBufferOffset;
        var remainingSize = (this.__entrySize - this.__firstChildEntryOffset);
        while (remainingSize > 8)
        {
            var child = new __VersionResourceEntry(this.__entry.__rscModule, this.__entry.__bufferAddress, 
                                                   this.__entry.__blobData, this.__entry.__blobSize, 
                                                   bufferOffset, this.__entry.Path);
            yield new host.indexedValue(child, [child.Name]);
            var childSize = child.__entrySize;
            if (childSize > remainingSize)
            {
                break;
            }
            bufferOffset += childSize;
            var alignedBufferOffset = __padToDword(bufferOffset);
            var alignment = alignedBufferOffset - bufferOffset;
            bufferOffset = alignedBufferOffset;
            remainingSize -= childSize + alignment;
        }
    }
}

// VersionResourceEntry:
//
// A single entry in the version tree of the resource table.
//
class __VersionResourceEntry
{
    //
    // A version resource is defined by:
    //
    //     WORD <total length>
    //     WORD <value length>
    //     WORD <type>
    //     STRING <key>
    //     DWORD padded to VALUE
    //     <children (each a version resource bock)>
    //
    constructor(rscModule, bufferAddress, blobData, blobSize, entryOffset, priorPath)
    {
        this.__rscModule = rscModule;
        this.__bufferAddress = bufferAddress;
        this.__blobData = blobData;
        this.__blobSize = blobSize;
        this.__entryOffset = entryOffset;
        this.__priorPath = priorPath;
    }

    toString()
    {
        return this.Path;
    }

    get Offset()
    {
        return this.__entryOffset;
    }

    get Name()
    {
        return this.__readString(this.__entryOffset + 6);
    }

    get VersionInfo()
    {
        if (this.Path == "VS_VERSION_INFO" && this.__isBinary)
        {
            var versionInfo = __createTypedObject(this.__bufferAddress.add(this.__dataOffset), __keyModule, "VS_FIXEDFILEINFO", this.__rscModule);
            var isSynthetic = (versionInfo.targetType === undefined);
            if (isSynthetic)
            {
                //
                // This is on symbols which don't have VS_FIXEDFILEINFO.  The visualizer will not work.  Attach it manually.
                // Yes -- it's not a visualizer -- but at least the fields will be there.
                //
                Object.setPrototypeOf(versionInfo, __FixedVersionInfo.prototype);
            }
            return versionInfo;
        }
        else
        {
            return undefined;
        }
    }

    get Path()
    {
        if (this.__priorPath.length == 0)
        {
            return this.Name;
        }
        else
        {
            return this.__priorPath + "\\" + this.Name;
        }
    }

    get EntryKind()
    {
        if (this.__isBinary)
        {
            return "Binary";
        }
        else
        {
            return "Text";
        }
    }

    get DataAddress()
    {
        return this.__bufferAddress.add(this.__dataOffset);
    }

    get Data()
    {
        if (this.__isBinary && this.__dataSize != 0)
        {
            return host.memory.readMemoryValues(this.DataAddress, this.__dataSize, 1, false, this.__rscModule);
        }
        else
        {
            return undefined;
        }
    }

    get Text()
    {
        if (this.__isBinary || this.__dataSize == 0)
        {
            return undefined;
        }
        else
        {
            return this.__readString(this.__dataOffset);
        }
    }

    get Children()
    {
        return new __VersionResourceChildren(this);
    }

    //*************************************************
    // Internal Utility:
    //

    get __entrySize()
    {
        return this.__readWord(this.__entryOffset);
    }

    get __dataSize()
    {
        var size = this.__readWord(this.__entryOffset + 2);
        if (!this.__isBinary)
        {
            // It's unicode characters.  Convert to bytes.
            size *= 2;
        }
        return size;
    }

    get __dataOffset()
    {
        var nameLen = (this.Name.length + 1) * 2;   // UTF-16
        var dataOffset = __padToDword(this.__entryOffset + 6 + nameLen);
        return dataOffset;
    }

    get __isBinary()
    {
        return this.__readWord(this.__entryOffset + 4) == 0;
    }

    __readByte(offset)
    {
        return this.__blobData[offset];
    }

    __readWord(offset)
    {
        return this.__blobData[offset] | (this.__blobData[offset + 1] << 8);
    }

    __readDWord(offset)
    {
        return this.__blobData[offset] | (this.__blobData[offset + 1] << 8) | (this.__blobData[offset + 2] << 16) |
               (this.__blobData[offset + 3] << 24);
    }

    __readString(offset)
    {
        var str = "";
        var cur = offset;

        while(true)
        {
            var code = this.__readWord(cur);
            if (code == 0)
            {
                break;
            }
            str += String.fromCharCode(code);
            cur += 2;
        }

        return str;
    }
}

//*******************************************************************************
// Import Table Implementation
//

// __NamedImport:
//
// Abstraction over a named import in the IAT.
//
class __NamedImport
{
    constructor(importModule, thunkEntry, boundThunkEntry)
    {
        this.__importModule = importModule;
        this.__thunkEntry = thunkEntry;
        this.__boundThunkEntry = boundThunkEntry;
    }

    toString()
    {
        return "Named Import of '" + this.ImportName +"'";
    }

    get ImportName()
    {
        // +2 == ignore the thunk hint
        var address = this.__importModule.BaseAddress.add(this.__thunkEntry).add(2);
        return host.memory.readString(address, this.__importModule);
    }

    get BoundFunctionAddress()
    {
        if (this.__boundThunkEntry === undefined)
        {
            return undefined;
        }
        return this.__boundThunkEntry;
    }

    get BoundFunction()
    {
        if (this.__boundThunkEntry === undefined)
        {
            return undefined;
        }
        return host.getModuleSymbol(this.__boundThunkEntry, this.__importModule);
    }    
}

// __OrdinalImport:
//
// Abstraction over an ordinal import in the IAT.
//
class __OrdinalImport
{
    constructor(importModule, thunkEntry, boundThunkEntry)
    {
        this.__importModule = importModule;
        this.__thunkEntry = thunkEntry;
        this.__boundThunkEntry = boundThunkEntry;
    }

    toString()
    {
        return "Ordinal Import of #" + this.OrdinalNumber; 
    }

    get OrdinalNumber()
    {
        var mask = new host.Int64(0xFFFFFFFF, 0x7FFFFFFF);
        return this.__thunkEntry.bitwiseAnd(mask);
    }

    get BoundFunctionAddress()
    {
        if (this.__boundThunkEntry === undefined)
        {
            return undefined;
        }
        return this.__boundThunkEntry;
    }

    get BoundFunction()
    {
        if (this.__boundThunkEntry === undefined)
        {
            return undefined;
        }
        return host.getModuleSymbol(this.__boundThunkEntry, this.__importModule);
    }
}

// __BoundImport:
//
// Abstraction over a bound import.
//
class __BoundImport
{
    constructor(importModule, thunkEntry)
    {
        this.__importModule = importModule;
        this.__thunkEntry = thunkEntry;
    }

    toString()
    {
        return "Bound Import of Function At " + this.__thunkEntry.toString(16);
    }

    get BoundFunctionAddress()
    {
        return this.__thunkEntry;
    }

    get BoundFunction()
    {
        return host.getModuleSymbol(this.BoundFunctionAddress, this.__importModule);
    }
}

// __ImportFunctions:
//
// An abstraction over an unbound thunk list for the IAT.
//
class __ImportFunctions
{
    constructor(importModule, thunkTable, boundThunkTable)
    {
        this.__importModule = importModule;
        this.__thunkTable = thunkTable;
        this.__boundThunkTable = boundThunkTable;
    }

    *[Symbol.iterator]()
    {
        if (this.__thunkTable === undefined)
        {
            return;
        }

        var size = (this.__importModule.__IsPE32) ? 4 : 8;
        var thunkTable = this.__thunkTable;
        var boundThunkTable = this.__boundThunkTable;
        while(true)
        {
            var thunkEntry = host.memory.readMemoryValues(thunkTable, 1, size, false, this.__importModule);
            if (thunkEntry[0].compareTo(0) == 0)
            {
                break;
            }

            var boundThunkEntry = undefined;
            if (!(boundThunkTable === undefined))
            {
                //
                // This is unreadable on a "-z"
                //
                try
                {
                    boundThunkEntry = host.memory.readMemoryValues(boundThunkTable, 1, size, false, this.__importModule)[0];
                }
                catch(exc)
                {
                    boundThunkEntry = undefined;
                }
            }

            //
            // Ensure the thunk isn't bound.
            //
            var lowMask = (size == 4) ? 0x7FFFFFFF : new host.Int64(0xFFFFFFFF, 0x7FFFFFFF);
            var isNamed = (size == 4) ? ((thunkEntry[0] & 0x80000000) == 0) : ((thunkEntry[0].getHighPart() & 0x80000000) == 0);
            var noHighThunk = thunkEntry[0].bitwiseAnd(lowMask);
            var moduleEnd = this.__importModule.BaseAddress.add(this.__importModule.Size).subtract(1);
            var rva = this.__importModule.BaseAddress.add(noHighThunk);
            
            if (rva.compareTo(moduleEnd) > 0)
            {
                yield new __BoundImport(this.__importModule, thunkEntry[0]);
            }
            else if (isNamed)
            {
                yield new __NamedImport(this.__importModule, thunkEntry[0], boundThunkEntry)
            }
            else
            {
                yield new __OrdinalImport(this.__importModule, thunkEntry[0], boundThunkEntry);
            }

            thunkTable = thunkTable.add(size);
            if (!(boundThunkTable === undefined))
            {
                boundThunkTable = boundThunkTable.add(size);
            }
        }
    }
}

// __ImportEntry:
//
// An abstraction over a particular DLL import entry within the IAT.
//
class __ImportEntry
{
    constructor(importModule, entry)
    {
        this.__importModule = importModule;
        this.__entry = entry;
    }

    toString()
    {
        return this.ModuleName;
    }

    get __FirstBoundThunk()
    {
        var size = this.__importModule.__IsPE32 ? 4 : 8;
        var thunkAddr = this.__importModule.BaseAddress.add(this.__entry.FirstThunk);
        var originalThunkAddr = this.__importModule.BaseAddress.add(this.__entry.OriginalFirstThunk);
        var thunk = undefined;
        try
        {
            thunk = host.memory.readMemoryValues(thunkAddr, 1, size, false, this.__importModule);
        }
        catch(exc)
        {
            return undefined;
        }
        var originalThunk = host.memory.readMemoryValues(originalThunkAddr, 1, size, false, this.__importModule);
        if (thunk[0].compareTo(originalThunk[0]) == 0)
        {
            //
            // If the values are identical, the IAT isn't bound.  Don't return anything.
            //
            return undefined;
        }
        return thunk[0];
    }

    get ModuleName()
    {
        var address = this.__importModule.BaseAddress.add(this.__entry.Name);
        return host.memory.readString(address, this.__importModule);
    }

    get ResolvedModule()
    {
        return __findRelatedModule(this.__importModule, this.ModuleName, this.__FirstBoundThunk);
    }

    get Functions()
    {
        var boundThunkTable = this.__importModule.BaseAddress.add(this.__entry.FirstThunk);
        var thunkTable = this.__importModule.BaseAddress.add(this.__entry.OriginalFirstThunk);
        return new __ImportFunctions(this.__importModule, thunkTable, boundThunkTable);
    }
}

// __ImportsTable:
//
// An abstraction over the IAT within an image.
//
class __ImportsTable
{
    constructor(importModule, startingEntry)
    {
        this.__importModule = importModule;
        this.__startingEntry = startingEntry;
    }

    getDimensionality()
    {
        return 1;
    }
    
    getValueAt(idx)
    {
        for (var idxEntry of this)
        {
            var entry = idxEntry.value;
            if (entry.ModuleName == idx)
            {
                return entry;
            }
        }
        
        throw new RangeError("Unable to find specified import: " + idx);
    }
    
    *[Symbol.iterator]()
    {
        if (this.__startingEntry === undefined)
        {
            return;
        }

        var currentEntry = this.__startingEntry;
        var size = currentEntry.targetSize;
        while (currentEntry.Name != 0)
        {
            var entry = new __ImportEntry(this.__importModule, currentEntry);
            yield new host.indexedValue(entry, [entry.ModuleName]);
            currentEntry = __createTypedObject(currentEntry.targetLocation.add(size), __keyModule, "_IMAGE_IMPORT_DESCRIPTOR", this.__importModule);
        }
    }
}

//*******************************************************************************
// DelayLoad Import Table Implementation
//

// __DelayImportEntry:
//
// An abstraction over a particular DLL delay import entry within the delay import table.
//
class __DelayImportEntry
{
    constructor(importModule, entry)
    {
        this.__importModule = importModule;
        this.__entry = entry;
    }

    toString()
    {
        return this.ModuleName;
    }

    get ModuleName()
    {
        var address = this.__importModule.BaseAddress.add(this.__entry.DllNameRVA);
        return host.memory.readString(address, this.__importModule);
    }

    get IsLoaded()
    {
        return (!(this.ResolvedModule === undefined));
    }

    get ResolvedModule()
    {
        return __findRelatedModule(this.__importModule, this.ModuleName);
    }

    get Functions()
    {
        var thunkTable = this.__importModule.BaseAddress.add(this.__entry.ImportAddressTableRVA);
        return new __ImportFunctions(this.__importModule, thunkTable);
    }
}

// __DelayImportsTable
//
// An abstraction over the delay load import table within an image.
//
class __DelayImportsTable
{
    constructor(importModule, startingEntry)
    {
        this.__importModule = importModule;
        this.__startingEntry = startingEntry;
    }

    getDimensionality()
    {
        return 1;
    }
    
    getValueAt(idx)
    {
        for (var idxEntry of this)
        {
            var entry = idxEntry.value;
            if (entry.ModuleName == idx)
            {
                return entry;
            }
        }
        
        throw new RangeError("Unable to find specified delay import: " + idx);
    }
    
    *[Symbol.iterator]()
    {
        if (this.__startingEntry === undefined)
        {
            return;
        }

        var currentEntry = this.__startingEntry;
        var size = currentEntry.targetSize;
        while (currentEntry.DllNameRVA != 0)
        {
            var entry = new __DelayImportEntry(this.__importModule, currentEntry);
            yield new host.indexedValue(entry, [entry.ModuleName]);
            currentEntry = __createTypedObject(currentEntry.targetLocation.add(size), __keyModule, "_IMAGE_DELAYLOAD_DESCRIPTOR", this.__importModule);
        }
    }
}

//*******************************************************************************
// Export Table Implementation
//

// __OrdinalExport
//
// An abstraction over an ordinal function export within the EAT of an image.
//
class __OrdinalExport
{
    constructor(exportModule, ordinal, codeAddr)
    {
        this.__exportModule = exportModule;
        this.__ordinal = ordinal;
        this.__codeAddr = codeAddr;
    }

    toString()
    {
        return "Function export by ordinal #" + this.OrdinalNumber;
    }

    get OrdinalNumber()
    {
        return this.__ordinal;
    }

    get CodeAddress()
    {
        return this.__codeAddr;
    }

    get Function()
    {
        return host.getModuleSymbol(this.CodeAddress, this.__exportModule);
    }
}

// __NamedExport
//
// An abstraction over a named function export within the EAT of an image.
//
class __NamedExport extends __OrdinalExport
{
    constructor(exportModule, name, ordinal, codeAddr)
    {
        super(exportModule, ordinal, codeAddr);
        this.__name = name;
    }

    toString()
    {
        return "Function export of '" + this.Name + "'";
    }

    get Name()
    {
        return this.__name;
    }
}

// __ExportsTable
//
// An abstraction over the EAT within an image.
//
class __ExportsTable
{
    constructor(exportModule, exportDescriptor)
    {
        this.__exportModule = exportModule;
        this.__exportDescriptor = exportDescriptor;
    }

    *[Symbol.iterator]()
    {
        if (this.__exportDescriptor === undefined)
        {
            return;
        }

        var modBase = this.__exportModule.BaseAddress;

        //
        // nameOrdinals will contains the ordinal index of every named export in the module.
        //
        var nameOrdAddr = modBase.add(this.__exportDescriptor.AddressOfNameOrdinals);
        var nameCount = this.__exportDescriptor.NumberOfNames;
        var nameOrdinals = host.memory.readMemoryValues(nameOrdAddr, nameCount, 2, false, this.__exportModule);

        //
        // funcAddrs will contains the RVA of every export function
        //
        var funcAddrAddr = modBase.add(this.__exportDescriptor.AddressOfFunctions);
        var funcCount = this.__exportDescriptor.NumberOfFunctions;
        var funcAddrs = host.memory.readMemoryValues(funcAddrAddr, funcCount, 4, false, this.__exportModule);

        //
        // Completely invert the ordering: nameOrdinals[i] will give you the ordinal number for the i-th named
        // export function.  We want ordinalNames[o] which will give you the index into the name tables of
        // ordinal o (or zero).
        //
        var ordinalNames = new Array(funcCount);
        for (var i = 0; i < funcCount; ++i)
        {
            ordinalNames[i] = 0xFFFF;
        }
        for (i = 0; i < nameCount; ++i)
        {
            var ordNum = nameOrdinals[i];
            ordinalNames[ordNum] = i;
        }

        //
        // nameStrings will contain the RVA of every string.
        //
        var nameStringsAddr = modBase.add(this.__exportDescriptor.AddressOfNames);
        var nameStrings = host.memory.readMemoryValues(nameStringsAddr, nameCount, 4, false, this.__exportModule);

        var ordinalAddr = modBase.add(this.__exportDescriptor.AddressOfNameOrdinals);
        for (var i = 0; i < funcCount; ++i)
        {
            var codeAddr = modBase.add(funcAddrs[i]);
            var nameIdx = ordinalNames[i];

            //
            // Is this a named or by-ordinal export...?
            //
            if (nameIdx != 0xFFFF)
            {
                var nameAddr = modBase.add(nameStrings[nameIdx]);
                var name = host.memory.readString(nameAddr, this.__exportModule);

                yield new __NamedExport(this.__exportModule, name, i, codeAddr);
            }
            else
            {
                yield new __OrdinalExport(this.__exportModule, i, codeAddr);
            }
        }
    }
}

//*******************************************************************************
// Debug Information:
//

// __RSDSCodeViewData:
//
// Abstraction over an RSDS CodeView entry in the debug directory.
//
class __RSDSCodeViewData
{
    constructor(debugModule, rsdsAddress)
    {
        this.__debugModule = debugModule;
        this.__rsdsAddress = rsdsAddress;
    }

    get Guid()
    {
        return __createTypedObject(this.__rsdsAddress.add(4), __keyModule, "GUID", this.__debugModule);
    }

    get Age()
    {
        return host.memory.readMemoryValues(this.__rsdsAddress.add(20), 1, 4, false, this.__debugModule)[0];
    }
}

//
// __DebugCodeView:
//
// Abstraction over a CodeView debug directory.
//
class __DebugCodeView
{
    constructor(debugModule, debugEntry)
    {
        this.__debugModule = debugModule;
        this.__debugEntry = debugEntry;
        this.__dataAddress = this.__debugModule.BaseAddress.add(this.__debugEntry.AddressOfRawData);
    }

    get Signature()
    {
        var sig = host.memory.readMemoryValues(this.__dataAddress, 1, 4, false, this.__debugModule)[0];
        var sigStr = String.fromCharCode(sig & 0xFF) +
                     String.fromCharCode((sig >> 8) & 0xFF) +
                     String.fromCharCode((sig >> 16) & 0xFF) +
                     String.fromCharCode((sig >> 24) & 0xFF);
        return sigStr;
    }

    get Data()
    {
        var sig = this.Signature;
        if (sig == "RSDS")
        {
            return new __RSDSCodeViewData(this.__debugModule, this.__dataAddress);
        }

        return undefined;
    }
}

//
// __DebugTable:
//
// An interpretive abstraction over the information in the debug directories.
//
class __DebugTable
{
    constructor(debugModule, debugDirectories)
    {
        this.__debugModule = debugModule;
        this.__debugDirectories = debugDirectories;
        this.__debugTypeNumbers =
        {
            unknown : 0,
            coff : 1,
            codeView : 2,
            fpo : 3,
            misc : 4,
            exception : 5,
            fixup : 6,
            omapToSrc : 7,
            omapFromSrc : 8,
            borland : 9,
            reserved10 : 10,
            clsId : 11
        };
    }

    __findEntry(debugType)
    {
        for (var directory of this.__debugDirectories)
        {
            if (directory.Type == debugType)
            {
                return directory;
            }
        }

        return undefined;
    }

    get CodeView()
    {
        var dbgEntry = this.__findEntry(this.__debugTypeNumbers.codeView);
        if (dbgEntry === undefined)
        {
            return undefined;
        }

        return new __DebugCodeView(this.__debugModule, dbgEntry);
    }
}


//*******************************************************************************
// Core Extension
//

//
// _ModuleInformation:
//
// This is our sub-namespace addition to "Module.Contents"
//
class __ModuleInformation
{
    //*************************************************
    // PE Defined Content:
    //

    get Headers()
    {
        var imageInfo = new __ImageInformation(this);
        if (imageInfo.ImageType != "PE")
        {
            return undefined;
        }

        var headers = new __ImageHeaders(this, imageInfo.Information);
        return headers;
    }

    get Directories()
    {
        var imageInfo = new __ImageInformation(this);
        if (imageInfo.ImageType != "PE")
        {
            return undefined;
        }

        var dirs = new __ImageDirectories(this, imageInfo.Information);
        return dirs;
    }
    
    get Resources()
    {
        var imageInfo = new __ImageInformation(this);
        if (imageInfo.ImageType != "PE")
        {
            return undefined;
        }

        var resources = new __ResourceTable(this, imageInfo.Information.ResourceDirectory, imageInfo.Information.ResourceDirectory);
        return resources;
    }

    get Imports()
    {
        var imageInfo = new __ImageInformation(this);
        if (imageInfo.ImageType != "PE")
        {
            return undefined;
        }

        var importTable = new __ImportsTable(this, imageInfo.Information.ImportDescriptor);
        return importTable;
    }

    get DelayImports()
    {
        var imageInfo = new __ImageInformation(this);
        if (imageInfo.ImageType != "PE")
        {
            return undefined;
        }

        var delayImportTable = new __DelayImportsTable(this, imageInfo.Information.DelayImportDescriptor);
        return delayImportTable;
    }

    get Exports()
    {
        var imageInfo = new __ImageInformation(this);
        if (imageInfo.ImageType != "PE")
        {
            return undefined;
        }

        var exportTable = new __ExportsTable(this, imageInfo.Information.ExportDescriptor);
        return exportTable;
    }

    get DebugInfo()
    {
        var imageInfo = new __ImageInformation(this);
        if (imageInfo.ImageType != "PE")
        {
            return undefined;
        }

        var debugTable = new __DebugTable(this, imageInfo.Information.DebugDescriptors);
        return debugTable;
    }

    get Version()
    {
        var imageInfo = new __ImageInformation(this);
        if (imageInfo.ImageType != "PE")
        {
            return undefined;
        }

        var rscTable = this.Contents.Resources;
        if (rscTable === undefined)
        {
            return undefined;
        }
        var versionResource = rscTable.getValueAt(16);  // RT_VERSION
        if (versionResource === undefined)
        {
            return undefined;
        }
        versionResource = versionResource.Children.getValueAt(1);
        if (versionResource === undefined)
        {
            return undefined;
        }
        var byLanguage = versionResource.Children;
        versionResource = undefined;
        for (var first of byLanguage)
        {
            versionResource = first.value;              // Internally an indexedValue
            break;
        }
        if (versionResource === undefined)
        {
            return undefined;
        }
        return new __VersionResourceEntry(this, versionResource.DataAddress, versionResource.Data, versionResource.Data.length, 0, "");
    }

    //*************************************************
    // ELF Defined Content:
    //

    get BuildID()
    {
        var imageInfo = new __ImageInformation(this);
        if (imageInfo.ImageType != "ELF")
        {
            return undefined;
        }

        var phTypes = imageInfo.Information.ProgramHeaderTypes;
        var ntTypes = imageInfo.Information.NoteTypes;

        //
        // Find a PT_NOTE section with a "GNU" note whose type is NT_GNU_BUILD_ID
        //
        for (var header of imageInfo.Information.ProgramHeaders)
        {
            if (header.__programHeader.Type == phTypes.PT_NOTE)
            {
                for (var note of header.Content.Notes)
                {
                    if (note.Name == "GNU" && note.__noteHeader.Type == ntTypes.NT_GNU_BUILD_ID)
                    {
                        return note.Content.BuildID;
                    }
                }
            }
        }

        return undefined;
    }

    get BuildInfo()
    {
        var imageInfo = new __ImageInformation(this);
        if (imageInfo.ImageType != "ELF")
        {
            return undefined;
        }

        var phTypes = imageInfo.Information.ProgramHeaderTypes;
        var ntTypes = imageInfo.Information.NoteTypes;

        //
        // Find a PT_NOTE section with a "FDO" note whose type is NT_FDO_BUILDINFO
        //
        for (var header of imageInfo.Information.ProgramHeaders)
        {
            if (header.__programHeader.Type == phTypes.PT_NOTE)
            {
                for (var note of header.Content.Notes)
                {
                    if (note.Name == "FDO" && note.__noteHeader.Type == ntTypes.NT_FDO_BUILDINFO)
                    {
                        return note.Content.BuildInfo;
                    }
                }
            }
        }

        return undefined;
    }

    get ELFHeader()
    {
        var imageInfo = new __ImageInformation(this);
        if (imageInfo.ImageType != "ELF")
        {
            return undefined;
        }

        return imageInfo.Information.ELFHeader;
    }

    get ProgramHeaders()
    {
        var imageInfo = new __ImageInformation(this);
        if (imageInfo.ImageType != "ELF")
        {
            return undefined;
        }

        return imageInfo.Information.ProgramHeaders;
    }
}

// __VersionFileFlags:
//
// Interprets the flags of a VS_FIXEDFILEINFO
//
class __VersionFileFlags
{
    constructor(versionInfo)
    {
        this.__versionInfo = versionInfo;
        this.__flags = (versionInfo.dwFileFlags & versionInfo.dwFileFlagsMask);
    }

    get IsDebug()
    {
        return (this.__flags & 0x00000001) != 0;
    }

    get IsInfoInferred()
    {
        return (this.__flags & 0x00000010) != 0;
    }

    get IsPatched()
    {
        return (this.__flags & 0x00000004) != 0;
    }

    get IsPreRelease()
    {
        return (this.__flags & 0x00000002) != 0;
    }

    get IsPrivateBuild()
    {
        return (this.__flags & 0x00000008) != 0;
    }

    get IsSpecialBuild()
    {
        return (this.__flags & 0x00000020) != 0;
    }
}

// __FixedVersionInfo:
//
// Visualizer/extension around VS_FIXEDFILEINFO
//
class __FixedVersionInfo
{
    get FileVersion()
    {
        return (this.dwFileVersionMS >> 16).toString() + "." +
               (this.dwFileVersionMS & 0x0000FFFF).toString() + "." +
               (this.dwFileVersionLS >> 16).toString() + "." +
               (this.dwFileVersionLS & 0x0000FFFF);
    }

    get ProductVersion()
    {
        return (this.dwFileVersionMS >> 16).toString() + "." +
               (this.dwFileVersionMS & 0x0000FFFF).toString() + "." +
               (this.dwFileVersionLS >> 16).toString() + "." +
               (this.dwFileVersionLS & 0x0000FFFF);
    }

    get FileFlags()
    {
        return new __VersionFileFlags(this);
    }

    __osString(val)
    {
        switch(val)
        {
            case 0x00010000:
                return "MS-DOS";
            
            case 0x00040000:
            case 0x00040004:
                return "Windows NT";

            case 0x00000001:
                return "16-bit Windows";

            case 0x00000004:
                return "32-bit Windows";

            case 0x00020000:
                return "16-bit OS/2";

            case 0x00030000:
                return "32-bit OS/2";

            case 0x00000002:
                return "16-bit Presentation Manager";

            case 0x00000003:
                return "32-bit Presentation Manager";

            default:
                return "Unknown";
        }
    }

    get OS()
    {
        var osFlags = this.dwFileOS;
        var desc = this.__osString(osFlags);
        if (desc != "Unknown")
        {
            return desc;
        }

        var topNib = (osFlags & 0xFFFF0000);
        var botNib = (osFlags & 0x0000FFFF);
        if (topNib == 0 || botNib == 0)
        {
            return desc;
        }
        else
        {
            return this.__osString(botNib) + " on " + this.__osString(topNib);
        }
    }

    get FileType()
    {
        switch(this.dwFileType)
        {
            case 1:
                return "Application";
            
            case 2:
                return "DLL";
            
            case 3:
                return "Device Driver";
            
            case 4:
                return "Font";
            
            case 7:
                return "Static Link Library";
            
            case 5:
                return "Virtual Device (VXD)";

            default:
                return "Unknown";
        }
    }

    get FileSubType()
    {
        if (this.dwFileType == 3)
        {
            switch(this.dwFileSubtype)
            {
                case 0:
                    return "Communications Driver";
                
                case 4:
                    return "Display Driver";
                
                case 8:
                    return "Installable Driver";
                
                case 2:
                    return "Keyboard Driver";

                case 3:
                    return "Language Driver";
                
                case 5:
                    return "Mouse Driver";
                
                case 6:
                    return "Network Driver";
                
                case 1:
                    return "Printer Driver";
                
                case 9:
                    return "Sound Driver";
                
                case 7:
                    return "System Driver";
                
                case 12:
                    return "Versioned Printer Driver"

                default:
                    return "Unknown Driver";
            }
        }
        else if (this.dwFileType == 4)
        {
            switch(this.dwFileSubtype)
            {
                case 1:
                    return "Raster Font";
                
                case 3:
                    return "TrueType Font";
                
                case 2:
                    return "Vector Font";
                
                default:
                    return "Unknown Font";
            }
        }

        return undefined;
    }
}

//*************************************************
// Private Items:
//
// These properties are only accessible within the bounds of this script.
// They do not marshal into any other context.
//

class __ModulePublic
{
    get ImageType()
    {
        var info = new __ImageInformation(this);
        return info.ImageType;
    }

}

class __ModulePrivate
{
    get __IsPE32()
    {
        var info = new __ImageInformation(this);
        return (info.ImageType == "PE" && info.Information.IsPE32);
    }

    get __ComparisonName()
    {
        var name = this.Name;
        var compName = name.substring(name.lastIndexOf("\\") + 1);
        return compName;
    }
}

function initializeScript()
{
    return [new host.namespacePropertyParent(__ModuleInformation, "Debugger.Models.Module", "Debugger.Models.Module.Contents", "Contents"),
            new host.namedModelParent(__ModulePrivate, "Debugger.Models.Module"),
            new host.namedModelParent(__ModulePublic, "Debugger.Models.Module"),
            new host.optionalRecord(new host.typeSignatureRegistration(__FixedVersionInfo, "VS_FIXEDFILEINFO"))];
     
}

// SIG // Begin signature block
// SIG // MIIl6AYJKoZIhvcNAQcCoIIl2TCCJdUCAQExDzANBglg
// SIG // hkgBZQMEAgEFADB3BgorBgEEAYI3AgEEoGkwZzAyBgor
// SIG // BgEEAYI3AgEeMCQCAQEEEBDgyQbOONQRoqMAEEvTUJAC
// SIG // AQACAQACAQACAQACAQAwMTANBglghkgBZQMEAgEFAAQg
// SIG // 63dns8I+YREOMSS0dr9sr8s65n66KzlQxz4rXnW2iUug
// SIG // ggtnMIIE7zCCA9egAwIBAgITMwAABI8LuXzfev9KVwAA
// SIG // AAAEjzANBgkqhkiG9w0BAQsFADB+MQswCQYDVQQGEwJV
// SIG // UzETMBEGA1UECBMKV2FzaGluZ3RvbjEQMA4GA1UEBxMH
// SIG // UmVkbW9uZDEeMBwGA1UEChMVTWljcm9zb2Z0IENvcnBv
// SIG // cmF0aW9uMSgwJgYDVQQDEx9NaWNyb3NvZnQgQ29kZSBT
// SIG // aWduaW5nIFBDQSAyMDEwMB4XDTIyMDUxMjIwNDcwNFoX
// SIG // DTIzMDUxMTIwNDcwNFowdDELMAkGA1UEBhMCVVMxEzAR
// SIG // BgNVBAgTCldhc2hpbmd0b24xEDAOBgNVBAcTB1JlZG1v
// SIG // bmQxHjAcBgNVBAoTFU1pY3Jvc29mdCBDb3Jwb3JhdGlv
// SIG // bjEeMBwGA1UEAxMVTWljcm9zb2Z0IENvcnBvcmF0aW9u
// SIG // MIIBIjANBgkqhkiG9w0BAQEFAAOCAQ8AMIIBCgKCAQEA
// SIG // rFg6I4sFMZksBRKLHGkPdpolDVGDvZHl4O+++GZTrC3L
// SIG // EanR0YLQILO3WPPIwvJtKIdYjCIodj8WW1zol9CGIuCB
// SIG // U9+iNyYC/VwR4NcBSoRX73JQoxhkK9UmlV6alaRI7BLX
// SIG // JU6yXXg+c+z1TFbMsmBDZM0c76cIjA0/B+UEl4UNPy1W
// SIG // 7nR95LWq+6VbMErXrGhL5q7uW8HPCHiu1Px/URbATCLU
// SIG // XFhJ/7zXgEW3RGwqwiblwp9rnPCcsmKiXi6SJdpEbMhc
// SIG // p+FZHGrSEUKvmTGx0eeElkRyCwtIJhz+IzImcH+GOcSc
// SIG // uK8HmiXdF7V8Ws2tb17h/14YGfk4kvTluwIDAQABo4IB
// SIG // bjCCAWowHwYDVR0lBBgwFgYKKwYBBAGCNz0GAQYIKwYB
// SIG // BQUHAwMwHQYDVR0OBBYEFNjsHfbiAnqmDcu6Bqgh77Q9
// SIG // ne7NMEUGA1UdEQQ+MDykOjA4MR4wHAYDVQQLExVNaWNy
// SIG // b3NvZnQgQ29ycG9yYXRpb24xFjAUBgNVBAUTDTIzMDg2
// SIG // NSs0NzA1NjEwHwYDVR0jBBgwFoAU5vxfe7siAFjkck61
// SIG // 9CF0IzLm76wwVgYDVR0fBE8wTTBLoEmgR4ZFaHR0cDov
// SIG // L2NybC5taWNyb3NvZnQuY29tL3BraS9jcmwvcHJvZHVj
// SIG // dHMvTWljQ29kU2lnUENBXzIwMTAtMDctMDYuY3JsMFoG
// SIG // CCsGAQUFBwEBBE4wTDBKBggrBgEFBQcwAoY+aHR0cDov
// SIG // L3d3dy5taWNyb3NvZnQuY29tL3BraS9jZXJ0cy9NaWND
// SIG // b2RTaWdQQ0FfMjAxMC0wNy0wNi5jcnQwDAYDVR0TAQH/
// SIG // BAIwADANBgkqhkiG9w0BAQsFAAOCAQEAply9JxwlaV3T
// SIG // C5ZdD1PSra9eTQjYsru5tLVPOEcXJQMSP5UfZe2dAzV/
// SIG // tyHT8as/XD2cK4YKZmHW3W5LdQzkaPS+rF4W7L5qylGW
// SIG // gKlZN1GEMz904vo1Y5pA90PfkSTth8UoRf03682EZLUx
// SIG // FMMvzIdA3iNtPnYp4b0n/cwZSf8YpxE1wFoqi0xKLtRN
// SIG // 3n/u7LNmhlMOv3eJ9yU0UPUnEHGEpFIgtlagmFaRllHu
// SIG // 1PojPSBYA0BeaGK89U8QmqxL5JRgXxm+oUTLNBZMZV91
// SIG // W6Pczb778rDwNF5TrjHyGXx1aRqzNcHEU+a62gO5U3mz
// SIG // /cI1Lr9VqU8W3Nn9MIZ0qDCCBnAwggRYoAMCAQICCmEM
// SIG // UkwAAAAAAAMwDQYJKoZIhvcNAQELBQAwgYgxCzAJBgNV
// SIG // BAYTAlVTMRMwEQYDVQQIEwpXYXNoaW5ndG9uMRAwDgYD
// SIG // VQQHEwdSZWRtb25kMR4wHAYDVQQKExVNaWNyb3NvZnQg
// SIG // Q29ycG9yYXRpb24xMjAwBgNVBAMTKU1pY3Jvc29mdCBS
// SIG // b290IENlcnRpZmljYXRlIEF1dGhvcml0eSAyMDEwMB4X
// SIG // DTEwMDcwNjIwNDAxN1oXDTI1MDcwNjIwNTAxN1owfjEL
// SIG // MAkGA1UEBhMCVVMxEzARBgNVBAgTCldhc2hpbmd0b24x
// SIG // EDAOBgNVBAcTB1JlZG1vbmQxHjAcBgNVBAoTFU1pY3Jv
// SIG // c29mdCBDb3Jwb3JhdGlvbjEoMCYGA1UEAxMfTWljcm9z
// SIG // b2Z0IENvZGUgU2lnbmluZyBQQ0EgMjAxMDCCASIwDQYJ
// SIG // KoZIhvcNAQEBBQADggEPADCCAQoCggEBAOkOZFB5Z7XE
// SIG // 4/0JAEyelKz3VmjqRNjPxVhPqaV2fG1FutM5krSkHvn5
// SIG // ZYLkF9KP/UScCOhlk84sVYS/fQjjLiuoQSsYt6JLbklM
// SIG // axUH3tHSwokecZTNtX9LtK8I2MyI1msXlDqTziY/7Ob+
// SIG // NJhX1R1dSfayKi7VhbtZP/iQtCuDdMorsztG4/BGScEX
// SIG // ZlTJHL0dxFViV3L4Z7klIDTeXaallV6rKIDN1bKe5QO1
// SIG // Y9OyFMjByIomCll/B+z/Du2AEjVMEqa+Ulv1ptrgiwtI
// SIG // d9aFR9UQucboqu6Lai0FXGDGtCpbnCMcX0XjGhQebzfL
// SIG // GTOAaolNo2pmY3iT1TDPlR8CAwEAAaOCAeMwggHfMBAG
// SIG // CSsGAQQBgjcVAQQDAgEAMB0GA1UdDgQWBBTm/F97uyIA
// SIG // WORyTrX0IXQjMubvrDAZBgkrBgEEAYI3FAIEDB4KAFMA
// SIG // dQBiAEMAQTALBgNVHQ8EBAMCAYYwDwYDVR0TAQH/BAUw
// SIG // AwEB/zAfBgNVHSMEGDAWgBTV9lbLj+iiXGJo0T2UkFvX
// SIG // zpoYxDBWBgNVHR8ETzBNMEugSaBHhkVodHRwOi8vY3Js
// SIG // Lm1pY3Jvc29mdC5jb20vcGtpL2NybC9wcm9kdWN0cy9N
// SIG // aWNSb29DZXJBdXRfMjAxMC0wNi0yMy5jcmwwWgYIKwYB
// SIG // BQUHAQEETjBMMEoGCCsGAQUFBzAChj5odHRwOi8vd3d3
// SIG // Lm1pY3Jvc29mdC5jb20vcGtpL2NlcnRzL01pY1Jvb0Nl
// SIG // ckF1dF8yMDEwLTA2LTIzLmNydDCBnQYDVR0gBIGVMIGS
// SIG // MIGPBgkrBgEEAYI3LgMwgYEwPQYIKwYBBQUHAgEWMWh0
// SIG // dHA6Ly93d3cubWljcm9zb2Z0LmNvbS9QS0kvZG9jcy9D
// SIG // UFMvZGVmYXVsdC5odG0wQAYIKwYBBQUHAgIwNB4yIB0A
// SIG // TABlAGcAYQBsAF8AUABvAGwAaQBjAHkAXwBTAHQAYQB0
// SIG // AGUAbQBlAG4AdAAuIB0wDQYJKoZIhvcNAQELBQADggIB
// SIG // ABp071dPKXvEFoV4uFDTIvwJnayCl/g0/yosl5US5eS/
// SIG // z7+TyOM0qduBuNweAL7SNW+v5X95lXflAtTx69jNTh4b
// SIG // YaLCWiMa8IyoYlFFZwjjPzwek/gwhRfIOUCm1w6zISnl
// SIG // paFpjCKTzHSY56FHQ/JTrMAPMGl//tIlIG1vYdPfB9XZ
// SIG // cgAsaYZ2PVHbpjlIyTdhbQfdUxnLp9Zhwr/ig6sP4Gub
// SIG // ldZ9KFGwiUpRpJpsyLcfShoOaanX3MF+0Ulwqratu3JH
// SIG // Yxf6ptaipobsqBBEm2O2smmJBsdGhnoYP+jFHSHVe/kC
// SIG // Iy3FQcu/HUzIFu+xnH/8IktJim4V46Z/dlvRU3mRhZ3V
// SIG // 0ts9czXzPK5UslJHasCqE5XSjhHamWdeMoz7N4XR3HWF
// SIG // nIfGWleFwr/dDY+Mmy3rtO7PJ9O1Xmn6pBYEAackZ3PP
// SIG // TU+23gVWl3r36VJN9HcFT4XG2Avxju1CCdENduMjVngi
// SIG // Jja+yrGMbqod5IXaRzNij6TJkTNfcR5Ar5hlySLoQiEl
// SIG // ihwtYNk3iUGJKhYP12E8lGhgUu/WR5mggEDuFYF3Ppzg
// SIG // UxgaUB04lZseZjMTJzkXeIc2zk7DX7L1PUdTtuDl2wth
// SIG // PSrXkizON1o+QEIxpB8QCMJWnL8kXVECnWp50hfT2sGU
// SIG // jgd7JXFEqwZq5tTG3yOalnXFMYIZ2TCCGdUCAQEwgZUw
// SIG // fjELMAkGA1UEBhMCVVMxEzARBgNVBAgTCldhc2hpbmd0
// SIG // b24xEDAOBgNVBAcTB1JlZG1vbmQxHjAcBgNVBAoTFU1p
// SIG // Y3Jvc29mdCBDb3Jwb3JhdGlvbjEoMCYGA1UEAxMfTWlj
// SIG // cm9zb2Z0IENvZGUgU2lnbmluZyBQQ0EgMjAxMAITMwAA
// SIG // BI8LuXzfev9KVwAAAAAEjzANBglghkgBZQMEAgEFAKCC
// SIG // AQQwGQYJKoZIhvcNAQkDMQwGCisGAQQBgjcCAQQwHAYK
// SIG // KwYBBAGCNwIBCzEOMAwGCisGAQQBgjcCARUwLwYJKoZI
// SIG // hvcNAQkEMSIEILvv5Nic3PWDZGPNYYt0GpGEA/suUNLd
// SIG // Em3V9bx+cLj+MDwGCisGAQQBgjcKAxwxLgwsc1BZN3hQ
// SIG // QjdoVDVnNUhIcll0OHJETFNNOVZ1WlJ1V1phZWYyZTIy
// SIG // UnM1ND0wWgYKKwYBBAGCNwIBDDFMMEqgJIAiAE0AaQBj
// SIG // AHIAbwBzAG8AZgB0ACAAVwBpAG4AZABvAHcAc6EigCBo
// SIG // dHRwOi8vd3d3Lm1pY3Jvc29mdC5jb20vd2luZG93czAN
// SIG // BgkqhkiG9w0BAQEFAASCAQAm8Nnmx6bhbH6U+7ZdMH++
// SIG // Ehw5v2HJbSe37u8y8Lg0uYgyVpSdynRs9jKEvBKutpsX
// SIG // kKv+0iAqVa2evNdalURzfIEH44wVFZFdp0FFbfxzk5Wo
// SIG // kxLeDQtpIPdISwZxI7WYI9FglmprXbJSdxChnwrUfUDK
// SIG // KsKZBzSeSIEj3d6MRO9NKMv1/cf5aTAsn2lU8rjI2+xc
// SIG // AZHQy+yuI3v4BgYN5SvQqk1eF2IVvzHrvN0NDIZnrH0H
// SIG // 2Y81jhgnPhgyKlo/lql5R+55STr8XySxIRZ94C8Gdcsq
// SIG // 6391SH+BULaUKny2oCgyItrWVwOC734x9KpQeci6dUTl
// SIG // xh8dk2mEC11/oYIXDDCCFwgGCisGAQQBgjcDAwExghb4
// SIG // MIIW9AYJKoZIhvcNAQcCoIIW5TCCFuECAQMxDzANBglg
// SIG // hkgBZQMEAgEFADCCAVUGCyqGSIb3DQEJEAEEoIIBRASC
// SIG // AUAwggE8AgEBBgorBgEEAYRZCgMBMDEwDQYJYIZIAWUD
// SIG // BAIBBQAEIK/TqdkRzT+OmPcJiT2wP1PCGEuOmL0ox0WS
// SIG // XQ0cJPR4AgZjTuAS/tcYEzIwMjIxMDE5MjEzMzEzLjE3
// SIG // NlowBIACAfSggdSkgdEwgc4xCzAJBgNVBAYTAlVTMRMw
// SIG // EQYDVQQIEwpXYXNoaW5ndG9uMRAwDgYDVQQHEwdSZWRt
// SIG // b25kMR4wHAYDVQQKExVNaWNyb3NvZnQgQ29ycG9yYXRp
// SIG // b24xKTAnBgNVBAsTIE1pY3Jvc29mdCBPcGVyYXRpb25z
// SIG // IFB1ZXJ0byBSaWNvMSYwJAYDVQQLEx1UaGFsZXMgVFNT
// SIG // IEVTTjo4OTdBLUUzNTYtMTcwMTElMCMGA1UEAxMcTWlj
// SIG // cm9zb2Z0IFRpbWUtU3RhbXAgU2VydmljZaCCEV8wggcQ
// SIG // MIIE+KADAgECAhMzAAABqwkJ76tj1OipAAEAAAGrMA0G
// SIG // CSqGSIb3DQEBCwUAMHwxCzAJBgNVBAYTAlVTMRMwEQYD
// SIG // VQQIEwpXYXNoaW5ndG9uMRAwDgYDVQQHEwdSZWRtb25k
// SIG // MR4wHAYDVQQKExVNaWNyb3NvZnQgQ29ycG9yYXRpb24x
// SIG // JjAkBgNVBAMTHU1pY3Jvc29mdCBUaW1lLVN0YW1wIFBD
// SIG // QSAyMDEwMB4XDTIyMDMwMjE4NTEyOFoXDTIzMDUxMTE4
// SIG // NTEyOFowgc4xCzAJBgNVBAYTAlVTMRMwEQYDVQQIEwpX
// SIG // YXNoaW5ndG9uMRAwDgYDVQQHEwdSZWRtb25kMR4wHAYD
// SIG // VQQKExVNaWNyb3NvZnQgQ29ycG9yYXRpb24xKTAnBgNV
// SIG // BAsTIE1pY3Jvc29mdCBPcGVyYXRpb25zIFB1ZXJ0byBS
// SIG // aWNvMSYwJAYDVQQLEx1UaGFsZXMgVFNTIEVTTjo4OTdB
// SIG // LUUzNTYtMTcwMTElMCMGA1UEAxMcTWljcm9zb2Z0IFRp
// SIG // bWUtU3RhbXAgU2VydmljZTCCAiIwDQYJKoZIhvcNAQEB
// SIG // BQADggIPADCCAgoCggIBAMmdS1o5dehASUsscLqyx2wm
// SIG // /WirNUfqkGBymDItYzEnoKtkhrd7wNsJs4g+BuM3uBX8
// SIG // 1WnO270lkrC0e1mmDqQt420Tmb8lwsjQKM6mEaNQIfXD
// SIG // ronrVN3aw1lx9bAf7VZEA3kHFql6YAO3kjQ6PftA4iVH
// SIG // X3JVv98ntjkbtqzKeJMaNWd8dBaAD3RCliMoajTDGbyY
// SIG // NKTvxBhWILyJ8WYdJ/NBDpqPzQl+pxm6ZZVSeBQAIOub
// SIG // ZjU0vfpECxHC5vI1ErrqapG+0oBhhON+gllVklPAWZv2
// SIG // iv0mgjCTj7YNKX7yL2x2TvrvHVq5GPNa5fNbpy39t5cv
// SIG // iiYqMf1RZVZccdr+2vApk5ib5a4O8SiAgPSUwYGoOwbZ
// SIG // G1onHij0ATPLkgKUfgaPzFfd5JZSbRl2Xg347/LjWQLR
// SIG // +KjAyACFb06bqWzvHtQJTND8Y0j5Y2SBnSCqV2zNHSVt
// SIG // s4+aUfkUhsKS+GAXS3j5XUgYA7SMNog76Nnss5l01nEX
// SIG // 7sHDdYykYhzuQKFrT70XVTZeX25tSBfy3VaczYd1JSI/
// SIG // 9wOGqbFU52NyrlsA1qimxOhsuds7Pxo+jO3RjV/kC+AE
// SIG // OoVaXDdminsc3PtlBCVh/sgYno9AUymblSRmee1gwlnl
// SIG // ZJ0uiHKI9q2HFgZWM10yPG5gVt0prXnJFi1Wxmmg+BH/
// SIG // AgMBAAGjggE2MIIBMjAdBgNVHQ4EFgQUFFvO8o1eNcSC
// SIG // IQZMvqGfdNL+pqowHwYDVR0jBBgwFoAUn6cVXQBeYl2D
// SIG // 9OXSZacbUzUZ6XIwXwYDVR0fBFgwVjBUoFKgUIZOaHR0
// SIG // cDovL3d3dy5taWNyb3NvZnQuY29tL3BraW9wcy9jcmwv
// SIG // TWljcm9zb2Z0JTIwVGltZS1TdGFtcCUyMFBDQSUyMDIw
// SIG // MTAoMSkuY3JsMGwGCCsGAQUFBwEBBGAwXjBcBggrBgEF
// SIG // BQcwAoZQaHR0cDovL3d3dy5taWNyb3NvZnQuY29tL3Br
// SIG // aW9wcy9jZXJ0cy9NaWNyb3NvZnQlMjBUaW1lLVN0YW1w
// SIG // JTIwUENBJTIwMjAxMCgxKS5jcnQwDAYDVR0TAQH/BAIw
// SIG // ADATBgNVHSUEDDAKBggrBgEFBQcDCDANBgkqhkiG9w0B
// SIG // AQsFAAOCAgEAykuUgTc1KMszMgsHbhgjgEGv/dCHFf0b
// SIG // y99C45SR770/udCNNeqlT610Ehz13xGFU6Hci+TLUPUn
// SIG // hvUnSuz7xkiWRru5RjZZmSonEVv8npa3z1QveUfngtyi
// SIG // 0Jd6qlSykoEVJ6tDuR1Kw9xU9yvthZWhQs/ymyOwh+mx
// SIG // t0C9wbeLJ92er2vc9ly12pFxbCNDJ+mQ7v520hAvreWq
// SIG // Z02GOJhw0R4c1iP39iNBzHOoz+DsO0sYjwhaz9HrvYME
// SIG // zOD1MJdLPWfUFsZ//iTd3jzEykk02WjnZNzIe2ENfmQ/
// SIG // KblGXHeSe8JYqimTFxl5keMfLUELjAh0mhQ1vLCJZ20B
// SIG // wC4O57Eg7yO/YuBno+4RrV0CD2gp4BO10KFW2SQ/MhvR
// SIG // WK7HbgS6Bzt70rkIeSUto7pRkHMqrnhubITcXddky6Gt
// SIG // ZsmwM3hvqXuStMeU1W5NN3HA8ypjPLd/bomfGx96Huw8
// SIG // OrftcQvk7thdNu4JhAyKUXUP7dKMCJfrOdplg0j1tE0a
// SIG // iE+pDTSQVmPzGezCL42slyPJVXpu4xxE0hpACr2ua0LH
// SIG // v/LB6RV5C4CO4Ms/pfal//F3O+hJZe5ixevzKNkXXbxP
// SIG // Oa1R+SIrW/rHZM6RIDLTJxTGFDM1hQDyafGu9S/a7umk
// SIG // vilgBHNxZfk0IYE7RRWJcG7oiY+FGdx1cs0wggdxMIIF
// SIG // WaADAgECAhMzAAAAFcXna54Cm0mZAAAAAAAVMA0GCSqG
// SIG // SIb3DQEBCwUAMIGIMQswCQYDVQQGEwJVUzETMBEGA1UE
// SIG // CBMKV2FzaGluZ3RvbjEQMA4GA1UEBxMHUmVkbW9uZDEe
// SIG // MBwGA1UEChMVTWljcm9zb2Z0IENvcnBvcmF0aW9uMTIw
// SIG // MAYDVQQDEylNaWNyb3NvZnQgUm9vdCBDZXJ0aWZpY2F0
// SIG // ZSBBdXRob3JpdHkgMjAxMDAeFw0yMTA5MzAxODIyMjVa
// SIG // Fw0zMDA5MzAxODMyMjVaMHwxCzAJBgNVBAYTAlVTMRMw
// SIG // EQYDVQQIEwpXYXNoaW5ndG9uMRAwDgYDVQQHEwdSZWRt
// SIG // b25kMR4wHAYDVQQKExVNaWNyb3NvZnQgQ29ycG9yYXRp
// SIG // b24xJjAkBgNVBAMTHU1pY3Jvc29mdCBUaW1lLVN0YW1w
// SIG // IFBDQSAyMDEwMIICIjANBgkqhkiG9w0BAQEFAAOCAg8A
// SIG // MIICCgKCAgEA5OGmTOe0ciELeaLL1yR5vQ7VgtP97pwH
// SIG // B9KpbE51yMo1V/YBf2xK4OK9uT4XYDP/XE/HZveVU3Fa
// SIG // 4n5KWv64NmeFRiMMtY0Tz3cywBAY6GB9alKDRLemjkZr
// SIG // BxTzxXb1hlDcwUTIcVxRMTegCjhuje3XD9gmU3w5YQJ6
// SIG // xKr9cmmvHaus9ja+NSZk2pg7uhp7M62AW36MEBydUv62
// SIG // 6GIl3GoPz130/o5Tz9bshVZN7928jaTjkY+yOSxRnOlw
// SIG // aQ3KNi1wjjHINSi947SHJMPgyY9+tVSP3PoFVZhtaDua
// SIG // Rr3tpK56KTesy+uDRedGbsoy1cCGMFxPLOJiss254o2I
// SIG // 5JasAUq7vnGpF1tnYN74kpEeHT39IM9zfUGaRnXNxF80
// SIG // 3RKJ1v2lIH1+/NmeRd+2ci/bfV+AutuqfjbsNkz2K26o
// SIG // ElHovwUDo9Fzpk03dJQcNIIP8BDyt0cY7afomXw/TNuv
// SIG // XsLz1dhzPUNOwTM5TI4CvEJoLhDqhFFG4tG9ahhaYQFz
// SIG // ymeiXtcodgLiMxhy16cg8ML6EgrXY28MyTZki1ugpoMh
// SIG // XV8wdJGUlNi5UPkLiWHzNgY1GIRH29wb0f2y1BzFa/Zc
// SIG // UlFdEtsluq9QBXpsxREdcu+N+VLEhReTwDwV2xo3xwgV
// SIG // GD94q0W29R6HXtqPnhZyacaue7e3PmriLq0CAwEAAaOC
// SIG // Ad0wggHZMBIGCSsGAQQBgjcVAQQFAgMBAAEwIwYJKwYB
// SIG // BAGCNxUCBBYEFCqnUv5kxJq+gpE8RjUpzxD/LwTuMB0G
// SIG // A1UdDgQWBBSfpxVdAF5iXYP05dJlpxtTNRnpcjBcBgNV
// SIG // HSAEVTBTMFEGDCsGAQQBgjdMg30BATBBMD8GCCsGAQUF
// SIG // BwIBFjNodHRwOi8vd3d3Lm1pY3Jvc29mdC5jb20vcGtp
// SIG // b3BzL0RvY3MvUmVwb3NpdG9yeS5odG0wEwYDVR0lBAww
// SIG // CgYIKwYBBQUHAwgwGQYJKwYBBAGCNxQCBAweCgBTAHUA
// SIG // YgBDAEEwCwYDVR0PBAQDAgGGMA8GA1UdEwEB/wQFMAMB
// SIG // Af8wHwYDVR0jBBgwFoAU1fZWy4/oolxiaNE9lJBb186a
// SIG // GMQwVgYDVR0fBE8wTTBLoEmgR4ZFaHR0cDovL2NybC5t
// SIG // aWNyb3NvZnQuY29tL3BraS9jcmwvcHJvZHVjdHMvTWlj
// SIG // Um9vQ2VyQXV0XzIwMTAtMDYtMjMuY3JsMFoGCCsGAQUF
// SIG // BwEBBE4wTDBKBggrBgEFBQcwAoY+aHR0cDovL3d3dy5t
// SIG // aWNyb3NvZnQuY29tL3BraS9jZXJ0cy9NaWNSb29DZXJB
// SIG // dXRfMjAxMC0wNi0yMy5jcnQwDQYJKoZIhvcNAQELBQAD
// SIG // ggIBAJ1VffwqreEsH2cBMSRb4Z5yS/ypb+pcFLY+Tkdk
// SIG // eLEGk5c9MTO1OdfCcTY/2mRsfNB1OW27DzHkwo/7bNGh
// SIG // lBgi7ulmZzpTTd2YurYeeNg2LpypglYAA7AFvonoaeC6
// SIG // Ce5732pvvinLbtg/SHUB2RjebYIM9W0jVOR4U3UkV7nd
// SIG // n/OOPcbzaN9l9qRWqveVtihVJ9AkvUCgvxm2EhIRXT0n
// SIG // 4ECWOKz3+SmJw7wXsFSFQrP8DJ6LGYnn8AtqgcKBGUIZ
// SIG // UnWKNsIdw2FzLixre24/LAl4FOmRsqlb30mjdAy87JGA
// SIG // 0j3mSj5mO0+7hvoyGtmW9I/2kQH2zsZ0/fZMcm8Qq3Uw
// SIG // xTSwethQ/gpY3UA8x1RtnWN0SCyxTkctwRQEcb9k+SS+
// SIG // c23Kjgm9swFXSVRk2XPXfx5bRAGOWhmRaw2fpCjcZxko
// SIG // JLo4S5pu+yFUa2pFEUep8beuyOiJXk+d0tBMdrVXVAmx
// SIG // aQFEfnyhYWxz/gq77EFmPWn9y8FBSX5+k77L+DvktxW/
// SIG // tM4+pTFRhLy/AsGConsXHRWJjXD+57XQKBqJC4822rpM
// SIG // +Zv/Cuk0+CQ1ZyvgDbjmjJnW4SLq8CdCPSWU5nR0W2rR
// SIG // nj7tfqAxM328y+l7vzhwRNGQ8cirOoo6CGJ/2XBjU02N
// SIG // 7oJtpQUQwXEGahC0HVUzWLOhcGbyoYIC0jCCAjsCAQEw
// SIG // gfyhgdSkgdEwgc4xCzAJBgNVBAYTAlVTMRMwEQYDVQQI
// SIG // EwpXYXNoaW5ndG9uMRAwDgYDVQQHEwdSZWRtb25kMR4w
// SIG // HAYDVQQKExVNaWNyb3NvZnQgQ29ycG9yYXRpb24xKTAn
// SIG // BgNVBAsTIE1pY3Jvc29mdCBPcGVyYXRpb25zIFB1ZXJ0
// SIG // byBSaWNvMSYwJAYDVQQLEx1UaGFsZXMgVFNTIEVTTjo4
// SIG // OTdBLUUzNTYtMTcwMTElMCMGA1UEAxMcTWljcm9zb2Z0
// SIG // IFRpbWUtU3RhbXAgU2VydmljZaIjCgEBMAcGBSsOAwIa
// SIG // AxUAW6h6/24WCo7WZz6CEVAeLztcmD6ggYMwgYCkfjB8
// SIG // MQswCQYDVQQGEwJVUzETMBEGA1UECBMKV2FzaGluZ3Rv
// SIG // bjEQMA4GA1UEBxMHUmVkbW9uZDEeMBwGA1UEChMVTWlj
// SIG // cm9zb2Z0IENvcnBvcmF0aW9uMSYwJAYDVQQDEx1NaWNy
// SIG // b3NvZnQgVGltZS1TdGFtcCBQQ0EgMjAxMDANBgkqhkiG
// SIG // 9w0BAQUFAAIFAOb6sAkwIhgPMjAyMjEwMTkyMTE5MDVa
// SIG // GA8yMDIyMTAyMDIxMTkwNVowdzA9BgorBgEEAYRZCgQB
// SIG // MS8wLTAKAgUA5vqwCQIBADAKAgEAAgIGTAIB/zAHAgEA
// SIG // AgIRYTAKAgUA5vwBiQIBADA2BgorBgEEAYRZCgQCMSgw
// SIG // JjAMBgorBgEEAYRZCgMCoAowCAIBAAIDB6EgoQowCAIB
// SIG // AAIDAYagMA0GCSqGSIb3DQEBBQUAA4GBAFUnwKDdRpRJ
// SIG // n4qtjkZ5a/um8/6n6nBd/zVXK+eXXhIRHoYHw/VYPXU8
// SIG // fCAkAeknI1DeV+/Utf2HHQL6IiHs/cFni3f4Mh3MdxAa
// SIG // 8PfbIfl0XGsP3NYhS5KiT2jJllzJDiw82H0agRT80GJ2
// SIG // TGtp3didz2e7ROMVVHwCd7nFWg6pMYIEDTCCBAkCAQEw
// SIG // gZMwfDELMAkGA1UEBhMCVVMxEzARBgNVBAgTCldhc2hp
// SIG // bmd0b24xEDAOBgNVBAcTB1JlZG1vbmQxHjAcBgNVBAoT
// SIG // FU1pY3Jvc29mdCBDb3Jwb3JhdGlvbjEmMCQGA1UEAxMd
// SIG // TWljcm9zb2Z0IFRpbWUtU3RhbXAgUENBIDIwMTACEzMA
// SIG // AAGrCQnvq2PU6KkAAQAAAaswDQYJYIZIAWUDBAIBBQCg
// SIG // ggFKMBoGCSqGSIb3DQEJAzENBgsqhkiG9w0BCRABBDAv
// SIG // BgkqhkiG9w0BCQQxIgQgV3ToTwAdzizJev/MLSj+BdRt
// SIG // 41WXeVB0s1D4xsK/ylQwgfoGCyqGSIb3DQEJEAIvMYHq
// SIG // MIHnMIHkMIG9BCAOHK/6sIVgEVSVD3Arvk6OyQKvRxFH
// SIG // KyraUzbN1/AKVzCBmDCBgKR+MHwxCzAJBgNVBAYTAlVT
// SIG // MRMwEQYDVQQIEwpXYXNoaW5ndG9uMRAwDgYDVQQHEwdS
// SIG // ZWRtb25kMR4wHAYDVQQKExVNaWNyb3NvZnQgQ29ycG9y
// SIG // YXRpb24xJjAkBgNVBAMTHU1pY3Jvc29mdCBUaW1lLVN0
// SIG // YW1wIFBDQSAyMDEwAhMzAAABqwkJ76tj1OipAAEAAAGr
// SIG // MCIEIPpp1eoiWqPOl81fTBkjBDX+KKxfAC0unrb7cNXQ
// SIG // 9shDMA0GCSqGSIb3DQEBCwUABIICAFetd6vo6u9GylF0
// SIG // pORrRymsXDDadBxQqcR+f8K3sAeZGUJtC6uWCA7b9HbN
// SIG // drkiN0IS91kN1sLws4waoKpCmj6Hu1OYqkPYWf9e7u6R
// SIG // sFU6q6gtz9XZBKnWxvNiPWoK61IS/lugRmt9ldyOR2ka
// SIG // WFVLBGEIMoGAF8RIn8iMTxKXRd8uYqT89i6igleHsk33
// SIG // emE2BqvozuXKjL03x03yzow7jyNUbeBU9QNp/2shDsJ4
// SIG // 4QEuQJQ+dWPbaDrRWaMa49mrdfGcbxISn8b3h8mZZvhq
// SIG // AbBQivkB++xM7on/eKth5Pw7MhWnEjafC8/0+ui3Fsi1
// SIG // IYm2RAZgbKQDEL03pYz2aSG+CunGH07MNpaECUaKHHlX
// SIG // feVNmSqTrHh8ZEt4S2TprZT0K0N610zMvaPBmiodgRep
// SIG // c2vJ3Oucu4gNcTV9TRZKAiLAilhx6zftlGMAhbZFcwAV
// SIG // I/QYGiQNrns8pxqYqq8RNVk+llMPwPTubIEhL5neEaZq
// SIG // cp1/bY4cQliga6tcwi9nvTC2kPP7kBWL/bGcbAThb8B3
// SIG // vJ42pyLwq3jReW7qdUqOw9opaEH/Xiub8E1auaSRur/E
// SIG // /T61EDBqZgrVn2Xylb/JPd1IQIo3f792xqMg8TiuSPg3
// SIG // D/t598fWGbY5N+XnoW8wLryn6tcYC9Sy36SdVi3WiJrl
// SIG // w/NgQ30M
// SIG // End signature block
