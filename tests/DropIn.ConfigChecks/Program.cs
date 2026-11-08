using System.Reflection;
using DropIn.Common;

var root = Path.Combine(Path.GetTempPath(), "dropin-config-check-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
try
{
    var missing = Path.Combine(root, "missing");
    Expect<DataDirectoryUnavailableException>(() => DropInConfig.LoadExisting(missing));
    Check(!Directory.Exists(missing), "Loading missing data must not recreate its directory.");

    var existing = Path.Combine(root, "existing");
    Directory.CreateDirectory(existing);
    Expect<DataDirectoryUnavailableException>(() => DropInConfig.LoadExisting(existing));
    Check(!File.Exists(Path.Combine(existing, DropInConfig.FileName)), "Loading missing settings must not create them.");

    var configPath = Path.Combine(existing, DropInConfig.FileName);
    File.WriteAllText(configPath, "{ invalid");
    Expect<InvalidDataException>(() => DropInConfig.LoadExisting(existing));
    Check(File.ReadAllText(configPath) == "{ invalid", "Invalid settings must be preserved.");

    File.Delete(configPath);
    var config = DropInConfig.LoadOrCreate(existing);
    config.Packager.DatabaseDirectory = "test-database";
    config.Save(existing);
    Check(DropInConfig.LoadExisting(existing).Packager.DatabaseDirectory == "test-database",
        "Existing settings must be retained.");
    config.EnsureDataAvailable();
    var configBackup = File.ReadAllText(configPath);
    File.Delete(configPath);
    Expect<DataDirectoryUnavailableException>(() => config.EnsureDataAvailable());
    Check(!File.Exists(configPath), "A missing active configuration must not be recreated.");
    File.WriteAllText(configPath, configBackup);

    var pointer = Path.Combine(root, "pointer", "data-directory.txt");
    Directory.CreateDirectory(Path.GetDirectoryName(pointer)!);
    File.WriteAllText(pointer, missing);
    var recover = typeof(DropInConfig).GetMethod("RecoverDataDirectory", BindingFlags.Static | BindingFlags.NonPublic,
        null, new[] { typeof(string), typeof(string) }, null)!;
    try { recover.Invoke(null, new object[] { missing, pointer }); throw new Exception("Recovery accepted a missing directory."); }
    catch (TargetInvocationException ex) when (ex.InnerException is DirectoryNotFoundException) { }
    Check(File.ReadAllText(pointer) == missing, "Failed recovery must preserve the pointer.");
    recover.Invoke(null, new object[] { existing, pointer });
    Check(File.ReadAllText(pointer) == existing, "Recovery must persist the chosen directory.");
    Check(DropInConfig.LoadExisting(existing).Packager.DatabaseDirectory == "test-database",
        "Recovery must preserve existing settings.");
    var invalid = Path.Combine(root, "invalid");
    Directory.CreateDirectory(invalid);
    var invalidConfig = Path.Combine(invalid, DropInConfig.FileName);
    File.WriteAllText(invalidConfig, "{ invalid");
    try { recover.Invoke(null, new object[] { invalid, pointer }); throw new Exception("Recovery accepted invalid settings."); }
    catch (TargetInvocationException ex) when (ex.InnerException is InvalidDataException) { }
    Check(File.ReadAllText(invalidConfig) == "{ invalid" && File.ReadAllText(pointer) == existing,
        "Failed recovery must preserve invalid settings and the previous pointer.");
    var logBase = Path.Combine(root, "log-base");
    Directory.CreateDirectory(logBase);
    AppLog.Configure("logs\\test.log", "Info", logBase);
    Directory.Delete(logBase);
    AppLog.Info("test");
    Check(!Directory.Exists(logBase), "Logging must not recreate a missing data directory.");
    Console.WriteLine("Configuration checks passed.");
}
finally { Directory.Delete(root, true); }

static void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
}

static void Expect<T>(Action action) where T : Exception
{
    try { action(); }
    catch (T) { return; }
    throw new Exception("Expected " + typeof(T).Name);
}
