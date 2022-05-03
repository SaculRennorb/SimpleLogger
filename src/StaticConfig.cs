using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Rennorb;

public static class StaticConfig
{
	internal static ILog _log = new ConsoleLog();

	public static JsonSerializerOptions SerializerOptions = new() {
		WriteIndented        = true,
		IncludeFields        = true,
		PropertyNamingPolicy = new SnakeCaseNamingPolicy(),
		AllowTrailingCommas  = true,
	};

	static Dictionary<Type, JsonConverter> _converters = new();

	public static void LoadFromDisk(Type type, string path)
	{
		if(!File.Exists(path))
		{
			TryRegenerate(type, path);
		}
		else
		{
			using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read);
			if(stream.Length < 1)
			{
				_log.WriteLine("file empty");
				stream.Close();
				TryRegenerate(type, path);
				return;
			}

			using var jdoc = JsonDocument.Parse(stream, new(){ AllowTrailingCommas = true });
			stream.Dispose();

			if(jdoc.RootElement.ValueKind == JsonValueKind.Null)
			{
				_log.WriteLine("file has null root object");
				TryRegenerate(type, path);
				return;
			}

			try
			{
				int relevantFields = 0;
				int fieldsFound    = 0;
				foreach(var member in type.GetMembers(BindingFlags.Public | BindingFlags.Static))
				{
					if(member.MemberType != MemberTypes.Field && member.MemberType != MemberTypes.Property) continue;
					if(member.GetCustomAttribute<JsonIgnoreAttribute>() != null) continue;

					relevantFields++;
					var jsonPropName = member.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name 
						?? SerializerOptions.PropertyNamingPolicy!.ConvertName(member.Name);
					
					//need to do top level converter attribs ourself
					var converterType = member.GetCustomAttribute<JsonConverterAttribute>()?.ConverterType;
					//TODO(Rennorb): @hammer this will fail in some scenarios
					JsonSerializerOptions newOptions;
					if(converterType != null)
					{
						JsonConverter converter;
						if(!_converters.TryGetValue(converterType, out converter!))
						{
							converter = (JsonConverter)Activator.CreateInstance(converterType)!;
							_converters.Add(converterType, converter);
						}

						newOptions = new(SerializerOptions) { Converters = { converter } };
					}
					else
					{
						newOptions = SerializerOptions;
					}

					if(jdoc.RootElement.TryGetProperty(jsonPropName, out var property))
					{
						switch (member.MemberType) {
							case MemberTypes.Field:
								var fieldInfo = (FieldInfo)member;
								fieldInfo.SetValue(null, JsonSerializer.Deserialize(property, fieldInfo.FieldType, newOptions));
								fieldsFound++;
								break;

							case MemberTypes.Property:
								var propInfo = (PropertyInfo)member;
								if(propInfo.SetMethod != null)
									propInfo.SetValue(null, JsonSerializer.Deserialize(property, propInfo.PropertyType, newOptions));
								fieldsFound++;
								break;
						}
					}
				}

				if(relevantFields > 0)
				{
					if(fieldsFound == 0)
					{
						_log.WriteLine("dit not load any fields, assume file corruption");
						TryRegenerate(type, path);
					}
					else if(fieldsFound < relevantFields)
					{
						_log.WriteLine($"[W] Some files did not get deserialized and retain their default value. Consider investigating {path}.");
					}
				}
			}
			catch(Exception ex)
			{
				_log.WriteLine($"deserialization error:\n{ex}");
				TryRegenerate(type, path);
			}
		}
	}

	public static void TryRegenerate(Type type, string path)
	{
		Directory.CreateDirectory(Path.GetDirectoryName(path)!);

		var setDefaults = type.GetMethod("SetDefaults", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
		if(setDefaults != null)
		{
			_log.WriteLine("missing settings object, regenerating...");
			setDefaults.Invoke(null, Type.EmptyTypes);
			SaveToDisk(type, path);
		}
		else
		{
			_log.WriteLine($"missing settings object, type {type} is missing a static method 'void SetDefaults()', can't regenerate!");
		}
	}

	public static void SaveToDisk(Type type, string path)
	{
		using var stream = File.Open(path, FileMode.Create, FileAccess.Write, FileShare.Read);
		using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions(){ Indented = SerializerOptions.WriteIndented, Encoder = SerializerOptions.Encoder });

		writer.WriteStartObject();
		foreach(var member in type.GetMembers(BindingFlags.Public | BindingFlags.Static))
		{
			if(member.MemberType != MemberTypes.Field && member.MemberType != MemberTypes.Property) continue;
			if(member.GetCustomAttribute<JsonIgnoreAttribute>() != null) continue;


			var jsonPropName = member.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name
							?? SerializerOptions.PropertyNamingPolicy!.ConvertName(member.Name);

			//need to do top level converter attribs ourself
			var converterType = member.GetCustomAttribute<JsonConverterAttribute>()?.ConverterType;
			//TODO(Rennorb): @hammer this will fail in some scenarios
			JsonSerializerOptions newOptions;
			if(converterType != null)
			{
				JsonConverter converter;
				if(!_converters.TryGetValue(converterType, out converter!))
				{
					converter = (JsonConverter)Activator.CreateInstance(converterType)!;
					_converters.Add(converterType, converter);
				}

				newOptions = new(SerializerOptions) { Converters = { converter } };
			}
			else
			{
				newOptions = SerializerOptions;
			}


			writer.WritePropertyName(jsonPropName);
			switch(member.MemberType)
			{
				case MemberTypes.Field:
					var fieldInfo = (FieldInfo)member;
					JsonSerializer.Serialize(writer, fieldInfo.GetValue(null), fieldInfo.FieldType, newOptions);
					break;
				case MemberTypes.Property:
					var propInfo = (PropertyInfo)member;
					if(propInfo.GetMethod != null)
						JsonSerializer.Serialize(writer, propInfo.GetValue(null), propInfo.PropertyType, newOptions);
					break;
			}
			
		}
		writer.WriteEndObject();
	}
}

class ConsoleLog : ILog
{
	public void WriteLine(string msg) => Console.WriteLine(msg);
}
class LogLog : ILog
{
	Logging.Logger _logger = new Logging.Logger("StaticConf");
	public void WriteLine(string msg) => _logger.WriteLine(msg);
}

internal interface ILog
{
	void WriteLine(string msg);
}

public class SnakeCaseNamingPolicy : JsonNamingPolicy
{
	readonly StringBuilder sb = new(128);
	public override string ConvertName(string name)
	{
		sb.Clear();
		sb.Append(char.ToLower(name[0]));
		for(int i = 1; i < name.Length; i++)
		{
			var c = name[i];
			if(char.IsUpper(c))
				sb.Append('_').Append(char.ToLower(c));
			else
				sb.Append(c);
		}
		return sb.ToString();
	}
}

