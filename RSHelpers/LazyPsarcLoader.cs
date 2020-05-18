using Newtonsoft.Json;
using RocksmithToolkitLib.DLCPackage;
using RocksmithToolkitLib.DLCPackage.Manifest2014;
using RocksmithToolkitLib.Extensions;
using RocksmithToolkitLib.PsarcLoader;
using RocksmithToolkitLib.Sng2014HSL;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace RockSnifferLib.RSHelpers
{
    public class LazyPsarcLoader : IDisposable
    {
        private PSARC _archive;
        private readonly string _filePath;
        private Stream _fileStream;

        public LazyPsarcLoader(FileInfo fileInfo, bool useMemory = true, bool lazy = true)
        {
            _filePath = fileInfo.FullName;
            _archive = new PSARC(useMemory);

            //Try to open the file over 10 seconds
            int tries = 0;
            while (_fileStream == null)
            {
                try
                {
                    _fileStream = fileInfo.OpenRead();
                }
                catch (Exception e)
                {
                    //Throw the exception if we tried 10 times
                    if (tries > 10)
                    {
                        throw e;
                    }

                    Thread.Sleep(1000);
                }
                tries++;
            }

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

        internal ArrangementData ExtractArrangementData(Attributes2014 attr)
        {
            string sngFilePath = $"songs/bin/generic/{attr.SongXml.Substring(20)}.sng";
            var entry = _archive.TOC.Where(x => x.Name.Equals(sngFilePath)).FirstOrDefault();

            if (entry == null)
                throw new Exception($"Could not find arrangement sng {_filePath}/{sngFilePath}");

            ArrangementData data = null;

            _archive.InflateEntry(entry);
            entry.Data.Position = 0;

            var sng = Sng2014File.ReadSng(entry.Data, new RocksmithToolkitLib.Platform(RocksmithToolkitLib.GamePlatform.Pc, RocksmithToolkitLib.GameVersion.RS2014));

            if (sng == null)
                throw new Exception($"Could not read sng {_filePath}{sngFilePath}");

            data = new ArrangementData(sng);

            entry.Dispose();

            return data;
        }

        public List<Manifest2014<Attributes2014>> ExtractJsonManifests()
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
                    using (var ms = new MemoryStream())
                    {
                        jsonEntry.Data.CopyTo(ms);
                        ms.Position = 0;

                        using (var reader = new StreamReader(ms, new UTF8Encoding(), false, 65536)) //4Kb is default alloc size for windows .. 64Kb is default PSARC alloc
                        {
                            dataObj = JsonConvert.DeserializeObject<Manifest2014<Attributes2014>>(reader.ReadToEnd());
                        }
                    }

                    jsonData.Add(dataObj);
                }
            }

            return jsonData;
        }

        /// <summary>
        /// Extracts a 256x256 bitmap album art from PsarcLoader
        /// </summary>
        /// <param name="loader"></param>
        /// <param name="artFile"></param>
        /// <returns></returns>
        internal Bitmap ExtractAlbumArt(Attributes2014 attr)
        {
            //Select the correct entry and load it into the memory stream
            using (MemoryStream ms = ExtractEntryData(x => (x.Name == "gfxassets/album_art/" + attr.AlbumArt.Substring(14) + "_256.dds")))
            {
                //Create a Pfim image from memory stream
                Pfim.Dds img = Pfim.Dds.Create(ms, new Pfim.PfimConfig());

                //Create bitmap
                Bitmap bm = new Bitmap(img.Width, img.Height);

                //Convert Pfim image to bitmap
                int bytesPerPixel = img.BytesPerPixel;
                for (int i = 0; i < img.Data.Length; i += bytesPerPixel)
                {
                    //Calculate pixel X and Y coordinates
                    int x = (i / bytesPerPixel) % img.Width;
                    int y = (i / bytesPerPixel) / img.Width;

                    //Get color from the Pfim image data array
                    Color c = Color.FromArgb(255, img.Data[i + 2], img.Data[i + 1], img.Data[i]);

                    //Set pixel in bitmap
                    bm.SetPixel(x, y, c);
                }

                //Return bitmap
                return bm;
            }
        }

        public ToolkitInfo ExtractToolkitInfo()
        {
            var tkInfo = new ToolkitInfo();
            var toolkitVersionEntry = _archive.TOC.FirstOrDefault(x => (x.Name.Equals("toolkit.version")));

            if (toolkitVersionEntry != null)
            {
                _archive.InflateEntry(toolkitVersionEntry);
                toolkitVersionEntry.Data.Position = 0;
                tkInfo = GeneralExtension.GetToolkitInfo(new StreamReader(toolkitVersionEntry.Data));
            }
            else
            {
                tkInfo.PackageAuthor = "Ubisoft";
            }

            return tkInfo;
        }
    }
}
