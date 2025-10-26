using System;
using System.IO;
using System.Text;

public static class Logger
{
    public enum Level { Trace = 0, Debug = 1, Info = 2, Warn = 3, Error = 4 }

    private static readonly object _gate = new();
    private static StreamWriter? _writer;
    private static string _logDir = "";
    private static string _currentFilePath = "";
    private static DateOnly _currentDate;
    private static bool _mirrorToConsole;
    private static int _retentionDays = 7;
    private static Level _minLevel = Level.Trace;
    private static TextWriter? _origOut;
    private static TextWriter? _origErr;

    public static void Init(bool mirrorToConsole = false, int retentionDays = 7, Level minLevel = Level.Trace, string? customBaseDir = null)
    {
        lock (_gate)
        {
            _mirrorToConsole = mirrorToConsole;
            _retentionDays = Math.Max(0, retentionDays);
            _minLevel = minLevel;

            var baseDir = customBaseDir ??
                          AppContext.BaseDirectory ??
                          Path.GetDirectoryName(Environment.ProcessPath!) ??
                          Directory.GetCurrentDirectory();

            _logDir = Path.Combine(baseDir, "Logs");
            Directory.CreateDirectory(_logDir);

            _currentDate = DateOnly.FromDateTime(DateTime.Now);
            _currentFilePath = Path.Combine(_logDir, $"DD-{DateTime.Now:yyyy-MM-dd-HH-mm-ss}.log");

            OpenWriter(_currentFilePath);
            CleanOldLogs_NoLock();
        }

        Info("Logger initialized");
    }

    public static void HookConsole(Level consoleOutLevel = Level.Info)
    {
        lock (_gate)
        {
            _origOut ??= Console.Out;
            _origErr ??= Console.Error;
            Console.SetOut(new LoggerTextWriter(consoleOutLevel));
            Console.SetError(new LoggerTextWriter(Level.Error));
        }
    }

    public static void UnhookConsole()
    {
        lock (_gate)
        {
            if (_origOut != null) Console.SetOut(_origOut);
            if (_origErr != null) Console.SetError(_origErr);
            _origOut = null;
            _origErr = null;
        }
    }

    public static void CleanAllLogs()
    {
        lock (_gate)
        {
            CloseWriter_NoLock();

            try
            {
                if (Directory.Exists(_logDir))
                {
                    foreach (var f in Directory.EnumerateFiles(_logDir, "*", SearchOption.TopDirectoryOnly))
                        TryDeleteFile(f);
                }
            }
            catch
            {
                // good girl action
            }

            Directory.CreateDirectory(_logDir);
            _currentDate = DateOnly.FromDateTime(DateTime.Now);
            _currentFilePath = Path.Combine(_logDir, $"DD-{DateTime.Now:yyyy-MM-dd-HH-mm-ss}.log");
            OpenWriter(_currentFilePath);

            _writer!.WriteLine($"{TimeStamp()} | INFO  | (restarted) log wiped via CleanAllLogs()");
            _writer!.Flush();
        }
    }

    public static void CleanOldLogs()
    {
        lock (_gate) CleanOldLogs_NoLock();
    }

    public static void Shutdown()
    {
        lock (_gate)
        {
            try
            {
                _writer?.Flush(); 
            } 
            catch
            {
                // good girl action
            }
            CloseWriter_NoLock();
        }
    }

    public static void Trace(string msg) => Write(Level.Trace, msg);
    public static void Debug(string msg) => Write(Level.Debug, msg);
    public static void Info (string msg) => Write(Level.Info , msg);
    public static void Warn (string msg) => Write(Level.Warn , msg);
    public static void Error(string msg) => Write(Level.Error, msg);

    public static void Error(Exception ex, string? message = null)
        => Write(Level.Error, message is null ? ex.ToString() : $"{message}{Environment.NewLine}{ex}");

    public static void Write(Level level, string message)
    {
        if (level < _minLevel) return;
        try
        {
            lock (_gate)
            {
                var today = DateOnly.FromDateTime(DateTime.Now);
                if (today != _currentDate)
                {
                    CloseWriter_NoLock();
                    _currentDate = today;
                    _currentFilePath = Path.Combine(_logDir, $"DD-{DateTime.Now:yyyy-MM-dd-HH-mm-ss}.log");
                    OpenWriter(_currentFilePath);
                    CleanOldLogs_NoLock();
                }

                var line = $"{TimeStamp()} | {LevelTag(level)} | {message}";
                _writer?.WriteLine(line);
                _writer?.Flush();

                if (_mirrorToConsole)
                {
                    if (level >= Level.Error) _origErr?.WriteLine(line);
                    else _origOut?.WriteLine(line);
                }
            }
        }
        catch
        {
            try
            {
                var line = $"{TimeStamp()} | {LevelTag(Level.Error)} | (logger failed) {message}";
                _origErr?.WriteLine(line);
            }
            catch
            {
                // good girl action
            }
        }
    }


    private static void OpenWriter(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var fs = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read);
        _writer = new StreamWriter(fs, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)) { AutoFlush = true };
    }

    private static void CloseWriter_NoLock()
    {
        try
        {
            _writer?.Flush();
            _writer?.Dispose();
        }
        catch
        {
            // good girl action
        }
        finally
        {
            _writer = null;
        }
    }

    private static void CleanOldLogs_NoLock()
    {
        if (_retentionDays <= 0) return;

        try
        {
            if (!Directory.Exists(_logDir)) return;
            var cutoff = DateTime.Now.Date.AddDays(-_retentionDays);

            foreach (var file in Directory.EnumerateFiles(_logDir, "DD-*.log", SearchOption.TopDirectoryOnly))
            {
                var name = Path.GetFileNameWithoutExtension(file);
                if (!name.StartsWith("DD-")) continue;
                var stamp = name.Substring(5);

                if (!DateTime.TryParseExact(stamp, "yyyy-MM-dd-HH-mm-ss", null, System.Globalization.DateTimeStyles.None, out var fileDt))
                {
                    if (!DateTime.TryParseExact(stamp, "yyyy-MM-dd", null, System.Globalization.DateTimeStyles.None, out fileDt))
                        continue;
                }

                if (fileDt.Date < cutoff)
                    TryDeleteFile(file);
            }
        }
        catch
        {
            // good girl action
        }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            var attr = File.GetAttributes(path);
            if ((attr & FileAttributes.ReadOnly) != 0)
                File.SetAttributes(path, attr & ~FileAttributes.ReadOnly);
            File.Delete(path);
        }
        catch
        {
            // good girl action
        }
    }

    private static string TimeStamp() => DateTime.Now.ToString("HH:mm:ss.fff");
    private static string LevelTag(Level l) => l switch
    {
        Level.Trace => "TRACE",
        Level.Debug => "DEBUG",
        Level.Info  => "INFO ",
        Level.Warn  => "WARN ",
        _           => "ERROR"
    };

    private sealed class LoggerTextWriter : TextWriter
    {
        private readonly Level _level;

        public LoggerTextWriter(Level level) => _level = level;

        public override Encoding Encoding => Encoding.UTF8;

        public override void Write(char value)
        {
            _buffer ??= new StringWriter();
            if (value == '\n')
            {
                var line = _buffer.ToString().TrimEnd('\r');
                _buffer.GetStringBuilder().Clear();
                if (line.Length > 0) Logger.Write(_level, line);
            }
            else
            {
                _buffer.Write(value);
            }
        }

        public override void Write(string? value)
        {
            if (string.IsNullOrEmpty(value)) return;
            foreach (var line in value.Replace("\r\n", "\n").Split('\n'))
                if (line.Length > 0)
                    Logger.Write(_level, line);
        }

        public override void WriteLine(string? value) => Write((value ?? "") + "\n");

        [ThreadStatic] private static StringWriter? _buffer;
    }
}