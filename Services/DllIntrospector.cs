using System;
using System.IO;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace DragonDen.ModManager.Services;

public static class DllIntrospector
{
    public static BepInResult? TryGetBepInExInfo(string dllPath)
    {
        try
        {
            if (!File.Exists(dllPath)) return null;

            using var asm = AssemblyDefinition.ReadAssembly(dllPath, new ReaderParameters { ReadSymbols = false });
            foreach (var type in asm.MainModule.Types)
            foreach (var ca in type.CustomAttributes)
            {
                var attrName = ca.AttributeType?.FullName ?? "";
                if (!attrName.EndsWith(".BepInPlugin", StringComparison.Ordinal)) continue;
                if (ca.ConstructorArguments.Count < 3) continue;

                var guid = ca.ConstructorArguments[0].Value?.ToString() ?? "";
                var name = ca.ConstructorArguments[1].Value?.ToString() ?? "";
                var ver = ca.ConstructorArguments[2].Value?.ToString() ?? "";

                if (!string.IsNullOrWhiteSpace(guid))
                    return new BepInResult(guid, name, ver);
            }
        }
        catch (Exception ex)
        {
            Logger.Error($"[DllIntrospector] Error reading BepInEx info from {dllPath}: {ex}");
        }

        return null;
    }

    public static string? TryGetServerModGuid(string dllPath)
    {
        try
        {
            if (!File.Exists(dllPath)) return null;

            using var asm = AssemblyDefinition.ReadAssembly(dllPath, new ReaderParameters { ReadSymbols = false });
            foreach (var type in asm.MainModule.Types)
            {
                if (type.IsAbstract) continue;
                var prop = type.Properties.FirstOrDefault(p => string.Equals(p.Name, "ModGuid", StringComparison.Ordinal));
                if (prop?.GetMethod?.HasBody == true)
                {
                    var instr = prop.GetMethod.Body.Instructions.FirstOrDefault(i => i.OpCode.Code == Code.Ldstr);
                    var val = instr?.Operand as string;
                    if (!string.IsNullOrWhiteSpace(val))
                        return val;
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Error($"[DllIntrospector] Error reading Server Mod GUID from {dllPath}: {ex}");
        }

        return null;
    }

    public sealed record BepInResult(string Guid, string Name, string Version);
}