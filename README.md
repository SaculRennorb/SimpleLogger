# SimpleLogger
d'fk you think it is...

### Do this:
```csharp
class Program {
  static Logger logger = new("Label");

  static void Main(string[] args)
  {
    logger.WriteLine("something to log");
  }
}
```
which will result in `logs/0000current.log` with
```log
[20:48:29][INFO][Label] something to log
```


### Or if you want to be fancy:
```csharp
class Program {
  static Logger logger = new("Label");

  static void Main(string[] args)
  {
    using var _ = logger.NewBlock("block");
  }
}
```
which will result in `logs/0000current.log` with
```log
[20:50:47][INFO][Main] 4A207594 << block
[20:50:47][INFO][Main] 4A207594 >> done with block
```

Also brings handling for static json config storage:

```csharp
// this is your config class
internal static class Config
{
	[JsonIgnore]
	public const string FILE_SOURCE = "config/updater.json";

	public static string ProgramDllPath;
	public static string GithubRepoId;
	public static string PublicKeyPath;

#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. thank net core
	static Config()
#pragma warning restore CS8618 
	{
		//does the actual loading
		StaticConfig.LoadFromDisk(typeof(Config), FILE_SOURCE);
	}

	//required
	public static void SetDefaults()
	{
		ProgramDllPath = "program.dll";
		GithubRepoId   = "owner/repo";
		PublicKeyPath  = "res/public.pem";
	}

	//call this if you want to save your storage
	public static void SaveToDisk() => StaticConfig.SaveToDisk(typeof(Config), FILE_SOURCE);
}
```