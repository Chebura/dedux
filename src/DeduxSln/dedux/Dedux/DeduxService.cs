using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using ProtoBuf;

namespace dedux.Dedux
{
    using Cache;

    public class DeduxService
    {
        private readonly ILogger<DeduxService> _logger;
        private readonly DeduxConfiguration _configuration;
        private readonly FileNameMaskMatcher _fileNameMaskMatcher = new FileNameMaskMatcher();

        public DeduxService(ILogger<DeduxService> logger, DeduxConfiguration configuration)
        {
            _logger = logger;
            _configuration = configuration;
        }

        public async Task RunAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Start dedux service ...");
            await ScanAsync(cancellationToken);
        }

        private string GetHex(byte[] bytes)
        {
            var sb = new StringBuilder();
            foreach (var bt in bytes)
                sb.Append(bt.ToString("x2"));
            return sb.ToString();
        }

        private async Task ScanAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Loading cache, creating file system structure ...");

            using var listOfCaches = new DisposableList();

            foreach (var target in _configuration.TargetDirs)
            {
                var cacheContext = await GetCacheDirectoryAsync(target, cancellationToken);
                listOfCaches.Add(cacheContext);
                if (cancellationToken.IsCancellationRequested)
                    break;
            }

            if (cancellationToken.IsCancellationRequested)
                await SaveCacheAsync(listOfCaches);

            cancellationToken.ThrowIfCancellationRequested();

            _logger.LogInformation("Searching duplicates ...");

            var duplicates = FindDuplicates(listOfCaches.SelectMany(x => x.Cache.Data)).ToList();

            if (duplicates.Any())
            {
                _logger.LogInformation($"Duplicates found ({duplicates.Count()})");
                
                if (File.Exists(_configuration.DuplicatesPath))
                {
                    _logger.LogInformation(
                        $"Duplicates file report `{_configuration.DuplicatesPath}` found and will be deleted.");
                    File.Delete(_configuration.DuplicatesPath);
                }

                await using var duplicatesFile = new FileStream(_configuration.DuplicatesPath, FileMode.CreateNew,
                    FileAccess.Write, FileShare.None, 1024, true);

                await using var sw = new StreamWriter(duplicatesFile, Encoding.UTF8);

                foreach (var dup in duplicates)
                {
                    await sw.WriteLineAsync("---");
                    foreach (var d in dup)
                    {
                        await sw.WriteLineAsync(d.Path);
                    }
                }

                await duplicatesFile.FlushAsync(cancellationToken);

                _logger.LogInformation(
                    $"Duplicates file report `{_configuration.DuplicatesPath}` saved.");

                if (_configuration.DuplicateDelete && (_configuration.DeletingMasks?.Any() ?? false))
                {
                    foreach (var mask in _configuration.DeletingMasks)
                    {
                        _logger.LogInformation("Deleting duplicates by mask `{0}`", mask);

                        foreach (var group in duplicates)
                        {
                            var g = group.Where(x => _fileNameMaskMatcher.IsMatch(x.Path, mask)).Select(x => x.Path);
                            if (g.Count() < group.Count())
                            {
                                foreach (var fileForDelete in g)
                                {
                                    _logger.LogInformation("Deleting: `{0}`", fileForDelete);
                                    File.Delete(fileForDelete);
                                }
                            }
                            else
                            {
                                if (_configuration.KeepSingleFileInDirectoryIfMultiple)
                                {
                                    var keepFirst = g.First();
                                    foreach (var fileForDelete in g)
                                    {
                                        if (fileForDelete == keepFirst) continue;
                                        _logger.LogInformation("Deleting: `{0}`", fileForDelete);
                                        File.Delete(fileForDelete);
                                    }
                                }
                            }
                        }
                        
                        
                    }
                }
            }
            else
            {
                _logger.LogInformation("There are no duplicates. Deleting: `{0}`", _configuration.DuplicatesPath);
                File.Delete(_configuration.DuplicatesPath);
            }

            await SaveCacheAsync(listOfCaches);
        }

        private async Task SaveCacheAsync(DisposableList listOfCaches)
        {
            _logger.LogInformation("Saving cache ...");

            foreach (var cacheContext in listOfCaches)
            {
                if (cacheContext.UpdateCache)
                {
                    Serializer.Serialize(cacheContext.CacheFile, cacheContext.Cache);
                    await cacheContext.CacheFile.FlushAsync();
                }
            }

            _logger.LogInformation("Cache saved.");
        }

        private async Task<CacheContext> GetCacheDirectoryAsync(string target, CancellationToken cancellationToken)
        {
            _logger.LogDebug($"Preparing target: {target}");

            var cacheContext = new CacheContext();

            using var md5 = MD5.Create();

            var fnHex = GetHex(md5.ComputeHash(
                Encoding.UTF8.GetBytes(AppDomain.CurrentDomain.BaseDirectory + "?" +
                                       target)));

            var cacheFilename = Path.Combine(_configuration.CachePath, fnHex + ".bin");

            if (!Directory.Exists(_configuration.CachePath))
                Directory.CreateDirectory(_configuration.CachePath);

            _logger.LogInformation($"Open file cache: {cacheFilename}");

            cacheContext.CacheFile = new FileStream(cacheFilename, FileMode.OpenOrCreate,
                FileAccess.ReadWrite,
                FileShare.None, 1024, true);

            
            var hashtab = new Hashtable();

            cacheContext.Cache = Serializer.Deserialize(cacheContext.CacheFile, new CacheDirectory()
            {
                Data = new List<CacheData>()
            });

            foreach (var item in cacheContext.Cache.Data)
            {
                cancellationToken.ThrowIfCancellationRequested();
                hashtab[Convert.ToBase64String(item.NameHash)] = item;
            }

            _logger.LogInformation("Creating actual structure ...");

            cacheContext.Cache.Data.Clear();

            _logger.LogInformation($"Scanning target: {target}");
            
            var files =  Directory.GetFiles(target, _configuration.TargetDirSearchPattern,
                SearchOption.AllDirectories);

            long i = 0, max = files.Length, j = -1;

            foreach (var file in files)
            {
                _logger.LogTrace(file);

                var perc = ((i++) * 100) / max;

                var (item, isnew) = await CreateNewCacheAsync(hashtab, file, md5);

                if (isnew)
                    cacheContext.UpdateCache = true;

                cacheContext.Cache.Data.Add(item);

                if (perc != j && perc % 5 == 0)
                {
                    j = perc;
                    _logger.LogDebug(perc + " %");
                }

                if (cancellationToken.IsCancellationRequested)
                {
                    return cacheContext;
                }
            }

            return cacheContext;
        }

        private async Task<(CacheData, bool)> CreateNewCacheAsync(Hashtable oldCache, string path, MD5 md5)
        {
            var info = new FileInfo(path);
            var ts = new DateTimeOffset(info.LastWriteTimeUtc).ToUnixTimeSeconds();
            var nameHash = md5.ComputeHash(Encoding.UTF8.GetBytes(path));
            var obj = oldCache[Convert.ToBase64String(nameHash)];

            if (obj is CacheData cd)
            {
                if (cd.Timestamp != ts)
                {
                    //need to recache
                    cd.Timestamp = ts;
                    cd.BodyHash = await GetFileHashAsync(path, md5);
                    return (cd, true);
                }

                return (cd, false); //no need to cache
            }

            var data = new CacheData
            {
                NameHash = nameHash,
                Timestamp = ts,
                BodyHash = await GetFileHashAsync(path, md5),
                Path = path
            };

            return (data, true);
        }

        private async Task<byte[]> GetFileHashAsync(string path, MD5 alg)
        {
            await using var file = new FileStream(path, FileMode.Open,
                FileAccess.Read,
                FileShare.Read, 1024, true);
            return await alg.ComputeHashAsync(file);
        }

        private IEnumerable<IEnumerable<CacheData>> FindDuplicates(IEnumerable<CacheData> items)
        {
            return items.Where(x => !_fileNameMaskMatcher.IsMatch(x.Path, _configuration.DuplicateExclusionMasks))
                .GroupBy(x => x.BodyHash, x => x, (_, elems) => elems, new StructuralEqualityComparer())
                .Where(x => x.Count() > 1);
        }


        private class CacheContext : IDisposable
        {
            public CacheDirectory Cache { get; set; }

            public bool UpdateCache { get; set; }

            public FileStream CacheFile { get; set; }

            public void Dispose()
            {
                using (CacheFile)
                {
                }
            }
        }

        private class DisposableList : List<CacheContext>, IDisposable
        {
            public void Dispose()
            {
                foreach (var i in this)
                    using (i)
                    {
                    }
            }
        }

        private class FileNameMaskMatcher
        {
            private readonly IDictionary<string, Regex> _rxCache = new ConcurrentDictionary<string, Regex>();

            public bool IsMatch(string fileName, string mask)
            {
                if (_rxCache.TryGetValue(mask, out var rx))
                    return rx.IsMatch(fileName);
                var convertedMask = "^" + Regex.Escape(mask).Replace("\\*", ".*").Replace("\\?", ".") + "$";
                rx = new Regex(convertedMask, RegexOptions.IgnoreCase);
                _rxCache[mask] = rx;
                return rx.IsMatch(fileName);
            }

            public bool IsMatch(string fileName, ICollection<string> masks)
            {
                if (masks == null)
                    return false;

                return masks.Any(x => IsMatch(fileName, x));
            }
        }
    }

    [Serializable]
    internal class StructuralEqualityComparer : IEqualityComparer, IEqualityComparer<object>
    {
        public new bool Equals(object x, object y)
        {
            var s = x as IStructuralEquatable;
            return s == null ? object.Equals(x, y) : s.Equals(y, this);
        }

        public int GetHashCode(object obj)
        {
            var s = obj as IStructuralEquatable;
            return s == null ? EqualityComparer<object>.Default.GetHashCode(obj) : s.GetHashCode(this);
        }
    }
}