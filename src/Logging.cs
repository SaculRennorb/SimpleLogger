using System.Diagnostics;
using System.Text;
using System.Text.Json.Serialization;

namespace Rennorb.Logging;

public enum LogLevel
{
	VERBOSE = 0,
	INFO    = 1,
	WARN    = 2,
	ERROR   = 3,
}

internal static class Config
{
	[JsonIgnore]
	const string FILE_SOURCE = "config/logger.json";

	public static string   LogsPath;
	public static LogLevel LogLevel;

#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
	static Config()
#pragma warning restore CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
	{
		StaticConfig.LoadFromDisk(typeof(Config), FILE_SOURCE);
		StaticConfig._log = new LogLog();
	}

	public static void SetDefaults()
	{
		LogsPath = "logs";
		LogLevel = LogLevel.INFO;
	}
}

public struct Logger
{
	public static readonly string CurrentFile;
	static readonly StreamWriter  s_writer;
	static readonly int           s_maxLvlStrLength;
	static          int           s_maxCategoryStrLength;
	static readonly Random        s_blockIdGenerator;

	static Logger()
	{
		s_blockIdGenerator = new Random();
		s_maxLvlStrLength = 0;
		foreach(var lvl in Enum.GetNames<LogLevel>())
		{
			if(lvl.Length > s_maxLvlStrLength)
				s_maxLvlStrLength = lvl.Length;
		}

		s_maxCategoryStrLength = 0;

#if DEBUG
		string ext = ".DBG.log";
#else
    string ext = ".REL.log";
#endif
		CurrentFile = $"{Config.LogsPath}/0000current{ext}";

		Directory.CreateDirectory(Config.LogsPath);
		if(File.Exists(CurrentFile))
		{
			var fileInfo = new FileInfo(CurrentFile);
			if(fileInfo.CreationTime.Date != DateTime.Now.Date)
			{
				string newPath;
				int i = 0;
				do
				{
					newPath = $"{Config.LogsPath}/{fileInfo.CreationTime:yyyyMMddHHmmss}.{i++}{ext}";
				}
				while(File.Exists(newPath));

				fileInfo.MoveTo(newPath);
			}
		}

		s_writer = new StreamWriter(File.Open(CurrentFile, FileMode.Append, FileAccess.Write, FileShare.Read), Encoding.UTF8) {
			AutoFlush = true,
		};

		Trace.Listeners.Add(new TextWriterTraceListener(s_writer));
	}

	public static uint GetNextBlockId() => unchecked((uint)s_blockIdGenerator.Next());

	string        _name;
	StringBuilder _sb;

	public Logger(string name)
	{
		this._name = name;
		this._sb = new StringBuilder(256);

		if(name.Length > s_maxCategoryStrLength)
			s_maxCategoryStrLength = name.Length;
	}

	public void WriteLine(string msg) => WriteLine(LogLevel.INFO, msg);
	public void WriteLine(LogLevel lvl, string msg)
	{
		//TODO(Rennorb): @performance theese locks could be removed if we where to use a writethough stringbuilder
		lock(_sb)
		{
			NewLineWithPreamble(lvl);

			_sb.Append(' ').AppendLine(msg);

			lock(s_writer)
			{
				s_writer.Write(_sb);
				if(lvl >= Config.LogLevel)
					Console.Write(_sb);
			}
		}
	}

	public uint WriteLineWithBlockId(string msg) => WriteLineWithBlockId(LogLevel.INFO, msg);
	public uint WriteLineWithBlockId(LogLevel lvl, string msg)
	{
		lock(_sb)
		{
			NewLineWithPreamble(lvl);

			var blockId = GetNextBlockId();
			_sb.Append(' ').AppendFormat("{0:X08}", blockId);

			_sb.Append(" << ").AppendLine(msg);

			lock(s_writer)
			{
				s_writer.Write(_sb);
				if(lvl >= Config.LogLevel)
					Console.Write(_sb);
			}

			return blockId;
		}
	}

	public void WriteLineEndBlock(uint blockId, string msg) => WriteLineEndBlock(blockId, LogLevel.INFO, msg);
	public void WriteLineEndBlock(uint blockId, LogLevel lvl) => WriteLineEndBlock(blockId, lvl, "block end");
	public void WriteLineEndBlock(uint blockId, LogLevel lvl, string msg)
	{
		lock(_sb)
		{
			NewLineWithPreamble(lvl);

			_sb.Append(' ').AppendFormat("{0:X08}", blockId);

			_sb.Append(" >> ").AppendLine(msg);

			lock(s_writer)
			{
				s_writer.Write(_sb);
				if(lvl >= Config.LogLevel)
					Console.Write(_sb);
			}
		}
	}

	public LoggerBlock NewBlock(string msg) => new(ref this, LogLevel.INFO, msg);
	public LoggerBlock NewBlock(LogLevel lvl, string msg) => new(ref this, lvl, msg);
	public struct LoggerBlock : IDisposable
	{
		Logger   _logger;
		LogLevel _lvl;
		uint     _blockId;
		string   _msg;

		public LoggerBlock(ref Logger logger, LogLevel lvl, string msg)
		{
			_logger = logger;
			_lvl = lvl;
			_msg = msg;
			_blockId = logger.WriteLineWithBlockId(lvl, msg);
		}

		bool _disposed = false;
		public void Dispose() { if(!_disposed) { _disposed = true; _logger.WriteLineEndBlock(_blockId, _lvl, $"done with {_msg}"); } }
	}

	void NewLineWithPreamble(LogLevel lvl)
	{
		_sb.Clear();
		_sb.Append('[').Append(DateTime.Now.ToString("HH:mm:ss")).Append(']');

		_sb.Append('[');
		var lvlString = lvl.ToString();
		_sb.Append(lvlString);
		_sb.Append(']').Append(' ', s_maxLvlStrLength - lvlString.Length);

		_sb.Append('[').Append(this._name).Append(']').Append(' ', s_maxCategoryStrLength - this._name.Length);
	}
}
