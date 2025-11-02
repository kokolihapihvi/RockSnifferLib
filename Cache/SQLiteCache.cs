using System;
using Newtonsoft.Json;
using RockSnifferLib.Logging;
using RockSnifferLib.Sniffing;
using System.Collections.Generic;
using System.Data;
using System.Data.SQLite;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;

namespace RockSnifferLib.Cache
{
    public class SQLiteCache : ICache
    {
        private SQLiteConnection Connection { get; set; }

        public SQLiteCache()
        {
            if (!File.Exists("cache.sqlite"))
            {
                SQLiteConnection.CreateFile("cache.sqlite");
            }

            Environment.SetEnvironmentVariable("SQLite_ConfigureDirectory", ".");
            Connection = new SQLiteConnection("Data Source=cache.sqlite;");
            Connection.Open();

            CreateTables();
        }

        private void CreateTables()
        {
            var q = @"
            CREATE TABLE IF NOT EXISTS `songs` (
	            `id`                INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
	            `psarcFile`         TEXT NOT NULL,
	            `psarcFileHash` 	TEXT NOT NULL,
	            `songid`            TEXT NOT NULL,
	            `songname`          TEXT NOT NULL,
	            `artistname`	    TEXT NOT NULL,
	            `albumname`         TEXT,
	            `songLength`	    REAL,
	            `albumYear`         INTEGER,
	            `arrangements`	    TEXT,
                `album_art`	        BLOB,
                `vocals`            TEXT,
	            `toolkit_version`	TEXT,
	            `toolkit_author`	TEXT,
	            `toolkit_package_version`	TEXT,
	            `toolkit_comment`	TEXT
            );";

            using (var cmd = Connection.CreateCommand())
            {
                cmd.CommandText = q;
                cmd.ExecuteNonQuery();
            }

            q = "CREATE INDEX IF NOT EXISTS `filepath` ON `songs` (`psarcFile` );";

            using (var cmd = Connection.CreateCommand())
            {
                cmd.CommandText = q;
                cmd.ExecuteNonQuery();
            }

            q = "CREATE INDEX IF NOT EXISTS`songid` ON `songs` (`songid` );";

            using (var cmd = Connection.CreateCommand())
            {
                cmd.CommandText = q;
                cmd.ExecuteNonQuery();
            }

            // Enable WAL mode, it is MUCH faster
            // It shouldn't have any downsides in this case
            q = "PRAGMA journal_mode = WAL";
            using (var cmd = Connection.CreateCommand())
            {
                cmd.CommandText = q;
                cmd.ExecuteNonQuery();
            }

            q = "PRAGMA synchronous = NORMAL";
            using (var cmd = Connection.CreateCommand())
            {
                cmd.CommandText = q;
                cmd.ExecuteNonQuery();
            }

            if (Logger.logCache)
            {
                Logger.Log("SQLite database initialised");
            }
        }

        public void Add(string filepath, Dictionary<string, SongDetails> allDetails)
        {
            var q = @"
            INSERT INTO `songs`(
                `psarcFile`,
                `psarcFileHash`,
                `songid`,
                `songname`,
                `artistname`,
                `albumname`,
                `songLength`,
                `albumYear`,
                `arrangements`,
                `album_art`,
                `vocals`,
                `toolkit_version`,
                `toolkit_author`,
                `toolkit_package_version`,
                `toolkit_comment`
            )
            VALUES (@psarcFile,@psarcFileHash,@songid,@songname,@artistname,@albumname,@songLength,@albumYear,@arrangements,@album_art,@vocals,@toolkit_version,@toolkit_author,@toolkit_package_version,@toolkit_comment);
            ";

            using (var cmd = Connection.CreateCommand())
            {
                cmd.CommandText = q;

                cmd.Parameters.Add("@psarcFile", DbType.String);
                cmd.Parameters.Add("@psarcFileHash", DbType.String);
                cmd.Parameters.Add("@songid", DbType.String);
                cmd.Parameters.Add("@songname", DbType.String);
                cmd.Parameters.Add("@artistname", DbType.String);
                cmd.Parameters.Add("@albumname", DbType.String);
                cmd.Parameters.Add("@songLength", DbType.Single);
                cmd.Parameters.Add("@albumYear", DbType.Int32);
                cmd.Parameters.Add("@arrangements", DbType.String);
                cmd.Parameters.Add("@album_art", DbType.Binary);
                cmd.Parameters.Add("@vocals", DbType.String);
                cmd.Parameters.Add("@toolkit_version", DbType.String);
                cmd.Parameters.Add("@toolkit_author", DbType.String);
                cmd.Parameters.Add("@toolkit_package_version", DbType.String);
                cmd.Parameters.Add("@toolkit_comment", DbType.String);

                foreach (KeyValuePair<string, SongDetails> pair in allDetails)
                {
                    var sd = pair.Value;

                    cmd.Parameters["@psarcFile"].Value = filepath;
                    cmd.Parameters["@psarcFileHash"].Value = sd.psarcFileHash;
                    cmd.Parameters["@songid"].Value = sd.songID;
                    cmd.Parameters["@songname"].Value = sd.songName;
                    cmd.Parameters["@artistname"].Value = sd.artistName;
                    cmd.Parameters["@albumname"].Value = sd.albumName;
                    cmd.Parameters["@songLength"].Value = sd.songLength;
                    cmd.Parameters["@albumYear"].Value = sd.albumYear;
                    cmd.Parameters["@arrangements"].Value = JsonConvert.SerializeObject(sd.arrangements);
                    cmd.Parameters["@toolkit_version"].Value = sd.toolkit.version;
                    cmd.Parameters["@toolkit_author"].Value = sd.toolkit.author;
                    cmd.Parameters["@toolkit_package_version"].Value = sd.toolkit.package_version;
                    cmd.Parameters["@toolkit_comment"].Value = sd.toolkit.comment;
                    cmd.Parameters["@album_art"].Value = null;
                    cmd.Parameters["@vocals"].Value = JsonConvert.SerializeObject(sd.vocals);

                    if (sd.albumArt != null)
                    {
                        using (var ms = new MemoryStream())
                        {
                            sd.albumArt.Save(ms, ImageFormat.Png);

                            cmd.Parameters["@album_art"].Value = ms.ToArray();
                        }
                    }

                    cmd.ExecuteNonQuery();

                    if (Logger.logCache)
                    {
                        Logger.Log("Cached {0}/{1}", Path.GetFileName(filepath), sd.songID);
                    }
                }
            }
        }

        public void Remove(string filepath, List<string> songIDs)
        {
            //Remove identical files
            string q = @"DELETE FROM `songs` WHERE psarcFile = @psarcFile";

            using (var cmd = Connection.CreateCommand())
            {
                cmd.CommandText = q;
                cmd.Parameters.AddWithValue("@psarcFile", filepath);

                cmd.ExecuteNonQuery();
            }

            //Remove identical song IDs
            q = @"DELETE FROM `songs` WHERE songid = @songid";

            using (var cmd = Connection.CreateCommand())
            {
                cmd.CommandText = q;
                cmd.Parameters.Add("@songid", DbType.String);

                foreach (string songid in songIDs)
                {
                    cmd.Parameters["@songid"].Value = songid;

                    cmd.ExecuteNonQuery();
                }
            }
        }

        public bool Contains(string filepath, string fileHash)
        {
            string q = @"SELECT EXISTS(SELECT 1 FROM songs WHERE psarcFile = @psarcFile and psarcFileHash = @psarcFileHash)";

            using (var cmd = Connection.CreateCommand())
            {
                cmd.CommandText = q;
                cmd.Parameters.AddWithValue("@psarcFile", filepath);
                cmd.Parameters.AddWithValue("@psarcFileHash", fileHash);

                var result = cmd.ExecuteScalar();
                return (long)result == 1;
            }
        }

        public SongDetails Get(string filepath, string songID)
        {
            return Get(songID);
        }

        public SongDetails Get(string SongID)
        {
            string q = @"SELECT * FROM songs WHERE songid = @songid LIMIT 1";

            using (var cmd = Connection.CreateCommand())
            {
                cmd.CommandText = q;
                cmd.Parameters.AddWithValue("@songid", SongID);

                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        var sd = new SongDetails
                        {
                            psarcFileHash = ReadField<string>(reader, "psarcFileHash"),
                            songID = ReadField<string>(reader, "songid"),
                            songName = ReadField<string>(reader, "songname"),
                            artistName = ReadField<string>(reader, "artistname"),
                            albumName = ReadField<string>(reader, "albumname"),
                            songLength = (float)ReadField<double>(reader, "songLength"),
                            albumYear = (int)ReadField<long>(reader, "albumYear"),
                            arrangements = JsonConvert.DeserializeObject<List<ArrangementDetails>>(ReadField<string>(reader, "arrangements")),
                            vocals = JsonConvert.DeserializeObject<List<SongDetails.VocalDetails>>(ReadField<string>(reader, "vocals")),
                            albumArt = null,

                            toolkit = new ToolkitDetails
                            {
                                version = ReadField<string>(reader, "toolkit_version"),
                                author = ReadField<string>(reader, "toolkit_author"),
                                package_version = ReadField<string>(reader, "toolkit_package_version"),
                                comment = ReadField<string>(reader, "toolkit_comment")
                            }
                        };

                        try
                        {
                            var blob = ReadField<byte[]>(reader, "album_art");

                            using (var ms = new MemoryStream(blob))
                            {
                                sd.albumArt = Image.FromStream(ms);
                            }
                        }
                        catch
                        {

                        }

                        return sd;
                    }
                }
            }

            return null;
        }

        private T ReadField<T>(SQLiteDataReader reader, string field)
        {
            int ordinal = reader.GetOrdinal(field);

            if (reader.IsDBNull(ordinal))
            {
                return default(T);
            }

            return (T)reader.GetValue(ordinal);
        }
    }
}
