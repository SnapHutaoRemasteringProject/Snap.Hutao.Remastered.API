using System.Text.Json;

namespace Snap.Hutao.Remastered.API
{
    public sealed class ConfigService : IConfigService, IDisposable
    {
        private readonly string filePath;
        private readonly ReaderWriterLockSlim rwLock = new();
        private ConfigModel currentConfig = new();
        private readonly FileSystemWatcher? watcher;
        private bool disposed;
        private readonly ILogger<ConfigService> logger;

        public ConfigService(IWebHostEnvironment env, ILogger<ConfigService> logger)
        {
            this.logger = logger;

            var configDir = Path.Combine(env.ContentRootPath, "Data");
            filePath = Path.Combine(configDir, "config.json");

            // ensure directory exists
            if (!Directory.Exists(configDir))
            {
                Directory.CreateDirectory(configDir);
            }

            // ensure file exists with a default if missing
            EnsureDefaultFileExists();

            // load initial config
            LoadFromFile(logIfChanged: false);

            try
            {
                watcher = new FileSystemWatcher(configDir, Path.GetFileName(filePath))
                {
                    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
                    EnableRaisingEvents = true,
                    IncludeSubdirectories = false
                };

                watcher.Changed += OnFileChanged;
                watcher.Created += OnFileChanged;
                watcher.Renamed += OnFileChanged;
                watcher.Deleted += OnFileChanged;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to initialize FileSystemWatcher for config auto-reload.");
            }
        }

        private void EnsureDefaultFileExists()
        {
            try
            {
                if (!File.Exists(filePath))
                {
                    var defaultConfig = new ConfigModel { IpAddresses = Array.Empty<string>() };
                    var options = new JsonSerializerOptions { WriteIndented = true };
                    var json = JsonSerializer.Serialize(defaultConfig, options);

                    // write file synchronously before watcher is active
                    File.WriteAllText(filePath, json);
                    logger.LogInformation("Default config file created at {FilePath}", filePath);
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to create default config file at {FilePath}", filePath);
            }
        }

        public Task<ConfigModel> GetConfigAsync()
        {
            rwLock.EnterReadLock();
            try
            {
                // return a copy to avoid external mutation
                return Task.FromResult(new ConfigModel { IpAddresses = currentConfig.IpAddresses?.ToArray() });
            }
            finally
            {
                rwLock.ExitReadLock();
            }
        }

        public async Task SaveConfigAsync(ConfigModel config)
        {
            // serialize to temp file and replace to be atomic
            var options = new JsonSerializerOptions { WriteIndented = true };
            var tempFile = filePath + ".tmp";

            // write to temp file
            using (var fs = File.Create(tempFile))
            {
                await JsonSerializer.SerializeAsync(fs, config, options).ConfigureAwait(false);
                await fs.FlushAsync().ConfigureAwait(false);
            }

            // prevent watcher from reacting to our own write while replacing
            if (watcher != null)
            {
                watcher.EnableRaisingEvents = false;
            }

            try
            {
                File.Copy(tempFile, filePath, true);
                File.Delete(tempFile);
            }
            finally
            {
                if (watcher != null)
                {
                    // small delay to let FS settle
                    await Task.Delay(50).ConfigureAwait(false);
                    watcher.EnableRaisingEvents = true;
                }
            }

            // update in-memory copy
            rwLock.EnterWriteLock();
            try
            {
                currentConfig = new ConfigModel { IpAddresses = config.IpAddresses?.ToArray() };
            }
            finally
            {
                rwLock.ExitWriteLock();
            }

            logger.LogInformation("Config saved to {FilePath}", filePath);
        }

        private void OnFileChanged(object? sender, FileSystemEventArgs e)
        {
            // run reload on thread-pool to avoid blocking watcher
            Task.Run(() =>
            {
                // small delay to wait for file write to complete
                Thread.Sleep(100);
                LoadFromFile(logIfChanged: true);
            });
        }

        private void LoadFromFile(bool logIfChanged)
        {
            // attempt multiple times in case file is locked by writer
            for (int attempt = 0; attempt < 5; attempt++)
            {
                try
                {
                    if (!File.Exists(filePath))
                    {
                        rwLock.EnterWriteLock();
                        try
                        {
                            var old = currentConfig;
                            currentConfig = new ConfigModel();
                            if (logIfChanged && !AreEqual(old, currentConfig))
                            {
                                logger.LogInformation("Config file deleted: {FilePath}", filePath);
                            }
                        }
                        finally
                        {
                            rwLock.ExitWriteLock();
                        }

                        return;
                    }

                    using var fs = File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                    var config = JsonSerializer.Deserialize<ConfigModel>(fs) ?? new ConfigModel();

                    rwLock.EnterWriteLock();
                    try
                    {
                        var old = currentConfig;
                        currentConfig = new ConfigModel { IpAddresses = config.IpAddresses?.ToArray() };
                        if (logIfChanged && !AreEqual(old, currentConfig))
                        {
                            logger.LogInformation("Config reloaded from {FilePath}. New IPs: {Ips}", filePath, currentConfig.IpAddresses ?? Array.Empty<string>());
                        }
                    }
                    finally
                    {
                        rwLock.ExitWriteLock();
                    }

                    return;
                }
                catch (IOException)
                {
                    Thread.Sleep(100);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to load config from {FilePath}", filePath);
                    return;
                }
            }
        }

        private static bool AreEqual(ConfigModel a, ConfigModel b)
        {
            var aa = a?.IpAddresses ?? Array.Empty<string>();
            var bb = b?.IpAddresses ?? Array.Empty<string>();
            if (aa.Length != bb.Length) return false;
            for (int i = 0; i < aa.Length; i++) if (aa[i] != bb[i]) return false;
            return true;
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            watcher?.Dispose();
            rwLock.Dispose();
        }
    }
}
