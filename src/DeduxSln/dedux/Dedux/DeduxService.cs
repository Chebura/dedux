using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
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
            _logger.LogInformation("Loading cache ...");

            
            using var md5 = MD5.Create();

            var fnHex = GetHex(md5.ComputeHash(
                Encoding.UTF8.GetBytes(AppDomain.CurrentDomain.BaseDirectory + "?" +
                                       string.Join(';', _configuration.TargetDirs))));

            var cacheFilename = Path.Combine(_configuration.CachePath, fnHex + ".bin");

            if (!Directory.Exists(_configuration.CachePath))
                Directory.CreateDirectory(_configuration.CachePath);

            await using var cacheFile = new FileStream(cacheFilename, FileMode.OpenOrCreate,
                FileAccess.ReadWrite,
                FileShare.None, 1024, true);

            
            var hashtab = new Hashtable();

            var cache = Serializer.Deserialize(cacheFile, new CacheDirectory()
            {
                Data = new List<CacheData>()
            });

            foreach (var item in cache.Data)
            {
                cancellationToken.ThrowIfCancellationRequested();
                hashtab[Convert.ToBase64String(item.NameHash)] = item;
            }

            _logger.LogInformation("Creating actual structure ...");

            cache.Data.Clear();

            var files = _configuration.TargetDirs.SelectMany(target =>
                {
                    _logger.LogInformation($"Scanning dir: {target}");
                        return Directory.GetFiles(target, _configuration.TargetDirSearchPattern,
                            SearchOption.AllDirectories);
                    })
                .ToArray();

            long i = 0, max = files.Length, j = -1;

            bool needToUpdateCache = false;

            foreach (var file in files)
            {
                var perc = ((i++) * 100) / max;

                var (item, isnew) = await CreateNewCacheAsync(hashtab, file, md5);

                if (isnew)
                    needToUpdateCache = true;

                cache.Data.Add(item);

                if (perc != j && perc % 5 == 0)
                {
                    j = perc;
                    _logger.LogDebug(perc + " %");
                }

                if (cancellationToken.IsCancellationRequested)
                {
                    Serializer.Serialize(cacheFile, cache);
                    _logger.LogInformation("Cancelled. Cache saved.");
                    return;
                }
            }

            _logger.LogInformation("Searching duplicates ...");

            var duplicates = FindDuplicates(cache.Data);

            if (duplicates.Any())
            {
                _logger.LogInformation($"Duplicates found ({duplicates.Count()})");
                
                await using var duplicatesFile = new FileStream(_configuration.DuplicatesPath, FileMode.CreateNew,
                    FileAccess.Write, FileShare.None, 1024, true);

                await using var sw = new StreamWriter(duplicatesFile);

                foreach (var dup in duplicates)
                {
                    sw.WriteLine("---");
                    foreach (var d in dup)
                    {
                        sw.WriteLine(d.Path);
                    }
                }

                await duplicatesFile.FlushAsync(cancellationToken);
            }

            if (needToUpdateCache)
            {
                Serializer.Serialize(cacheFile, cache);
                _logger.LogInformation("Cache saved.");
            }
            else
                _logger.LogInformation("Structure not changed.");

            await cacheFile.FlushAsync(cancellationToken);
        }

        private async Task<(CacheData, bool)> CreateNewCacheAsync(Hashtable oldCache, string path, MD5 md5)
        {
            var info = new FileInfo(path);
            var ts = new DateTimeOffset(info.LastWriteTimeUtc).ToUnixTimeSeconds();
            var nameHash = md5.ComputeHash(System.Text.Encoding.UTF8.GetBytes(path));
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
            return items.GroupBy(x => x.BodyHash, x => x, (k, elems) => elems, new StructuralEqualityComparer())
                .Where(x => x.Count() > 1);
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