using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RocksmithToolkitLib.DLCPackage.Manifest2014;
using RocksmithToolkitLib.PsarcLoader;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace RockSnifferLib.RSHelpers
{
    public class LazyPsarcLoader : IDisposable
    {
        private PSARC _archive;
        private string _filePath;
        private Stream _fileStream;

        public LazyPsarcLoader(string fileName, bool useMemory = true, bool lazy = true)
        {
            _filePath = fileName;
            _archive = new PSARC(true);
            _fileStream = File.OpenRead(_filePath);
            _archive.Read(_fileStream, lazy);
        }

        public void Dispose()
        {
            if (_fileStream != null)
            {
                _fileStream.Dispose();
                _fileStream = null;
            }
            if (_archive != null)
            {
                _archive.Dispose();
                _archive = null;
            }

            GC.SuppressFinalize(this);
        }

        public MemoryStream ExtractEntryData(Func<Entry, bool> entryLINQ)
        {
            var entry = _archive.TOC.Where(entryLINQ).FirstOrDefault();
            if (entry != null)
            {
                MemoryStream ms = new MemoryStream();
                _archive.InflateEntry(entry);
                if (entry.Data == null)
                    return null;

                entry.Data.Position = 0;
                entry.Data.CopyTo(ms);
                entry.Dispose();
                ms.Position = 0;
                return ms;
            }
            return null;
        }

        public IEnumerable<Manifest2014<Attributes2014>> ExtractJsonManifests()
        {
            // every song contains gamesxblock but may not contain showlights.xml
            var xblockEntries = _archive.TOC.Where(x => x.Name.StartsWith("gamexblocks/nsongs") && x.Name.EndsWith(".xblock")).ToList();
            if (!xblockEntries.Any())
                throw new Exception("Could not find valid xblock file in archive.");

            var jsonData = new List<Manifest2014<Attributes2014>>();
            // this foreach loop addresses song packs otherwise it is only done one time
            foreach (var xblockEntry in xblockEntries)
            {
                // CAREFUL with use of Contains and Replace to avoid creating duplicates
                var strippedName = xblockEntry.Name.Replace(".xblock", "").Replace("gamexblocks/nsongs", "");
                if (strippedName.Contains("_fcp_dlc"))
                    strippedName = strippedName.Replace("fcp_dlc", "");

                var jsonEntries = _archive.TOC.Where(x => x.Name.StartsWith("manifests/songs") &&
                    x.Name.EndsWith(".json") && x.Name.Contains(strippedName)).OrderBy(x => x.Name).ToList();

                // looping through song multiple times gathering each arrangement
                foreach (var jsonEntry in jsonEntries)
                {
                    var dataObj = new Manifest2014<Attributes2014>();

                    _archive.InflateEntry(jsonEntry);
                    jsonEntry.Data.Position = 0;
                    var ms = new MemoryStream();
                    using (var reader = new StreamReader(ms, new UTF8Encoding(), false, 65536)) //4Kb is default alloc size for windows .. 64Kb is default PSARC alloc
                    {
                        jsonEntry.Data.Position = 0;
                        jsonEntry.Data.CopyTo(ms);
                        ms.Position = 0;
                        var jsonObj = JObject.Parse(reader.ReadToEnd());
                        dataObj = JsonConvert.DeserializeObject<Manifest2014<Attributes2014>>(jsonObj.ToString());
                    }

                    jsonData.Add(dataObj);
                }
            }

            return jsonData;
        }
    }
}
