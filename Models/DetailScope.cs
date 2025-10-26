using System;
using System.Collections.Generic;

namespace DragonDen.ModManager.Models;

internal sealed class DetailScope : IDisposable
{
    private readonly string _name;
    private readonly List<string> _lines = new();
    private bool _failed;
    private readonly bool _alwaysDump;

    public DetailScope(string name, bool alwaysDump = false)
    {
        _name = name;
        _alwaysDump = alwaysDump;
    }

    public void Detail(string msg) => _lines.Add(msg);
    public void Fail(string msg) { _failed = true; _lines.Add(msg); }

    public void Dispose()
    {
        if ((_failed || _alwaysDump) && _lines.Count > 0)
        {
            Console.Error.WriteLine($"[{_name}] details:");
            foreach (var l in _lines) Console.Error.WriteLine("  " + l);
        }
    }
}